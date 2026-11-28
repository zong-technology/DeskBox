# DeskBox 当前项目状态

## 开始开发前

先按[仓库指令](../AGENTS.md)核对当前分支、工作区与远端。当前活跃开发分支为 `codex/deskbox-v5-p0`。以下为最近一次核实的状态，后续以最新有效提交和对应验收记录更新。验收通过的构建与随后补录的文档分别追踪。

| 项目 | 已核实状态 |
| --- | --- |
| 最新验收版本 | `5.0.0-alpha.5.1` 仪表校准 Windows 候选，WinForms / .NET Framework 4.8 |
| Windows 受测源码 | `b3188abdab1133d7aff344eb33e086419d4d09a4`，从 `5d7b791` 增量开发 |
| Windows 构建与验收 | GitHub 托管 Windows 运行 `46145171210` 成功。仪表专项 38 项和安装、升级、回滚、卸载 85 项通过 |
| 原生 UI | 100%、125%、150% 各 127 项工程上下文和界面断言，归档 141 张截图 |
| 开发策略 | 功能优先，保留现有数据与可用入口。验证业务可脱离 UI 测试，集中视觉重构留到 beta |
| 用户可用入口 | 选择工程后进入「工程驾驶舱 → 通知 → 查看校准到期」，完整台账入口为「现场工程 → 仪表资产」 |

[Windows 运行](https://github.com/zong-technology/DeskBox/actions/runs/46145171210) · [本轮验收](V5-ALPHA5-INSTRUMENTS.md#alpha51-校准到期提醒) · [版本证据](V5-BUILD-PROVENANCE.md) · [功能矩阵](V5-FUNCTION-MATRIX.md)

UI/UX Pro Max v2.15.0 已纳入仓库项目技能。已验证 Codex 发现、设计系统生成、可访问性检索与数据校验。后续 UI 任务依据项目规则使用。 [工具链及安装来源](UI-TOOLCHAIN.md#uiux-pro-max)

## alpha.5 仪表资产首批增量

从 `b629418` 继续开发，首批候选源码为 `2497092`。仪表台账已经接入现场工程、工程上下文和统一 Tag。支持稳定 ID、型号及量程、手册、校准及维护、证据引用和校验、到期筛选、导出与恢复。Linux 交叉构建和 28 项仪表专项通过。 [操作及测试](V5-ALPHA5-INSTRUMENTS.md) · [首批 Windows 候选运行](https://github.com/zong-technology/DeskBox/actions/runs/46134844982)

alpha.5.1 接通当前项目的校准到期汇总、跨重启去重、通知快捷入口和设置开关。业务增量 `92a2310`，最终候选 `b3188ab` 修复通知中心高 DPI 标签截断。Linux 交叉构建和 38 项仪表专项已通过。 [操作及测试](V5-ALPHA5-INSTRUMENTS.md#alpha51-校准到期提醒) · [Windows 候选运行](https://github.com/zong-technology/DeskBox/actions/runs/46145171210)

## DeskBox 专属规范与 ECO 准备

已基于分支 `codex/deskbox-v5-p0` 的 `51d7a81` 核对 WinForms、项目上下文、文件检索、任务、Agent Runtime、MCP、工作流和移动同步。DeskBox 保留独立运行和完整人工入口。项目及文件查询、执行、状态和回执已有可复用的内部服务。ECO 的可选接入入口、调用者权限、对外能力目录和事件契约仍待实现。先随实际用例验证最小授权只读范围，继续按既定顺序开发 alpha.5。

本轮 Linux .NET Framework 4.8 交叉构建通过，0 错误、5 项既有警告。使用合成文件、进程内模拟调用方、回环 HTTP/SSE 和独立 stdio 夹具复跑，工程上下文 32 项、Agent／MCP 72 项、离线工程数据 48 项全部通过。覆盖项目及文件隔离、取消、授权撤销、Schema 变化和回执。ECO 公开契约落地后单独进行端到端验收。

[专属规范](DESKBOX_SPEC.md) · [接口现状、缺口和接入顺序](INTERFACE_SPEC.md#eco-接入准备评估) · [技术决策](DECISIONS.md) · [工程上下文测试](../tools/v5-context-accept.cs) · [Agent／MCP 测试](../tools/v5-runtime-accept.cs) · [工程数据测试](../tools/v5-engineering-data-accept.cs)

基于 `ed731e3` 增补 ECO × DeskBox 协议草案 `1.0.0-draft.1`。消息 Schema、示例和进程内模拟任务服务的 10 项契约测试通过，覆盖 36 种状态组合。桌面运行时仍使用已验收版本。传输、认证、事件投递及双方兼容验证等待确认。 [协议草案](INTERFACE_SPEC.md#eco--deskbox-统一通信协议-v10-草案) · [契约测试](../test/eco_deskbox_contract_test.py)

## 未验证与限制

- 持证并获授权的 TIA Portal V20、真实 DeltaV 版本样本、外部商业模型效果、iPhone 真机、200% DPI、2K／4K 及多屏需要相应环境。
- alpha.4 提供验证辅助与基于本机身份的人工审核。正式电子签名、受监管审计追踪及部署合规验证未验收。RTM 当前状态与独立历史记录的呈现范围、企业模板限制及 CAPA/Punch 的关联边界见技术债。
- Windows 包为未签名候选。GitHub Actions 保存构建和运行证据，`V5-BUILD-PROVENANCE.md` 记录其与源码及文档的对应。

[技术债](V5-TECHNICAL-DEBT.md) · [项目接口](INTERFACE_SPEC.md)

## 下一阶段与本文件维护

alpha.5 已交付仪表资产和到期提醒增量。后续按路线完善与现场事件、验证记录的关联，再推进通信资料、PI/UFL、工程知识和现场证据。任务结束时更新本文件的版本、分支、提交、测试环境、实际结果、数据兼容、阻塞和后续阶段。需要详细复现时链接验收报告，保留历史版本证据。

[路线](ROADMAP.md) · [变更记录](../CHANGELOG.md)
