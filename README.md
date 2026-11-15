# DeskBox · 全息 AI 实时通话

原生 HTML / CSS / Canvas + Node.js，无第三方运行时依赖。动态粒子全息人像配合 OpenAI Realtime WebRTC 实时语音，支持打断、麦克风静音、挂断、声音与语言选择，以及实时字幕。全息形象为屏幕内的视觉效果，不需要投影硬件。

## 本地运行

使用 Node.js 22.9+（推荐 Node.js 24）。

```bash
cp .env.example .env
# 在 .env 中填写自己的 OPENAI_API_KEY
npm start
```

打开 http://127.0.0.1:3000 。无需 npm install。未配置密钥时可使用「体验全息效果」，该模式不会开启麦克风或模拟 AI 通话。

## 真实通话

1. 为 OpenAI API 账号配置有效密钥、可用额度及 `gpt-realtime-2.1` 模型权限。
2. 点击「开始通话」并允许麦克风权限，即可与 NOVA 交谈。语音活动检测会自动回复，支持直接说话打断。
3. 点击麦克风可静音；点击「结束通话」、连接失败或关闭页面时，会停止麦克风轨道并关闭连接。

密钥仅在服务器使用，浏览器通过 `/api/call` 交换 SDP，随后通过 WebRTC 与 OpenAI 连接。语音发送至 OpenAI 处理；字幕只保留在当前页面内存中，刷新即清除。实时语音和字幕转写会产生 API 费用。

远程访问必须使用 HTTPS（localhost 除外）并允许 WebRTC 网络通信。默认仅监听 `127.0.0.1`；如需设置 `HOST=0.0.0.0` 部署，必须先在反向代理添加身份验证与使用限额，避免任何访客消耗你的 API 额度。本版本用于本地单用户运行，未提供公共服务的登录和配额系统。

## 验证

```bash
npm test
node --check public/app.js
node --check server.mjs
```

自动检查覆盖接口校验、跨站拦截、密钥隔离、SDP 转发、上游错误、通话生命周期与取消时的麦克风清理。真实麦克风、浏览器音频播放和 OpenAI 连通性需使用有效密钥做端到端验证。
