# 页面与对话框清单

| 宿主 / 入口 | 实际表现层 | 现有内容与设计交接 |
| --- | --- | --- |
| 全局工作区 | `ReferenceWorkspace` / `MainForm` | 项目选择器、工作模式、一级模块、各模块的二级入口。 |
| AI 工程中心的工程会话 | `ChatView` | 会话列表、消息输出、附件、模型、停止、执行记录。兼容经典工作台。 |
| 工程会话中的执行记录 | `AgentTasksForm` | 按项目、AI 环境读取最近任务，选择查看步骤，刷新，停止所选活动任务。 |
| AI 工程中心的 MCP 管理 | `McpCenterView` | 本机 TIA 策略、一个私有进程探测、回执及第三方管理入口。 |
| 第三方 MCP | `McpGatewayForm` | 选服务、注册、编辑、启停、探测、工具目录、Schema、逐工具授权、参数、调用、取消及历史。 |
| MCP 注册／编辑 | `McpRegistrationForm` | 名称、HTTP 或 stdio、地址、stdio 参数、超时、保存和取消。 |
| 系统设置中的 AI 服务商 | `SettingsCenterView` | AI 环境、供应商配置、普通连接检查及独立的文本流探测。凭据输入保持遮挡。 |

[组件清单](COMPONENT-INVENTORY.md) · [其他模块状态](../V5-FUNCTION-MATRIX.md)
