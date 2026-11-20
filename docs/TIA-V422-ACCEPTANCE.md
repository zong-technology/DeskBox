# DeskBox V4.2.2 TIA 验收

**4.2.2-tia.3 Windows 预览版**已重新下载核对安装包 SHA-256，包含从真实安装进程取得的 12 张不同截图。GitHub Actions 构建通过 115 项 TIA 工作台检查、69 项安装相关检查和 138 项语音回归。工作台工程资料及语音传输为测试夹具。 [Windows 构建](https://github.com/zong-technology/DeskBox/actions/runs/44764484503) · [安装包及校验清单](https://github.com/zong-technology/DeskBox/releases/tag/v4.2.2-tia.3) · [安装报告](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-tia.3/INSTALL-TEST-REPORT.md) · [12 张安装版截图](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-tia.3/installed-tia-screenshots.zip)

真实工程闭环状态为 **BLOCKED**。当前开发工作区为 Linux x64，GitHub 托管 Windows 回归机没有发现 TIA Portal V20 PublicAPI。没有取得可连接的持证 Siemens 测试会话或用户授权的 `.ap20` 工程。真实工程读取、编译回执和实测修改前后差异均待取得。

## P0 真实工程

| 验收项 | 结果 | 取得的证据与缺项 |
|---|---|---|
| V20 与 Openness 探测机制 | PASS | Windows 原生程序执行了检测，并显示探测结果。 |
| V20 许可、安装和 API 访问 | BLOCKED | 需要持证 Windows V20、Openness 用户权限及实际项目打开记录。 |
| 读取设备、程序块、变量表及组态 | BLOCKED | 连接器使用 `ProjectOpenMode.Secondary` 只读打开隔离副本。独立的块正文读取入口另行确认。需要授权 `.ap20` 对照清单。 |
| 使用真实数据建立 AI 上下文 | BLOCKED | 上下文区分文件导出和 Openness 来源，原生来源包含程序集版本和快照哈希。现有自动化使用文件夹具和本机回环模型。 |
| SCL 草稿、Diff 与人工批准 | PASS | 自动化核对逐行草稿差异、用户确认和精确源哈希。审批限制为单个新 FB，已有同名对象无法写入。真实工程里的人工批准仍待执行。 |
| 写入隔离副本 | BLOCKED | 按 Openness 文档接受 ASCII SCL。连接器复读对象范围后才允许保存。需要真实工程写入与复读记录。 |
| TIA 编译、错误定位和重编译 | BLOCKED | 已接入 `ICompilable` 及原生路径。路径能唯一匹配时可定位对应对象；缺少原生行号时保留 Siemens 路径。需要实际失败诊断、批准修订和再次编译的回执。 |
| 失败后恢复与输入保全 | BLOCKED | 文件级回归覆盖失败作业回执、哈希校验和恢复到新副本。持证环境下的导入或编译失败与恢复待验。硬中止可能只留下 `failed.txt` 及快照，此时没有可供自动恢复的完整回执。 |
| 真实 PLC 在线操作禁用 | PASS | 自动测试确认在线下载请求在连接器入口即被拒绝，语音工具仅开放授权查询。 |

连接器预期在授权作业目录落盘 `source-sha256.txt`、`approved-operation.json`、`approved-source-diff.txt`、`diff.txt`、`result.json` 和 `engineering-report.md`。只有完成编译并验证对象变化的作业会写入实际工程差异。所有证据保留在本机作业目录，是否共享由项目授权决定。 [Siemens 只读打开项目](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-for-projects-and-project-data/opening-a-project) · [Siemens 外部源生成](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-for-accessing-the-data-of-a-plc-device/blocks/generating-blocks-from-source) · [Siemens 编译回执](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-for-projects-and-project-data/compiling-a-project)

## P1 安装版与视觉

| 验收项 | 结果 | 取得的证据与缺项 |
|---|---|---|
| 鼠标焦点、英文与中文粘贴、复制、多行、快捷键 | PASS | 实际安装进程通过鼠标聚焦、英文输入、中文粘贴、Ctrl+A、Ctrl+C、Shift+Enter。配置后二次提交由 Windows 工作台测试覆盖。 |
| 微软拼音组字与候选上屏 | BLOCKED | 中文剪贴板验证独立记录。微软拼音需要目标 Windows 用户会话。 |
| 100% 系统 DPI | PASS | Microsoft Windows Server 2025 Datacenter 26100。实际安装窗口的 `GetDpiForWindow` 返回 96，用户输入法配置只有 `0409:00000409`。 [系统记录](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-tia.3/installed-tia-environment.json) |
| 125% 与 150% 系统 DPI | BLOCKED | 安装脚本支持 `-ExpectedDpi 120` 和 `-ExpectedDpi 144`，会核对实际窗口 DPI。需要对应缩放的 Windows 会话。 |
| 空间分配、拖动、保存、恢复默认 | PASS | 日志独立置底。工程树和助手在宽屏可调整比例，窄屏助手改为底部。Windows 回归验证日志拖动和状态文件，以及恢复默认。 |
| 工程名称、变量和诊断可读性 | PASS | 工程树字号提高，变量在可横向滚动的视图中定位，诊断悬停可查看完整来源和说明。1024 像素宽且助手展开时，诊断列表可见高度有限。高 DPI 复查待完成。 |
| 12 张真实安装版截图 | PASS | 来自安装后启动的 DeskBox 进程，覆盖输入、选区、六个工作页、环境、助手折叠、默认布局和工程语音窗口。无授权工程的页面截图记录实际空状态。 |
| Figma 完整逐页核对 | BLOCKED | 已读取 Input 组件的默认、焦点、禁用、错误状态。读取业务原型和交接页时，Figma 返回 Starter 套餐调用额度限制，完整页尚待获取。 |

输入框把焦点背景强调度从 12% 调到 15%，与 Figma Input 焦点状态一致。原生需求编辑器保留多行高度和 Windows IME，圆角单行样式仍有差异。禁用与错误参考保留在组件对照中，当前编辑器在 AI 生成时可继续输入，错误在工程输出区显示。 [Figma Input 组件](https://www.figma.com/design/jdAhri31Qx53x6T4gG4a9D?node-id=1-4836)

第一次布局回归为 **FAIL**。底部拖动会触发中间区域的尺寸事件，导致旧高度在保存前恢复。布局改为只在外层工作区尺寸变化时重新计算，分隔条移动后单独写入新尺寸。 [首次回归](https://github.com/zong-technology/DeskBox/actions/runs/44763896763) · [修复回归](https://github.com/zong-technology/DeskBox/actions/runs/44764484503)

## P2 工程语音

| 验收项 | 结果 | 取得的证据与缺项 |
|---|---|---|
| 实时双向音频 | BLOCKED | 音频采集、播放和供应商传输代码已接入。回归使用测试传输，真实麦克风、扬声器和供应商会话待验证。 |
| 打断、暂停、继续及文字同步 | BLOCKED | 测试传输验证打断与暂停释放资源，继续建立新的实时连接，文字保存在原本地会话。真实会话的打断与字幕同步待验。 |
| PLC 块、变量与硬件高亮 | PASS | Windows 自动化通过实际授权查询工具核对树节点、变量选区及设备清单选中状态。真实语音驱动待验。 |
| 动态显示与实时状态 | BLOCKED | 回归覆盖协议事件、接收语音帧与状态变化。真实供应商流量及设备播放对应关系待验。 |

## 持证主机验收入口

从 `tools/tia-v20-smoke.ps1` 以只读模式核对工程对象。用户在工作台授权读取正文、审核模型生成的单块 ASCII SCL 和差异后，可在同一个工作台执行批准写入；脚本也支持以 `-ApprovedSclFile` 和 `-ApprovedSclSha256` 重跑已审批样本。脚本会单独记录原始目录的哈希、保存后复读结果，以及恢复副本是否与原始清单一致。 [工程验收脚本](../tools/tia-v20-smoke.ps1)
