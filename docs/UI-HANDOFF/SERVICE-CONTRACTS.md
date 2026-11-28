# 业务服务接口

接口为进程内 C# 方法。需要网络、文件索引或子进程的同步接口应放到后台线程。调用方传入当前工程 ID 或已授权的工程上下文。取消来自操作专用 CancellationToken，运行环境变更也会取消网络或第三方工具运行。

| 服务入口 | 输入 | 输出与错误 |
| --- | --- | --- |
| `ChatService.SetProjectContext(ProjectContext)` | 已选中的工程快照 | 选中该工程最近会话。权限或范围变化时取消旧运行，清除会话临时文件和目录授权。 |
| `ChatService.Send(string)`、`Regenerate()`、`Cancel()` | 当前会话消息或活动操作 | 会话通过 Changed、MessageDelta、StatusChanged 反馈。忙时拒绝第二个文本请求。 |
| `AgentRuntime.Run(AgentRunRequest, CancellationToken, …)` | Text、SessionId、Project、Provider、Model、ToolRounds、可选 BuildPrompt / CreateToolContext / ConfirmTool | AgentTask、步骤记录、状态与文本回调。未传工具上下文时仅有直接文本及受限的本地工具。BuildPrompt 会在每轮模型请求前重建。持久化失败向调用方抛出。 |
| `AgentRuntime.ProbeModel(AiProvider, CancellationToken)` | 模型服务商快照与取消令牌 | ModelProbe。以一个短流式请求检测文本增量和终止帧。未检测能力保持未知。 |
| `AgentTaskStore.List(projectId, environment, limit)` | 工程 ID 或空字符串、`network` / `local`、1–100 条 | 最近 AgentTask。读原文件失败时读上一快照，恢复待运行记录为中断。 |
| `McpGateway.Servers(projectId, out recovered)` | 当前范围 | McpServer 列表及恢复标志。 |
| `McpGateway.SaveServer(projectId, draft)` | 草稿，编辑时包含原 Id 和 Revision | 保存后当前记录。冲突、无效输入或活动操作抛出。端点或启动参数改变会停用服务并清空原授权。 |
| `McpGateway.Discover(projectId, serverId, bearer, cancellation)` | 当前范围、已注册服务器、可选令牌、取消 | McpResult。包含回执；成功时附实际 McpCatalog。初始化时协商版本，每页读取实际目录。 |
| `McpGateway.SetToolGrant(projectId, catalog, name, approved)` | 当前工程内实际发现的目录、工具名和决定 | 持久化新权限，返回当前配置。写入或破坏提示拦截授权。 |
| `McpGateway.RevokeTool(projectId, serverId, name)` | 同一工程内的注册及授权名称 | 无需网络即可撤销。已运行操作期间须先取消或等待。 |
| `McpGateway.Invoke(projectId, serverId, name, argumentsJson, operatorApproved, bearer, cancellation)` | 当前范围、指定工具、原样 JSON、人工确认和可选令牌 | McpResult。发送前核对凭据、实时 Schema、服务身份和权限。返回实际工具结果、RPC 错误、超时或取消。 |
| `McpGateway.Receipts(projectId, serverId)` | 当前范围与服务器 ID | 最近 50 条回执。读取失败向调用方抛出。 |
| `TiaOpenness.ProbeMcp(projectContext, cancellation)` | 当前工程和取消 | 本机私有 stdio 的实际协议协商及回执。由 TiaMcpPolicy 管理。 |

[输入命令](COMMANDS.md) · [事件与状态](EVENTS.md) · [数据结构](DATA-MODELS.md)

## alpha.3 读资料、差异及 Tag

| 服务入口 | 输入 | 输出和校验 |
| --- | --- | --- |
| `EngineeringDocuments.Read(project, path, version, token, encoding, worksheet)` | 项目快照，文件，版本，可选编码、工作表 | EngineeringDocument，源哈希、对象、字段、位置及诊断。仅本地当前工程路径。 |
| `EngineeringDocuments.Excerpt(project, source, firstLine, maxLines, token)` | 当前项目、原始来源和行号 | 有界带行号的原文。读取前重新验证哈希。XLSX 返回工作表/行定位给导入流程。 |
| `EngineeringDocuments.Headers(project,path,worksheet,encoding,token)` | 授权工作簿／CSV／TSV | 列标题。可用 `TagCenter.DefaultMapping` 得到默认索引。 |
| `EngineeringDocuments.Compare(project,beforePath,afterPath,token,…)` | 两份授权资料，版本、工作表、映射 | EngineeringDiff。保留源哈希、差异计数和字段位置。阻塞诊断时返回诊断状态。 |
| `EngineeringDocuments.ExportDiff(project,diff,destination,token)` | 当前项目的差异对象与新 JSON 路径 | 仅创建新文件。 |
| `TagCenter.Preview(project,path,version,defaultSystem,token,mapping,worksheet,encoding)` | 授权来源，零基列映射，默认系统 | TagImportPreview、校验诊断、字段冲突与来源。 |
| `TagCenter.Commit(project, preview, replaceExistingFields, token)` | 原预览、合并决定 | TagImportResult 和各 TagId。核对工程、源哈希、台账版本，单次原子写入。 |
| `TagCenter.Query / Save / History / CompareRevisions / Link` | 项目、查询/筛选/排序、TagId、预期版本或关联 ID | 查询分页、版本和关联。写入检查并发。 |
| `TagCenter.Export / Restore` | 项目、新 CSV 或 XLSX 路径，或明确恢复请求 | 导出或恢复完成。恢复保留损坏原件。 |
| `TiaOpenness.Run / PlcEngineering.ReadExports` | 已有项目、已有授权任务或导出物、CancellationToken | 沿用真实 TIA 及离线服务。结构数据层复用同一 PLC 导出解析器。 |
| `McpGateway.SetToolGrant(..., allowAgent) / AgentTools / InvokeForAgent` | 项目、真实工具目录、工具身份、JSON、单次审批 | 已授权工具目录、验证错误、真实 MCP 响应和执行回执。每次重新发现。 |

