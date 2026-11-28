# DeskBox 5.0.0-alpha.4 验收

## 源码与验证边界

以 alpha.3 文档提交 `77b52ef` 为开发起点，对照已验收 Windows 构建 `55fb00393322a309799910e7fde1f7c270c1d382`。本轮候选程序、安装包和样例从 `5f9c95c5f5c256082797325fa6f146ad2452bf9a` 构建。WinForms 和 .NET Framework 4.8 保持，验证台账单独写入 `validation/`，既有 ValidationProgress 继续记录阶段计划。

验证使用合成工程资料、实际磁盘文件、Windows WinForms 及回环协议服务。用户可见的业务审核使用 Windows 账户身份及版本记录。软件提供项目验证辅助、变更历史及附件校验。正式电子签名、受监管审计追踪及部署验证需要单独验收。 [版本对应](V5-BUILD-PROVENANCE.md)

## Windows 构建与交付

GitHub 托管 Windows Server 2025 上使用 .NET Framework 4.8 和 Inno Setup，通过原生应用、安装包、升级、回滚与卸载验收。安装程序未签名。便携包使用同一次运行产生的相同 `DeskBox.exe`。

| 原生文件 | SHA-256 |
| --- | --- |
| `DeskBox-5.0.0-alpha.4-Setup.exe` | `95f942fb5115d1ec63329514fb5eb0c41ff583b3ab08debc279f0c1a6469fdf4` |
| `DeskBox.exe` | `26efa7820ce279dbedfe5fbdc10efbf4bedc931845c595d037bed1af55035c5a` |

[Windows 运行](https://github.com/zong-technology/DeskBox/actions/runs/46036702085) · [安装包与 SHA-256](https://github.com/zong-technology/DeskBox/actions/runs/46036702085/artifacts/15217947060) · [原生程序](https://github.com/zong-technology/DeskBox/actions/runs/46036702085/artifacts/15218006888) · [安装、升级和回滚检查](https://github.com/zong-technology/DeskBox/actions/runs/46036702085/artifacts/15217857141)

## 业务场景

| 场景 | 验收范围 |
| --- | --- |
| A · 两版需求 | CSV 预览显示新增、删除及修改。提交保留需求 ID，历史修订与原文位置可查。另验证 XLSX 映射及 DOCX 段落或表格候选，提交前需要用户确认。 |
| B · 需求追溯 | 从 URS、FS、DS、风险、测试、Tag 和证据追到各自稳定 ID。验证双向关系、未覆盖、无来源、无证据、无效关联及跨项目拒绝。 |
| C · 人工执行与原始证据 | 人工记录实际结果，保存执行时的步骤和用例修订。校验 SHA-256、重复内容和衍生件关系，验证同长度内容篡改和文件缺失。 |
| D · 偏差及复验 | 保留失败执行，将重测独立记录。RTM 在复验通过后继续展示原执行上仍开放的偏差。缺少当版批准、根因和实际处理材料时拒绝关闭。偏差详情显示关闭依据和批准人。 |
| E · 变更影响 | 以确定性资料差异和稳定 Tag 修订确定候选。导出前后来源与哈希，引用需求、风险、用例和证据。历史执行结果继续可查。 |
| F · 报告与指标 | 导出的 RTM 包含需求和执行 ID，验证总结及影响报告显示待审核草稿，并引用记录和来源。核对零分母、重测与当前状态、交付完整性。 |
| G · 项目隔离 | 同一需求编号、Tag 名和附件名分别保存。导入预览和项目切换后的提交使用捕获的项目 ID。验证错误项目和越权写入拒绝。 |
| H · 恢复 | 覆盖取消、文件损坏、乐观锁冲突、写入失败、证据恢复、校验和错误及安全备份。验证旧版数据快照拒绝覆盖高版本验证台账。 |
| I · AI | 回环模型选择只读验证工具，得到实际台账 ID 和来源。伪造审核请求被拒绝。已经包含验证结果的会话保留本机执行。 |
| J · 累积回归 | 保留 AI/MCP、工程 Tag、FHX、PLC 离线资料、TIA、安装升级、回滚、桌面和移动协议的原有验收路径。 |

Linux 交叉构建通过，0 错误、5 项既有编译警告。独立业务套件通过 71 项，AI/MCP 通过 72 项，备份恢复专项通过 18 项。合成 RTM 由 openpyxl 重新打开，生成的 Word 报告由 python-docx 重新读取。测试样例用于演练和核对可追溯字段。

Windows 同一提交通过验证业务 71 项、备份恢复 18 项、AI/MCP 72 项、工程数据 48 项、TIA 离线 186 项及安装升级回滚 85 项。Linux 另实测单个证据恢复拒绝相同大小的错误副本，保留损坏文件，以原始字节恢复归档，证据 ID、元数据和外部源文件保持一致。

[Windows 验证原始日志与合成 XLSX、DOCX 样例](https://github.com/zong-technology/DeskBox/actions/runs/46036702085/artifacts/15216957024)

## 界面与剩余验收

验证工作台从「验证中心 → 打开验证工作台」使用当前工程。经典项目工作区保留验证入口。Windows 运行在 1366×768、1600×900、1920×1080 三种窗体尺寸与 100%、125%、150% 缩放档验证，每档 117 项，通过 135 张截图归档。原生交互自动化覆盖打开验证工作台、读取需求、总览和 RTM。复核总览及 RTM 的表头、滚动、状态和操作入口。现场人员对全部编辑窗体的独立手工验收仍待完成。 [Windows 截图及日志](https://github.com/zong-technology/DeskBox/actions/runs/46036702085/artifacts/15219665132) · [工作指南](V5-ALPHA4-GUIDE.md) · [统计口径](V5-VALIDATION-METRICS.md) · [UI 交接及验收准则](UI-HANDOFF/ACCEPTANCE-CRITERIA.md)

- 授权 Siemens TIA Portal V20 实机、真实 DeltaV 发行版样本、外部模型效果及 iPhone 真机待专门环境。
- 高 DPI、4K、多屏及现场用户完整流程按 [技术债](V5-TECHNICAL-DEBT.md) 的实测边界记录。
- CAPA 和 Punch 可记录并引用已有问题及任务，专用提醒和完整审批流继续后续开发。Word 候选支持带编号段落和常规表格，复杂模板需要明确映射。

[功能矩阵](V5-FUNCTION-MATRIX.md) · [alpha.5 开发顺序](V5-DEVELOPMENT-ROADMAP.md#alpha5-工程效率开发顺序)
