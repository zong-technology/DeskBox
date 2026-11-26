# DeskBox 5.0.0-alpha.2 候选记录

源码来自 `codex/deskbox-v5-p0`，原生 Windows 候选构建提交 `3047cb7`。源码、未签名候选安装包和验收资料归档至独立开发分支及 GitHub Actions。正式 GitHub Release 留至发布里程碑。

## 变更

- 桌面 Agent 记录多步执行、模型响应证据、权限、步骤失败和取消，既有会话和数据布局保留。
- 第三方 MCP 支持真实 HTTP/stdio 注册、工具发现、Schema、按项目授权、人工调用和结果回执。
- 抽出服务、工程模型、存储与外部适配。提供最小业务入口和可追溯的界面交接包。
- 显式本地检索及工程计算可以与模型配置并存。普通模型连接测试继续可用，另有流式探测入口。

## 兼容与回退

继续安装至既有 DeskBox 位置。退出旧版托盘程序，先以现有备份功能保存数据，再开始升级。跨版本沿用 .NET Framework 4.8 与当前数据目录。若需要回退，使用安装的回退脚本；alpha.2 的任务、服务器及回执为独立目录。

[Windows 候选安装包](https://github.com/zong-technology/DeskBox/actions/runs/45863717693/artifacts/15140640651) · [Windows CI](https://github.com/zong-technology/DeskBox/actions/runs/45863717693) · [安装、备份和操作](V5-ALPHA2-GUIDE.md) · [测试及剩余事项](V5-ALPHA2-VERIFICATION.md)
