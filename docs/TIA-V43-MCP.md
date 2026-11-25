# DeskBox V4.3 · MCP 与 V20 工程连接

续接 [V4.3 PR #4](https://github.com/zong-technology/DeskBox/pull/4) 的 `90c1681`。该基线的 Windows 安装和工作台回归已通过。源码使用 WinForms、C# 5 和 .NET Framework 4.8。AI 规划使用现有模型供应商，工程操作由本机 Openness 工作进程处理。基线通过作业文件与子进程交换请求和回执，TIA 源码中没有 MCP 客户端或服务器。基线验收使用导出夹具和本地测试模型，V20 原生操作仍待实际许可环境确认。 [基线报告](TIA-V43-PHASES-1-2.md)

## 源码能力矩阵

| 能力 | 基线源码 | 本分支状态与证据 |
| --- | --- | --- |
| WinForms 工程工作台，导出资料、目标绑定、SCL 编辑与差异 | `TiaEngineerView*`、`TiaProgramming.cs`、`PlcEngineering.cs` | 已实现且基线 Windows 回归通过。本分支 Windows 安装与工作台回归通过。 |
| MCP 协议与调用权限 | 未实现。Openness 子进程以本机 JSON 作业文件为入口 | 已实现私有 stdio MCP 2025-06-18 客户端与服务端。每次协商并发现当前作业授权工具，校验输入输出契约。已实现且 Windows 真进程协议回归通过。 |
| V20 实例、工程与 PLC 发现 | 仅通过 Windows 进程名和安装线索探测 | 已实现但尚未验证实际 Siemens 运行时。使用 `GetProcesses` 读取兼容实例和已打开的独立 `.ap20` 主工程。多个项目要求选择，附加前后重复核对 PID 和路径。 |
| 读取设备、OB/FB/FC/DB、PLC Tags | 原生独立副本走设备组、软件容器、块组、变量表。导出数据已回归 | 部分实现。已打开工程只枚举元数据。独立副本可请求正文导出。受保护块、可导出语言、实际 CPU 订货号及固件需要授权样本验证。 |
| 审批新增 FC/FB、编译、保存与回读 | 静态审批单块 FB。调用 `GenerateBlocksFromSource` 和 `ICompilable`，编译后检查内存中对象 | 已实现但尚未验证实际 Siemens 运行时。新增 FC 复用同一调用链。校验目标 PLC、源哈希、块名、块类型和同名冲突。真实编译通过后保存，并重新打开副本核对目标块语言、接口和导出正文。SCL 源与 SimaticML 的语义等价仍待验收。 |
| 现有 SCL 块替换、DB 写入、自动调用关系 | 草拟与分析 | 部分实现。已有块替换草稿保留。原生覆盖、创建和修改 DB、自动布置调用仍未实现。 |
| 文档、依赖图及 HMI/LAD/FBD | 依据导出资料提供部分拓扑及引用分析 | 部分实现。LAD/FBD 原生编辑、HMI 关联、完整 P2 检查仍待完成与验收。 |
| 作业状态、审计和恢复 | 绑定、生成、审核及最终作业记录。快照恢复到另一个目录 | 部分实现。编译、回读和已保存待核验的部分成功可由回执区分。MCP 审计与中断时恢复检查点已接入。统一状态机及现场异常验收仍待完成。 |
| 原工作台布局、分栏、DPI | 三栏工作台、需求输入、侧栏与输出折叠。基线 96、120、144 DPI 安装回归 | 已实现且基线 Windows 回归通过。本分支新增发现入口、TIA 与 MCP 状态及只读目标显示。Windows 安装回归通过。 |
| Windows V20、工程许可、实际离线 A–F | 托管回归在无 Siemens 的 Windows 上运行 | 当前环境不支持。未发现可用的持证测试机及授权 `.ap20`，真实工程操作、实际编译与故障用例保持 BLOCKED。 |

## 模块与调用链

| 模块 | 修改文件 | 实现、测试和下一步 |
| --- | --- | --- |
| MCP 执行 | `src/Engineering/Tia/TiaMcp.cs`、`src/Engineering/Tia/TiaOpenness.cs`、`src/Program.cs` | UI 审批 → 本地作业 → MCP 客户端 → 私有 `--tia-mcp` → `initialize` → `tools/list` → `tools/call` → Siemens Openness。一个子进程只授权一个预先落盘的作业。工具参数只有作业 ID，UI 批准的路径、PLC、源码、范围与指纹由服务器重新验证。进程退出、超时、协议异常和工具失败给出不同回执。回归验证真实进程协商、调用、收尾、审计，以及只读作业拒绝编译和错误 ID。 |
| 工程发现与工作区 | `src/Engineering/Tia/TiaEnvironmentInspector.cs`、`src/UI/Views/TiaEngineerView.cs`、`src/UI/Views/TiaEngineerView.Layout.cs` | 入口检测 TIA 进程，也支持手动刷新 → 发现 V20 主工程 → 必要时选择工程 → 只读附加 → 枚举设备、PLC、块与标签。项目列表使用 Siemens 报告的主工程路径。共享项目和同一实例中的次级工程还未接入。读到的活跃工程显示来源与 CPU。要编译或写入，先关闭源工程、读取隔离副本并显式绑定。 |
| 离线新增与回读 | `src/Engineering/Tia/TiaOpenness.cs`、`src/Engineering/Tia/TiaProgramming.cs`、`src/UI/Views/TiaEngineerView.Programming.cs`、`src/UI/Views/TiaEngineerView.cs` | 确认工程路径、PLC、块类型与差异 → 创建源快照和隔离副本 → 生成单块 → 编译并取诊断 → 校验变更范围 → 保存并重开 → 复读接口和正文。写入前落盘 `write-started.json`，中断时可用该文件从验证过的快照恢复到新副本。自动检查覆盖 FC 审批篡改、FC/FB 回读类型错配、恢复检查点。下一步在真实 V20 上比对编译及读回证据。 |
| 验收入口 | `tools/tia-engineer-accept.cs`、`tools/tia-v20-smoke.ps1` | Windows CI 运行自动回归。人工验收脚本支持已打开工程的 PID 和路径绑定，以及关闭后的单块审批、编译、重开和恢复。 |

## 服务端工具

| 授权范围 | 工具 | Openness 动作 |
| --- | --- | --- |
| 发现 | `tia_discover_projects` | `TiaPortal.GetProcesses` 和 `TiaPortalProcess.ProjectPath` |
| 只读 | `tia_read_open_project`、`tia_read_offline_copy` | 经确认的 `Attach` 或打开独立副本，枚举设备、块组和变量表 |
| 只读 | `tia_read_hardware_catalog` | 本机 `HardwareCatalog.Find` |
| 编译 | `tia_compile_offline_copy` | 目标 PLC 的 `ICompilable.Compile` |
| 新增及编译 | `tia_insert_scl_and_compile_copy`、`tia_insert_scale_and_compile_copy` | `CreateFromFile`、`GenerateBlocksFromSource`、编译、保存并重开副本 |

## Windows 构建与验收记录

`4.3.0-alpha.4` 对应产品提交 `b9ca0e6`。Windows Server 2025 build 26100 完成 70 项安装、启动、升级、回滚和卸载检查，以及 165 项 TIA 工作台回归。实测安装窗口 1920×1080，96、120、144 DPI 的实际窗口及输入验证有截图。150% 缩放下工具栏允许折行。微软拼音输入仍待测。 [CI 日志](https://github.com/zong-technology/DeskBox/actions/runs/45148291731) · [安装包与安装证据](https://github.com/zong-technology/DeskBox/actions/runs/45148291731/artifacts/14806015943)

安装包需要 .NET Framework 4.8，为未签名测试版。重新下载的 `DeskBox-4.3.0-alpha.4-Setup.exe` SHA-256 为 `d7aa46b0efc290860b31e201a5fc3c9d80c7d94074d60f2533c355925111aa5d`，与 CI 清单一致。

| 原验收项 | 结果 |
| --- | --- |
| A · 真实 V20 工程、CPU、块和变量发现 | BLOCKED。已验证真实 MCP 请求到达服务器。托管机无 Siemens 运行时，收到真实工具错误，未返回工程数据。 |
| B、C、E · 实际 FC 新增、编译、保存和回读 | BLOCKED。FC 审批与回读校验逻辑通过自动化测试。尚无 Siemens 写入或编译回执。 |
| D · 已有 FC 的原生修改 | 未实现。现有编辑仅生成差异与替换草稿。 |
| F · 工具与工程异常 | 部分通过。真实 MCP 进程拒绝越权操作及错误作业 ID，并在输入流结束后退出。快照恢复与审批失效通过回归。真实工程关闭、访问权限、重名、编译错误和故障后的恢复待持证测试。 |
| G · Windows UI | 当前安装回归通过。实际 V20、多工程选择及微软拼音仍待人工验证。 |

## 持证 Windows 验收

使用已打开的授权测试项目、Windows PowerShell 5.1、TIA Portal V20、匹配 PublicAPI、许可有效的 STEP 7 和具有 Siemens TIA Openness 组权限的账户。从 DeskBox 安装包或 CI 的 Windows 构建取得 `DeskBox.exe`。当 Siemens 防火墙弹窗出现时，确认正在连接的是本次测试的 DeskBox 进程。

```powershell
powershell -NoProfile -File tools\tia-v20-smoke.ps1 `
  -OpenProject 'D:\TIA-QA\Test\Test.ap20' `
  -EvidenceDirectory 'D:\TIA-QA\evidence-open' `
  -DeskBoxExe 'C:\path\to\DeskBox.exe' `
  -ExpectedDevice 'PLC_1' -ExpectedBlock 'Main' -ExpectedTag 'PressureRaw'
```

核对设备、块、标签和真实 CPU 类型。不同实例打开同一路径时，通过 `-PortalProcessId` 指定目标。随后关闭源工程，在新的证据目录用 `-ClosedOfflineProject` 读取并复制。新增测试使用工程中未占用的块名，`-TargetCpu` 使用只读返回的准确 PLC 路径，`-ApprovedSclFile` 和 `-ApprovedSclSha256` 对应已审批的文本。验证工作副本中新块、编译错误和警告、重开复读及恢复副本，同时核对原始目录的 SHA-256 清单。

每次调用在证据目录的 `tia-jobs/<id>/` 留下 `approved-operation.json`、`mcp-audit.jsonl` 和 `result.json`。尝试写入前另有 `write-started.json`。测试 B、C、E 需要实际 V20，测试 D 的原生覆盖仍缺少实现。测试 F 的断连、关闭工程、权限、重名和真实编译错误需在授权测试项目逐项复验。受控 V20 附加和离线副本作业没有 PLC 下载、CPU 控制或在线强制工具。

原生接口依据 [连接到 TIA Portal](https://docs.tia.siemens.cloud/api/khub/maps/Rc0LxW522mLHDCX_sejL~A/topics/adLw3rfN9TpyKEtxYa3ghA/content)、[从外部源生成块](https://docs.tia.siemens.cloud/api/khub/maps/Rc0LxW522mLHDCX_sejL~A/topics/kxNuDb_uRdA8BYt~s8WRXw/content)、[离线编译](https://docs.tia.siemens.cloud/api/khub/maps/Rc0LxW522mLHDCX_sejL~A/topics/DwXLzDT2glAOnCAxC4jvtA/content)和 [MCP 2025-06-18 工具规范](https://modelcontextprotocol.io/specification/2025-06-18/server/tools)。

## 已打开工程发现修复

用户反馈点击「发现打开的工程」后显示空引用异常。核对源码发现 V20 会话构造器忽略了会话模式。发现和附加也会尝试构造无界面的 TIA 实例，副本会话则没有标记工程所有权。

- 发现时只加载 Openness 并调用静态 `GetProcesses()`，处理空条目、失效实例和缺失接口。读取时验证 PID 和完整工程路径，经用户确认附加到所选进程并读取元数据。
- 附加会话结束后释放连接。独立副本仍由其专用会话关闭。回执保留失败阶段、完整异常和 MCP 审计路径，工作台「操作结果」可直接查看。
- Windows 回归使用测试宿主中的 Siemens API 签名夹具，重现发现、附加时错误构造无界面实例的路径。夹具覆盖静态发现、所选 PID 附加、空枚举结果和副本关闭行为。真实 V20 已打开工程仍需持证机器复验。 [Siemens 连接接口文档](https://docs.tia.siemens.cloud/api/khub/maps/Rc0LxW522mLHDCX_sejL~A/topics/adLw3rfN9TpyKEtxYa3ghA/content)

`4.3.0-alpha.7` 从 `21e89d7` 构建，通过 186 项 TIA 回归和 70 项安装检查，安装包已下载并校验 SHA-256。 [Windows CI](https://github.com/zong-technology/DeskBox/actions/runs/45555072419) · [测试安装包](https://github.com/zong-technology/DeskBox/actions/runs/45555072419/artifacts/14994824519)
