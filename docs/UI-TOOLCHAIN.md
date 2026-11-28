# DeskBox UI 工具链

本项目保留原生 WinForms 架构、`build.ps1` 和 `tools/render-v4-ui.cs`。自动化使用隔离测试宿主，不启动或更新生产 DeskBox。脚本、截图、合成数据和编译临时文件落在 D 盘源码的 `dist/ui-qa-*`，无需恢复真实工程数据或配置实际云账号。

## UI/UX Pro Max

仓库内安装 `ui-ux-pro-max` v2.15.0，固定上游提交 `a38d04c3d5c298c851dbe5e6ee1965ee3de42cb5`。使用官方 Codex 模板，保留离线数据、搜索与校验脚本及 MIT 许可。Codex 从项目的 `.agents/skills` 发现此技能。脚本使用 Python 3 标准库。 [已安装技能](../.agents/skills/ui-ux-pro-max/SKILL.md) · [MIT 许可](../.agents/skills/ui-ux-pro-max/LICENSE) · [上游版本](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill/releases/tag/v2.15.0)

- 新增页面或统一视觉时生成候选设计系统，结合工程场景检查信息密度、状态层级及实际工作流程。已批准的方案以 DeskBox 设计规范和现有设计变量为准。
- 修复或审查现有页面时，聚焦实际问题，如键盘焦点遮挡、长中文标签、缩放、空状态或错误提示。WinForms 使用通用 UX 建议和本项目的原生控件、布局与验收标准。SwiftUI 等其他页面按实际技术栈选择指引。
- 设计检索使用概括性描述。新样式或交互按现有 Windows 矩阵实测，涉及打印、导出或高风险按钮时同时核对相应业务流程。

在仓库根目录运行已有的 Python 3 命令，Windows 通常用 `python`。

```sh
python3 -B .agents/skills/ui-ux-pro-max/scripts/search.py "industrial engineering dashboard" --design-system -p DeskBox
python3 -B .agents/skills/ui-ux-pro-max/scripts/search.py "keyboard focus not obscured" --domain ux
```

Codex CLI 已验证技能发现及仓库作用域。设计系统生成、键盘焦点检索和上游数据校验均已通过。

## 运行与验收矩阵

在 Windows 隔离测试账户或虚拟机中，将显示比例设为 100%、125% 或 150%，重新打开测试进程后分别运行。使用能容纳目标窗口的桌面分辨率。

```powershell
.\tools\ui-regression.ps1 -ExpectedDpi 96
.\tools\ui-regression.ps1 -ExpectedDpi 120
.\tools\ui-regression.ps1 -ExpectedDpi 144
```

每档检查窗口 WinForms DeviceDpi 和监视器缩放值，以及实际窗口尺寸。每档运行 1600×900、1280×800、1024×768。每个窗口用真实导航控件的 Enter 事件进入 12 页，断言可见的目标页面，逐页截图。另用保留的渲染器捕获 Mission / Focus / System 工作台。前者是参考工作区的 12 页，后者是经典工作台的三种布局，两者不可互相代替。核对键盘遍历、焦点、可读性、裁切与滚动。截图为 WinForms 控件渲染，安装版需另抓实际窗口。

| 页面 | 视觉与交互验收重点 |
|---|---|
| home 工作台 | 标题层级、卡片、空/多任务 |
| desktop 智能桌面 | 文件卡、排序、长文件名、预览 |
| projects 项目管理 | 目录和概览、长项目名、进度、窄窗滚动 |
| engineering 工程工具 | 工具分类、单位、结果和输入焦点 |
| ai AI&SI | 三栏、长会话、工具过程和输入区 |
| search 文件中心 | 搜索与结果列、空状态、长路径和预览 |
| tasks 任务与提醒 | 月历翻月、列表、密集日期和详情 |
| history 工作记录 | 筛选、长记录、滚动和导出入口 |
| automation 自动化 | 空与非空流程、节点、参数、运行结果 |
| incidents 现场事件 | 列表、长文本、附件与滚动 |
| weather 天气 | 实况、预报、离线或无数据状态 |
| mobile 移动同步 | LAN/云状态、二维码、队列和冲突 |

## 自动差异

先为每个 DPI 和窗口大小选定同尺寸的已审批 baseline。使用相同渲染方式、主题、样例数据和页面状态，保留原图与生成的差异图。比较工具由上面的脚本编译。

```powershell
& "$run\visual-compare.exe" $baseline $actual $diff 0 0
```

每张图输出洋红色变化像素、变化比例、包围框和报告 `report.md`。退出码 1 表示变化超限、缺图、尺寸不符或无输入。通道容差和像素占比阈值须经人工确认；0 和 0 是严格校验。分辨率不符时保留尺寸失败，不自动重采样。遇到时间、天气、云状态、用户名等变化，先固定环境或给出人工判定。对照 `assets/ui-reference-12-pages.png` 和 `docs/visual-targets` 做视觉审查；拼图或未对齐的效果图需要先导出各页的匹配尺寸和状态，才适合当像素基线。

## 安装版验收

