# 附件读取、任务同步与操作回执：本轮验收

> 本页为 Windows 10 / iPhone 0.10.0 历史验收；当前 Windows 13 / iPhone 0.11.0 已增加手机独立 AI 和本地 / 网络环境，见 [本轮验收](AI-ENVIRONMENT-RELEASE-REVIEW.md)。

2026-11-18：Windows `2.1.0-hologram.10` 与 iPhone `0.10.0`（build 11）均已完成原生检查并发布。上一轮的全息操作及折叠工具结果继续保留，见 [历史验收](AI-OPERATIONS-RELEASE-REVIEW.md)。

| 产物 | 固定源码与下载 |
|---|---|
| Windows 安装升级包 | [测试版 10](https://github.com/zong-technology/DeskBox/releases/tag/v2.1.0-hologram.10)，源码 `da0c34e9059a2391076d6196280f6b79f74f67c0` |
| iPhone IPA | [0.10.0 / build 11](https://github.com/zong-technology/DeskBox/releases/tag/v0.10.0-mobile-hologram-build11)，源码 `811b47f0912897b596462c4295219b41d9a68a2d` |
| 最新完整源码 | [开发分支](https://github.com/zong-technology/DeskBox/tree/feat/holographic-ai-call)，尚未合入 main |

## 本轮完成

- 手机文字 AI 可以选择 PDF、图片或文本附件，使用原生 PDFKit/Vision 本地提取文字；发送前可预览和移除。键盘打开时收起为文件条，保留输入和发送空间。提取文字及文件名经过现有电脑中继供 AI 分析，随聊天和归档记录保存。
- 每条一个附件，文件最大 10MB；PDF 前 8 页、最多 12000 个 UTF-16 单位，超限会明确提示。发送内容同时校验服务端真实消息及 JSON 长度限制，不截断代理对或把文件名伪装成电脑上的可读路径。图片支持文字识别，未提供图片构图分析。
- 任务负责人、备注和修改时间在手机及电脑间保留；按负责人检索可用。语义相同但字段排列不同的 JSON 不会制造新版本或假冲突。电脑重启后、首次发布快照前删除任务也保留删除标记。
- 手机删除任务前核对当前记录；确认期间被改动的任务不会误删。保存失败会恢复任务、上传队列和冲突状态，并显示错误；重启仍保留已保存的删除标记。
- 电脑全息修改工具保存最近 128 次请求的回执，同一实例内重发相同请求不会重复写入；复用 ID 却改变参数会报错。查询继续读取最新数据。这不是跨进程的永久去重数据库。
- 手机发布支持复用已通过检查的原生安装包，先上传草稿、核对四个附件的哈希再公开，适配 GitHub 不可变发布。已锁定而缺少文件的旧页面保留，使用同版本的 build 标签恢复，不修改已发布附件或重新执行 Xcode。

## 检查与来源

| 检查 | 结果 |
|---|---|
| Windows 全息、工具与重复请求 | 原始及安装后载荷各 128 项通过 |
| 手机通道实际应用操作 | 14 项通过 |
| 任务元数据、版本与重启删除同步 | 11 项通过 |
| 文字 AI 工具及附件正文中继 | 13 项通过，本地模拟模型 |
| 原有手机桥接 / 冲突 / 云端账号回归 | 7 / 14 / 16 项通过 |
| Windows 安装、升级保留、回退、卸载 | 47 项通过；三种窗口尺寸各 12 页键盘导航，实际 96 DPI |
| iPhone 离线、事件、任务及冲突规则 | 51 项通过 |
| iPhone 协议、历史、工具及动态状态 | 56 项通过 |
| iPhone PDF/图片/文本原生提取及请求限额 | 13 项通过，真实合成文件及本地 OCR |
| iPhone AVAudioEngine 离线音频 | 49 项通过 |
| iPhone 原生 UI | 两种尺寸各 4 个交互测试、零失败；54 个页面截图及路由标记通过 |

[Windows 发布构建](https://github.com/zong-technology/DeskBox/actions/runs/44215028468) · [Windows 额外同步兼容检查](https://github.com/zong-technology/DeskBox/actions/runs/44215718887) · [iPhone 原生构建和检查](https://github.com/zong-technology/DeskBox/actions/runs/44214054323)。Windows 额外检查使用 `ac3e337bca12b60b5adac2772087418cb04efd50`，与安装包源码仅相差工作流及文档，产品代码相同。手机后续发布工具和 Windows 修复没有改变已验证的手机产品代码。

手机原生构建的两个检查任务成功，原发布任务返回 403。最终 [恢复发布运行](https://github.com/zong-technology/DeskBox/actions/runs/44218717532) 成功复用这些固定产物，校验四个附件哈希后公开。原 `v0.10.0-mobile-hologram` 无附件页面已被 GitHub 锁定，新安装包放在 `v0.10.0-mobile-hologram-build11`，内部版本仍为 0.10.0/build 11。发布工具另以真实产物和模拟 GitHub 接口通过 9 种恢复/拒绝场景，包括无标签草稿、不可变页面、相同文件重试、错误源码、失败原生检查和错误校验和。

安装包 SHA-256：

```text
96b3494c4084a98d6d5a344f92b46e3d4bc169c9595901c64df033e2f3a268a0  DeskBox-2.1.0-hologram.10-Setup.exe
7765f6ab209d599e067b20e82a800afcd8f6a4b2249fbe408d5f7e188344d327  DeskBoxMobile-0.10.0-unsigned.ipa
```

IPA 已核对为 iPhoneOS arm64，Bundle ID `com.deskbox.mobile`，最低 iOS 17.0。需要沿用原签名团队与 Bundle ID 签名覆盖安装，不要先卸载。电脑先从托盘退出旧版，升级时选择原目录。

全部自动化使用隔离的合成数据、模拟模型或离线音频，不读取生产项目、不调用付费语音服务。真机千问/Gemini、麦克风、蓝牙、私人账号电脑联动及 Windows 125%/150% 缩放仍需目标设备实测。电脑文件与工作流操作需要对应电脑在线及已有授权。未完成的其他事项继续列在 [后续清单](DEFERRED-DECISIONS.md)。
