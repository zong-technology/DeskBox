# DeskBox 产品交接

DeskBox 面向制药设备自动化、PLC、DCS、现场调试与验证工程师。顶部项目选择器决定本轮会话、文件检索、工程作业与事件的范围。未绑定工程是独立范围，保留旧数据和全局工具。

alpha.2 版交接包含桌面文字 Agent、第三方 MCP、本机 TIA MCP 与现有工程导航。第三方 MCP 供操作者按项目注册、发现和人工执行只读工具。Agent 使用 DeskBox 已注册的工具和现有工程权限。项目的外部 AI 权限与 MCP 服务授权分别配置。

现有 WinForms 保留。后续表现层重构可以复用业务入口、业务状态和标识。功能及服务接口在 beta.1 冻结后交给视觉设计阶段。

[功能清单](FEATURE-INVENTORY.md) · [导航](NAVIGATION-MAP.md) · [验收](ACCEPTANCE-CRITERIA.md)

## 5.0.0-alpha.3

项目中心、PLC Studio 和 DCS Studio 共用工程文件服务和统一 Tag 台账。资料、来源、差异、审核前导入和版本恢复使用相同的工程范围。已许可的本机 MCP 只读工具可以由桌面 Agent 申请，随后由操作者单次授权。

[工程数据使用](../V5-ALPHA3-GUIDE.md) · [版本证据](../V5-BUILD-PROVENANCE.md)

## alpha.4 验证工作台

以当前项目的需求、设计依据、风险、测试、证据、偏差和交付资料形成验证记录链。工作台通过稳定标识衔接统一 Tag，通过文件 SHA-256 和原文位置衔接离线工程资料。现有阶段进度继续用于计划跟踪。

人工作出实际测试和审核决定。报告及变更影响都是工程准备材料，用于复核、交付及历史回查。

[验证工作指南](../V5-ALPHA4-GUIDE.md) · [统计定义](../V5-VALIDATION-METRICS.md)
