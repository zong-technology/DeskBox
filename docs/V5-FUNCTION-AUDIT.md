# DeskBox V5.0 Engineering OS · 基线审查

盘点日期 2026-11-26 UTC。开发从独立分支 `codex/deskbox-v5-p0` 开始。

| 核对项 | 观察结果 |
| --- | --- |
| 本地源码与开发分支 | 初始 Linux 工作区只有空的文件交换目录。完整克隆后确认 Git 工作树干净。采用可读取的最新工程开发分支 `origin/feat/tia-openness-attach` 的 `1930b40`，它包含 `90c1681` V4.3 编程分支与较早的 V4.2 UI、TIA 分支。 |
| GitHub main | 实时远端引用为 `bc9f0ed77f87540fbaa51218b59851b0f5f15e1a`，是 2026-11-15 云迁移快照。快照中的构建方式、语音工作台和 TIA 功能早于开发分支。 [main](https://github.com/zong-technology/DeskBox/tree/main) |
| GitHub Release | 最新公开预发布为 `v4.3.0-alpha.3`（2026-11-23，`b1fcfd7`），提供安装包、校验清单和截图。开发基线的程序集版本为 `4.3.0-alpha.7`，已有构建产物但无同名 Release。 [Release](https://github.com/zong-technology/DeskBox/releases/tag/v4.3.0-alpha.3) · [alpha.7 CI](https://github.com/zong-technology/DeskBox/actions/runs/45555072419) |
| 安装、进程、dist | 当前 Linux 工作区没有 Windows 安装、运行中的 DeskBox 进程或随源码提供的 `dist`。没有连接用户的 Windows 工程电脑，实际安装版本与本机未发布改动为 BLOCKED。已有 CI 产物单独记录。 |
| 历史与备份 | 克隆取得远端分支与历史标签。云交接说明指出原 Windows 工作树、运行数据、备份、缓存和密钥留在原电脑。此环境未读取或覆盖它们。 |
| Figma | 读取到在线文件的业务与语音原型、开发交接页。Design System 树遇到 Starter 工具额度。仓库有 V4.1 Final 29 张导出稿、12 页图册和 Mission / Focus / System 参考图。核对实机布局以运行产物为依据。 [设计文件](https://www.figma.com/design/jdAhri31Qx53x6T4gG4a9D) |
| 既有 Windows 验收 | alpha.7 的历史 CI 记录和 [TIA 报告](TIA-V43-MCP.md) 记录 186 条 TIA 断言和 70 条安装断言。工程夹具、模型夹具、协议真实进程、Siemens 持证实机分别记账。旧版截图与通过记录不计入 V5 验收。 |
| 既有 MCP | `--tia-mcp` 启动私有 stdio JSON-RPC 进程。协商 MCP 2025-06-18，列出当前审批作业的唯一工具，校验 ID、输入输出 schema 与操作回执。其他 AI 工具通过进程内 ToolRegistry 注册。没有通用第三方 MCP 管理器。 |
| 既有 TIA | Windows 环境检查加反射加载 V20 PublicAPI。静态发现与显式确认后只读附加，闭合工程通过副本操作。可读取导出物，支持 SCL 草稿、静态分析和差异。已实现的副本编译、单块新增及回读仍须持证 V20 和授权 `.ap20` 取证。 |
| 基线编译 | Debian 13 上以 SDK 8.0.428 和已有 `DeskBox.Build.csproj` 真实生成 .NET Framework 4.8 Windows PE。0 个错误，5 个既有警告。此结果证明可编译。 |

## 入口与数据流

- 主入口在 `src/Program.cs`。进程模式包含桌面、同步桥和 TIA 子进程。默认参考工作区和经典模式共享 `MainForm` 服务。页面、主菜单、右键、二级工具入口通过 `MainForm`、`ReferenceWorkspace` 及各 `Views` 追踪，详见功能矩阵。
- 工作区、任务、AI 会话、验证进度、文件索引、自动化、同步、通知、备份均有既有文件存储。工程信息在 `engineering-workspaces.json`，当前项目在 `engineering-current.json`，聊天按 AI 环境分文件，同步有独立实体、队列、冲突、设备信任文件。云 Worker 有独立 SQL 模式。桌面业务库未采用 SQLite。
- 任务数据既可存项目 ID 也可存历史手输名称。现场事件与移动实体已含项目 ID。验证数据使用项目内阶段进度。原 AI 会话没有项目 ID，目录权限按应用或会话授权，文件搜索可停在上次的手工路径。两套 UI 均有直接访问全局任务记录的路径。
- `AiClient`、`ProviderStore`、`ChatService`、`MobileAiRunner` 和 TIA 规划器有不同调用入口。现有能力涵盖流式、停止、有限重试、错误分类和本地隔离。模型结果和工具执行状态存在重复记录，当前只有普通应用记录和 TIA 运行回执。
- 仓库有桌面端、SwiftUI iOS、独立 Node/Electron 语音样片、云 Worker、文档 Python 适配器和 PowerShell 安装脚本。编辑及测试仅使用隔离数据和仓库中的测试夹具。

[功能矩阵](V5-FUNCTION-MATRIX.md) · [技术债](V5-TECHNICAL-DEBT.md) · [开发路线](V5-DEVELOPMENT-ROADMAP.md)
