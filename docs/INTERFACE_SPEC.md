# DeskBox 核心接口

本页用于客户端接入和功能交接。下列方法是桌面应用可复用的 C# 服务，代理工具使用列出的名称和参数。历史设计见各阶段文档。

## 公共契约

- 工程业务接受当前 `ProjectContext`，以稳定的 ProjectId 核验项目范围。实体 ID 与业务编号分开。
- 只读请求可在后台运行并响应 `CancellationToken`。保存当前记录传入 `expectedRevision`，冲突后应重新读取。导入预览绑定来源文件哈希、项目和台账版本。
- 可取消、数据缺失、授权拒绝、修订冲突与损坏恢复在界面分别显示。项目切换后丢弃晚到响应。
- 模型、回执、证据与报告引用实际数据。任何写入、恢复及审核均应通过相应服务。

## 工程与验证

| 服务 | 调用边界与返回 |
| --- | --- |
| `EngineeringDocuments.Read / Excerpt / Compare / ExportDiff` | 项目、来源文件、版本和取消。返回结构化记录、原文、确定性差异或报告。以文件哈希核对来源定位。 |
| `TagCenter.Query / Save / History / CompareRevisions` | 项目、筛选、稳定 TagId 或预期修订。返回分页、台账记录或版本差异。 |
| `TagCenter.Preview / Commit / Export / Restore` | 映射列、确认模式、项目及来源。先预览，再原子提交或导出。恢复前复核备份。 |
| `ValidationService.Read / Save / Link / WithdrawLink` | 项目、实体、预期修订或双方 ID。校验实体类型和项目后保存。 |
| `ValidationService.RecordExecution / Review / CloseDeviation / AssessImpact` | 记录执行步骤、人工结果、对应修订和意见。审核、关闭及影响评估还校验当前账户权限。 |
| `ValidationService.ArchiveEvidence / VerifyEvidence / ExportEvidence / RestoreEvidence` | 归档或核对项目文件，返回证据状态。恢复要求大小和 SHA‑256 均与记录相符。 |
| `ValidationImport.Preview / Commit / CompareRequirement / Locate` | 先定位列、段落或行并预览，确认后提交。可按稳定 ID 查询正式历史。 |
| `ValidationTrace.Query / Related / Statistics` | 接收项目快照及过滤条件，返回双向关系、RTM、覆盖缺口、计数和可空比率。传入服务以核对文件完整性。 |
| `ValidationTrace.RecordImpact / CaptureBaseline / CompareBaselines` | 用实际工程差异、稳定 ID、哈希与版本记录待评估影响和固定基线。 |
| `ValidationReports.Export` | 项目、报告种类、目标和可选模板映射。生成 XLSX 或待审核 DOCX。仅创建新报告文件。 |

[业务接口、参数和错误](UI-HANDOFF/SERVICE-CONTRACTS.md) · [领域模型](UI-HANDOFF/DATA-MODELS.md) · [统计定义](V5-VALIDATION-METRICS.md)

## AI 与集成

| 接口 | 调用边界 |
| --- | --- |
| `AgentRuntime.Run / ProbeModel` | 控制具有当前项目、提供方、取消和工具授权的请求。返回任务、真实流式反馈和工具状态。 |
| `McpGateway.Discover / Invoke / InvokeForAgent` | 先注册和实际发现，再按项目授权调用。Agent 路径检查 Schema、实时授权和人工确认。 |
| `validation.query` | 只读当前项目的需求、执行、RTM、证据、偏差、历史、影响与交付资料。限本机模型或显式本地调用。 |
| `engineering.compare / engineering.tags` | 读取获授权工程资料或查询当前项目 Tag。使用确定性计算，返回来源及定位。 |
| `TiaOpenness.Run / PlcEngineering.ReadExports` | 区分 TIA 真实作业回执与本机导出文件分析。实际设备、用途和工具需匹配作业授权。 |

[命令参数](UI-HANDOFF/COMMANDS.md) · [事件](UI-HANDOFF/EVENTS.md) · [错误](UI-HANDOFF/ERROR-STATES.md)

## ECO × DeskBox 统一通信协议 V1.0 草案

