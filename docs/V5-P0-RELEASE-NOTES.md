# DeskBox 5.0.0-alpha.1 · 第一阶段候选版

Windows 产物对应 `89b4fd7999c10f4f8e9549c54be2d3ceb1be0717`，开发基线为 V4.3 `1930b40`。候选版保留 WinForms 和 .NET Framework 4.8 部署方式。

- 工作台新增项目切换器、12 个一级模块、工具抽屉、四种工作模式入口，以及以项目数据驱动的驾驶舱。
- 工程范围覆盖任务、验证进度、现场事件、索引和 PLC 资料。A/B 切换会取消旧 AI 请求，并撤销附件授权。
- 项目 AI 会话独立保存，默认只允许本机模型。无获授权模型时提供本地文件检索。
- PLC Studio 继续使用原有 Openness 和 TIA MCP 作业，新增项目路径边界及本地能力管理。DCS Studio 提供项目导出文本预览，验证中心复用现有进度与模板。
- 已通过 Windows 原生构建、安装、升级、回滚、卸载和离线 TIA 回归。实际 Windows 100%、125%、150% DPI 的运行图附在测试中。

候选安装包未签名。正式 V5.0 发布需完成剩余功能和外部条件验收。版本差异、当前已知问题和验证范围见功能矩阵与报告。

[源代码分支](https://github.com/zong-technology/DeskBox/tree/codex/deskbox-v5-p0) · [安装及升级包](https://github.com/zong-technology/DeskBox/actions/runs/45775554408/artifacts/15096656235) · [真实运行截图](https://github.com/zong-technology/DeskBox/actions/runs/45775554408/artifacts/15095812707) · [功能矩阵](V5-FUNCTION-MATRIX.md) · [测试报告](V5-P0-VERIFICATION.md) · [操作与回滚](V5-P0-GUIDE.md)