## alpha.4 验证接口

所有业务接口接收当前项目的 `ProjectContext` 或绑定该上下文的快照，耗时操作接收 `CancellationToken`。表单刷新前核对项目与快照版本。

| 服务 | 职责 |
| --- | --- |
| `ValidationService.Read / Save / Link / WithdrawLink` | 读取项目记录、校验当前修订、保存实体，以及按项目稳定 ID 建立或撤回有效关系。 |
| `ValidationService.LinkTask / LinkIncident` | 为 Punch Item 关联当前项目的已有任务和现场事件。调用方从相应模块选择现有记录。 |
| `ValidationService.CaptureSource(project, path, location, version, token, type)` | 从工程授权路径获取 SHA-256、相对路径、版本和位置。 |
| `ValidationService.RecordExecution` | 记录人工测试执行或复验，验证用例及步骤版本，保留旧执行。 |
| `ValidationService.ArchiveEvidence / VerifyEvidence / EvidencePath / ExportEvidence / RestoreEvidence` | 归档原件或衍生件，通过 SHA-256 与大小复核证据，定位或导出当前项目归档；只允许用大小和哈希一致的项目文件恢复异常证据。 |
| `ValidationService.Review / CloseDeviation / AssessImpact` | 根据验证计划中的审核人执行验证工作流，处理偏差、依据明确的审核版本关闭，以及授权人员评估变更重测决定。 |
| `ValidationService.Restore` | 从可验证的上一版台账执行明确恢复。 |
| `ValidationTrace.Related(project, snapshot, id, token, service)` | 按记录 ID 或 TagId 读取同项目有效入边和出边。 |
| `ValidationTrace.Query(project, snapshot, filter, token, service)` | 从同一项目的快照计算 RTM。可传服务核对证据实际文件。返回当前执行及证据、各次执行的关联偏差和按记录 ID 可回查的诊断。 |
| `ValidationTrace.Statistics(project, snapshot, filter, token, service)` | 计算实际统计，分别返回指标、分子、可空分母和可空比率。 |
| `ValidationTrace.RecordImpact(service, project, diff, title, actor, token)` | 结合 alpha.3 的 `EngineeringDiff` 及可核对来源，为受影响记录保存待评估清单。 |
| `ValidationTrace.CaptureBaseline(service, project, title, actor, token)` | 固定记录修订、Tag 版本、来源、关联和作者。 |
| `ValidationTrace.CompareBaselines(project, before, after, token)` | 提供两个已保存基线间按实体 ID 的变化。 |
| `ValidationImport.Headers / DefaultMapping` | 读取 CSV、单工作表 XLSX 或 DOCX 候选列，生成按列号的默认映射。 |
| `ValidationImport.Preview(project, path, options, token)` | 选阶段、逻辑文档号及其版本、导入类型和列映射。返回新增、修改、已移除、未变及阻塞诊断。 |
| `ValidationImport.Commit(project, preview, applyRevision, token, retireMissing)` | 人工确认后按原源哈希、项目和台账版本提交。仅当完整的需求表格修订获明确确认时支持作废缺失项。 |
| `ValidationImport.CompareRequirement / Locate` | 按需求稳定 ID 比较两个正式历史修订，或按绑定的来源哈希定位原表格行、Word 段落或表格行。 |
| `ValidationReports.Export(project, kind, destination, token, templatePath, templateMapping, stage)` | 从真实记录生成 XLSX 或 DOCX，带来源、版本、状态和草稿标记。可选无宏 DOCX 模板及正文占位映射。 |
| `validation.query` | 确定性验证查询，仅供本机回环模型或显式本地命令。读当前项目，不提供通过、关闭、审核或附件修改能力。 |

[状态和错误](ERROR-STATES.md) · [统计口径](../V5-VALIDATION-METRICS.md)
