# DeskBox V5 功能矩阵

当前增量见 alpha.4 表，alpha.3 与 alpha.2 保留各阶段历史状态。历史基线与 alpha.1 条目保留供追溯。VERIFIED 表示在列出的环境经验证，IMPLEMENTED 表示已经接通，PARTIAL 表示覆盖不完整，BLOCKED 表示受测试环境限制。MISSING、UI_ONLY、MOCK、BROKEN 用于记录缺少实现、仅有界面、回归夹具或缺陷。

## V5.0-alpha.4 制药验证与工程追溯

| 功能 | 状态 | 交付与验收范围 |
| --- | --- | --- |
| 计划、阶段、验证文档与交付资料 | IMPLEMENTED | 独立验证计划和记录，保留原有阶段计划进度。实际统计由业务数据产生。 |
| URS、FS、DS、需求版本与导入 | VERIFIED | Windows 业务验收覆盖手工需求、修订、来源、CSV/XLSX 字段映射、DOCX 候选提取及人工确认。 |
| RTM 与统一 Tag | VERIFIED | Windows 业务验收覆盖稳定 ID、双向追溯、关联诊断、统计和 XLSX。RTM 按当前修订显示执行，汇总各次执行的偏差。 |
| 测试方案、用例、人工执行及复验 | VERIFIED | Windows 验收覆盖版本锁定和执行历史，状态与审核分别记录。 |
| 证据、偏差与人工审核 | VERIFIED | Windows 验收覆盖证据归档、哈希、衍生件、复验及审批。单证据原始字节修复另有 Linux 专项结果。 |
| 风险评估 | PARTIAL | 项目风险规则说明、控制、剩余风险及审核具备实际台账。可配置自动评分和风险流程扩展待后续。 |
| CAPA、Punch List 与合规辅助 | PARTIAL | 按项目记录、关系和审核。工作流集成及需正式验证的受监管能力留待后续。 |
| 变更影响与验证基线 | VERIFIED | 业务套件覆盖候选影响、稳定 ID、证据引用和版本冻结。WinForms 双文件入口使用默认映射。 |
| 验证报告与模板 | VERIFIED | Windows 业务验收产生 XLSX、DOCX 及 DOCX 企业模板草稿，独立解析库可重新打开。 |
| 受控 AI 验证助手 | VERIFIED | 回环协议核对当前项目只读查询和审核拒绝。商业外部模型验收单独记录。 |
| 持证 TIA 与授权 DeltaV 样本 | BLOCKED | 维持 alpha.3 对授权工程样本和外部环境的验收边界。 |

[验证指南](V5-ALPHA4-GUIDE.md) · [指标](V5-VALIDATION-METRICS.md) · [验收](V5-ALPHA4-VERIFICATION.md) · [交接](UI-HANDOFF/PRODUCT-OVERVIEW.md)

## V5.0-alpha.3 功能增量

| 功能 | 状态 | 交付与验收范围 |
| --- | --- | --- |
| alpha.2 证据及交接核对 | VERIFIED | 受测源码 `3047cb7`、文档 `9a6e77a`、14 份交接、试验范围和双文件 SHA-256 已核对。 |
| Agent 资料比较与 Tag 查询 | VERIFIED | 真实回环模型协议选择工程工具。结果包括真实差异、文件哈希、行号与稳定 Tag ID。外部商业模型的实际效果待授权测试。 |
| Agent 与第三方 MCP 联动 | VERIFIED | 默认停用，项目级授权、只读封闭能力、参数 Schema、逐次人工确认、实时协商、跨项目、撤销和断流已测试。模型介导的调用限于本机模型和回环 HTTP 工具。 |
| PLC 离线工程资料 | VERIFIED | 复用 SCL、XML、AML、CSV 分析和 TIA 工作台。增加来源定位、统一 Tag、字段差异和离线诊断回执查阅。 |
| TIA Openness 实机 | BLOCKED | 现有实例、版本、工程和 PLC 对象只读路径继续保留。真实连接、授权项目及 Siemens 实际编译待持证隔离环境。 |
| DeltaV FHX | PARTIAL | 结构、字段、未知对象和两版差异有真实文件与离线语法夹具测试。品牌版本和生产项目导出样本待验收。 |
| 统一工程 Tag | VERIFIED | 项目与系统隔离导入、字段映射、冲突、合并、手工管理、分页、版本、双格式导出和恢复。支持 PLC、FHX 与 IO List 汇入同一实体。 |
| 制药验证、仪表与事件关联 | PARTIAL | Tag 提供稳定实体关联接口。验证需求、用例、证据正式业务及仪表统一管理安排后续版本。 |
| 第三方 MCP OAuth | MISSING | 现有手动 HTTP Bearer 与 stdio 保持。OAuth、远程 Agent 服务与更宽的 Schema 方言待后续兼容验证。 |

