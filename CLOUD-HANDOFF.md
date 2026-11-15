# DeskBox 云端设计迁移

此副本来自本地分支 v2.1-foundation，基线提交 14fbeeaddf4757998eae01f9a3d3646c27cf5643，包含迁移时工作目录中的源码和未提交改动。

- Windows 桌面版由 build.ps1 构建。移动端源码在 mobile，云同步服务源码在 cloud/deskbox-worker。
- 实时语音原型、独立 WebGL 视口和验收定帧在 design/voice。WebGL 视口可用 Node.js 运行 serve.cjs 并打开 http://127.0.0.1:4178。
- 原 Figma 设计为 https://www.figma.com/design/jdAhri31Qx53x6T4gG4a9D 。design 目录里的 ZIP 是导出稿。在线编辑使用原 Figma 文件的授权。
- WebGL 独立样片的截图验证记录随包保留。桌面嵌入仍待实际验证。上一轮时长有异常的录像留在本机。
- 迁移副本包含当前文件快照。源目录继续保留原 Git 历史。原运行数据、备份、密钥、缓存、构建产物和未审核的截图目录留在本地。