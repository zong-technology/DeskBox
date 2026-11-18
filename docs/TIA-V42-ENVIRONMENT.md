# DeskBox V4.2 · 博图环境与官方接口

当前工作机为 Linux x64。检查了源码、已装 SDK 和执行环境，没有 TIA Portal、STEP 7、Openness 或 PLCSIM 可供调用。DeskBox 是原生 WinForms，目标 .NET Framework 4.8，按 C# 5 构建。`build.ps1` 使用 Windows Framework 编译器，`DeskBox.Build.csproj` 使用固定参考程序集进行 SDK 编译，安装器使用 Inno Setup。

桌面已有 `AiClient`、`ProviderStore` 和 AI 环境隔离。密钥由 DPAPI 保存。文件中心通过现有文件索引与用户主动选取接入，模型文件访问遵循现有隐私设置。工程工具复用 `EngineeringTools`，项目用 `WorkspaceStore`，工作记录和待办用 `Store`。桌面源码未引用 WebView2。`docs/V4.1-IMPLEMENTATION.md` 记录了既有 Figma 参考与未取得的属性，本模块使用原生控件和现有 Palette。实时语音与文字使用同一工具白名单。

## 安装探测

检测器读取 32 位及 64 位 Windows 安装登记、Program Files 下 V18–V20 的 PublicAPI 目录、Openness 用户令牌、进程列表、PLCSIM 和硬件支持包路径线索。安装名称用于识别 STEP 7 Basic/Professional 的登记，实际许可证以 Windows 上的 Automation License Manager 与 TIA 启动结果为准。检测不推断 HSP 已安装或某型号兼容。V20 目录查询由 `HardwareCatalog.Find` 返回本机条目，按订货号与固件核对。

## 能力矩阵

| 操作 | Siemens V20 手册接口 | DeskBox 首版 | V18/V19 |
|---|---|---|---|
| 实例 | `TiaPortal(TiaPortalMode.WithoutUserInterface)` | 启动专用实例。检测运行 PID，不附加用户会话 | 探测安装，未接入 |
| 创建与打开 | `Projects.Create`、`Projects.Open` | 打开独立 `.ap20` 的副本，无工程升级或受保护项目代登录。创建空工程尚未接入 | 探测格式，未接入 |
| 设备与软件 | `Devices`、`DeviceItems`、`SoftwareContainer` | 递归枚举，标出返回的 PLC 和对象范围 | 未测试 |
| 块、类型及变量 | `BlockGroup`、`TypeGroup`、`TagTableGroup` | 读取导出的块结构及变量。块正文受保护或不支持时显示缺口 | 未测试 |
| 工程导出 | `Export(FileInfo, ExportOptions)` | 有界 SimaticML 导出，正文仅用于审查 | 未测试 |
| SCL 新增 | `CreateFromFile(name, path)` 与 `GenerateBlocksFromSource()` | 仅经审批、未修改的缩放 FB。手册指出生成会覆盖同名块，因此先检查无同名对象 | 未测试 |
| 硬件目录 | `TiaPortal.HardwareCatalog.Find(query)` | 查询本机订货号、类型标识和目录。组态写入尚未接入 | 未测试 |
| 离线编译 | `ICompilable.Compile()` | 编译明确选择的 PLC，读取嵌套诊断。编译结果、仿真和工程师审核分别标记 | 未测试 |
| 保存和关闭 | `Project.Save()`、`Project.Close()` | 只在批准的新增块通过编译并复读后保存独立工作副本，始终关闭专用会话 | 未测试 |

上述 V20 连接层依据官方文档实现，必须在真实 Siemens 安装上运行样本工程验证。检测成功、反射绑定成功、编译成功与现场验收各为不同检查。

官方依据：[Siemens V20 Openness 手册](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows) · [项目打开](https://docs.tia.siemens.cloud/api/khub/maps/Rc0LxW522mLHDCX_sejL~A/topics/bj_3j5_TiiIIs0wEhO0JHQ/content) · [工程编译](https://docs.tia.siemens.cloud/api/khub/maps/Rc0LxW522mLHDCX_sejL~A/topics/DwXLzDT2glAOnCAxC4jvtA/content) · [外部源导入](https://docs.tia.siemens.cloud/api/khub/maps/Rc0LxW522mLHDCX_sejL~A/topics/7uZIiuJJ0sZyioGHa_qBEw/content) · [从外部源生成块](https://docs.tia.siemens.cloud/api/khub/maps/Rc0LxW522mLHDCX_sejL~A/topics/kxNuDb_uRdA8BYt~s8WRXw/content) · [硬件目录](https://docs.tia.siemens.cloud/api/khub/maps/Rc0LxW522mLHDCX_sejL~A/topics/wGo9C06GmdclJwfMLN33Pw/content)。

## 持证机器原生验收

用关闭后的独立 S7-1200 或 S7-1500 V20 样本和已加入 Openness 用户组的 Windows 测试账户运行。脚本生成独立证据目录，记录枚举出的精确 PLC 路径。首次指定源文件和证据目录，可同时用 ExpectedDevice、ExpectedBlock 和 ExpectedTag 核对从原 TIA 工程抄录的唯一对象名。检查输出与 TIA 中的设备、块、类型和变量数量一致后，可用新证据目录、`-TargetCpu` 和 `-Compile` 验证离线编译。要单独验证新增缩放模板，使用 `-ApprovedNewScaleBlock` 指定新块名并检查回执、差异、源 SHA-256 清单和工作副本。脚本通过真实连接器重新打开保存后的副本，检查差异文件并核对源目录的 SHA-256 清单。

```powershell
powershell -NoProfile -File tools\tia-v20-smoke.ps1 `
  -ClosedOfflineProject 'D:\TIA-QA\S7-1200\Example.ap20' `
  -EvidenceDirectory 'D:\TIA-QA\evidence-inspect'
```

`tools/tia-v20-smoke.ps1` 的语法已检查。当前执行环境缺少 Siemens 运行时，尚未运行该脚本的原生工程流程。
