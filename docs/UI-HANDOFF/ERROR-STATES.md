# 错误处理和反馈

| 错误类别或场景 | 展示与恢复 |
| --- | --- |
| Agent `protocol` / `output_limit` | 说明缺少文本、结束帧或服务商报告截断。保留已有文本与步骤。 |
| Agent `permission` / `tool_failed` | 展示该工具失败。若完成最终回复，保留部分完成状态。 |
| Agent `timeout` / `cancelled` | 解释网络空闲、步骤上限或人工取消。保留已收到片段。 |
| Agent `auth` / `model` / `network` | 在模型设置中核对配置并再次测试。凭据不显示在单次任务回执。 |
| `storage` / 恢复 | 暂停发起新的外部操作，展示写入或读取问题。必要时使用既有备份恢复。 |
| MCP `policy` | 检查工程、服务启停、工具授权、人工确认、AI 环境或动态 Schema。 |
| MCP `arguments` | 工具参数须为 JSON 对象；详情按实时 Schema 和实际服务端响应定位。 |
| MCP `auth` / `http` / `rpc` | 展示状态码。重新核对端点和本窗口令牌。 |
| MCP `protocol` / `response_limit` | 显示协商、不匹配的 JSON-RPC 或超出目录/内容边界。更改服务配置后手动检测。 |
| MCP `tool_error` | 显示服务实际返回的 isError 和本次原始响应。 |
| MCP `timeout` / `cancelled` / `disconnected` | 调用记录保留发送标志。已尝试发送时需核对服务端，以免重复触发。 |
| MCP 并发和旧配置冲突 | 等待或取消当前操作，重新载入服务和目录后再编辑。 |

[可见状态](UI-STATES.md) · [操作流程](USER-FLOWS.md)

## alpha.3 工程错误

- 源文件超出项目、哈希变化、路径不可读或经过重解析目录时拒绝访问。重新选择授权工程中的对应版本。
- FHX 未知对象需要显示类型与原文定位。括号、引号或身份问题提供阻塞诊断。
- 台账主文件损坏、未来数据版本、导入来源改变和并发编辑各自提示。恢复仅在存在有效备份时写入。
- MCP 的 `arguments` 指向不符合 Schema 的字段，`schema` 表示未能验证的方言，`policy` 表示项目、用户许可、实时 Schema 或权限不符。任一情况均不执行工具。