[alpha.3 指南](V5-ALPHA3-GUIDE.md) · [FHX 矩阵](V5-DELTA-V-FHX-COMPATIBILITY.md) · [版本证据](V5-BUILD-PROVENANCE.md)

## V5.0-alpha.2 功能优先增量

| 功能 | 状态 | 已实现与验收边界 |
| --- | --- | --- |
| 桌面 Agent Runtime 与六层架构 | VERIFIED | 真模型协议、分步执行、真实工程计算、有限重试、项目／环境隔离、读写回执与崩溃恢复路径均有直接服务测试。保留 WinForms 及旧业务入口。 |
| 多模型协议及能力探测 | VERIFIED | 实测本地 Chat Completions 和 Responses 的 SSE，记录有据的文本增量、终止帧和供应商上报的 Tokens。实际商业供应商、价格及高级模型原生 function calling 留待专项。 |
| 本地确定性降级 | VERIFIED | 当前项目索引搜索、无可访问根时的拒绝、用量程确定性计算已有验收。明确本地命令支持在已配置模型时绕过模型。 |
| 通用 MCP 注册与协议 | VERIFIED | 项目注册、HTTP＋SSE、独立 stdio、JSON-RPC、分页、结构化参数/响应、权限、诊断和取消有实际传输证据。可同时保留内置 TIA MCP。 |
| 第三方 MCP 凭据、配置交换 | PARTIAL | 临时 Bearer 可用于 HTTP。OAuth、旧 SSE 传输和配置交换待评估。当前数据目录参与应用备份。 |
| 桌面模型调用第三方 MCP | MISSING | 本期第三方工具提供逐工具授权和单次人工执行。桌面 Agent 使用已有受控工具注册表。 |
| TIA 项目结构、DeltaV、统一 Tag | PARTIAL | 沿用 alpha.1 已验收的工程范围和离线导出读取。持证 V20 工程实机及 DeltaV FHX 兼容、统一 Tag 待 alpha.3。 |
| 验证追踪矩阵、证据与审核 | MISSING | alpha.4 按用户确定的需求、用例、证据和偏差模型推进。 |
| UI 设计交接 | IMPLEMENTED | 维护 14 份表现层交接文档和接口，先交付普通功能入口。最终视觉、布局和交互统一后置。 |

[alpha.2 服务、数据与操作](V5-ALPHA2-GUIDE.md) · [验收证据](V5-ALPHA2-VERIFICATION.md) · [设计交接](UI-HANDOFF/PRODUCT-OVERVIEW.md)

## V5.0-alpha.1 第一阶段增量

| 模块 | 状态 | 当期范围 |
| --- | --- | --- |
| 工程上下文、项目切换、任务与事件隔离 | VERIFIED | 读写范围、重名任务和嵌套目录、保存失败、重新加载均有验收。Tag 与资产数据关系仍待实现。 |
| 工程驾驶舱 | IMPLEMENTED | 当前工程、近 7 日到期任务、未关闭事件、变更文件、近期动作和服务状态已接入真实数据。RTM 覆盖率待实施。 |
| 工作台导航、工具抽屉和工作模式 | VERIFIED | 主模块键盘激活、项目选择器和顶栏布局有运行与截图证据。四种模式的入口已实现，模式切换的多屏布局和项目级停靠恢复待验收。 |
| AI 项目会话与文件访问 | VERIFIED | 验证开发版存储、项目切换、活跃请求取消、迟到流隔离、本地检索和旧版回滚历史范围。外部供应商费用和完整 Agent 任务中心为 PARTIAL。 |
| 本机 TIA MCP 管理 | VERIFIED | 实际 stdio 进程启动、协议、工具发现、策略、耗时和响应回执有测试。第三方 MCP 注册、配置交换与通用工具权限仍为 MISSING。 |
| PLC Studio | PARTIAL | 增加项目资料边界、独立草稿与作业。离线导出和工作台有证据，授权 V20 工程仍为 BLOCKED。 |
| DCS Studio | PARTIAL | 支持从项目内文件源读取 FHX 等文本预览。版本兼容性、对象解析与差异仍为 MISSING。 |
| 验证中心 | PARTIAL | 项目阶段、加权进度、资料入口和已有模板接入。证据库、追溯、偏差与签核仍为 MISSING。 |

