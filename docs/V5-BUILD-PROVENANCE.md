# DeskBox V5 构建与文档证据

| 版本 | Windows 应用、安装与验收使用的源码 | 文档及 UI 交接 | 运行证据 |
| --- | --- | --- | --- |
| 5.0.0-alpha.1 | `89b4fd7`，从 `1930b40` 增量开发 | `8f00f9a` 的指南、审查文件、矩阵与报告 | [Windows CI](https://github.com/zong-technology/DeskBox/actions/runs/45775554408) · [验收报告](V5-P0-VERIFICATION.md) |
| 5.0.0-alpha.2 | `3047cb71b7b4ef358ed2321583c86d107da0f4a3` | `9a6e77afe28e6ff7705b7c7ec0e7cf4f4ea3da35` 的矩阵、测试报告与 14 份 `docs/UI-HANDOFF/` 文件 | [Windows CI](https://github.com/zong-technology/DeskBox/actions/runs/45863717693) · [alpha.2 验收](V5-ALPHA2-VERIFICATION.md) · [当期文档](https://github.com/zong-technology/DeskBox/tree/9a6e77afe28e6ff7705b7c7ec0e7cf4f4ea3da35/docs/UI-HANDOFF) |
| 5.0.0-alpha.3 | `55fb00393322a309799910e7fde1f7c270c1d382` | 本版验收及 14 份 UI 交接文件随文档提交归档 | [Windows CI](https://github.com/zong-technology/DeskBox/actions/runs/45987405048) · [alpha.3 验收](V5-ALPHA3-VERIFICATION.md) |

## alpha.2 安装哈希复核

| 文件 | SHA-256 |
| --- | --- |
| `DeskBox-5.0.0-alpha.2-Setup.exe` | `1d5f9d3cee160b94b4ce75dd06e1881942025c687adde807f751e5eb1642bdf3` |
| `DeskBox.exe` | `ae0cdb9744257e6ae048823695990fb5c87ca2fd63daff86b319acb757850f03` |

alpha.3 开始前从开发分支得到了上述文档提交，并对工作区已归档的两个 Windows 文件重新执行了 SHA‑256 校验。受测源码提交中的历史矩阵保留当时状态。alpha.2 功能增量与 UI 交接以随后的文档提交为准。

原 14 份交接路径位于 `docs/UI-HANDOFF/`，包括 PRODUCT-OVERVIEW、FEATURE-INVENTORY、PAGE-INVENTORY、SERVICE-CONTRACTS、DATA-MODELS、COMMANDS、EVENTS、USER-FLOWS、COMPONENT-INVENTORY、UI-STATES、ERROR-STATES、NAVIGATION-MAP、DESIGN-CONSTRAINTS、ACCEPTANCE-CRITERIA，每项为 Markdown。

[alpha.2 安装产物](https://github.com/zong-technology/DeskBox/actions/runs/45863717693/artifacts/15140640651) · [完整功能矩阵](V5-FUNCTION-MATRIX.md)
