// 本地 OpenAI 兼容 mock 服务：用于验证 DeskBox 的 AI 链路（流式/错误分类/超时）
// 用法：node tools/mock-ai-server.js [port]
const http = require('http');

const PORT = Number(process.argv[2] || 18080);

function sse(res, deltas, delayMs) {
  res.writeHead(200, {
    'Content-Type': 'text/event-stream; charset=utf-8',
    'Cache-Control': 'no-cache',
    'Connection': 'keep-alive',
  });
  let i = 0;
  const timer = setInterval(() => {
    if (i >= deltas.length) {
      res.write('data: [DONE]\n\n');
      clearInterval(timer);
      res.end();
      return;
    }
    const chunk = {
      id: 'chatcmpl-mock',
      object: 'chat.completion.chunk',
      choices: [{ index: 0, delta: { content: deltas[i] }, finish_reason: null }],
    };
    res.write('data: ' + JSON.stringify(chunk) + '\n\n');
    i++;
  }, delayMs);
  res.on('close', () => clearInterval(timer));
}

function readBody(req) {
  return new Promise((resolve) => {
    const chunks = [];
    req.on('data', c => chunks.push(Buffer.from(c)));
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
  });
}

const server = http.createServer(async (req, res) => {
  const url = req.url || '/';
  if (req.method === 'GET' && url.startsWith('/health')) {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ ok: true, server: 'mock-openai', models: ['mock-chat', 'mock-fast'] }));
    return;
  }
  if (!url.includes('/chat/completions')) {
    res.writeHead(404, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: { message: 'not found: ' + url } }));
    return;
  }

  const body = await readBody(req);
  let payload = {};
  try { payload = JSON.parse(body || '{}'); } catch {}
  const model = payload.model || '';
  console.log(req.method + ' ' + url + ' model=' + model + ' bodyBytes=' + Buffer.byteLength(body));
  if (model === 'mock-mobile-create') {
    const results=(payload.messages||[]).filter(m=>(m.content||'').startsWith('[工具'));
    const project=results.find(m=>m.content.startsWith('[工具project.list'));
    let reply;
    if(results.some(m=>m.content.startsWith('[工具task.create'))) reply='已保存超滤项目的SIP任务，等待手机同步。';
    else if(project) {
      const data=JSON.parse(project.content.slice(project.content.indexOf('\n')+1));
      reply='```tool\n'+JSON.stringify({name:'task.create',args:{title:'SIP的FS需要更新步骤',project_id:data.Projects[0].Id,notes:'请供应商提供修订步骤'}})+'\n```';
    } else reply='```tool\n'+JSON.stringify({name:'project.list',args:{query:'超滤'}})+'\n```';
    sse(res,[reply],20);return;
  }
  if (model === 'mock-mobile-tools' || model === 'mock-mobile-write') {
    const result=(payload.messages||[]).find(m=>(m.content||'').startsWith('[工具'));
    const reply=result?'真实结果：'+result.content:'```tool\n'+JSON.stringify({name:model==='mock-mobile-write'?'task.update':'task.list',args:{}})+'\n```';
    sse(res,[reply],20);return;
  }
  if (model === 'mock-long-stream') {
    sse(res, Array.from({ length: 40 }, (_, i) => '分片' + i + '：持续生成中。\n'), 850);
    return;
  }
  if (model === 'mock-document' || model === 'mock-document-ocr') {
    const messages = payload.messages || [];
    const toolResult = [...messages].reverse().find(m => (m.content || '').includes('的执行结果'));
    const source = messages.find(m => (m.content || '').startsWith('DOC_PATH:'));
    const retryOcr = model === 'mock-document-ocr' && toolResult && toolResult.content.includes('doc.read') && toolResult.content.includes('OCR');
    const reply = retryOcr ? '```tool\n' + JSON.stringify({ name: 'doc.ocr', args: { path: source.content.slice(9), page: '1' } }) + '\n```' : toolResult ? '根据实际工具结果回答：' + toolResult.content : '```tool\n' + JSON.stringify({ name: 'doc.read', args: { path: source.content.slice(9), maxChars: '16000' } }) + '\n```';
    sse(res, [reply], 20);
    return;
  }
  const auth = req.headers['authorization'] || '';

  // 错误分支（用特殊 model 名触发，便于自动化测试）
  if (model === 'err-auth' || auth === 'Bearer bad-key') {
    res.writeHead(401, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: { message: 'Incorrect API key provided', type: 'invalid_request_error' } }));
    return;
  }
  if (model === 'err-model') {
    res.writeHead(404, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: { message: 'The model `nope` does not exist', type: 'invalid_request_error' } }));
    return;
  }
  if (model === 'err-ratelimit') {
    res.writeHead(429, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: { message: 'Rate limit reached' } }));
    return;
  }
  if (model === 'err-server') {
    res.writeHead(500, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: { message: 'internal error' } }));
    return;
  }
  if (model === 'err-protocol') {
    res.writeHead(200, { 'Content-Type': 'text/html' });
    res.end('<html>not json</html>');
    return;
  }
  if (model === 'err-timeout') {
    setTimeout(() => { try { res.end(''); } catch {} }, 30000);
    return;
  }

  const lastUser = [...(payload.messages || [])].reverse().find((m) => m.role === 'user');
  const q = (lastUser && lastUser.content) || '';
  const reply =
    '这是 mock 回复（模型 ' + (model || 'default') + '）。\n\n' +
    '收到你的问题：**' + q.slice(0, 40) + '**\n\n' +
    '```js\nconsole.log("hello from mock");\n```\n\n' +
    '1. 第一点：链路已连通\n2. 第二点：流式可用\n\n> 提示：换成你真实的 API Key 即可调用真实模型。';

  if (payload.stream) {
    const deltas = [];
    for (let i = 0; i < reply.length; i += 6) deltas.push(reply.slice(i, i + 6));
    sse(res, deltas, 18);
    return;
  }

  res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify({
    id: 'chatcmpl-mock',
    object: 'chat.completion',
    model: model || 'mock-chat',
    choices: [{ index: 0, message: { role: 'assistant', content: reply }, finish_reason: 'stop' }],
    usage: { prompt_tokens: 20, completion_tokens: 60, total_tokens: 80 },
  }));
});

server.listen(PORT, '127.0.0.1', () => {
  console.log('mock-openai listening on http://127.0.0.1:' + PORT);
});