**待双方确认。** 仓库内候选版本为 `1.0.0-draft.1`。ECO 负责理解需求、记忆、决策与任务调度。DeskBox 负责专业工作、工程辅助及受授权的实际执行。双方独立开发、部署、运行和升级，只通过版本化接口交换数据，各自管理存储与内部类。接口按调用方和服务方建模，双方可在各自授权范围内发起请求或发送事件，同一契约可供未来第三方应用和硬件终端接入。 [机器可读 Schema](../protocol/eco-deskbox/contract.schema.json) · [合成消息示例](../protocol/eco-deskbox/examples.json)

### 消息与能力

| 消息 | 草案字段与含义 |
| --- | --- |
| 请求 | `protocolVersion`、唯一 `requestId`、经认证的 `callerId`、UTC `timestamp`、`operation`、`params`，可选服务端受理截止时间 `deadline`。 |
| 响应 | 回显协议版本、请求 ID 与操作名，附 `serviceId`、时间戳和 `result` 或 `error`。错误含稳定 `code`、可展示的 `message`、`retryable`，可选 `retryAfterMs`。 |
| 任务 | 包含服务端分配的 `taskId`、`projectId`、`capabilityId` 和能力版本、任务状态、递增 `revision`、更新时间，可附进度和取消请求标记。只有实际成功的终态包含执行结果，失败终态记录错误。 |
| 事件 | 唯一 `eventId`、发布服务、订阅 ID、订阅内序号、时间戳、事件类型和最小任务状态。执行结果通过独立授权的查询读取。 |

- `service.discover` 返回服务支持的协议版本，`service.health` 返回是否就绪。首次定位及版本协商入口由双方确定，发现响应只发布最低必要信息。
- `capabilities.register` 由具备管理权限的主体登记能力元数据及版本化 Schema 引用。`capabilities.list` 返回调用者在指定项目可见的能力。处理函数在各自应用中配置，注册消息无权指定执行代码或启动进程。
- `data.query` 读取能力授权范围内的数据。调用方提供项目、能力 ID、能力版本及参数。能力输入与输出须另有 Schema。Schema 引用解析到已审批的版本化目录。 [合成能力 Schema](../protocol/eco-deskbox/example-capability.schema.json)
- `tasks.submit` 创建并安排执行，受理回执为 `pending`，使用 `tasks.status` 查询实际进度、结果或错误。通过 `tasks.cancel` 请求取消。DeskBox 的执行任务与界面内已有待办是独立概念。
- `events.subscribe` 注册经授权的项目订阅，`events.poll` 使用 `afterSequence` 补取，`events.unsubscribe` 关闭。建议支持至少一次交付，按 `eventId` 去重并按订阅内序号顺序消费。游标过期返回 `cursor_expired`，重新订阅并查询当前状态。

### 状态、可靠性与错误

| 当前状态 | 允许转换 |
| --- | --- |
| `pending` | `running`、`awaiting_approval`、`failed`、`cancelled` |
| `running` | `awaiting_approval`、`succeeded`、`failed`、`cancelled` |
| `awaiting_approval` | `pending`、`failed`、`cancelled` |
| `succeeded`、`failed`、`cancelled` | 终态，无后续状态转换。 |

状态未变时可递增修订号记录进度和取消请求。授权由执行方按其可信流程处理，获准后回到 `pending` 重新调度。取消执行中的任务先记录请求，等实际停止后才进入 `cancelled`。如果动作已经完成，应返回真实终态。已受理、已排队或仅完成需求分析的任务继续报告实际所处阶段。

同一逻辑写请求重试复用请求 ID，执行方按经认证主体、操作及请求 ID 去重。先复核权限，参数相同重用原受理回执，冲突参数返回 `request_conflict`。截止时间约束首次受理，过期的未受理请求返回 `deadline_exceeded`，已经受理的请求仍可重取其原回执。连接超时后用同一 ID 重试受理或以任务 ID 查询。仅在可重试且不会重复副作用时退避重试。重启后需要还原任务、审批与去重记录并核验执行证据。执行结果无法确认时记录 `outcome_unknown`，交由人工复核，禁止自动重放可能已产生副作用的动作。

其他公共错误包括 `invalid_request`、`unsupported_version`、`unauthenticated`、`access_denied`、`not_found`、`capability_unavailable`、`approval_denied` 和 `temporarily_unavailable`。错误处理通过稳定代码区分，不依赖展示文字或内部异常类型。

### 安全边界

