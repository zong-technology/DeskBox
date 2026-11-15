# DeskBox v2 · 模块架构与实现状态

> 本文档记录第一阶段（核心文件管理）、第二阶段（AI 与自动化）、第三阶段（工作台增强）的落地情况。
> 所有数据均来自真实系统接口，无静态演示数据。

## 一、现有项目检查结果（改造前）

| 项 | 检查结果 |
|---|---|
| 技术栈 | C# WinForms / .NET Framework 4.8，`csc.exe` 直接编译，零第三方库 |
| 入口 | `src/Program.cs`（单实例 Mutex、托盘、数据目录解析） |
| 已有能力 | 桌面图标分类整理、隐藏/显示、三视图、时钟/日期/天气/位置/任务/日志/统计、主题、桌面接管、开机自启 |
| 数据存储 | `D:\DeskBox\data\`：`state.json`（分类+条目覆盖+设置）、`tasks.json`、`logs.jsonl`、`cache/weather.json` |
| 窗口形态 | 无边框全屏置底窗口（不抢焦点），系统桌面图标被隐藏后由自绘图标层接管 |
| 可复用 | `Store`（JSON 持久化 + 日志）、`Palette`（三主题色板）、`IconBoard`/`Sidebar`（分类与拖拽） |

**改造原则**：不重写、不破坏已验证功能；新能力以"新增模块 + 左侧导航视图"的方式挂载，桌面视图保持原样。

## 二、分层结构

```
src/
├─ Native/Win32.cs           ← 系统交互（桌面层、图标开关、Shell 图标、打开/定位）
├─ Core/                     ← 基础层（无 UI 依赖）
│  ├─ Models.cs              AppSettings / Category / DesktopItem / TaskItem / LogEntry
│  ├─ Contracts.cs           跨模块契约：FileEntry / SearchQuery / RenameRule / PreviewResult
│  │                         / DiskReport / SystemSample / ClipEntry / UndoBatch / Json / AppPaths
│  ├─ Store.cs               原子 JSON 持久化 + 结构化日志
│  ├─ DesktopScanner.cs      注册表探测桌面目录 + 扫描
│  └─ Weather.cs             Open-Meteo + 缓存 + 离线降级
├─ Fs/                       ← 文件系统访问层
│  ├─ FileIndex.cs           索引（后台线程）+ 多条件搜索 + 打分排序 + 补全
│  ├─ FileLibrary.cs         收藏 / 最近访问 / 标签
│  ├─ FileOps.cs             批量重命名、移动、复制、删除到备份、撤销批次
│  ├─ FileHistory.cs         操作历史（JSONL）
│  └─ PreviewService.cs      按类型预览（图片/文本/代码/CSV/XLSX/PDF 元信息）
├─ Sys/                      ← 系统接口层
│  ├─ DiskAnalyzer.cs        大文件 / 目录占用 / SHA-256 重复检测
│  ├─ SystemMonitor.cs       GetSystemTimes / GlobalMemoryStatusEx / DriveInfo / 网卡计数器
│  └─ ClipboardWatcher.cs    剪贴板历史 + 敏感内容屏蔽
├─ Ai/                       ← AI 层
│  ├─ AiService.cs           本地引擎（摘要/对比/总结/建议）+ 可选远程兼容接口
│  └─ NlQuery.cs             自然语言 → SearchQuery
├─ Automation/WorkflowEngine.cs  触发器/条件/动作/预演/运行日志
└─ UI/                       ← 表现层
   ├─ MainForm.cs            顶栏 + 导航栏 + 内容区 + 小部件 + 状态栏
   ├─ UiKit.cs / DarkList.cs 自绘控件（按钮/输入/下拉/列表）
   ├─ ViewBase + Views/*     搜索/收藏/磁盘/历史/AI/自动化/监控/剪贴板
   ├─ PreviewPanel.cs        右侧预览面板
   ├─ BatchRenameForm.cs     批量重命名对话框
   └─ IconBoard / Sidebar / WidgetsPanel / SettingsForm（v1 原有）
```

依赖方向：`UI → Fs/Sys/Ai/Automation → Core → Native`，下层不引用上层。

## 三、关键数据结构（节选，完整见 `src/Core/Contracts.cs`）

```csharp
class FileEntry      { Path, Name, Ext, Size, Mtime, IsDir, Root }
class SearchQuery    { Text, PathContains, Exts[], Roots[], MinSize, MaxSize,
                       ModifiedAfter, ModifiedBefore, Kind, SortBy, SortDesc, Limit }
class SearchOutcome  { List<SearchHit> Hits, TotalMatched, ElapsedMs, IndexState, Message }
class RenameRule     { Find, Replace, UseRegex, NumberStart/Step/Pattern,
                       LowerCase, UpperCase, TrimSpace }
class RenamePlan     { Items[], ChangedCount, ConflictCount, Errors }
class UndoBatch      { Id, Ts, Title, List<UndoAction{Kind,From,To}>, Used }
class PreviewResult  { Kind, Title, Text, Image, Rows[][], Meta[], Truncated, Error }
class DiskReport     { Root, TotalBytes, FileCount, DirCount, LargeFiles[], Dirs[], Duplicates[] }
class SystemSample   { CpuPercent, MemUsed/Total, NetRx/TxBps, Disks[] }
class ClipEntry      { Id, Ts, Text, Kind, Sensitive }
```

## 四、实现顺序与状态

### 第一阶段 · 核心文件管理

| # | 功能 | 实现 | 验收（真实文件） |
|---|---|---|---|
| 1 | 真实文件搜索（名称/类型/路径/时间/大小） | `FileIndex`：后台线程遍历 + 内存索引 + 打分排序 + 补全；启动自动载入 `index.json` | 8 项通过（含组合条件、无结果、limit、不存在根目录） |
| 2 | 收藏 / 最近访问 / 标签 / 智能分类 | `FileLibrary` → `favorites.json` / `recent.json` / `tags.json` | 6 项通过（去重、计数、按标签反查、重载持久化） |
| 3 | 快速预览（图片/PDF/文本/代码/表格） | `PreviewService`：Image 解码、编码探测（BOM→UTF-8→GBK）、CSV 解析、XLSX 走 ZipArchive 读工作表、PDF 给页数估算 + 元信息 | 6 项通过（含 GBK、截断、不存在文件） |
| 4 | 批量重命名（规则/对照/冲突/结果） | `FileOps.PlanRename` + `BatchRenameForm`（新旧名对照表、冲突标红、执行反馈） | 4 项通过（预览不改文件、执行、非法字符、冲突检出） |
| 5 | 磁盘空间分析（大文件/目录/重复） | `DiskAnalyzer`：显式栈遍历、目录聚合、两段式 SHA-256 查重 | 6 项通过（总量精确到字节、重复组与浪费空间） |
| 6 | 文件操作历史 | `FileHistory` → `history.jsonl`，`HistoryView` 可筛可查可撤销 | 通过（6 条真实记录，可按动作/关键词过滤） |
| 7 | 确认 / 错误处理 / 冲突检测 / 撤销 | 所有高危动作前 `Prompt.Confirm`；`UndoBatch` 逆序回滚；删除只进备份目录 | 5 项通过（撤销、重复撤销被拒、备份恢复） |

### 第二阶段 · AI 与自动化

| # | 功能 | 实现 | 验收 |
|---|---|---|---|
| 1 | 全局快捷启动器 | `FileIndex.Suggest` 提供补全数据（UI 入口：搜索视图 `Ctrl+F` 聚焦） | 补全返回 5 条 |
| 2 | 自然语言文件搜索 | `NlQuery.Parse`：时间/类型/大小/路径/关键词 → `SearchQuery`，`Explain` 回显理解结果 | 8 项通过 |
| 3 | AI 文件助手 | `LocalAiProvider`：摘要（字数/高频词/摘录）、对比（行级差异 + 相似度）、工作总结（类型分布/最大文件）、整理建议（规则化） | 6 项通过 |
| 4 | 可视化自动化工作流 | `WorkflowEngine`：手动/启动/定时触发 → 条件 → 动作，先预演再执行，运行记录落盘 | 5 项通过 |
| 5 | 项目工作区 | 通过「收藏分组 + 标签」实现（`FavoriteEntry.Group` / `TagEntry`） | 标签反查通过 |
| 6 | 定期备份与恢复预览 | 工作流 `backup`/`deleteToBackup` 动作 → `data/backup/<时间戳>/`；恢复走 `UndoBatch` | 备份与恢复各 1 项通过 |
| 7 | 高风险动作确认 | 批量重命名、工作流执行、删除到备份、清空历史/日志均二次确认 | 代码路径 + 手测确认 |

**AI 安全边界**：默认 `LocalAiProvider`，全程本地计算、不联网；远程兼容接口（OpenAI 兼容）需在设置中显式填写 endpoint + key 并开启，未启用时 `ActiveProvider == "本地"`。

### 第三阶段 · 工作台增强

| # | 功能 | 状态 |
|---|---|---|
| 1 | 小部件可拖动/排序/折叠/隐藏 | 部分（折叠、隐藏已可用；拖动排序待做） |
| 2 | 任务截止/优先级/提醒/子任务/项目关联 | 截止日期、优先级、筛选已可用；提醒与子任务待做 |
| 3 | CPU/内存/磁盘/网络面板 | ✅ `MonitorView`（全部来自系统接口：GetSystemTimes / GlobalMemoryStatusEx / DriveInfo / IPv4Statistics） |
| 4 | 剪贴板历史 + 敏感排除 | ✅ `ClipboardView`（事件驱动监听、120ms 去抖、密码/卡号/密钥屏蔽且不落盘） |
| 5 | 主题/强调色/图标/布局/动画强度/快捷键 | 主题、强调色、图标尺寸、布局已可用；动画强度与快捷键待做 |
| 6 | 插件中心 + 权限控制 | 未做（设计：manifest + 白名单动作类型，需单独一期） |

## 五、测试与证据

- 核心模块真机测试：`tools/v2-core-test.cs`，**66 项断言全部通过**（在 `%TEMP%` 沙箱内造真实文件，不触碰用户数据）。
  覆盖：索引搜索 10、收藏标签 6、重命名撤销 14、预览 6、自然语言 8、AI 6、磁盘 6、监控 5、自动化 5。
- UI 冒烟：`tools/smoke-ui.cs` 依次点击 9 个导航项，全部视图成功创建，日志零错误。
- 搜索实测：索引 60477 个真实文件，`pdf` 空条件搜索 800 条上限耗时 162 ms。

## 六、已知限制

1. 索引默认只覆盖「桌面 + 文档 + 下载 + D:\桌面」，不做全盘扫描（隐私与性能）。
2. PDF 不内置渲染，给出页数估算与元信息，打开走系统默认程序。
3. 工作流 `interval` 仅在程序运行期间生效；关闭 DeskBox 后不会后台执行。
4. 远程 AI 未做流式输出与 token 计费提示。
5. 插件中心未实现，扩展能力目前通过工作流动作扩展。
