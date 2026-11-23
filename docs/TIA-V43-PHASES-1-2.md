# DeskBox V4.3 工程绑定与 SCL 编程任务

该分支落实前两阶段。源码差距清单在任何产品改动前单独提交。现有五区工作台与 Openness 工作进程继续使用，原生操作仍受独立副本、同版本及人工审批约束。 [差距清单](TIA-V43-GAPS.md) · [GitHub PR](https://github.com/zong-technology/DeskBox/pull/4)

## 当前编程目标

从「绑定工程」选择已读取工程或导出资料。多个可确认 PLC 时必须选定一个才能确认。向导按范围显示具备来源的程序块、变量或硬件选项，并显示路径、版本、CPU、已知订货号和固件、读取时间及当前能力。提交后，顶部和右侧的「当前编程目标」跟随已确认的目标。点击右侧目标可展开完整路径。

`.zap20` 入口识别 V20 归档并提示在 Siemens TIA Portal 中恢复独立工程。导出与手动标记的测试资料支持分析和本机编辑。对同一资料的再次读取、选择其他已确认 PLC 或绑定其他范围后，旧的可审查任务会标记为失效，保留源码和差异。恢复历史任务会核对路径、内容哈希、PLC 和范围。历史恢复用于检查和编辑草稿，新的工程操作需要新任务与确认。

## 本机 SCL 编程任务

使用固定需求输入提交。无已确认工程时，任务可创建通用隔离草稿。已确认工程时，程序先核对本机版本，再按所选 PLC 和对象组装经授权的上下文。供应商调用有 90 秒上限。模型仅返回计划时任务进入 `BLOCKED`，列出待确认事项，用户可另行选择「生成隔离草稿」。隔离草稿只用于本机审查，并拒绝直接现场 I/O 地址。

每个任务在当前 AI 环境的本机数据目录保存 `task.json`。收到符合范围的实际正文后先保存 `.scl`，再生成 `review.diff` 与 `static-review.txt`。后续处理失败时保留已写出文件。任务区域展示 ID、绑定目标、步骤、UTC 时间、结果、错误和下一步。静态检查与 TIA 编译分别记录。涉及电机、阀门、联锁、急停等条件的草稿需要独立工程师复核。

## 可下载交付

- `4.3.0-alpha.3` Windows 测试安装包，要求 .NET Framework 4.8，当前无代码签名。安装包 SHA-256 为 `783fb8856e425a45c690ca04ed8cec37deeafe2d753667de491af38ef091a0ae`。 [安装包](https://github.com/zong-technology/DeskBox/releases/download/v4.3.0-alpha.3/DeskBox-4.3.0-alpha.3-Setup.exe) · [SHA-256 文件](https://github.com/zong-technology/DeskBox/releases/download/v4.3.0-alpha.3/SHA256SUMS.txt)
- Windows Server 2025 Datacenter build 26100 完成 70 项安装、启动、升级、回滚及卸载检查和 152 项 TIA 工作台回归。现场环境独立验证留待持证测试机。 [构建记录](https://github.com/zong-technology/DeskBox/actions/runs/45111728831) · [安装报告](https://github.com/zong-technology/DeskBox/releases/download/v4.3.0-alpha.3/INSTALL-TEST-REPORT.md)
- 安装版绑定向导、绑定后的工作台及 12 张带索引的安装版截图。 [绑定向导](https://github.com/zong-technology/DeskBox/releases/download/v4.3.0-alpha.3/installed-v43-binding.png) · [绑定工作台](https://github.com/zong-technology/DeskBox/releases/download/v4.3.0-alpha.3/installed-v43-target.png) · [截图包](https://github.com/zong-technology/DeskBox/releases/download/v4.3.0-alpha.3/installed-tia-screenshots.zip)
- 本地测试模型返回的模拟量缩放 `FB_BoundScale.scl`，以及实际工作台保存的 `review.diff`、`static-review.txt` 和 `task.json`。回归任务目标为测试导出资料中的 `PLC_B`，最终状态为 `AWAITING_REVIEW`。 [源码与任务证据](https://github.com/zong-technology/DeskBox/releases/download/v4.3.0-alpha.3/v43-programming-output.zip) · [任务测试宿主截图](https://github.com/zong-technology/DeskBox/releases/download/v4.3.0-alpha.3/tia-v43-programming.png)

## 验收用例

| 用例 | 状态 | 实测范围 |
| --- | --- | --- |
| A 无工程 | PASS | 隔离任务生成 SCL 并落盘，任务无工程目标。 |
| B 多 PLC | PASS | 必须明确选择 PLC，给模型的请求限于所选 PLC，跨 PLC 变量被拒绝。切换工程废止旧审批并保留草稿。 |
| C 生成程序 | PASS | Windows 工作台连测试模型完成指定 PLC 的任务，产生可编辑源文件、差异、静态检查文件和结构化任务日志。已核对源码 SHA-256 与任务记录匹配。 |
| D 修改已有程序 | BLOCKED | 支持草稿和差异。原生已有块替换仍缺少受验证写入入口。 |
| E 真实编译 | BLOCKED | 缺少许可可用的 V20、Openness 及已授权 `.ap20`。保留独立副本编译入口，等待真实 Siemens 回执。 |
| F 本机异常恢复 | PASS | 已测试找不到来源、资料内容变化、没有 Openness、模型只返回计划和模型断线。 |
| F 原生异常恢复 | BLOCKED | 原生工程占用、权限不足和事务失败恢复需要持证环境。 |
| G 视觉与已测输入 | PASS | Windows 安装版在 100%、125%、150% DPI 下验证了工作台和输入手势。绑定弹窗在 100% 截图并操作。1920×1080 布局另由原生测试宿主检查。 |
| G 微软拼音与弹窗 DPI | BLOCKED | CI 只装有英语输入法。绑定弹窗的 125%、150% DPI 全量验收待测。 |
| H 写入防护 | PASS | 测试资料、错误 PLC、旧审批无法授予原生写入。平台无授权时拒绝全部原生事务。线上操作没有 AI 执行入口。 |
| H 真实工程保护 | BLOCKED | 真实原始工程保护和副本恢复需要持证环境。 |

测试资料和模型为回归夹具。安装、交互、草稿文件创建和日志持久化由实际运行的应用完成。另需在用户所用模型供应商上验证生成质量与故障行为。

## 后续阶段与阻塞

| 项目 | 状态 | 说明 |
| --- | --- | --- |
| 授权 V20 原生闭环 | BLOCKED | 当前工作区和托管 Windows 回归机缺少许可可用的 V20 和已授权 `.ap20`。真实读写、Siemens 编译和接口调用证据待取得。 |
| 原生归档恢复、已有块替换、FC/DB/UDT 导入、硬件修改和 LAD/FBD 导入 | BLOCKED | 现有接口仅支持在审批后尝试新增单个 ASCII FB；其它范围可以受限分析和草拟。归档恢复和新原生操作仍需逐项验证。 |
| 完整 IDE 重构 | 后续阶段 | 保留现有编辑区、输入、诊断和分栏。多文档标签、重做及其它第五至第六阶段需求继续单独处理。 |
