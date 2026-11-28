# 命令与动作

## AI

- 发送消息。Enter 发送，Shift+Enter 换行。任务运行时使用「停止」。
- 重生成从当前会话最后一条用户消息启动新的 Agent 任务。历史记录保留原任务元数据。
- `本地查找 关键词` 明确走本地文件索引，即使已配置模型。没有可用模型时也可直接输入文件名关键词，或者 `查找：关键词`。
- `工具 {"name":"eng.scale_4_20","args":{"mA":12,"low":0,"high":100}}` 执行工程换算。另支持 `eng.s7_address` 的 `address`、`eng.modbus_float` 的 `register1`、`register2` 和可选 `order`。
- 「执行记录」打开当前工程与 AI 环境的任务列表。刷新后选择记录即可查看步骤。停止操作只针对仍在运行的任务 ID。
- 设置中的「测试连接」保留普通请求检查。「检测文本流」发送一条固定的短模型探针。

## MCP

- 注册或编辑服务，按操作系统输入 HTTP 端点或可信本机可执行文件及参数。
- 选择服务后启用，输入可选令牌，点击「检测并发现工具」。需要重新连接时再次检测。
- 对照真实 Schema 和服务文档，授予选中的只读工具权限。撤销菜单列出已保存的授权，服务断开后仍可撤销。
- 在参数框输入 JSON 对象，点击「人工调用」并确认。取消正在等待的调用后，核对回执的发送标志。
- 「调用记录」展示当前所选服务的持久化回执。
- 「检测 MCP 协议」是本机 TIA 私有服务的独立检测。PLC Studio 中的作业由当前项目和工程策略授权。

[流程](USER-FLOWS.md) · [接口](SERVICE-CONTRACTS.md)

## alpha.3 命令

- `engineering.compare {before,after,worksheet?,encoding?}` 在当前项目和已授权路径查找两份资料，返回可追溯差异。路径支持工程相对或绝对。
- `engineering.tags {query?,system?}` 查询当前项目 Tag 台账，返回记录数、稳定 TagId、真实字段和来源。
- `mcp.read {server,tool,arguments}` 仅可申请本轮公布的工具。arguments 是 JSON 对象，按实际 Schema 校验。调用前请求人工确认。
- 资料工作台的「映射并预览」只读，「导入／补充来源」和「确认更新映射字段」才写入。选择不同项目会撤下当前操作范围。

## alpha.4 业务动作

- **录入和修订** 保存需求、验证资料、计划和测试设计。提交时传入预期版本。
- **导入预览和确认** 选择当前项目的 Excel、CSV 或 Word 资料，核对字段映射、定位和重复项后确认正式写入。Word 提取候选可按原文复核。
- **关联、执行、归档** 按记录 ID 建立关系。执行或重测时填写实际结果、人员及关联。选中证据后可验证文件或定位原件。
- **人工审核** 按当前审核许可提交审核结论。关闭偏差时读取关联复验及审核记录。
- **导出** 从项目记录生成 RTM、执行结果、交付清单或报告草稿。显式选定目标，新文件名保留历史输出。
- **AI 查找** `validation.query {view,query?,id?,stage?,system?,equipment?,tag?,risk?,fromRevision?,toRevision?}` 从当前项目查询需要的记录。可用视图为 `requirements`、`testcases`、`rtm`、`uncovered`、`evidence`、`missing-evidence`、`open-deviations`、`history`、`tags`、`impact`、`deliverables` 和 `summary-draft`。本机回环模型可调用，也可通过「工具」显式提交查询 JSON。
