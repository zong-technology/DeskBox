// 工具调用 E2E 用的 OpenAI 兼容 mock：
// 第一轮返回会触发 fs.search 的 ```tool 块；第二轮（上下文含工具结果）返回基于结果的回答。
const http = require('http');

const port = Number(process.argv[2] || 18082);
const workDir = process.argv[3] || process.cwd();
const fence = '```';

http.createServer((req, res) => {
  let body = '';
  req.on('data', (c) => (body += c));
  req.on('end', () => {
    let payload = {};
    try { payload = JSON.parse(body || '{}'); } catch {}
    const msgs = payload.messages || [];
    const hasTool = msgs.some((m) => typeof m.content === 'string' && m.content.includes('的执行结果'));

    let reply;
    if (hasTool) {
      reply = '已根据工具执行结果回答：目录里有报告A.pdf 和 报告B.pdf 两个文件。';
    } else {
      const call = { name: 'fs.search', args: { root: workDir, keyword: 'pdf', limit: '5' } };
      reply = '我先搜索一下目录。\n\n' + fence + 'tool\n' + JSON.stringify(call, null, 0) + '\n' + fence + '\n\n请稍等。';
    }

    if (payload.stream) {
      res.writeHead(200, { 'Content-Type': 'text/event-stream; charset=utf-8', 'Cache-Control': 'no-cache' });
      const parts = [];
      for (let i = 0; i < reply.length; i += 8) parts.push(reply.slice(i, i + 8));
      let i = 0;
      const t = setInterval(() => {
        if (i >= parts.length) { res.write('data: [DONE]\n\n'); clearInterval(t); res.end(); return; }
        res.write('data: ' + JSON.stringify({ choices: [{ index: 0, delta: { content: parts[i] } }] }) + '\n\n');
        i++;
      }, 10);
      return;
    }
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify({ choices: [{ message: { role: 'assistant', content: reply } }] }));
  });
}).listen(port, '127.0.0.1', () => console.log('tool-mock ready on ' + port + ' workDir=' + workDir));
