# DeskBox 5.0.0-alpha.3 Windows 验收

## 构建和交付

最新开发基线为 `9a6e77a`，已通过的 alpha.2 Windows 构建来自 `3047cb7`。alpha.3 源码、编译和验收使用 `55fb00393322a309799910e7fde1f7c270c1d382`。Windows Server 2025 原生 .NET Framework 4.8 和 Inno Setup 构建通过。Linux net48 交叉构建也通过，0 错误、5 项已知编译警告。

| 产物 | SHA-256 |
| --- | --- |
| `DeskBox-5.0.0-alpha.3-Setup.exe` | `4fdff5df26c5e91a55454438d6cbf6219d71081caa7470b154d4b85097a11667` |
| `DeskBox.exe` | `b7b1137834e5e0b365f2cc98f665119331b8df64c444fa7b661b380ce4a79e6a` |

候选安装包未签名。安装、升级、回滚及卸载在独立 Windows 环境通过 85 项。回退使用产品旧版形态的独立二进制夹具，验证了原数据、安装身份、回退副本及本机启动。安装和便携包采用上表同一 Windows 可执行文件。

[Windows Actions](https://github.com/zong-technology/DeskBox/actions/runs/45987405048) · [安装与 SHA-256](https://github.com/zong-technology/DeskBox/actions/runs/45987405048/artifacts/15191859358) · [安装证据](https://github.com/zong-technology/DeskBox/actions/runs/45987405048/artifacts/15192880463)

## 业务与回归结果

| 场景 | 结果与证据 |
| --- | --- |
| A · AI 工程资料比较、MCP 调度 | Windows 与 Linux 协议套件共 70 项。回环模型夹具请求工程对比，实际文件差异及原始来源进入后一轮请求。实际 MCP 工具发现、Schema 材料、单次确认、调用回执、参数拒绝、授权撤销、断流、断线和取消均有单独断言。 |
| B · PLC 导出和 TIA | 186 项既有 TIA 离线回归。alpha.3 补充 SCL 的 OB/FB/FC/DB、块体差异、SimaticML 变量和原有 TIA 作业回执格式读取。厂商编译记录按源文件展示。 |
| C · DeltaV 离线解析 | 工程数据套件的 FHX 夹具覆盖嵌套对象、UTF‑16、未知对象、定位、字段差异、编码错误、身份重复、重读缓存和 24000 个对象的结构读取。 |
| D · 两版 IO List 与 Tag | 覆盖 XLSX／CSV、列映射、工程单位和字段冲突、历史版本、来源定位及差异报告。Linux 另使用 openpyxl 3.1.5 生成多工作表 XLSX 作为独立输入并反向打开 DeskBox 导出文件。 |
| E · 跨模块统一 | PLC XML、DeltaV FHX 与 IO List 输入关联到同一稳定 TagId。相同项目显示名、相同文件名和相同 Tag 名都有隔离断言。 |
| F · 取消、并发与损坏恢复 | 工程数据套件共 48 项，覆盖跨项目拒绝、原文件保护、并发预览、已变更源文件、无效数据、非覆盖导出、取消、损坏校验、写入阻塞及原子恢复。MCP 断线、取消、授权撤销由 AI／MCP 套件验证。 |

Linux 独立互操作使用官方 MCP Python SDK 1.29.0 作为 Streamable HTTP 服务，完成按实际 Schema 拦截无效参数及合法调用，收到了结构化结果。协议夹具和 SDK 样例均使用专门测试数据。模型协议结果不代表商业模型效果或正式工程准确性。

首次 Windows 验收发现深路径下原子临时文件采用过长文件名。最终候选以较短同目录路径修复，重跑了同一深路径用例及后续工程、UI、安装回归。 [首次失败日志](https://github.com/zong-technology/DeskBox/actions/runs/45987126865)

[原生程序与业务日志](https://github.com/zong-technology/DeskBox/actions/runs/45987405048/artifacts/15193030366) · [工程格式兼容矩阵](V5-DELTA-V-FHX-COMPATIBILITY.md)

## Windows 界面实测

1366×768、1600×900、1920×1080 在 100%、125%、150% 三档进行原生运行验收，每档 114 项。归档 129 张 WinForms 截图，包括三档下的 PLC 资料入口、DCS 结构化预览和统一 Tag。150% 下复核了三个新增窗口，字段、位置、按钮和状态可见。1920×1080 以上及 200% DPI 待专用设备。 [Windows UI 截图和原始日志](https://github.com/zong-technology/DeskBox/actions/runs/45987405048/artifacts/15191828909)

## 仍需真实环境

- TIA Portal V20 需持证、授权的隔离项目，才能验证真实实例、项目识别、CPU 对象和编译回执。本次无生产设备连接。
- DeltaV 发行版本与厂商扩展缺经授权的真实样本。当前只证实所列语法、对象、位置及未知数据处理。
- 商业模型效果和远程第三方实际服务需要授权账户和样本。OAuth 及远程模型介导的第三方调用尚未接入。

[功能矩阵](V5-FUNCTION-MATRIX.md) · [指南](V5-ALPHA3-GUIDE.md) · [源代码与文档版本对应](V5-BUILD-PROVENANCE.md) · [alpha.4 路线](V5-DEVELOPMENT-ROADMAP.md#alpha4-工程交付顺序)