[操作、技术架构、数据兼容和回退](V5-P0-GUIDE.md) · [综合验收](V5-P0-VERIFICATION.md)

## 历史基线 1930b40

| 模块 / 页面 | 基线 | 实际入口、服务与验收缺口 |
| --- | --- | --- |
| 驾驶舱、快捷启动、状态 | PARTIAL | `ReferenceOperationsDesk`、`ReferenceMissionHome` 和经典 `EngineerHomeView`，项目与验证进度有源，任务、近期文件、通知与工作记录未统一范围。 |
| 智能桌面 | IMPLEMENTED | `MainForm`、`IconBoard`、`FolderSession`、`DesktopWatcher`。已有真实路径、返回/前进/上级、分类、排序、显示隐藏项、预览与操作菜单。 |
| 桌面文件操作 | IMPLEMENTED | `ShellFileOperationService`、`ShellThumbnailService`、`FileOps`。双击、拖拽、剪切/复制/粘贴、重命名、回收站、部分撤销、Shell 缩略图。多屏、文件属性与异常退出恢复待专项验收。 |
| 多屏项目桌面恢复 | MISSING | 既有布局开关和文件夹历史，缺按项目的启动应用、文档、停靠布局和多屏恢复。 |
| 项目中心 | IMPLEMENTED | `WorkspaceStore`、`ReferencePortfolio`、`ReferenceProjectStudio`、`WorkspaceView`。CRUD、固定、选中、进度、项目目录、备注与日期。 |
| 跨模块项目隔离 | BROKEN | 全局选中项目已落盘。任务历史名称、搜索路径、AI 会话、TIA 状态和事件过滤各自维护。 |
| 任务、详情、月历、截止提醒 | IMPLEMENTED | `TasksView`、`TaskMonthCalendar`、`NotificationStore`。详情、提醒、列表与月历有后端，项目一致性待整改。 |
| 里程碑、风险、交付清单、日报周报 | PARTIAL | 项目状态、风险等级、备注、任务与工作记录可读取。缺完整甘特、里程碑和交付结构，以及可追溯工作报告草稿。 |
| PLC Studio 离线资料 | IMPLEMENTED | `TiaEngineerView`、`PlcEngineering`、`SclEditing`、`AmlTopology`。支持 AML/XML/SCL/CSV、对象树、IO、静态检查、编辑与差异。 |
| PLC Studio SCL 规划和任务 | IMPLEMENTED | `TiaPlanning`、`TiaProgramming`。复用模型，区分计划、草稿和回执。需实际服务商、数据权限和项目隔离回归。 |
| PLC Studio V20 工程发现、只读附加 | BLOCKED | `TiaOpenness`、`TiaEnvironmentInspector`、`TiaMcp`，历史回归只证实接口夹具和私有进程往返。实际工程、CPU、变量与诊断需要持证 Windows。 |
| PLC Studio 副本编译、单块新增、签批 | BLOCKED | 存在离线副本/指纹/审批/回读路径。真实项目编译、回读、断线恢复待授权验证。 |
| 生产 PLC 控制 | MISSING | 没有 PLC 下载、RUN/STOP、强制变量或生产写入工具。 |
| DeltaV、SCADA、WinCC、Kepware 启动 | IMPLEMENTED | 驾驶舱与工程工具保存用户选定的本机软件路径。 |
| DeltaV Studio FHX / Alarm / Batch | MISSING | 无实际 FHX 台账、语法解析、跨版本兼容或批次分析。 |
| 通用 Tag 数据中心 | MISSING | 已有 TIA 变量/IO 资料解析，缺共享 Tag ID、来源追踪、跨模块关系及 Excel 导入导出。 |
| 验证阶段与模板 | IMPLEMENTED | `ValidationProgress`、`ValidationFlowControl`、`ProjectProgressForm`、`ValidationTemplates`。阶段权重、日期、进度和生成模板有真实实现。 |
| 验证追踪矩阵、原始证据与偏差 | MISSING | 现有进度不能推出需求覆盖率或通过率。缺正式实体关系、证据哈希与版本、偏差和授权审核。 |
| 合规辅助与日志 | PARTIAL | CSV 验证模板、应用运行日志、工程作业记录。缺完整 ALCOA+、Part 11、Annex 11 检查工作区及授权审核。 |
| 现场事件 | IMPLEMENTED | `IncidentsView`、`SyncStore`。附件、项目、调查、等级、版本、状态与同步，待按项目隔离。 |
| 仪表资产、校准、再利用案例 | PARTIAL | 现场事件含设备、原因和处理措施，缺单独资产台账、校准证书到期与可维护案例索引。 |
| 现场计算及通信辅助 | IMPLEMENTED | `EngineeringTools`、`EngineeringToolkitView`、`EngineeringSignalWorkspace`、计算器。包含信号换算、Pt100、S7、Modbus、用户主动 Ping/TCP 等。 |
| GSD/GSDML、OPC UA 信息与诊断库 | PARTIAL | 有启动、网络及协议工具，缺正式元数据管理、质量码资料库及授权导出诊断归档。 |
| PI 历史数据与 UFL | MISSING | 通用 CSV 和文本预览存在，缺 PI 质量和趋势分析、批读、DAT/UFL 检查。 |
| AI 文本、语音和模型设置 | IMPLEMENTED | `AiClient`、`ProviderStore`、`ChatService`、`ChatView`、`HolographicCallView`。多供应商、模型、流式、停止、连接探测、普通/离线环境。实际供应商本轮需分开验收。 |
| AI 应用及文档工具 | IMPLEMENTED | `ToolRegistry`、`ApplicationTools`、`DocumentSkills`。真实任务/项目/事件、文件/文档分析、路径授权、人工确认。历史客户端使用模拟端点做协议回归。 |
| AI 项目会话、工具范围与外发策略 | BROKEN | 历史按环境隔离，缺项目边界。历史内容及应用工具读数可能送到无项目级授权的模型。 |
| 通用 Agent Runtime | PARTIAL | 已有请求执行器与会话持久化，缺贯穿全部入口的项目级执行记录和费用，以及本地降级的一致入口。 |
| MCP TIA 本地能力 | IMPLEMENTED | 一作业一工具动态发现。已有真实进程审核和响应，非通用注册、导入导出和能力管理。 |
| MCP 通用管理中心 | MISSING | 可管理第三方服务器、逐工具权限和连接诊断的 UI 和持久化配置尚缺。 |
| 工程知识库、文件中心 | PARTIAL | `FileIndex`、`SearchView`、`IndexedFolderTree`、`PreviewService`、`FileLibrary`。索引元数据、收藏、过滤、预览可用，全文、项目治理、引用与页码整合尚不完整。 |
| 文档正文、OCR、预览 | PARTIAL | Python 文档 worker、系统 OCR、Office/文本格式路由存在。依赖安装需实测，且无可追溯证据库。 |
| 工程截图与证据助手 | MISSING | 现场事件可挂附件，缺全局截图入口、证据原图、标注副本、哈希与验证关联。 |
| 自动化中心 | PARTIAL | `WorkflowEngine`、`WorkflowRunner`、`ReferenceWorkflowStudio`、`WorkflowGraphControl`，已有模板、节点、预演、运行与历史。任意连线、事件触发、版本能力及现场需求仍缺。 |
| 移动协同本地服务 | IMPLEMENTED | `LocalSyncCoordinator`、TLS、设备信任、离线队列、冲突、附件；可选 CloudSyncAgent/Worker。Windows 与真机端到端须单测。 |
| iOS | BLOCKED | `mobile/DeskBoxMobile` 含项目、任务、现场事件、附件、云同步及语音。当前 Linux 无 Xcode、签名或真机环境。 |
| 天气、时钟、监控、剪贴板、存储 | IMPLEMENTED | `WeatherView`、`WidgetsPanel`、`MonitorView`、`ClipboardView`、`DiskView`。保留后台服务及入口。 |
| 历史、收藏、通知 | IMPLEMENTED | `RecordsView`、`HistoryView`、`FavoritesView`、`NotificationsView`，保留现有数据文件与操作。 |
| 设置、权限、备份、安装与回滚 | IMPLEMENTED | `SettingsCenterView`、`AiEnvironment`、`DataBackupService`、Inno Setup、既有 Windows CI。自动更新、数据故障细分与完整健康中心待补。 |
| 四种工作模式与布局持久化 | PARTIAL | 已有主导航、经典模式、TIA 折叠和部分联动，缺统一工作模式、停靠存档和多屏。 |
| NOVA 语音样片与回归夹具 | MOCK | 仓库保存测试模型、Siemens 反射夹具和独立样片。它们用于受控回归或原型，实际桌面统计不依赖这些数据。 |
