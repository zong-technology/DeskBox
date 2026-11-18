# AI 操作与折叠工具结果：发布验收

> 本页保留 2026-11-17 的 Windows 9 / iPhone 0.9.0 历史验收；当前 Windows 13 / iPhone 0.11.0 的下载与证据见 [本轮验收](AI-ENVIRONMENT-RELEASE-REVIEW.md)。

2026-11-17：Windows `2.1.0-hologram.9` 与 iPhone `0.9.0`（build 10）均已通过对应云端检查并发布测试安装包。仓库公开后，标准 GitHub Windows/macOS 构建机已实测可用，后续流程仍为手动触发。

| 产物 | 下载与来源 |
|---|---|
| Windows 安装升级包 | [测试版 9 Release](https://github.com/zong-technology/DeskBox/releases/tag/v2.1.0-hologram.9)，源码 `b7c0c457f21eac60fccd85a4ef609eb3c134b4e3` |
| iPhone IPA | [0.9.0 Release](https://github.com/zong-technology/DeskBox/releases/tag/v0.9.0-mobile-hologram)，源码 `f21acaacc56b2e53c1774c709a677d9e51057d34` |
| 最新完整源码 | [开发分支](https://github.com/zong-technology/DeskBox/tree/feat/holographic-ai-call)，尚未合入 main |

两个构建提交的 `mobile/DeskBoxMobile` 目录完全一致；后续 Windows 补丁没有改变本次 iPhone 产物源码。

## 实际完成的功能

- 工具调用与结果默认折叠。手机展开后先显示中文清单，原始 JSON 另行展开；旧通话记录与归档文字对话均可显示。展开、原始结果开关与收起使用独立按钮，避免点击区域随内容变化。
- 手机全息可直接新建和修改任务、记录现场问题及更新处理措施，使用原有离线存储与同步队列。项目名不唯一时拒绝猜测，修改前检查最新快照；重复函数 ID 不重复创建。
- 保存、等待同步与失败分别返回。手机保存不等于电脑已经收到；保存失败回退内存修改，重启可恢复已保存记录和上传队列。
- 普通 AI 和电脑全息共用注册工具。手机全息通过现有配对账号的任务通道调用在线电脑，支持授权文件、文档、页面和已配置工作流。直接转发工具不需要额外请求文字模型，保留原有文件授权、确认与版本检查。
- 长操作移出语音接收循环；工具正文限制长度并标记截断。旧操作还在取消或确认时，新指令返回忙碌状态，保持通话连接，避免重复执行。中断、退出后的迟到结果仍保存到手机记录；取消不能撤销已经完成的修改，结果不明时先查询。

## 验证证据

| 检查 | 结果 |
|---|---|
| Windows 全息、工具卡片点击、取消中收到新指令 | 原始及安装后载荷各通过 126 项检查 |
| 配对手机任务通道的实际操作 | 14 项通过：任务/问题保存、项目绑定、版本校验、授权文件、取消与保存失败回退 |
| 文字 AI 多轮工具闭环 | 本地模拟模型，12 项通过；先查询超滤项目，再真实创建 SIP 的 FS 步骤任务及备注 |
| Windows 安装、升级保留、回退、卸载与运行身份 | 47 项通过；三个尺寸各完成 12 页键盘导航，实际 96 DPI |
| iPhone 原生音频 | 49 项通过，AVAudioEngine 离线渲染 |
| iPhone 离线任务、事件、冲突及连接规则 | 43 项通过 |
| iPhone 协议、记录、实际本机操作及动态状态 | 54 项通过 |
| iPhone 原生界面 | 两种尺寸各 3 项交互通过，零失败；50 个页面截图及对应路由标记校验通过 |

[Windows 完整运行记录](https://github.com/zong-technology/DeskBox/actions/runs/44071192151) · [iPhone 完整运行记录](https://github.com/zong-technology/DeskBox/actions/runs/44070344625)。截图与日志在运行附件中按保留期提供，安装包长期放在 Releases。

已重新下载两个发布文件并核对 SHA-256：

```text
d8916f38b57c6bc7774a5f0af911ff004df29f248f4a2b0106ff8843b32c41ab  DeskBox-2.1.0-hologram.9-Setup.exe
741fc1015cd3ff4afb632512e922efd5096555ae1497d6b8c59ff0418564b3d6  DeskBoxMobile-0.9.0-unsigned.ipa
```

IPA 内部已核对为实际 iPhoneOS arm64 可执行文件，版本 0.9.0/build 10、Bundle ID `com.deskbox.mobile`、最低 iOS 17.0。它未签名，需要沿用原签名团队及 Bundle ID 签名后覆盖安装，保留资料，不要先卸载。Windows 同样先退出旧版再覆盖安装。

全部自动化使用合成数据、模拟模型或离线音频，不读取生产项目、不调用付费语音服务。真机千问/Gemini、麦克风、蓝牙、私人账号电脑联动及 Windows 125%/150% 缩放仍需目标设备实测。电脑文件与工作流操作需要对应电脑在线及已有授权；iOS 本地功能遵循系统沙盒。
