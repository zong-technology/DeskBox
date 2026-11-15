import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

const assets = new Map([
  ['/', ['index.html', 'text/html; charset=utf-8']],
  ['/app.js', ['app.js', 'text/javascript; charset=utf-8']],
  ['/style.css', ['style.css', 'text/css; charset=utf-8']],
  ['/hologram.bundle.js', ['hologram.bundle.js', 'text/javascript; charset=utf-8']],
  ['/assets/nova-female.vrm', ['assets/nova-female.vrm', 'model/gltf-binary']],
  ['/assets/NOVA-model-license.txt', ['assets/NOVA-model-license.txt', 'text/plain; charset=utf-8']],
]);

export function createServer({ apiKey = process.env.OPENAI_API_KEY, getApiKey = () => apiKey, upstream = fetch } = {}) {
  return http.createServer(async (req, res) => {
    const send = (status, body, type = 'application/json') => {
      res.writeHead(status, { 'Content-Type': type, 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff', 'Referrer-Policy': 'no-referrer' });
      res.end(type === 'application/json' ? JSON.stringify(body) : body);
    };
    try {
      const url = new URL(req.url, 'http://localhost');
      if (req.method === 'GET' && url.pathname === '/api/status') return send(200, { configured: Boolean(getApiKey()) });
      if (req.method === 'POST' && url.pathname === '/api/call') {
        // Reject cross-origin use of the server's paid API credentials.
        const origin = req.headers.origin;
        if (!origin || !URL.canParse(origin) || new URL(origin).host !== req.headers.host || req.headers['sec-fetch-site'] === 'cross-site') {
          return send(403, { error: '不允许跨站发起通话。' });
        }
        const key = getApiKey();
        if (!key) return send(503, { error: '语音服务尚未配置，请在设置中配置服务密钥。' });
        if (!req.headers['content-type']?.startsWith('application/json')) return send(415, { error: '请求格式错误。' });
        const chunks = [];
        let size = 0;
        for await (const chunk of req) {
          size += chunk.length;
          if (size > 65536) return send(413, { error: '请求过大。' });
          chunks.push(chunk);
        }
        let body;
        try { body = JSON.parse(Buffer.concat(chunks).toString()); }
        catch { return send(400, { error: '请求内容无效。' }); }
        if (!body || typeof body.sdp !== 'string' || !body.sdp.startsWith('v=0') || !['marin', 'cedar'].includes(body.voice) || !['zh', 'en'].includes(body.language)) {
          return send(400, { error: '通话参数无效。' });
        }
        const session = {
          type: 'realtime', model: 'gpt-realtime-2.1', output_modalities: ['audio'],
          instructions: `You are NOVA, the DeskBox voice assistant. Be warm, concise, and helpful. Speak ${body.language === 'zh' ? 'Mandarin Chinese' : 'English'} by default. This is a live voice conversation. Never claim to see the user or control their computer.`,
          audio: {
            input: { transcription: { model: 'gpt-4o-mini-transcribe' }, turn_detection: { type: 'server_vad', create_response: true, interrupt_response: true } },
            output: { voice: body.voice },
          },
        };
        const form = new FormData();
        form.set('sdp', body.sdp);
        form.set('session', JSON.stringify(session));
        const response = await upstream('https://api.openai.com/v1/realtime/calls', {
          method: 'POST', headers: { Authorization: `Bearer ${key}` }, body: form,
          signal: AbortSignal.timeout(25000),
        });
        if (!response.ok) {
          // Never return upstream details that could contain account information.
          return send(502, { error: response.status === 429 ? '语音服务额度不足或请求过于频繁，请稍后重试。' : '语音服务连接失败，请检查服务配置后重试。' });
        }
        return send(200, await response.text(), 'application/sdp');
      }
      const asset = assets.get(url.pathname);
      if (req.method === 'GET' && asset) return send(200, await readFile(new URL(`./public/${asset[0]}`, import.meta.url)), asset[1]);
      send(404, { error: 'Not found' });
    } catch (error) {
      if (!res.headersSent) send(error.name === 'TimeoutError' ? 504 : 500, { error: '连接暂时不可用，请稍后重试。' });
      else res.end();
    }
  });
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const port = Number(process.env.PORT || 3000);
  const host = process.env.HOST || '127.0.0.1';
  createServer().listen(port, host, () => console.log(`DeskBox: http://${host}:${port}`));
}
