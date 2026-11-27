# 业务数据模型

| 数据 | 关键字段 | 关联与持久化 |
| --- | --- | --- |
| ProjectContext | Id、Name、工程路径、工程版本、当前任务、外部模型权限 | `WorkspaceStore` 生成快照。子项目和重解析路径受项目边界校验。 |
| ChatSession | Id、ProjectId、ProviderId、Model、Messages | 复用 alpha.1 会话文件，项目会话与全局历史、网络与本地分别保存。 |
| AgentTask | Version、Id、ProjectId、SessionId、Environment、ProviderId、Model、Mode、State、ErrorKind、起止时间、Steps | `agent-tasks/<环境>/<项目目录键>/<任务 ID>.json`。目录键为项目 ID 哈希或 unbound。每个记录原子替换并保留 .bak。 |
| AgentStep | Kind、Name、State、ErrorKind、起始时间、ElapsedMs、HttpStatus、Attempts、OutputCharacters、InputTokens、OutputTokens、TerminalFrame、StreamedText | 分步归属 AgentTask；可用 Tokens 来自模型响应，未知为 null。 |
| ModelProbe | Model、Protocol、Ok、StreamedText、TerminalFrame、状态码、耗时、错误类型、Tokens | 设置界面即时结果。既有服务商持久化连接状态。 |
| McpServer / McpGrant | Id、ProjectId、Name、Transport、Address、Arguments、Enabled、TimeoutMs、Revision、Grants | `mcp-gateway/servers.json`，事务式原子替换。授权指纹包含服务名、服务版本、协商版本和工具说明／Schema／提示。 |
| McpCatalog / McpTool | ServerId、ProjectId、服务标识、协议版本、工具名、描述、两个 Schema、Annotations | 来自实时协议的内存目录。工具权限以再次发现的定义为依据。 |
| McpReceipt / McpResult | 项目、服务、操作、工具、回执 ID、状态、耗时、协议版本、HTTP/RPC 码、工具数、输出长度、是否尝试发送 | `mcp-gateway/receipts/<服务 ID>/<回执 ID>.json`。最近 250 条服务回执保留。原始结果和参数随内存返回，Bearer 在管理窗口内提供。 |

应用备份可复制新增目录和 JSON 数据。Agent 执行任务在重开后以中断状态显示，避免自动再次执行。MCP 注册从备份加载时停用服务并清空旧授权。日志仅由实际操作生成，作工程诊断和追溯用途。

[状态定义](UI-STATES.md) · [数据边界与兼容性](DESIGN-CONSTRAINTS.md)

## alpha.3 数据

| 模型 | 关键字段 |
| --- | --- |
| EngineeringSource | Type、项目相对 Document、原文件 Sha256、Version、Encoding、Bytes。 |
| EngineeringDocument / EngineeringRecord | Source、Records、Issues。对象 Key、Kind、Name、Parent、Fields、Recognized、StartLine / EndLine、原文 Location。 |
| EngineeringDiff / EngineeringDelta | 项目、两个来源和哈希、计数、诊断及单字段前后值、前后定位。 |
| EngineeringTag | TagId、ProjectId、TagName、SystemId、EquipmentId、Description、DataType、SignalType、EngineeringUnit、RangeLow/RangeHigh、Address、来源、时间和 Revision。 |
| TagSourceLink / TagRelation | 来源类型、原文、版本、哈希和编码。Relation 预留 instrument、requirement、test、incident、document 的稳定 ID。 |
| TagImportPreview / TagImportResult | 带锁定源和台账版本的候选行、冲突/错误、写入结果统计和 TagIds。 |
| McpGrant / AgentStep | 增加明确授权的 AllowAgent 与批准时实际 Schema。AgentStep.ReceiptId 关联执行时的网关回执。 |

台账 `engineering-tags/` 分项目存储，带校验和、版本、原子写入和上一版。备份只读、修复明确确认。旧会话、项目文件、TIA 和 MCP 手动配置沿用。
