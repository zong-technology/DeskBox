# 版本变更记录

本文件记录交付和兼容性变化。受测源码、构建产物和回归报告见[版本证据](docs/V5-BUILD-PROVENANCE.md)。

## 未发布

- alpha.5.1 加入当前工程的校准到期提醒。通知实时汇总 30 天内到期、今日到期和逾期，保留清理状态并可直接打开筛选后的台账。

- 增加 alpha.5 首批仪表资产功能，接通项目仪表台账、Tag 关联、手册、校准维护、证明附件核验、到期筛选、双格式导出及恢复。 [仪表指南](docs/V5-ALPHA5-INSTRUMENTS.md)
- 新增 ECO × DeskBox 通信协议 V1.0 草案、JSON Schema、示例与进程内契约测试。正式传输与双方联调待确认。
- 增补 DeskBox 长期持续开发规范 V1.0，记录独立操作、工程安全及 ECO 可选接入的接口现状、缺口与验收顺序。本次变更范围为项目文档。
- 将通用软件开发规范 V2.0、DeskBox 项目边界与后续上下文索引纳入仓库。此项只涉及文档。

## 5.0.0-alpha.4

- 新增验证计划、需求导入和修订、RTM、风险和测试方案。测试执行保留当时的用例和步骤版本。
- 新增按项目归档的证据、衍生版本、SHA-256 校验与恢复，以及偏差、复验和人工审核。通过复验后原执行的开放偏差继续保留在 RTM。
- 新增基线、变更影响、业务统计及 XLSX／DOCX 草稿输出。验证查询在本机运行。既有验证进度及工程资料服务继续保留。
- [验证指南](docs/V5-ALPHA4-GUIDE.md) · [Windows 原生验收](docs/V5-ALPHA4-VERIFICATION.md)

## 5.0.0-alpha.3

- 新增 PLC 与 FHX 离线资料分析、统一 Tag 与工程差异，以及按权限联动的 Agent/MCP 调用。 [验收](docs/V5-ALPHA3-VERIFICATION.md)

## 5.0.0-alpha.2

- 独立 Agent Runtime、第三方 MCP Gateway、任务状态、项目隔离与受控权限。 [验收](docs/V5-ALPHA2-VERIFICATION.md)

## 5.0.0-alpha.1

- 增量建立 V5 工程上下文、导航和工程入口，覆盖安装回退和主要页面。 [验收](docs/V5-P0-VERIFICATION.md)
