# DeskBox 5.0.0-alpha.2 开发与验收

## 构建基线

开发分支为 `codex/deskbox-v5-p0`。启动时远端最新为 `8f00f9a`，此前实际 Windows 构建为 `89b4fd7`，原始工程基线为 `1930b40`。本次用于原生验收的源代码为 `3047cb7`。保留 C# 5、WinForms 和 .NET Framework 4.8。Windows 文件版本为 `5.0.0.2`。

[变更与源码](https://github.com/zong-technology/DeskBox/compare/8f00f9a...3047cb7) · [Windows Actions](https://github.com/zong-technology/DeskBox/actions/runs/45863717693)

## 可独立复验的业务服务

| 范围 | 证据 |
| --- | --- |
| Linux 交叉构建 | .NET SDK 8.0.428 编译 net48 Windows 目标，0 错误、5 条基线警告。 |
| Agent 和通用 MCP | Linux／Mono 运行 58 项回归。真实回环 HTTP、SSE 和独立 stdio 夹具验证多步运行、模型文本与结束帧、Tokens、重试、超时、取消、兜底计算、工程隔离和恢复。 |
| MCP SDK 兼容 | Linux 使用官方 MCP Python SDK 1.29.0 的独立服务，通过 HTTP 和 stdio 协商、发现输入／输出 Schema、执行和读取结构化响应。使用独立的只读回显测试数据。 |
| 既有桌面能力 | Linux／Mono 的工程上下文 32 项、项目切换中真实 SSE 5 项、旧 AI 客户端和会话 22 项、备份恢复 12 项通过。 |
| 故障修复 | 本地回归发现响应头已到后空闲计时不稳定。已加入按服务商超时重置的流式取消。Windows 回归发现 .NET Framework 向第三方 stdio 写入 UTF‑8 BOM，已针对重定向子进程启动修复。严格 JSON 解析夹具保留。 |

协议测试使用专用测试端点或进程，包含空注册、无授权、响应过大、无结束帧、Schema 漂移、未经确认、HTTP 401、RPC 错误、断开、取消、超时、并发冲突、配置恢复及未来版本条件。网络交互与落盘真实发生，测试数据为回归材料。

## 复验入口

在 Windows 源码目录运行 `build.ps1 -OutDir dist -NoPause`。工作流用同一份 Windows EXE 编译并执行 `tools/v5-runtime-accept.cs`、`tools/v5-context-accept.cs` 和 `tools/v5-stream-accept.cs`。协议套件的 stdio 服务使用 Python 3 标准库的 `tools/v5-mcp-stdio-fixture.py`。界面套件验证正常入口、真实服务回执和原有工程导航。Windows 安装测试使用临时独立配置、构建产物和旧版二进制夹具进行升级与恢复。

## Windows 原生协议与界面

原生构建的同一 Windows 程序通过 58 项 Agent／MCP 协议验收。12 个一级模块及新增对话框在 100%、125%、150% 下复验共三轮，各 107 项。覆盖 1366×768、1600×900、1920×1080。保留 120 张实际 WinForms 截图，其中包含 Agent 记录、第三方 MCP 发现、原有 TIA MCP 和 DCS 预览。目视复核 150% 下的新增窗口，确认任务步骤和第三方回执可见，长文本区域可滚动。入场菜单、启用状态、注册保存及真实握手均通过。

托管 Windows 设备无法提供 2560×1440、4K、200% 和物理多屏验收。本轮仅补必要入口、结果与关键操作显示。

[Windows 截图及测试日志](https://github.com/zong-technology/DeskBox/actions/runs/45863717693/artifacts/15138794791)

## 本轮功能

- 新增无 WinForms 依赖的桌面 Agent Runtime、任务和步骤元数据、模型文本流能力检测，以及固定公开工程计算的本地执行。会话内容沿用项目隔离的数据文件。旧普通连接测试保留。
- 新增第三方 MCP 的工程级注册、启停、HTTP/stdio、实际 Schema、授权／撤销、人工调用和诊断回执。授权可在服务断开时撤销。继续使用既有 TIA 私有 MCP 入口。
- 分层和数据模型见操作指南。补齐可供后续 UI Agent 独立使用的 14 份交接资料。

[操作与分层](V5-ALPHA2-GUIDE.md) · [功能矩阵](V5-FUNCTION-MATRIX.md) · [界面交接](UI-HANDOFF/PRODUCT-OVERVIEW.md)

## 安装、升级与兼容性

Windows Server 2025 GitHub 托管机完成原生编译和 Inno Setup 打包。独立用户数据目录实际执行安装、运行、升级、重装、回退、再次升级与卸载，共通过 85 项。已安装运行路径及可执行文件 SHA-256 与构建一致。升级与回退使用不同字节的旧版测试程序，验证旧程序与测试数据保留。

既有 TIA 离线回归 186 项、NOVA 原生运行回归 138 项、AI 环境 22 项、AI 应用操作 14 项、移动 AI 13 项通过。工程和语音回归采用授权范围内的测试夹具及本机传输。移动 AI 的结果来自 Windows 中继及桌面运行测试。

| 文件 | SHA-256 |
| --- | --- |
| `DeskBox-5.0.0-alpha.2-Setup.exe` | `1d5f9d3cee160b94b4ce75dd06e1881942025c687adde807f751e5eb1642bdf3` |
| `DeskBox.exe` | `ae0cdb9744257e6ae048823695990fb5c87ca2fd63daff86b319acb757850f03` |

候选安装包未签名，通过 GitHub Actions 留档。安装包和可执行文件的 SHA-256 已与 CI 清单核对。

[候选安装包与校验清单](https://github.com/zong-technology/DeskBox/actions/runs/45863717693/artifacts/15140640651) · [安装日志与屏幕证据](https://github.com/zong-technology/DeskBox/actions/runs/45863717693/artifacts/15140780566) · [原生程序及回归日志](https://github.com/zong-technology/DeskBox/actions/runs/45863717693/artifacts/15140156305)

## 未完成与阻塞

| 范围 | 状态与原因 |
| --- | --- |
| 真实外部模型、供应商费用 | 没有获授权的服务商账号和测试预算。当前核实协议响应字段，支持记录服务商返回的 Tokens。 |
| 第三方 MCP 扩展 | 远端正式服务、OAuth、配置交换、旧版双端点 SSE、新增注册共享及模型直接访问第三方工具待验收或开发。目前通用入口为只读、人工审核调用。 |
| Siemens 和工程目标 | 本轮沿用 TIA 的离线回归。持证 V20、授权 PLC 工程及实机诊断依赖独立环境。DeltaV FHX 结构化与互操作样本、统一 Tag 属于 alpha.3。 |
| 其他交付 | 移动、语音、工作流和 TIA 规划的共用 Agent 调度待后续评估。高级界面和多屏留到功能冻结后。 |

[已知问题](V5-TECHNICAL-DEBT.md) · [分阶段路线图](V5-DEVELOPMENT-ROADMAP.md)
