# DeskBox 升级交付报告

> 交付日期：2026-11-01（Asia/Shanghai）
> 技术栈：C# WinForms / .NET Framework 4.8  
> AI 助手名称：AI&SI
> 版本：V2.1 Foundation Preview

## 1. 交付物

| 交付物 | 位置 |
|---|---|
| 完整源代码 | `D:\桌面\SureHand_Project\Deepseek\deskbox\src` |
| 新版 EXE | `D:\桌面\SureHand_Project\Deepseek\deskbox\dist\DeskBox.exe` |
| 发布副本 | `D:\DeskBox\DeskBox.V2.1.Foundation.exe`（与正式安装版哈希一致） |
| 构建脚本 | `build.ps1` |
| 安装/升级脚本 | `install.ps1`（保留用户数据与上一版 EXE） |
| 卸载脚本 | `uninstall.ps1` |
| 审查报告 | `docs\AUDIT-REPORT.md` |
| 本交付报告 | `docs\DELIVERY-REPORT.md` |

## 2. 本轮主要改动

- 修复重复系统监控/工作流调度与退出资源泄漏。
- 接通 AI&SI 的 `workflow.run` 工具与同一工作流引擎。
- 新增统一工作记录、通知中心、托盘气泡与全局任务提醒。
- 新增数据备份、有效性检查、下次启动恢复与恢复前安全快照。
- 自动化页面新增 v2 AI 总结/报告/任务通知模板、复制、启停、后台预演/执行与取消。
- 天气刷新周期和温度单位真实生效；旧缓存只在网络失败时降级使用并明确标记。
- 统一产品内助手名称为 AI&SI。
- 修复安装脚本覆盖用户数据的严重风险；`dist\data` 不再安装到用户目录。
- 新增默认工程驾驶舱、工程项目舱、全局命令矩阵和 `Ctrl+Alt+Space` 呼出。
- 新增 4–20mA、Pt100、压力/温度、S7 地址、Modbus Float、Ping/TCP 诊断工具。
- 新增 URS/DQ/IQ/OQ/PQ、CSV 风险评估、21 CFR Part 11 清单与验证任务生成。
- AI&SI 新增自动化、PLC、DCS、验证、故障诊断、通用工程六种模式，以及四个确定性工程工具。
- 新增 Cyber 工程矩阵主题与 1080p/2K 自适应卡片布局。
- Smart Desktop 新增 Known Folder/Public Desktop 聚合、内部文件夹导航、回收站、复制/剪切/粘贴、撤销、文件监视、拖放与 Details 视图。
- 新增异步 Windows Shell 缩略图，带有界 LRU、UNC/Offline 安全短路、mtime 失效和图标无闪烁回退。
- Mission Control 新增加权 Project Health、Project Pulse、Engineering Timeline 与 Next Action。

## 3. 测试结果

| 套件 | 结果 |
|---|---:|
| `v2-core-test.cs` | 66 / 66 |
| `tool-accept.cs` | 20 / 20 |
| `workflow-accept.cs` | 23 / 23 |
| `settings-accept.cs` | 31 / 31 |
| `layout-check.cs` | 10 / 10 |
| `ai-accept.cs` | 22 / 22 |
| `workflow-cancel-accept.cs` | 4 / 4 |
| `e2e-tool.cs` | 9 / 9 |
| `city-switch-accept.cs` | 22 / 22 |
| `p0-notification-accept.cs` | 14 / 14 |
| `backup-restore-accept.cs` | 12 / 12 |
| `automation-ui-accept.cs` | 10 / 10 |
| `weather-settings-accept.cs` | 5 / 5 |
| `engineering-accept.cs` | 32 / 32 |
| `engineering-ui-accept.cs` | 16 / 16 |
| `smart-desktop-accept.cs` | 31 / 31 |
| `smart-desktop-ui-accept.cs` | 20 / 20 |
| `project-control-accept.cs` | 16 / 16 |
| `thumbnail-accept.cs` | 43 / 43 |

合计：**406 / 406 项断言通过，0 项失败**。

另执行安装升级沙箱检查 3 项：用户数据保留、上一版 EXE 保留、新版 EXE 哈希一致，全部通过。

原生启动检查：正式安装版 `D:\DeskBox\DeskBox.exe` 已启动并持续存活，启动日志新增且最近 30 条中 error=0。

最终 EXE：584,704 字节；SHA-256：`006E803E4602F021D9950BE766D440AFB57AC1A9B09C83EDF4188D26904F9E0C`。

## 4. 构建与升级

构建：

```powershell
cd D:\桌面\SureHand_Project\Deepseek\deskbox
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

升级（先从托盘退出当前 DeskBox）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -InstallDir D:\DeskBox
```

升级行为：

- 不复制 `dist\data`，不覆盖现有 `D:\DeskBox\data`。
- 覆盖前把当前 EXE 保存为 `D:\DeskBox\DeskBox.previous.exe`。
- API Key 仍以当前 Windows 用户的 DPAPI 密文保存。

## 5. 数据备份与恢复

设置 → 系统 → 数据备份：

1. “立即备份数据”创建 `data\backup\manual_时间戳`。
2. “从备份恢复”先验证目录，再写入恢复请求。
3. 从托盘退出并重启；程序在加载 Store 前创建 `pre_restore_时间戳` 安全快照，然后原子恢复。
4. 换 Windows 用户或换电脑后，DPAPI 密钥无法解密，需要重新填写 API Key。

## 6. 已知限制

- 未提供真实 AI 服务商 Key；AI 协议与完整链路通过本地 OpenAI 兼容 mock，但不能替代真实厂商验收。
- 自动化页面是模板式 v2 编辑，不是任意节点图形编辑器。
- 文件变化/通用事件触发、周期任务、自动更新、动态缩放尚未实现。
- `index.json` 仍为约 10 MB 的单文件索引，启动加载可继续优化。
- 工程工具不会主动连接或修改生产 PLC/DCS；网络诊断仅测试用户指定的主机和端口，不做网段扫描。
- 正式安装已完成；`D:\DeskBox\DeskBox.previous.exe` 保留旧版，用户数据顶层条目安装前后保持 13→13。
