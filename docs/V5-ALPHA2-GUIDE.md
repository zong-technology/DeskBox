# DeskBox 5.0.0-alpha.2 · AI、MCP 与工程架构

## 操作

在「AI 工程中心 → 工程会话」选项目和模型后发送。等待期间可停止；会话左侧的「执行记录」显示步骤、真实工具结果状态、成功、部分完成、失败或取消。记录按工程和 AI 环境保存。系统设置保留普通连接测试，另提供「检测文本流」，以一条固定短消息检查文本流及结束帧。

本地文件名检索支持 `本地查找 关键词`。无可用模型时，可直接输入文件名关键词。确定性工程计算可在任意模型配置下显式运行，例如 `工具 {"name":"eng.scale_4_20","args":{"mA":12,"low":0,"high":100}}`。

第三方服务从「AI 工程中心 → MCP 管理 → 第三方 MCP」打开。注册后须启用，再读取服务真实工具、输入与输出 Schema。授权只读工具后，在参数框输入 JSON 对象并确认本次调用。服务断开时仍可撤销已保存的权限。已发送的取消或断线在回执里标记，需要核对服务端情况。现有 TIA MCP 策略和探测入口可继续独立使用。

## 分层

| 层 | 源码入口 | 责任 |
| --- | --- | --- |
| Presentation | `src/UI` | WinForms 输入、展示、状态、错误及人工确认。 |
| Application | `src/Application/AgentRuntime.cs`、`src/Application/McpGateway.cs`、既有 `ChatService` | 执行生命周期、权限、工具轮次、项目上下文和返回结果。 |
| Domain | `src/Domain/AgentTask.cs`、`src/Domain/Mcp.cs`、既有 `ProjectContext` | 任务、步骤、服务、权限、Schema、回执与工程边界。 |
| Infrastructure | `src/Core`、`src/Fs` | 存储路径、原子文件操作、索引、备份、取消后的资源释放。 |
| Integration | `src/Integration/McpProtocol.cs`、`McpWire.cs`、既有 `AiClient`、`Engineering/Tia` | JSON-RPC、HTTP/SSE、stdio、模型协议和 Siemens 适配。 |
| Data | `src/Data/AgentTaskStore.cs`、`McpStore.cs`、既有 ProviderStore / WorkspaceStore | 版本化数据、项目分区、备份恢复、配置冲突。 |

本轮保留 WinForms、C# 5 和 .NET Framework 4.8。任务、协议与持久化服务独立于 WinForms 运行。桌面应用已有的任务、事件和文件工具从既有 ToolRegistry 复用。需要在应用主线程更新旧工作区的工具通过回调桥接。移动、声音、工作流和 TIA 规划保留各自的业务入口，并继续复用现有权限边界与传输客户端。

## 持久化与版本切换

升级沿用 alpha.1 的用户数据路径和会话配置。新增 `agent-tasks/` 和 `mcp-gateway/`，在首次运行相应功能时创建。运行时保留原子保存的上一版本。读出未结束任务或回执时标记中断，恢复后由操作者决定是否再次发起。

原有备份包含这些数据目录。配置备份恢复后，MCP 服务停用并清除旧授权。Agent 元数据与原会话正文分别保存；MCP 调用参数、原始返回和 HTTP Bearer 不进入调用记录。应用记录用于诊断。

同路径安装继续保留一个可回滚版本。操作前退出托盘 DeskBox 并备份数据。回滚命令沿用 `tools/rollback-install.ps1 -InstallDir "<DeskBox 安装目录>"`。alpha.1 可读取原项目与会话，alpha.2 的任务及 MCP 目录仍在数据路径中，可在重新升级后使用。

[服务接口](UI-HANDOFF/SERVICE-CONTRACTS.md) · [数据模型](UI-HANDOFF/DATA-MODELS.md) · [交接索引](UI-HANDOFF/PRODUCT-OVERVIEW.md) · [第一阶段备份与回退](V5-P0-GUIDE.md)
