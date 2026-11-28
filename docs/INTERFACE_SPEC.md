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
