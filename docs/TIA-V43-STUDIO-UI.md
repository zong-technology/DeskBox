# DeskBox V4.3 · 博图 AI 工程师交互更新

沿用 DeskBox 的 WinForms 工作台、已有工程绑定和离线审批。当前版本继续使用 [PR #5](https://github.com/zong-technology/DeskBox/pull/5) 的 MCP 与 Openness 调用链。 [工程能力矩阵](TIA-V43-MCP.md)

| 模块 | 修改文件 | 交互与数据来源 |
| --- | --- | --- |
| 固定需求栏 | `src/UI/Views/TiaEngineerView.Layout.cs`、`src/UI/Views/TiaEngineerView.cs` | 工程需求、生成和停止固定在工作台底部。收起助手、切换工程页签和缩窄窗口时仍可输入。窄窗初始收起助手，可按需展开。目标名称、PLC 及资料引用授权取自当前绑定和 AI 设置。保存需求、示例、重生成、导入及服务商配置从「更多操作」进入。 |
| 任务进度 | `src/UI/Views/TiaEngineerView.Programming.cs`、`src/UI/Views/TiaEngineerView.Layout.cs` | 右侧状态来自实际已记录的步骤。任务详情先显示当前状态、下一步和目标，再按时间倒序显示步骤、异常和产出。底部停止请求使用现有取消令牌，任务保存真实取消状态。 |
| 操作结果和诊断 | `src/UI/Views/TiaEngineerView.cs`、`src/UI/Views/TiaEngineerView.Layout.cs` | 输出区区分操作结果、诊断和运行记录。诊断展示静态检查或 Openness 来源，优先排列错误。选中后可点击「定位所选」，也可按 Enter。提供行列时定位到源码，只有块路径时尝试定位到对应块。 |
| Windows 回归 | `tools/tia-engineer-accept.cs`、`.github/workflows/windows-hologram.yml` | 实际安装进程和 WinForms 测试宿主覆盖鼠标、键盘、折叠、窄窗、绑定、任务取消及诊断定位。真实 Siemens 验收仍需持证环境。 |

提交 SCL 需求时，工作台继续经过目标及 AI 资料权限检查，实际执行由已有任务处理器完成。任务日志和诊断使用真实回执或实测静态检查结果。托管 Windows 回归使用工程导出夹具和本机测试模型。实际 V20 工程操作以及微软拼音组字仍待相应环境验证。

## Windows 验收

`4.3.0-alpha.5` 从 `42372dc` 构建。通过 173 项 TIA 工作台回归及 70 项安装、启动、升级、回滚和卸载检查。实测 1920×1080 的安装窗及 96、120、144 DPI 下的输入。原生测试宿主在 1024×768 下检查了宽窄切换、固定需求栏和焦点，SCL 编辑控件实测高 103 px。使用本机测试模型验证了真实请求、重生成、差异确认、停止及保留编辑内容。 [Windows CI](https://github.com/zong-technology/DeskBox/actions/runs/45304140089)

本次回归定位并修复了菜单在 WinForms 完成点击派发前释放，以及从宽窗缩窄时助手占用编辑区的问题。测试覆盖三个菜单入口、窗口切换和助手折叠状态下停止任务。 [菜单复测](https://github.com/zong-technology/DeskBox/actions/runs/45302882433) · [窄窗复测](https://github.com/zong-technology/DeskBox/actions/runs/45304140089)

Windows 安装包及 12 张安装截图随构建产物交付。安装包为未签名测试版，要求 .NET Framework 4.8，已下载并核对 SHA-256 清单。任务运行、窄窗和诊断的额外截图位于原生测试产物的 `hologram-qa/tia/`。 [安装包及截图](https://github.com/zong-technology/DeskBox/actions/runs/45304140089/artifacts/14879833407) · [交互证据](https://github.com/zong-technology/DeskBox/actions/runs/45304140089/artifacts/14879873423)

## 连续对话、任务进度与连接错误

| 模块 | 源码 | 行为 |
| --- | --- | --- |
| 连续对话 | `src/UI/Views/TiaEngineerView.Programming.cs`、`src/Engineering/Tia/TiaPlanning.cs` | 发送成功受理后清除本次输入，运行时可编辑下一条。最近三轮成功返回的需求、回复及有长度限制的 AI 草稿通过模型消息角色传递。切换项目、模型或资料权限时重新建立会话范围。「更多操作」可新建工程对话。历史只在当前工作台会话内传递。 |
| 回复和进度 | `src/UI/Views/TiaEngineerView.Pages.cs`、`src/UI/Views/TiaEngineerView.cs` | 发出与收到的条目使用不同标题、颜色和缩进。发送后打开工程对话，近期真实任务步骤显示在对话上方，当前操作显示在输入栏旁。解释类回复记录为「已回复」，草稿生成及替换保留已有审核流程。 |
| 绑定和发现 | `src/UI/Views/TiaEngineerView.cs`、`src/Engineering/Tia/TiaOpenness.cs` | 工程窗由 DeskBox 主窗托管。后台读取结束后再打开选择和确认窗；失败的资料读取会保留旧绑定。打开的工程必须在发现后明确确认读取，缺少运行时、权限、连接掉线及无打开工程的情况显示各自的工作台状态。 |
| MCP 进程错误 | `src/Program.cs`、`src/Engineering/Tia/TiaMcp.cs` | 未处理的作业启动异常现在返回非零退出码，并经标准错误管道附加到当前作业的失败详情。非法作业和实际未安装 V20 分别进入 Windows 回归。 |

`4.3.0-alpha.6` 从 `c61da43` 构建并通过 181 项 TIA 回归与 70 项安装检查。回归在本机模型端核对了第二条请求包含上一轮需求及量程确认，也验证了继续解释上一版代码时源码不变。在 Windows 实测绑定窗所有者、取消操作、读取失败保留绑定、缺失 V20 的页内提示及损坏作业通过 MCP 标准错误返回。真实 Siemens 附加仍需许可环境复验。安装包已下载并与 SHA-256 清单核对。 [CI 日志](https://github.com/zong-technology/DeskBox/actions/runs/45412651856) · [Windows 安装包](https://github.com/zong-technology/DeskBox/actions/runs/45412651856/artifacts/14932130241) · [原生界面及任务证据](https://github.com/zong-technology/DeskBox/actions/runs/45412651856/artifacts/14931275319)
