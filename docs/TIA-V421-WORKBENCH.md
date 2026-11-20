# DeskBox V4.2.1 博图 AI 工程师

Windows 预览版为 4.2.1-tia.11，已重新下载并核对安装包 SHA-256。[下载](https://github.com/zong-technology/DeskBox/releases/tag/v4.2.1-tia.11) · [产品提交](https://github.com/zong-technology/DeskBox/commit/58bb4ebc473b11e9dc8b5edaabb9d3224c6ad355)

## 从哪里输入需求

1. 在 DeskBox 左侧打开「博图 AI 工程师」，点击顶部的「输入工程需求」。焦点会进入「AI 编程」页的「工程需求」输入框。
2. 输入目标、PLC 系列、输入输出、限制和预期结果。空白时可用「填入示例」查看一条完整需求。
3. 点击「生成工程计划」。默认 Enter 发送，Shift+Enter 换行。可在「设置 → AI 服务商」关闭 Enter 发送，改用 Ctrl+Enter。
4. 在工程助手查看接口、待确认参数、测试用例和复核项。下方编辑器支持行号、高亮、查找替换、括号匹配、静态检查和另存 SCL。已有草稿时，再次生成或替换会显示逐行差异，点击「应用已预览草稿」后才替换编辑器内容。

可在生成期间继续写下一条需求。取消任务保留输入，重新生成使用上一次已提交的文字。「保存需求草稿」将草稿写入当前 AI 环境的本机资料目录。切换页面保留未发送的内容。默认只读针对工程副本。生成计划和载入模板会进入辅助设计模式。

「添加工程资料」和拖放支持 SimaticML、SCL、UDT、DB、CAx AML 与 IO CSV。勾选「引用已加载的工程」时，当前对象和可见工程摘要会作为模型上下文。文件与隐私许可及网络发送确认继续生效。没有配置模型时，可使用缩放模板、资料分析和静态检查。

## 工作区操作

| 页面 | 输入与输出 |
|---|---|
| AI 编程 | 文字与已授权工程上下文生成结构化计划及单块 SCL 草稿。可载入本地缩放模板、检查草稿和导出。 |
| 程序分析 | 左侧按名称、类型或路径筛选设备、块和变量。选中对象可查看接口、调用、变量引用和来源。SCL 源文件另显示控制结构数量及最大嵌套深度。双击唯一匹配的关联项可打开对象。右键可复制工程路径及来源。 |
| 硬件组态 | 导入 CAx AML 后可选择设备、机架或含子模块的通信接口。选择槽位卡片可查看订货号、固件及来源。可从工程树定位模块，或导出 Markdown 硬件清单。 |
| 网络拓扑 | 导入 CAx AML 查看已记录的端口连线与子网成员。选中连接可查看两端及来源，点击有明确工程归属的端点可跳到对应设备或模块。 |
| 变量管理 | 从 IO CSV 或工程变量检查重叠，从指定 I/Q 起点规划地址。建议须按目标模块核对。 |
| 工程对比 | 用两组同类导出资料或两个 V20 工程副本生成可见对象差异。 |
| 测试验证 | 可按问题级别筛选诊断。SCL 检查覆盖 IF、CASE、FOR、WHILE、REPEAT 的结构配对和分支关键字。双击当前草稿的诊断可定位行列，「生成修复草稿」根据诊断请求 AI 并要求应用前确认差异。原生诊断保留 Siemens 返回的路径。经批准的缩放模板是目前唯一支持写入工程副本的操作。 |

宽窗口可拖动左右分隔条。窄窗口中工程助手移到底部，可点击「收起助手」留出编辑空间。「AI 编程」页支持纵向滚动。顶部任务状态来自实际请求、确认、校验或执行结果。输入的焦点表现参考现有设计系统的 Input 状态。[Figma 输入控件](https://www.figma.com/design/jdAhri31Qx53x6T4gG4a9D?node-id=1-4836)

机架仅显示选定容器的直属层级。缺少槽位的模块标为「未记录槽位」。图中的位置来自资料，硬件选型、槽位兼容性和现场接线仍需按目标工程验收。[机架操作截图](https://github.com/zong-technology/DeskBox/releases/download/v4.2.1-tia.11/tia-rack-layout.png) · [Siemens CAx 子模块](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/export/import/importing/exporting-hardware-data/export/import-of-sub-modules) · [Siemens SCL 控制指令](https://docs.tia.siemens.cloud/r/en-us/v20/scl-s7-1200-s7-1500/program-control-operations-s7-1200-s7-1500)

顶部「工程语音」会按当前 AI 环境的实时语音配置打开独立窗口。已授权的工程查询能定位工作台中的对象。说完一条需求后，点击「将转写加入需求」将结果放入输入框，检查后再发送。[工程语音截图](https://github.com/zong-technology/DeskBox/releases/download/v4.2.1-tia.11/tia-voice.png)

离线作业保存成功时，「测试验证」会显示作业回执路径。点「从快照恢复副本」，选择该作业的 `result.json` 并确认。重启后也能选择当前资料目录中 `tia-jobs` 下的历史回执。恢复前会核对审批、保存回执和原工程快照的 SHA-256 清单，成功后显示新工程路径并在原作业目录生成恢复回执。

## 验收边界

V4.2.1 优先整改输入和工作台布局。自动化分别启动真实 EXE 发行包安装后的应用与合成资料测试宿主。模型交互测试使用本机回环端点。Windows 输入法拼音候选提交及 125%、150% 实际系统缩放须另留目标机证据。

- 103 项 Windows TIA 检查通过，覆盖需求输入、SCL 控制结构、机架槽位、网络端点、修改预览和历史快照恢复。工作区覆盖 1920×1080、1600×900、1366×768、1024×768，实际为 96 DPI。[Windows 回归](https://github.com/zong-technology/DeskBox/actions/runs/44718605644) · [分页面截图](https://github.com/zong-technology/DeskBox/releases/tag/v4.2.1-tia.11)
- 67 项安装、升级、回退及卸载检查和 132 项安装后语音与全息回归通过。从实际安装路径启动 DeskBox 验证了鼠标聚焦、英文输入、中文粘贴、换行和原有 12 个页面的键盘导航。[安装报告](https://github.com/zong-technology/DeskBox/releases/download/v4.2.1-tia.11/INSTALL-TEST-REPORT.md) · [安装版输入截图](https://github.com/zong-technology/DeskBox/releases/download/v4.2.1-tia.11/installed-tia-input.png)
- 两次中间构建分别通过了 92 项机架阶段检查和 98 项 SCL 阶段检查。首轮机架测试的同名节点匹配已修复。中间两版发布环节返回 GitHub 403，4.2.1-tia.11 已通过发布并包含两阶段功能。[机架首次测试](https://github.com/zong-technology/DeskBox/actions/runs/44717415143) · [机架回归](https://github.com/zong-technology/DeskBox/actions/runs/44717951942) · [SCL 回归](https://github.com/zong-technology/DeskBox/actions/runs/44718023679) · [历史回执恢复基线](https://github.com/zong-technology/DeskBox/actions/runs/44707145567)

| 验收项 | 当前边界 |
|---|---|
| 中文输入和系统缩放 | 使用原生 Rich Edit，支持保持 IME 组字。安装版已测中文粘贴。需要在安装微软拼音的 Windows 用户会话中实测候选与上屏，并在 125%、150% 真实缩放及 2560×1440 下复验。 |
| 工程读取、AI 分析和生成 | 已覆盖导出文件读入、工程树与模型协议。真实 Siemens 工程的准确性和真实模型的工程输出需在授权工程上比对。 |
| SCL 编辑与预览 | 支持行号、缩进、查找替换、括号匹配、高亮、控制结构检查、嵌套统计和逐行差异。代码折叠、完整类型与状态机检查、扫描周期分析及 Siemens 编译诊断到编辑器的精确定位待交付。 |
| Openness 修改、编译与回退 | 文件测试覆盖从备份生成恢复副本及拒绝被更改的快照。真实读取、审批写入、编译、保存后回读、恢复及 PLCSIM 需要持证 Windows 和已关闭的测试 `.ap20`。AI 修复当前应用到草稿。自动修改、编译迭代待交付。 |
| 硬件与拓扑 | 已有目录查询、CAx AML 导入、I/O 规划、二维槽位和显式端点到所属硬件的定位。真实工程导出对照待验，硬件及变量写入、完整兼容性检查和图形块编辑待交付。 |
| 语音与布局 | 支持独立工程语音窗口、转写填入及只读对象查询。自动化使用回环模型和测试转写。实际麦克风、供应商实时语音及打断待验。助手停靠切换、三维图形及可调底部输出区待交付。 |
| 其他交付项 | 目前附带模拟量缩放模板。其余工程模板、项目级知识库、受控 GUI 编辑及 V18/V19 的原生工程连接待交付。 |

[V20 环境与接口](TIA-V42-ENVIRONMENT.md) · [工程案例验收](TIA-V42-ACCEPTANCE.md)