1. 在隔离 Windows 用户或 VM 中准备 D 盘合成工程和空白配置，禁用桌面接管、自启及生产同步。先保留上一版安装包与测试数据备份。
2. 安装候选包，分别核对 ZIP 内 EXE、解包 EXE、实际运行路径和 SHA-256。用 `tools/shot-rail.cs` 的 `--main` 或 Windows 原生截图捕获实际窗口；逐页确认标题与内容。禁止把只有按键或截图数量的日志当断言。
3. 重复 DPI 和窗口矩阵，另查任务月历、输入焦点、真实窗口布局、中文截断、托盘退出与桌面图标恢复。对齐抓图方式后再比较像素。
4. 在复制的测试数据上验收旧版升级、数据保留和回退。用户接受视觉差异并核对缺失的矩阵后，再安排生产安装。

## 工具和连接盘点（2026-11-08）

- Windows Store Codex `26.1029.7803.0`，CLI `0.166.0-alpha.6`。Windows 注册表运行时 `4.8.09221`（Release 533509，.NET Framework 4.8.1）；项目构建使用机器自带 Framework csc 和 WinForms DLL，源码约定为 4.8，构建脚本没有独立项目文件或显式 TargetFramework 引用程序集钉住。
- 用户配置启用 codex-app-tools、visualize、unified-computer-use、documents、pdf、spreadsheets、presentations、template-creator、browser、chrome、computer-use。托管缓存另外包含 browser-use、engineering-suite-ponytail、figma、github、openai-templates、plugin-creator、plugin-management、product-design、remotion、sites、visualize、work-pets。缓存目录存在不代表连接已授权。
- CLI MCP 列表显示 cua_repl、node_repl 启用，codex_app 标为禁用；当前桌面会话仍能调用独立桌面工具。CLI 没有配置 figma 或 playwright。Figma 当前会话可见身份探测工具，但调用返回传输错误，连接未验证。
- 用户目录发现系统 Skills imagegen、openai-docs、review-agent、skill-creator、skill-installer，个人 Skills hindsight-coding-agent；项目原有 documents 工程技能在 `skills/documents`。新增项目专属 Skills 位于 `.agents/skills`，以源码目录作为 Codex 工作目录重新打开后发现。
- DeskBox 桌面源码未发现 WebView2 或 Web 前端。另有独立私人云 Worker 在 `cloud/deskbox-worker/index.ts` 输出 HTML。Playwright MCP 可以考虑用于该网页的测试环境，桌面验收使用原生工具。本轮未安装 Playwright MCP，也未访问生产云网页。

| 可选工具 | 用途、来源和兼容性 | 本轮状态 |
|---|---|---|
| FlaUI.UIA3 5.0.0 + FlaUI.Core 5.0.0 | NuGet 官方包，Windows UI Automation。两包均提供 net48，UIA3 依赖 Core。未来只放在独立安装版测试宿主，先用 Inspect/UIA 树验证自绘控件的可访问性。 | 未安装；当前 smoke 使用 Win32/WinForms。 |
| Figma Remote MCP | Figma 官方 Streamable HTTP，OAuth；当前 Codex CLI 已确认支持 `mcp add --url` 和 `mcp login`。 | 本地未配置，托管身份探测失败；以下命令供用户迁移 Codex 数据到 D 盘后使用。 |

```powershell
codex mcp add figma --url https://mcp.figma.com/mcp
codex mcp login figma
codex mcp list
```

完成浏览器授权后重开会话，先用 `whoami` 确认，再读取用户授权的设计文件。参考 [Figma Remote MCP](https://developers.figma.com/docs/figma-mcp-server/remote-server-installation/)、[FlaUI.UIA3 5.0.0](https://www.nuget.org/packages/FlaUI.UIA3/5.0.0) 和 [FlaUI.Core 5.0.0](https://www.nuget.org/packages/FlaUI.Core/5.0.0)。

## 本次实测

- D 盘构建通过，产生 5 条编译警告。1600×900、1280×800、1024×768 均通过 12 页导航断言，逐页捕获共 36 张。三种工作台布局在每个尺寸均切换并截图，共 9 张。
- 测试宿主报告 DeviceDpi 96，监视器缩放接口返回 140%。因此 100% / 125% / 150% 的真实 DPI 验收均未通过验证，脚本按设计以非零退出。本轮没有修改系统缩放或显示设置。
- 像素工具通过相同图片和单像素变化的验证。尚无逐页同尺寸的已批准基线，也未给出设计达标结论。
- 生产运行文件 `D:\DeskBox\DeskBox.exe` 只进行了只读核对，未用本轮构建替换。本轮没有对安装版执行安装、升级或回退。

### 效果图与路由的映射

参考拼图包含项目详情和 4–20 mA 计算器两个二级页面，而左栏的 12 个导航路由另含工作台和工作记录。应分别保留路由导航矩阵和效果图验收清单，不按顺序把两组图强行配对。效果图中的 AI&SI 是带已发送消息、文档附件和回答的界面，本次隔离截图是空会话。1024×768 实测截图中，AI&SI 右栏出现水平与垂直滚动，右下的说明文字被窗口下沿遮挡，亮色原生滚动条与深色主题差异明显。UI 改造时先做窄窗布局，再用同一组长消息、文件和提示内容做对照。

五个项目 Skill 的名称、frontmatter、说明及无占位符检查均通过。Skill Creator 自带 Python 校验器因本机所用 Python 缺少 PyYAML 未能运行；本轮用无依赖的结构检查替代，未安装 PyYAML。
