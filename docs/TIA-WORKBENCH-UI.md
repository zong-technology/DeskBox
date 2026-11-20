# DeskBox V4.2 博图 AI 工程师工作台

## 范围与基线

第一阶段以本次确认的 TIA 工作台效果图为目标。仓库的 29 页 Figma 导出用于核对全局导航、色板和信息层级。工作台由现有 WinForms 和 .NET Framework 4.8 应用承载，继续使用 `ReferenceWorkspace`、`MainForm`、`Palette` 和既有工程服务。开发分支 `feat/tia-workbench-ui-v42` 从 `feat/tia-engineer-v42` 的 `3db2fc2` 切出，上一安装版为 `v4.2.2-tia.3`。 [Figma 导出](../design/DeskBox-V4.1-29-Previews-Final.zip) · [草稿 PR](https://github.com/zong-technology/DeskBox/pull/3)

现有 12 个回归入口包括总览、工作台、项目、工程工具、AI、文件搜索、任务、工作记录、自动化、现场故障、天气和手机连接。语音入口继续使用既有会话窗口和音频服务。全页改造与语音状态改造属于后续阶段，工作台测试单独记录安装、导出夹具和原生 TIA 三类证据。

## 实现规范

| 项目 | 第一阶段约定 |
| --- | --- |
| 色彩 | 沿用 DeskBox `Palette` 和用户强调色。Cyber 中背景 `#080F1C`，栏头 `#070E19`，面板 `#111E31`，次级面板 `#16243A`，主文字 `#EDF5FF`，次级文字 `#8DA3BF`。默认强调色 `#6EE7FF`，成功 `#53D8AC`，告警 `#FFC564`，错误 `#FF7E91`。浅色主题使用对应的深色状态文字。 |
| 单位和组件 | 设计尺寸按 96 DPI 逻辑单位，使用 8 单位基础间距、6 单位分隔带、43 单位工程状态栏和 32 单位工具栏。中文采用 Microsoft YaHei UI，SCL 使用 Consolas。交互控件沿用 `TreeView`、`RichTextBox`、`TabControl`、`ListView` 和 `DarkButton`，保留键盘操作、焦点和控件可访问名称。 |
| 空间分配 | 导航默认 260，助手默认 350，底部输出默认 152。中央编辑区优先，满足宽度后才显示程序信息。工作区域小于 1110 时将助手移至底部抽屉，代码获得焦点时临时收起。 |
| 分栏 | 左、右、下三条分隔线独立调整，顶部入口可分别折叠，设置写入用户配置。「恢复默认布局」同时恢复尺寸及展开状态。 |
| 状态来源 | 工程名、CPU、对象数量及源码来自当前工程快照或导出资料。状态栏区分待检测、发现接口、原生接口未就绪和已读副本。底部计数来自实际加载的诊断，日志来自已记录的操作事件。 |
| 输入与审查 | 右下「工程需求」和工具栏「输入工程需求」抵达同一多行控件，可保留独立草稿。SCL 有行号、行列状态、语法高亮、搜索/替换、复制粘贴和原生撤销。AI 有效草稿可进入编辑器；现存草稿的替换需要差异预览和确认。写入工程仍走既有的离线副本审批路径。 |

## 参考图与安装版对照

| 区域 | 当前实现 | 仍有差异 |
| --- | --- | --- |
| 顶部和全局导航 | 工作台启用图标窄栏，顶部保留工程/CPU、检测状态及工程操作。 | 沿用 DeskBox 全局页头，工程动作占独立工具条。 |
| 工程树 | 工程对象按设备、程序块、变量、网络等分组，有搜索、节点计数、对象提示和点击联动。 | 节点图标、路径与右键操作沿用现有 WinForms 行为。 |
| 中央编辑区 | 程序编辑、分析、硬件组态、拓扑、变量、工程对比及验证通过真实标签切换。选中的 SCL 与程序属性同步显示。 | 当前使用单文档编辑视图。参考图中的多程序块标签及代码小地图留待另行实现。 |
| 工程助手 | 右侧显示工程来源、当前对象、服务状态、计划及可直接录入的需求。 | 工程上下文以实际对象元数据呈现。参考图的对话、任务计划和建议页签及设备照片未逐项复刻。 |
| 底部输出 | 编译回执、诊断、操作日志可切换。诊断保留严重级别过滤及定位。 | 编译回执与操作日志采用文本视图，诊断采用表格。 |

从 `4.2.2-ui.3` 安装进程取得 11 张 1920×1080 工作台截图和 1 张 580×520 的独立语音窗口截图。截图覆盖需求输入、文字选区、六个工作标签、检测、折叠和默认布局。验收未加载授权项目，安装进程截图保留真实空状态。另有带明确测试资料的原生工作台、窄窗编辑器、诊断及日志截图，用于核对对象有数据时的视觉效果。在窄窗中，工具栏换行，长节点名称可由提示查看。部分 Windows 原生滚动条、下拉箭头和选区仍使用系统亮色。 [安装版截图包](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.3/installed-tia-screenshots.zip) · [工作台样例](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.3/tia-workbench-1920.png) · [窄窗样例](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.3/tia-editor-narrow.png) · [诊断样例](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.3/tia-diagnostics.png) · [操作日志样例](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.3/tia-operation-history.png)

第一轮 Windows 回归发现 1024×768 窄窗同时展开助手和输出时，代码编辑区高度不足。工作台现可在编辑器获得焦点时自动让出助手空间。第二轮安装进程在 1920×1080 下完成输入验证和截图，扩大后的测试桌面使后续语音回归超时。截图操作结束时恢复测试桌面尺寸，后续视觉矩阵按原流程再调整分辨率。 [第一轮](https://github.com/zong-technology/DeskBox/actions/runs/44791341995) · [第二轮](https://github.com/zong-technology/DeskBox/actions/runs/44791954761)

## 验收边界

**Windows 预览版 `4.2.2-ui.3`** 由 `ea33d37` 构建并通过下列回归。安装程序为未签名预览，需要 .NET Framework 4.8 或更高。下载核对的安装包 SHA-256 为 `33902915585e20dc3ec56c47637080461393f0369c962881769c2ed39336178a`。 [安装包](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.3/DeskBox-4.2.2-ui.3-Setup.exe) · [校验清单](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.3/SHA256SUMS.txt) · [Windows CI](https://github.com/zong-technology/DeskBox/actions/runs/44793583013) · [安装报告](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.3/INSTALL-TEST-REPORT.md)

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| .NET Framework 源码构建 | PASS | 5 个既有警告，0 个错误。 |
| 安装、启动、升级、回滚与卸载 | PASS | 70 项安装检查通过，核对构建、安装和运行的程序哈希一致。升级和回滚使用隔离目录的旧版夹具。 |
| 100% 缩放与真实安装进程输入 | PASS | Windows Server 2025 Datacenter build 26100。安装窗为 1920×1080，`GetDpiForWindow` 返回 96。验证鼠标聚焦、英文、中文粘贴、Ctrl+A、Ctrl+C 和 Shift+Enter。 [实测环境](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.3/installed-tia-environment.json) |
| TIA 工作台交互及项目级数据流 | PASS | Windows 上 125 项夹具回归，包括三分栏拖动、独立折叠、持久化、恢复默认、工作台输入、SCL 复制粘贴与撤销、差异确认、诊断定位及带来源的工程对象。需求输入覆盖 1920 及 1366 宽度和 1024×768 窄窗。 |
| 现有页面与语音回归 | PASS | 以安装版程序的相同二进制进行 12 页键盘导航、三种布局和 138 项既有语音测试。语音使用测试传输。 |
| 文件级工程读写、校验及回滚 | PASS | 53 项离线回归通过。资料为导出和工程目录夹具。 |
| 微软拼音组字、125% 和 150% 系统缩放 | BLOCKED | 当前环境缺少配置对应输入法和缩放的 Windows 测试会话。中文粘贴单独记录。 |
| 实际 `.ap20` 读取、批准写入、实际编译及回滚 | BLOCKED | 当前能运行 Windows 安装回归，缺少持证 V20、Openness 访问和经用户授权的 `.ap20`。完整工程验收需包含真实修改前后差异及编译诊断。 [原生验收记录](TIA-V422-ACCEPTANCE.md) |
| 真实供应商双向音频和全部语音状态 | BLOCKED | 保留现有语音入口及授权查询工具。本阶段缺少实际设备与供应商会话，测试传输结果单独记录。 |

工作台高 DPI 和目标环境验收完成后再推广到其余页面。本轮保留现有导航与业务接口，并继续禁用在线 PLC 操作。
