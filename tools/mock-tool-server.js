// 端到端工具调用验证：起一个有本地 HTTP mock 的服务商 → 让"AI"返回 ```tool 块 →
// 校验 ChatService 真的执行了工具、把结果回灌、并继续下一轮对话。
const http = require('http');

// 一个会返回工具调用的 OpenAI 兼容 mock
function startMock(port, workDir) {
  return new Promise((resolve) => {
    const server = http.createServer((req, res) => {
      let body = '';
      req.on('data', (c) => (body += c));
      req.on('end', () => {
        let payload = {};
        try { payload = JSON.parse(body || '{}'); } catch {}
        const msgs = payload.messages || [];
        const lastUser = [...msgs].reverse().find((m) => m.role === 'user');
        const q = (lastUser && lastUser.content) || '';

        // 判断是不是第二轮（上下文里已经带了工具结果）
        const hasToolResult = msgs.some((m) => typeof m.content === 'string' && m.content.includes('的执行结果'));

        let reply;
        if (!hasToolResult) {
          // 第一轮：要求搜索文件
          reply = '好的，我先看看目录里有哪些 pdf。\n\n```tool\n' +
            JSON.stringify({ name: 'fs.search', args: { root: workDir, keyword: 'pdf', limit: '5' } }) +
            '\n```\n\n请稍等。';
        } else {
          reply = '找到的文件我已经看到了，一共两个 pdf（报告A、报告B）。这是基于真实搜索结果的回答。';
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
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ choices: [{ message: { role: 'assistant', content: reply } }] }));
      });
    });
    server.listen(port, '127.0.0.1', () => resolve(server));
  });
}

module.exports = { startMock };
