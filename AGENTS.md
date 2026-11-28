# DeskBox 智能体开发入口

本文件适用于整个仓库。后续智能体必须遵守[通用开发规范 V2.0](docs/DEVELOPMENT_RULES.md)、[DeskBox 长期持续开发规范 V1.0](docs/DESKBOX_SPEC.md)及下列项目规则。子目录若有更具体的 `AGENTS.md`，进入该范围时一并读取。使用仓库文件恢复上下文，包括仅从手机端发起的 Codex Cloud 任务。

## 从这里开始

1. 先读[当前项目状态](docs/PROJECT_STATE.md)，核对本地分支、工作区、远端开发分支和受测源码。尊重已有修改，禁止用旧快照覆盖新功能或用户数据。
2. 按任务补读[系统架构](docs/ARCHITECTURE.md)、[接口规范](docs/INTERFACE_SPEC.md)、[技术决策](docs/DECISIONS.md)或[开发路线](docs/ROADMAP.md)。已有能力与范围见[功能矩阵](docs/V5-FUNCTION-MATRIX.md)，阶段变更见[变更记录](CHANGELOG.md)。UI 任务按需进入[交接索引](docs/UI-HANDOFF/PRODUCT-OVERVIEW.md)。
3. 开发后按改动选择实际测试。更新当前状态、必要的决策与变更记录，记录通过、失败、阻塞及其证据。按授权提交并检查远端引用。

## DeskBox 专属规则

- V5 增量开发在 `codex/deskbox-v5-p0`。`main` 是独立的默认分支，切换或合并前核对其实际基线与授权范围。版本状态和受测源码以当前状态文档、Git 和验收证据共同确认。
- 当前功能优先，保留 WinForms / .NET Framework 4.8、既有数据格式和有效入口。新业务尽量独立于窗体并支持无 UI 测试。只做功能可用所需的 UI 调整，集中视觉重构安排在 beta 阶段。高耗时操作应支持后台执行、取消和错误展示。
- UI/UX 设计、实现及审查应使用 [UI/UX Pro Max](.agents/skills/ui-ux-pro-max/SKILL.md)。新增页面或调整整体视觉时核对设计系统建议，修复现有组件时按实际问题检索。将适用建议落实到 DeskBox 的现有设计变量和控件中，并依照 [UI 工具链](docs/UI-TOOLCHAIN.md#uiux-pro-max)验收。原生 WinForms 优先使用键盘、可访问性、布局与高 DPI 指引。
- 保留完整的人工操作和独立运行路径。新增可对外调用的业务复用同一服务层和权限规则。ECO 是可选调用方，禁止依赖 ECO 数据库或内部实现。涉及 ECO 预留时，核对[现有接口、缺口和验收要求](docs/INTERFACE_SPEC.md#eco-接入准备评估)，使用模拟客户端验证实际服务，并维护公开接口的版本兼容说明。
- 涉及跨系统通信时，先读 [ECO × DeskBox 统一通信协议 V1.0 草案](docs/INTERFACE_SPEC.md#eco--deskbox-统一通信协议-v10-草案)。协议变更须同步更新版本、Schema、示例、测试和兼容说明。共享契约及传输选型由双方确认后生效。
- 工程数据绑定稳定 ProjectId，跨模块沿用稳定实体 ID。新增持久化考虑并发、原子发布、损坏检测、备份、恢复和旧版兼容。只在隔离测试目录使用合成资料或获授权的脱敏副本。
- 项目资料默认本地。AI/MCP 权限在服务端核验，工具输出与模型请求属于不受信任输入。工程许可、文件访问、外部模型授权和人工审核分别核验。
- 工业集成默认离线分析和授权只读诊断。生产 PLC/DCS 禁止自动写入、下载、强制、切换运行状态或修改安全联锁。测试执行、证据和审核基于真实记录与授权人员，普通日志与审核记录不得宣称为正式电子签名、受监管审计追踪或已获合规验证。
- GitHub Cloud 的原生 Windows 候选流程见 [Windows workflow](.github/workflows/windows-hologram.yml) 和[构建说明](docs/LOCAL-RELEASE.md)。每次验收分别记录源码 SHA、Windows 运行、文档提交、包 SHA-256 与环境范围。Linux 交叉构建、原生 Windows、iOS 签名及工业实机结果分别报告。
