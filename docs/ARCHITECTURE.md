# DeskBox 当前架构

以 5.0.0-alpha.4 为当前基线。Windows 候选的受测源码为 `5f9c95c5f5c256082797325fa6f146ad2452bf9a`。各阶段的构建、文档提交、安装哈希及验收证据独立留档。 [基线与产物](V5-BUILD-PROVENANCE.md) · [alpha.4 验收](V5-ALPHA4-VERIFICATION.md) · [Windows 构建](https://github.com/zong-technology/DeskBox/actions/runs/46036702085)

## 职责与依赖

| 层 | 代码入口 | 职责 |
| --- | --- | --- |
| 表现 | `src/UI/` | WinForms 输入、显示、人工选择与操作确认。绑定当前项目并传递取消令牌。 |
| 应用 | `src/Application/` | 验证、追溯、统计、证据、工程资料、Tag、AI 调度和 MCP 权限。核心服务可独立于窗口测试。 |
| 领域 | `src/Domain/`、`src/Engineering/ProjectContext.cs` | 项目范围、稳定 ID、修订、原文来源、执行记录、证据、关联和审核。 |
| 数据 | `src/Data/`、`src/Core/` | 项目隔离、并发校验、原子写入、历史和恢复。既有项目、历史进度、会话和自动化继续使用现有位置。 |
| 集成 | `src/Integration/`、`src/Engineering/Tia/`、`src/Ai/` | 授权工程文件、MCP 与模型协议、本机 TIA 服务、离线 PLC/FHX 解析及报告格式。 |
| 基础能力 | `src/Fs/`、`src/Native/`、`src/Sync/`、`src/Automation/` | 文件索引和访问、Windows 系统接入、既有同步及工作流。 |

当前桌面基于 WinForms 和 .NET Framework 4.8。新增能力沿用现有客户端配置、应用数据位置和项目选择入口。设计、追溯与存储规则写入服务，表现层迭代复用这些接口。仓库规则要求先完成可操作流程与证据，beta 阶段再统一重构视觉和布局。具体任务如有明确 UX 要求，在该范围内实施并记录。 [仓库规则](../AGENTS.md) · [接口](INTERFACE_SPEC.md) · [UI 交接](UI-HANDOFF/PRODUCT-OVERVIEW.md) · [路线图](V5-DEVELOPMENT-ROADMAP.md)

## 工程范围与数据

- `ProjectContext` 随请求捕获，服务校验记录和文件的项目归属。归档、导入、导出和比较都在项目授权范围内处理。异步完工后先复核项目范围，再显示结果。
- 需求、测试、证据、偏差与工程 Tag 通过稳定 ID 相连。业务号可供查询，正式修订和来源 SHA‑256 用于追踪。测试执行固定用例和步骤的修订，重新测试追加独立记录。
- 新验证数据按项目写入 `validation/`。元数据记录 Schema、校验和、业务修订及上个快照。证据字节独立保存，衍生件有自己的 ID。落盘和恢复按项目锁保护，证据通过校验后才发布引用它的快照。
- 人工审核绑定当前授权账户及目标版本。偏差关闭保留实际批准记录 ID。业务修订、运行日志和摘要各有用途，正式电子签名与受监管审计追踪需要部署验证。
- 统计、RTM、基线和变更影响从台账及实际工程差异计算。历史 ValidationProgress 继续显示阶段计划。AI 协助查阅和生成待审核文字，工程统计由确定性服务计算。

[验证指南](V5-ALPHA4-GUIDE.md) · [统计口径](V5-VALIDATION-METRICS.md) · [工程资料指南](V5-ALPHA3-GUIDE.md)

## 外部执行与回归

DeskBox 的人工入口与核心业务在本机运行。未来 ECO 通过可选适配入口调用已有服务。适配入口负责调用者认证、项目权限、输入输出筛选和外部回执。应用服务负责业务规则、本地台账及人工审批。ECO 缺席或连接中断时，DeskBox 的本地工作照常进行。 [项目规范](DESKBOX_SPEC.md) · [ECO 接入评估](INTERFACE_SPEC.md#eco-接入准备评估)

本机 TIA 工程集成继续区分离线分析、需授权的只读入口和已有隔离副本审批。托管 Windows CI 使用隔离资料完成原生构建与候选验收，结果只覆盖该环境。私有工程、本机使用、iOS 真机和真实授权工业设备各需对应测试证据。MCP Agent 调用限定已获许可的项目内本机只读服务，按 Schema 校验并请求单次人工确认。验证查询限本机回环模型或显式本地命令，包含验证结果的会话保持本机。 [TIA 架构](TIA-V43-MCP.md) · [MCP 及 AI](V5-ALPHA2-GUIDE.md) · [当前技术债](V5-TECHNICAL-DEBT.md)

旧架构文档保留各自的版本背景。 [V2 架构](ARCHITECTURE-V2.md)
