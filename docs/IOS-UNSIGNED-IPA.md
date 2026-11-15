# 供自行签名的 IPA

## 最新版本0.4.0（build 6，2026-11-03）

- 安装包：`dist/ios/DeskBoxMobile-0.4.0-unsigned.ipa`，1,586,848字节。
- iPhoneOS / arm64，最低iOS17；Bundle ID `com.deskbox.mobile`；保持原签名覆盖安装。
- SHA-256：`fe7f638a8e303f2a15c6f0e6c0e4544e76d6386b907e7634cd63d5bca126d01b`。
- 构建：https://github.com/zong-technology/dsh-desktop/actions/runs/41585558307
- 源码：`f7e5679139144836d3ec60acc2c103ec75baa15b`。Swift规则与离线存储37项通过，Xcode真机架构构建通过。
- 新增事件编辑与手机同步冲突比较/选择；配套升级Windows至 `dist/field-0.4.0/DeskBox.exe`。现有云服务无需重新部署。
- 用户已确认之前版本账号自动同步连通；0.4.0新增交互仍待iPhone真机验收。

## 最新版本 0.3.1（build 5，2026-11-03）

- `dist/ios/DeskBoxMobile-0.3.1-unsigned.ipa`，1,515,835 字节；iPhoneOS/arm64，最低 iOS 17，Bundle ID `com.deskbox.mobile`。
- SHA-256：`3dbdb650c91d1f89087ee8062f7bd653fd0f9f35625c84a48599a489393a6326`。
- Xcode 构建：https://github.com/zong-technology/dsh-desktop/actions/runs/41562024994
- 提交：`ef7129ae47cc2c7d5e04f3e8ff3ac5c853180856`；24 项 Swift 简报/日期/连接/云凭证规则检查通过。
- 新增私人云账号地址配置、设备授权导入、每日任务范围与指定任务通知、城市天气和中文朗读、邮件草稿。
- 保留原签名团队和 Bundle ID 覆盖安装；勿先卸载。系统通知、语音、邮件及真实跨网络仍需真机验收。
- 自有云账号开通见 `云同步最少开通步骤.md`；用户尚未注册云账号，不宣称已经跨网联通。

## 最新版本 0.2.0（build 3，2026-11-02）

- 安装包：`dist/ios/DeskBoxMobile-0.2.0-unsigned.ipa`，1,358,829 字节（约 1.30 MiB）。
- Bundle ID：`com.deskbox.mobile`，最低 iOS 17.0；iPhoneOS / arm64，未签名。
- SHA-256：`8e188877cb9906b07fc55efa9ca9013b74a58ba172a79363356d931cc6784c79`。
- Xcode 构建成功：https://github.com/zong-technology/dsh-desktop/actions/runs/41411692451
- 源码提交：`521ad01e39e3cf14a3757e864377620aac8ecfb0`，仍在授权的独立 iOS 分支。
- 校验通过：Mach-O iOS/arm64、Info.plist 版本/平台、AppIcon 配置、编译后的 Assets.car、本地网络配置。
- 新增 Arthur.Zhong 图标和工程首页、项目搜索/详情、前台自动同步/重连、接近到期续信任及连接地址切换。
- 配套电脑程序：`dist/mobile-0.2.0/DeskBox.exe`；35 套验收 / 541 项断言通过。
- 用户已确认旧 0.1.1 首次真机连通；0.2.0 新界面、自动同步、覆盖升级和异地网络尚未真机复验。
- 使用相同签名团队与 Bundle ID 覆盖安装，保留数据与 Keychain；电脑升级保留原数据目录和证书。

完整清单见 `REMAINING-WORK-2026-11-02.md`。下文为最初版本的构建记录。

生成命令（macOS + Xcode）：

```sh
bash tools/build-ios-unsigned-ipa.sh
```

成功后输出 `dist/DeskBoxMobile-unsigned.ipa`，用户可自行签名。
脚本真实构建 Release / iPhoneOS / arm64，验证平台和主可执行文件后才打包成 `Payload/DeskBoxMobile.app`。
不需要在构建时提供 Apple 团队或证书；安装到 iPhone 前仍需重新签名。

也可把当前仓库放入有权限的 GitHub 仓库，手动运行 Actions 中的
`DeskBox unsigned iPhone IPA`，下载 `DeskBoxMobile-unsigned-iphone-ipa` 产物。
此前的模拟器工作流不能产生供 iPhone 签名安装的包。

已于 2026-11-02 在用户授权的 `zong-technology/dsh-desktop` 独立分支完成 Xcode 真机编译与打包：
https://github.com/zong-technology/dsh-desktop/actions/runs/41397215828

本地产物：`dist/ios/DeskBoxMobile-unsigned.ipa`，192422 字节。
Bundle ID：`com.deskbox.mobile`；iPhoneOS / arm64；最低 iOS 17.0。
SHA-256：`dc59583516e487055c227f529fb6634471d78b385137e66fd90e2035e4ffc549`。

已验证 Payload 目录、Info.plist 平台、Mach-O 架构及 iOS load command，并与构建端摘要核对一致。
该包未签名、未做 iPhone 安装和运行验收；用户自行签名后安装。原仓库 main 未修改。