DeskBox 独立认证请求主体并比对 `callerId`，对能力、项目、动作及可传输字段逐项授权，注册、订阅、结果读取及取消同样校验。等待审批或重新执行时检查权限是否已撤销。敏感动作沿用人工审批与文件备份。工业生产系统默认只读，禁止未授权的远程或生产控制。审计应关联主体、项目、能力、请求及任务 ID、权限决定、结果和时间，避免写入凭证、敏感内容和原始工程路径。保持 DeskBox 无网络或 ECO 不在线时的独立工作和人工操作路径。

### 传输选型、共享与发布

DeskBox 原生端使用 C#／.NET Framework 4.8。现有移动同步是 TLS 上的 HTTP/JSON，MCP Gateway 使用 HTTP 或 stdio，TIA 的 stdio 服务范围限定于本地预置作业。候选传输优先评估 HTTP/JSON 和 OpenAPI 3.1，先比较带游标轮询与 SSE 通知，按双方实际框架、重连恢复能力、证书部署和硬件要求选择。JSON Schema 2020-12 草案只定义消息，路径、认证机制、状态码映射和事件传输待双方确认。

双方确认后应引用同一版本化规范来源。条件成熟时可迁入独立的 `ECO-DeskBox-Protocol` 仓库，通过版本标签或提交固定依赖。变更同时提交版本、文档、Schema、样例、契约测试和兼容说明。先协商共同支持的版本，再使用该版字段。兼容新增由版本协商启用，破坏性变更通过新主版本并行支持和迁移。当前草案验证器严格使用单一候选版本。 [测试和复跑方法](#草案契约测试)

### 草案契约测试

使用 Python 3.11 或以上及以下命令。测试校验消息 Schema、合成示例、状态转换以及进程内模拟服务的任务提交、取消、幂等和授权边界。模拟服务使用合成数据与注入的测试身份。真实认证、事件传输、跨进程持久恢复和双方兼容需在后续适配器验证。

```sh
python -m pip install -r protocol/eco-deskbox/test-requirements.txt
python -B -m unittest discover -s test -p eco_deskbox_contract_test.py -v
```

[模拟服务与测试](../test/eco_deskbox_contract_test.py)

## ECO 接入准备评估

基于 `codex/deskbox-v5-p0` 的 5.0.0-alpha.4 源码。项目、文件、工程资料及部分执行已具备进程内复用基础。当前缺少面向 ECO 的统一接入层、公开契约及联调结果。下列内部方法、工具名和已有移动协议按各自现有范围使用。 [项目规范](DESKBOX_SPEC.md) · [当前架构](ARCHITECTURE.md)

| 所需能力 | 现有可复用入口 | ECO 侧缺口 |
| --- | --- | --- |
| 服务发现与连接状态 | `LocalSyncDiscovery` 提供局域网手机配对发现。通用 `McpGateway.Discover` 由 DeskBox 主动发现已配置的第三方工具。TIA 另有绑定本地预置作业的 stdio 服务。 [手机发现](../src/Sync/LocalSyncDiscovery.cs) · [MCP Gateway](../src/Application/McpGateway.cs) · [TIA 作业](../src/Engineering/Tia/TiaMcp.cs) | 缺少供 ECO 使用的可选服务入口、服务身份、就绪状态和版本协商。 |
| 能力注册与查询 | `ToolRegistry` 提供进程内白名单及 `app.context` 工具列表。MCP Gateway 管理第三方服务、发现目录及 Schema 授权。 [工具注册表](../src/Ai/ToolRegistry.cs) · [应用工具](../src/Ai/ApplicationTools.cs) · [MCP Gateway](../src/Application/McpGateway.cs) | 缺少按外部调用者与项目过滤的能力目录，以及公开、带版本的输入输出契约。部分内部工具依赖 UI 确认或调度回调，需逐项确认可开放。 |
| 项目及文件查询 | `WorkspaceStore.Items / Context`、`ProjectContext.CanAccessPath`、`FileIndex.Search` 及内部 `project.list`、`fs.search`、`fs.read`。工程文档服务可读取有来源定位的项目资料。 [项目范围](../src/Engineering/ProjectContext.cs) · [文件索引](../src/Fs/FileIndex.cs) · [工程文档](../src/Application/EngineeringDocuments.cs) | 需要按显式项目 ID 和授权解析上下文，避免改变人工界面的当前项目。需要精简的分页结果、文件引用及访问复核，原始台账、绝对路径和工程正文按权限处理。 |
| 任务创建、执行与取消 | `task.create / task.list / task.update` 管理本地待办 `TaskItem`。执行记录由 `AgentRuntime.Run`、`WorkflowEngine.RunV2 / Cancel` 处理。Agent 接收取消令牌。 [应用工具](../src/Ai/ApplicationTools.cs) · [任务创建](../src/Ai/ToolRegistry.cs) · [Agent Runtime](../src/Application/AgentRuntime.cs) · [工作流](../src/Automation/WorkflowEngine.cs) | 缺少统一的外部运行提交和按运行 ID 的权限、幂等及取消契约。待办和执行分别建模，取消执行应以实际终态回执为准。 |
| 进度查询与结果反馈 | Agent 通过回调传递状态、文本和工具响应。按项目与 AI 环境保存任务和步骤摘要，可使用 `AgentTaskStore.List` 查看。MCP 返回即时结果和独立回执，工作流记录运行结果。 [任务记录](../src/Data/AgentTaskStore.cs) · [MCP 回执](../src/Domain/Mcp.cs) · [工作流记录](../src/Automation/WorkflowEngine.cs) | 需要按调用者、项目和运行 ID 查询进度与授权结果的接口，同时定义已受理、执行中、取消请求、终态和重试语义。现有摘要和即时回调不能提供完整可重取的外部结果。 |
| 事件通知 | `WorkspaceStore.Changed`、`ChatService` 的状态与文本事件、`WorkflowEngine.RunCompleted` 都是进程内机制。手机云中继轮询独立的移动作业协议。 [应用事件](UI-HANDOFF/EVENTS.md) · [移动云中继](../src/Sync/CloudSyncAgent.cs) | 缺少按项目和能力授权的外部事件、版本、序号及断线重取约定。可先用状态轮询，按需要增加事件订阅。 |
| 权限校验及审计 | 现有项目和文件范围校验、AI 环境与项目外发授权、MCP 工具授权及回执，以及面向手机同步的设备信任。 [项目权限](../src/Engineering/ProjectContext.cs) · [AI 环境](../src/Ai/AiEnvironment.cs) · [MCP 权限](../src/Application/McpGateway.cs) · [设备配对凭证](../src/Sync/DeviceTrustStore.cs) | 缺少 ECO 调用者身份和权限授予及撤销、每次调用的项目与能力复核、字段级出域策略，以及串联请求、决策和结果的统一审计记录。现有手机设备凭证按手机同步场景管理。 |

### 接入顺序与验收

1. **先验证草案及授权只读用例。** 以进程内模拟服务验证消息及状态规则。双方确认传输和最小用例后，在现有应用服务之上增加独立且默认关闭的适配入口。优先验证服务身份、版本、能力查询、已授权项目列表和文件元数据，保持桌面当前项目及各自存储独立。当前工程功能优先级按既有版本路线执行。 [开发路线](ROADMAP.md)
2. **按需求增加任务与运行接口。** 始终区分待办创建和运行提交。发布时定义能力 ID、版本、参数和结果 Schema、权限、分页、错误及兼容范围。受理返回可追踪的请求和运行 ID。写入及执行应有幂等策略，长任务提供查询和取消。以真实结果给出终态，保留项目归属和必要的结果引用。
3. **按需加入通知及联调。** 明确通知的数据范围、顺序、去重和断线后的恢复。每次调用校验权限，有风险的变更沿用 DeskBox 确认与备份。审计保存调用者、项目、能力、请求或运行 ID、时间及结果，默认排除令牌、私有路径和工程正文。双方各自达到独立正式版验收标准后，在授权环境开展真实联合测试。
4. **用模拟客户端覆盖契约。** 使用临时数据目录与合成工程文件调用实际服务，覆盖无 ECO 配置或断线时人工使用、未授权和权限撤销、跨项目拒绝、重复提交、取消和终态、事件重取、数据最小化及旧版本兼容。外部契约实现后再执行端到端协议测试。已有无窗口验收可复用项目／文件隔离、Agent 取消与 MCP 回执的断言。 [工程上下文测试](../tools/v5-context-accept.cs) · [Agent 与 MCP 契约测试](../tools/v5-runtime-accept.cs)
