# DeskBox V5 · 功能矩阵

状态含义：VERIFIED 为同一版本与已注明环境中实测；IMPLEMENTED 为已接通实现、未完整复测；PARTIAL 为已有路径但覆盖缺口；UI_ONLY 为视觉或入口未接通业务；MOCK 为测试夹具或历史演示；BROKEN 为已查明缺陷；MISSING 为无产品实现；BLOCKED 为外部环境缺失。此表的基线为 `1930b40`。

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
