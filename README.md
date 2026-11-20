# 桌匣 DeskBox · Windows 桌面管理器（原生软件）

面向自动化与制药设备工程的 Windows 工作台：智能桌面、项目验证进度、文件与文档搜索、AI&SI 和场景上下文面板。

- **原生程序**：C# WinForms，`.NET Framework 4.8`，主程序无需 Node 或 .NET Core；AI 文档扩展需要 Python 运行时及解析库，详见文档技能说明。
- **接管桌面**：隐藏系统桌面图标，由 DeskBox 绘制自己的图标层；退出或取消接管时自动恢复。
- **可靠数据目录**：优先 `DESKBOX_HOME`，其次 `D:\DeskBox\data\`；D 盘不可写时回退 `%LOCALAPPDATA%\DeskBox\data`。

## V2.1 Foundation Preview

新增操作说明：[多项目验证进度](docs/多项目验证进度使用说明.md)、[手机配对与同步](docs/手机配对与同步说明.md)。

AI&SI 实际文档读取与内容搜索：[文档技能使用说明](docs/AI文档技能使用说明.md)。

AI&SI 的「全息 AI 通话」现在直接在软件内进入沉浸投影，只显示全息光场与下方实时字幕。在「功能与设置 → 全息语音」一次保存服务、模型和密钥，后续进入自动连接；通话中没有配置面板、场景菜单或工具栏。Esc 返回，Ctrl+M 静音。使用说明见 [全息对话](docs/SILVERWING-NATIVE.md)。

![沉浸全息界面原生渲染预览](docs/quantum-native.png)

最新验证版本与启动路径：[当前版本说明](docs/LATEST-REVIEW.md)。不同路径的旧进程必须从托盘退出后，才能加载新程序功能。

面向自动化、DCS、Siemens PLC、系统工程与制药设备验证工作：

- 默认“工程驾驶舱”：项目、系统遥测、任务、风险、最近文件和 AI&SI 快速入口集中显示。
- `Ctrl+Alt+Space` 可从其他应用全局呼出命令矩阵；DeskBox 内按 `Ctrl+K`。
- 工程项目舱：固定 PLC/DCS/SCADA/CSV 项目根目录，自动加入文件索引，不复制或改动工程源文件。
- 工程工具矩阵：4–20mA、Pt100、压力/温度单位、S7 地址解析、Modbus Float 字节序、Ping/TCP 端口诊断。
- 制药验证：生成 URS/DQ/IQ/OQ/PQ、CSV 风险评估与 21 CFR Part 11 Markdown 清单，可生成跟踪任务。
- AI&SI 工程模式：自动化系统、Siemens PLC、DCS、CSV/验证、故障诊断、通用工程；参数真实传入 API。
- AI 确定性工具：量程计算、S7 地址、Modbus Float、验证模板，不依赖模型心算。
- Cyber 工程矩阵主题：深色青蓝仪表风格，自适应 1080p/2K 主屏。

![界面](shots/native/final-grid.png)

## 功能

| 模块 | 说明 |
|---|---|
| 分类整理 | 9 个内置分类（文档/图片/视频/音频/程序/压缩包/快捷方式/文件夹/其他）按扩展名自动归类；可新建、重命名、换色、拖拽排序、右键删除 |
| 归入分类 | 拖动图标到左侧分类即归类；右键 →「移动到分类」；支持 Ctrl 点选 / Ctrl+A 多选后批量操作 |
| 隐藏与显示 | 右键或 `Delete` 隐藏（默认只改 DeskBox，不动文件）；设置里可切「同时写 Windows 隐藏属性」；侧栏「已隐藏」一键查看/恢复 |
| 多视图 | 网格 / 列表 / 紧凑 三视图（顶栏按钮循环切换），名称·时间·大小·类型排序，图标 40–96px 可调，搜索框实时过滤 |
| 时间日期 | 大号时钟（24/12 小时制、秒可关）、`2026年10月29日 周四`、时区 `UTC+08:00 · 北京` |
| 天气定位 | Open-Meteo 实时天气 + 6 日预报，自动定位/手动城市，`实时 / 缓存 / 离线示意` 三种来源标记，断网自动降级 |
| 任务 | 回车快速添加、优先级（低/普通/高点击循环）、截止日期、完成勾选、全部/未完成/已完成筛选 |
| 日志 | 扫描、隐藏、显示、归类、打开、任务、设置全部落盘 `logs.jsonl`，卡片实时刷新，可一键清空 |
| 托盘 | 隐藏/显示 DeskBox、接管桌面图标开关、开机自启开关、刷新桌面、设置、退出（自动恢复系统图标） |

![列表视图](shots/native/view-list.png)
![紧凑视图](shots/native/view-compact.png)
![设置](shots/native/settings.png)

## 安装与运行

在左侧打开「博图 AI 工程师」，点击「输入工程需求」开始。V4.2.2 增加经人工批准的 SCL 离线写入路径、可保存布局和工程语音暂停与继续。真实 V20 工程闭环待持证 Windows 和授权工程验证。[Windows 预览安装包](https://github.com/zong-technology/DeskBox/releases/tag/v4.2.2-tia.3) · [逐项验收](docs/TIA-V422-ACCEPTANCE.md)

本地一键更新打包：Windows 双击 `build-update.cmd`；iPhone 在 Mac 运行 `bash tools/release-ios.sh`。日常更新不依赖 Actions 云端额度，见[本地构建与低成本更新](docs/LOCAL-RELEASE.md)。

最新测试版：[Windows 测试版 13 安装包](https://github.com/zong-technology/DeskBox/releases/tag/v2.1.0-hologram.13) 和 [iPhone 0.11.0 IPA（build 12）](https://github.com/zong-technology/DeskBox/releases/tag/v0.11.0-mobile-hologram) 均已发布。手机文字 AI 和全息 AI 可独立连接模型，不需要电脑版在线；支持手机任务、问题及附件操作，工具结果继续默认折叠。所有 AI 新增「本地隔离 / 网络开放」，分别保存配置和对话，电脑目录授权也分别保存；本地暂停 AI 联网及设备同步，业务任务、项目仍共用。电脑文件和工作流为可选能力，需要在线电脑及原有授权。手机离线生成另需 iOS 26+、支持且启用 Apple Intelligence 的设备；不满足时明确提示，不回退云端。见 [环境设置说明](docs/AI-ENVIRONMENTS.md) 与 [本轮验收记录](docs/AI-ENVIRONMENT-RELEASE-REVIEW.md)。

Windows 10/11 x64 需 .NET Framework 4.8+；先从托盘退出旧版，升级时选择原目录。安装包保留数据、备份旧 EXE 并提供回退入口。详见 [Windows 安装升级说明](docs/WINDOWS-INSTALL.md)。当前安装包未签名。

仓库已公开，标准 GitHub Windows/macOS 云端构建已恢复，后续仍手动触发，见[构建流程](docs/LOCAL-RELEASE.md)。最新功能源码在 [feat/holographic-ai-call](https://github.com/zong-technology/DeskBox/tree/feat/holographic-ai-call)，尚未合入 main；下载源码时请选该分支。

重新构建 / 换机器安装：

```powershell
cd D:\桌面\SureHand_Project\Deepseek\deskbox
powershell -ExecutionPolicy Bypass -File build.ps1 -OutDir dist     # 编译 -> dist\DeskBox.exe
powershell -ExecutionPolicy Bypass -File install.ps1 -InstallDir D:\DeskBox   # 复制到 D 盘 + 快捷方式（可选 -AutoStart）
powershell -ExecutionPolicy Bypass -File uninstall.ps1 -InstallDir D:\DeskBox # 卸载（-Purge 连数据一起删）
```

> 构建前请先退出正在运行的 DeskBox。升级脚本不覆盖 `data`，并在覆盖前保留 `DeskBox.previous.exe`。

## 使用要点

- **退出**：托盘图标右键 →「退出（恢复系统图标）」。直接用任务管理器结束进程不会恢复图标；此时可在设置里取消「接管桌面」并重启 DeskBox，或手工执行 `uninstall.ps1`（它会恢复图标）。
- **误隐藏找不回**：左侧「已隐藏」→ 右键条目 →「取消隐藏」。
- **看不到壁纸**：DeskBox 会读取系统壁纸并按比例铺满，再叠一层主题色；设置 →「外观」里的主题（深空/极光/明亮）会改变这层色调。
- **多屏**：当前版本管理主屏（`Screen.PrimaryScreen.WorkingArea`），副屏保持系统默认。
- **开机自启**：托盘或设置里打开，写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不落文件。

## 目录结构

```
deskbox/
├─ src/
│  ├─ Program.cs              入口：单实例、托盘、数据目录
│  ├─ Native/Win32.cs         Desktop 层、系统图标开关、Shell 图标、打开/定位
│  ├─ Core/
│  │  ├─ Models.cs            AppSettings / Category / DesktopItem / TaskItem / LogEntry
│  │  ├─ Store.cs             state.json / tasks.json / logs.jsonl 原子读写
│  │  ├─ DesktopScanner.cs    注册表探测桌面目录 + 扫描 + 按 id 保留分类/隐藏
│  │  └─ Weather.cs           Open-Meteo + 10 分钟缓存 + 离线降级
│  └─ UI/
│     ├─ MainForm.cs          顶栏/侧栏/画板/小部件/状态栏、壁纸背景、拖放、设置
│     ├─ IconBoard.cs         图标画板（三视图、命中测试、拖拽、右键、滚动）
│     ├─ Sidebar.cs           分类栏（计数、拖放高亮、右键菜单）
│     ├─ WidgetsPanel.cs      时钟/天气/任务/日志/统计卡片
│     ├─ SettingsForm.cs      设置 + 开机自启
│     ├─ MenuBuilder.cs       暗色菜单/输入框/确认框
│     └─ Palette.cs           三套主题色板 + 圆角/文字工具
├─ assets/deskbox.ico
├─ build.ps1 / install.ps1 / uninstall.ps1
└─ shots/native/              实机截图
```

## 数据文件（`D:\DeskBox\data`）

| 文件 | 内容 |
|---|---|
| `state.json` | 分类、条目覆盖信息（分类/隐藏）、设置 |
| `tasks.json` | 任务列表 |
| `logs.jsonl` | 操作日志（JSON Lines） |
| `notifications.json` | 可回看的应用内通知 |
| `ai-providers.json` | AI&SI 服务商配置（API Key 为 DPAPI 密文） |
| `chats.json` | AI&SI 对话历史 |
| `workflows.json` / `workflow-runs.jsonl` | 自动化定义与运行记录 |
| `engineering-workspaces.json` | PLC/DCS/CSV 工程项目路径与元数据 |
| `cache/weather.json` | 天气缓存（只缓存在线结果） |

删掉 `data` 目录即恢复出厂状态。

## 已知限制

- 只支持 Windows（依赖 `SHELLDLL_DefView` / `SysListView32` 与 `HKCU` 注册表）。
- 桌面图标层是 DeskBox 自己绘制的，**不是** Windows 原图标；系统图标被隐藏期间，Explorer 的图标拖拽布局不可用（由 DeskBox 接管）。
- 窗口置底：按 Alt+Tab / Win+D 时 DeskBox 会和桌面一起被遮挡或最小化，从托盘双击可重新置底显示。
- 天气/城市依赖 `api.open-meteo.com` 可访问；被代理拦截时显示「离线示意」。
- 设置窗口沿用系统控件风格（未做自绘），与主界面玻璃拟态存在视觉差异。

---

# v2 更新：从桌面整理工具 → 个人桌面工作台

新增 9 个模块（左侧导航切换），全部基于真实系统接口，无演示数据。详细架构见 [docs/ARCHITECTURE-V2.md](docs/ARCHITECTURE-V2.md)。

| 模块 | 能力 | 真实数据来源 |
|---|---|---|
| 🔍 搜索 | 文件名/类型/路径/时间/大小组合筛选、排序、补全；启动自动建索引（默认桌面+文档+下载） | 文件系统遍历，60477 条实测，162 ms 查询 |
| ★ 收藏 | 收藏分组、最近访问计数、标签与反查 | `favorites.json` / `recent.json` / `tags.json` |
| 💽 磁盘 | 大文件 Top N、目录占用、SHA-256 重复文件候选、一键移入备份 | `Directory` + `SHA256` |
| 🕘 历史 | 打开/移动/复制/重命名/删除/收藏/搜索全记录，按动作筛选，一键撤销 | `history.jsonl` + `undo.json` |
| ✦ AI | 文档摘要、文件对比（行级差异+相似度）、工作总结、整理建议、自然语言搜索 | 本地解析，**不联网**；远程需显式配置 |
| ⚡ 自动化 | 触发器（手动/启动/定时）→ 条件 → 动作，先预演后执行，运行日志 | `workflows.json` + `workflow-runs.jsonl` |
| 📈 监控 | CPU、内存、磁盘卷、网络上下行 | GetSystemTimes / GlobalMemoryStatusEx / DriveInfo / IPv4Statistics |
| 📋 剪贴板 | 历史记录、回写、暂停、敏感内容屏蔽且不落盘 | AddClipboardFormatListener（事件驱动） |
| 预览 | 图片/文本/代码/CSV/XLSX/PDF 元信息，右侧面板联动 | 编码探测（BOM→UTF-8→GBK）、ZipArchive 读 xlsx |

另外新增：批量重命名对话框（规则配置 + 新旧名对照 + 冲突标红 + 撤销）。

**验收**：`tools/v2-core-test.cs` 在临时沙箱造真实文件，66 项断言全部通过（索引搜索 10 / 收藏标签 6 / 重命名撤销 14 / 预览 6 / 自然语言 8 / AI 6 / 磁盘 6 / 监控 5 / 自动化 5）。

**安全边界**：索引不扫全盘、可配置排除目录；AI 默认全本地；删除类操作只能进 `data/backup/`，全部可撤销；高危动作（批量重命名/工作流执行/清空）均二次确认。

---

# v3 更新：AI&SI、自动化 v2 与数据安全

- AI&SI：多服务商、DPAPI 密钥保护、真实 HTTP/SSE、多轮/取消/重生成、受控工具调用。
- 自动化 v2：条件/AI/动作节点、`{{prev}}` 数据传递、超时、重试、取消、并发限制；页面提供真实可执行模板。
- 天气：城市唯一 ID/经纬度切换、24 小时/6 日预报、摄氏/华氏、可配置刷新周期与缓存降级标记。
- 通知与记录：任务/自动化非阻塞通知中心，统一工作记录筛选与详情。
- 备份恢复：设置中心可备份、验证并安排下次启动恢复；恢复前自动创建安全快照。
- 安装安全：升级绝不复制开发目录的 `dist\data`，保留用户数据与上一版 EXE。

当前完整审查、测试与已知限制见 `docs/AUDIT-REPORT.md` 和 `docs/DELIVERY-REPORT.md`。
