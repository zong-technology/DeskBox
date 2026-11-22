# TIA 主工作台安装版 UI 整改

## 整改基准

以 `4.2.2-ui.3` 发布中的 12 张 `installed-tia-screenshots.zip` 原图和上一轮确认的 TIA 工作台效果图为基准。工作区内的发布包 SHA-256 为 `cb26f75595d5d75e0cdd8fd3c598344aaf3914ff3b23c3e679179e69f449e6b2`。`(1)` 后缀的下载副本未单独附到当前工作区。 [基线截图](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.3/installed-tia-screenshots.zip) · [上一轮设计规范](TIA-WORKBENCH-UI.md)

## 前后对照和 Figma 差异

| 区域 | `ui.3` 安装版观察 | 整改及参考图差异 |
| --- | --- | --- |
| 顶部和导航 | 顶部工程、CPU 与任务挤在一行。工程树为空时缺少下一步。 | 62px 逻辑全局栏。工程标题、操作模式和连接状态分层。工程树宽 260px，可折叠，空态说明导入入口。沿用现有全局标题栏。 |
| 程序编辑 | 原生编辑器和对象信息有基本布局，草稿为空时无明确状态。 | 以代码区为主，顶部显示草稿/只读状态与行数，右侧按剩余空间显示程序信息。当前仍为单文档视图，未配置程序小地图。 |
| 程序分析 | 空引用表和空文字区难以区分，缺少结论层级。 | 结论在上，引用表在下。对所选对象列出可匹配的静态严重级别、来源和引用。来源唯一时可定位；修复建议仍走已有 AI 解释和审批流程。 |
| 硬件组态 | 空机架、清单与详情上下叠放。 | 二维机架占整行，下方并排显示硬件清单与设备属性。槽位依据导出资料，仍会标示未知位置。 |
| 网络拓扑 | 连接清单和单连接详情纵向堆叠。 | 加入可滚动及缩放的连接画布，保留连接清单、端点明细和跳到所属硬件。画布最多显示前 48 个有来源的端点，清单提供更多连接。 |
| 变量管理 | 原始多行文本，缺少排序、筛选和列。 | 使用五列表格，可按列排序或按名称、类型、地址和来源筛选。选中行与来源详情联动。 |
| 工程差异 | 文本报告占据整个页。 | SCL 草稿使用双栏行级差异和上/下一处，仍可导出原有工程差异摘要；工程对象级对照仍为报告格式。 |
| 验证与环境 | 工作台说明和环境输出分散。 | 当前静态检查、编译、PLCSIM 和现场验证各有状态。环境块集中显示安装、版本、程序集、依赖和权限线索。实际许可与项目读取单独验证。 |
| 输出区 | 默认 152px，诊断和日志区偏薄。 | 默认 196px，分隔条仍可拖动及折叠。宽屏保留中央空间，窄窗口按高度和焦点收缩，避免遮挡 SCL 编辑。 |
| 助手和语音 | 单一状态文本区。语音窗口的真实安装版只展示未连接。 | 助手分任务概览和已提交对话，固定输入，保留显式资料与语音授权。已启动的工程语音仍使用现有独立窗口和对象联动。实时通话界面证据需要实际会话。 |

视觉指导采用 96 DPI 逻辑尺寸。常规控件高 36px，正文 10.5pt。传递工程语义时使用真实数据和实际任务回执。文件级验证可用 `fixture-` 测试资料覆盖正常状态，授权项目的读取和编译证据另外记录。

## 复测发现与修正

- 导出资料测试原先把文件列表行的自动化标识当作打开按钮，导致资料停在选择器中。现按打开按钮的可访问名称定位，并验证安装进程已读到五份资料和工程树。 [失败记录](https://github.com/zong-technology/DeskBox/actions/runs/44847475198)
- 工程助手在提交时切换到对话标签，造成需求输入失焦。对话照常记录，显示标签由用户选择。 [失败记录](https://github.com/zong-technology/DeskBox/actions/runs/44847748515) · [输入复测](https://github.com/zong-technology/DeskBox/actions/runs/44848164255)
- 首轮 150% 输入通过时，标题和标签仍出现省略。现使用 Windows 实测 DPI 计算工作区逻辑尺寸，并按字体实测文本宽度分配标签。输入回归与截图外观独立核对。 [早期 DPI 记录](https://github.com/zong-technology/DeskBox/actions/runs/44847748515)
- 实际缩放增加时，输出区域按可用逻辑高度收缩，给编辑器增加可见空间。关闭或调整输出条仍由用户控制。修改已静态检查的草稿后，工作台会立即将前次结果标记为需重查。

## `4.2.2-ui.11` 安装版验收

运行环境为 Windows Server 2025 Datacenter build 26100。三个缩放档均从重新启动的已安装进程读取实际窗口 DPI，使用真实鼠标和键盘验证需求输入。100% 的安装窗口为 1920×1080。125% 与 150% 的窗口为 1920×1020 和 1920×1008。 [构建与回归](https://github.com/zong-technology/DeskBox/actions/runs/44849521654) · [安装报告](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.11/INSTALL-TEST-REPORT.md) · [实际环境数据](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.11/installed-tia-environment.json)

安装包已下载校验，SHA-256 为 `86492f250e231b695b9ca15bea7755dd85e4a1d7b3b1bea2ea8114b05580a418`。 [安装包](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.11/DeskBox-4.2.2-ui.11-Setup.exe) · [校验清单](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.11/SHA256SUMS.txt)

| 项目 | 状态 | 验证范围 |
| --- | --- | --- |
| 安装与核心交互 | PASS | 70 项安装、升级、回滚与卸载检查。132 项 TIA 工作台及文件夹具检查覆盖聚焦、键盘输入、代码编辑、工程树、标签、过滤、拓扑缩放、差异导航、三处分栏拖动及恢复默认。原有 12 页导航回归通过。 |
| 100% 主工作台和安装截图 | PASS | 真实已安装程序取得 11 张 1920×1080 和 1 张独立语音窗口原图。截图索引逐张记录功能、测试状态及数据来源。 [截图包](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.11/installed-tia-screenshots.zip) |
| 125% 与 150% 主工作台 | PASS | 观测到实际 120 和 144 DPI。对照检查标题、标签、代码区、底部输出及右侧输入位置，并完成实测点击与输入。两档证据覆盖主工作台。 [125% PNG](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.11/installed-tia-dpi-120.png) · [150% PNG](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.11/installed-tia-dpi-144.png) |
| 各页面安装画面与对照 | PASS | 前后各 12 张原图，按功能配对。改后分析、硬件、拓扑和变量画面由安装版实际读取 `fixture-` 导出文件生成。两版截图的数据状态有差异，供核对布局与交互。 [前后对照](https://github.com/zong-technology/DeskBox/releases/download/v4.2.2-ui.11-evidence/DeskBox-TIA-before-after.zip) |
| 工程对象级双栏差异 | FAIL | SCL 草稿双栏和变更导航已回归。工程对象级差异继续以单栏报告呈现。 |
| 授权工程读写、编译及现场验证 | BLOCKED | 测试环境缺少已授权的 Siemens V20、Openness 访问及用户工程。可查看实际环境检测和本机静态检查。 |
| 已连接语音会话及微软拼音组字 | BLOCKED | 安装截图为真实空闲语音窗口。授权对象定位和 138 项现有语音回归已用测试传输验证。输入环境仅登记英文方案，已验中文剪贴板。 |

该版本供 TIA 主工作台视觉确认。原有页面仅覆盖回归测试。
