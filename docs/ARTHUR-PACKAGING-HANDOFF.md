# 给本地 Arthur：DeskBox 最新 IPA 打包与上传

> 2026-11-17 更新：这是一份固定 0.8.0 源码的历史本地交接。仓库现已公开，原阻塞任务已重跑成功，0.8.0 IPA 已发布。当前 0.9.0 与 Windows 7 的云端构建使用功能提交 `5e113836f025fb4e5b915ef2e570be7b432cbe59`，无需等待 Arthur；后续流程见 [本地与云端构建](LOCAL-RELEASE.md)。

任务：使用下面固定的功能源码，在可用的 Mac/Xcode 上生成 iPhone 0.8.0 IPA，完成检查后上传到私有 GitHub 仓库的测试 Release。用户已要求执行打包和上传。

## 源码与目标

- 仓库：`https://github.com/zong-technology/DeskBox.git`
- 分支：`release/ios-hologram-0.8.0`（开发分支为 `feat/holographic-ai-call`，尚未合入 main）。
- 功能源码提交：`6ac3b90ae652c7ce177846eefe099e19b2926e7f`。
- iPhone：版本 `0.8.0`、build `9`、Bundle ID `com.deskbox.mobile`、iOS 17+、iPhoneOS arm64。
- 目标文件：`DeskBoxMobile-0.8.0-unsigned.ipa`。这是未签名 IPA，安装时需要沿用原 Apple 签名团队及 Bundle ID；不要先卸载旧应用。

当前主要改动包括手机主要页面/AI 键盘布局、长播报缓冲、收音上传抖动、回声处理、千问单轮失败续接和未完成记录保留。交接建立时 0.8.0 尚无最终 IPA；现已在公开仓库的云端完成 Xcode 构建并发布，真机语音仍需安装后测试。

## 先确认执行条件

用户目前只有 Windows 电脑。Arthur 如果运行在 Windows，也需要一台已授权且可连接的 Mac 才能完成 SwiftUI/iPhoneOS 编译；单独的 Windows 环境可以生成桌面版包，无法执行本次 IPA 构建。

Mac 需要完整 Xcode、iPhoneOS SDK、可用 iOS 模拟器、Swift 编译器、Python 3、Git；上传需要仓库写权限及正常的 GitHub CLI 登录。签名证书或账号只留在构建机。没有 Mac 时应明确报告缺少的构建机，保留本任务与源码。

交接建立时的托管 Actions 尝试：[44037347279](https://github.com/zong-technology/DeskBox/actions/runs/44037347279)，在启动前因账号付款/额度限制被拒绝，没有生成新 IPA。该次任务现已重跑成功，不再是当前阻塞；以下保留原本地 Mac 打包方式。

## 在 Mac 的新目录准备源码

下面的 clone 使用新目录；若目录已存在，先检查现有工作，不覆盖未提交修改。

```bash
git clone --branch release/ios-hologram-0.8.0 https://github.com/zong-technology/DeskBox.git DeskBox-ipa-0.8.0
cd DeskBox-ipa-0.8.0
git switch --detach 6ac3b90ae652c7ce177846eefe099e19b2926e7f
git status --porcelain
xcodebuild -version
```

工作区应干净。若修复实际编译问题，在独立修复分支提交，并在最终清单中记录新的真实提交；保持源码来源可核对。

## 执行完整打包入口

```bash
bash tools/release-ios.sh --verify-ui
```

入口已包含离线/同步规则、全息协议/历史/工具、原生 AVAudioEngine 音频检查、真实 iPhoneOS arm64 构建，以及两种 iPhone 模拟器的交互和页面截图。所有检查使用合成数据。输出路径打印在命令末尾，形如 `dist/local-release/ios-0.8.0-时间-进程号/`。

检查输出中的 IPA、`BUILD-MANIFEST.json`、`SHA256SUMS.txt`、`RELEASE-NOTES.md` 和 `review/`。确认版本 0.8.0/build 9、平台 iphoneos、arm64、Bundle ID 正确，清单的源码与实际构建相符，校验和正确。

真实千问服务、麦克风、扬声器/蓝牙和签名覆盖升级仍需目标 iPhone 验收；报告实际结果与尚未验证项。

## 上传 GitHub 测试版

使用本机自己的正常 GitHub 登录。先检查 `v0.8.0-mobile-hologram` 是否已存在，已有发布版本应核对来源，不覆盖旧附件或标签。全部本地检查通过且该版本尚未发布时，可执行：

```bash
# 将 OUT 改成刚才打印的实际输出目录。
OUT='dist/local-release/ios-0.8.0-实际输出目录'
gh auth status
gh release create v0.8.0-mobile-hologram \
  "$OUT/DeskBoxMobile-0.8.0-unsigned.ipa" \
  "$OUT/SHA256SUMS.txt" "$OUT/BUILD-MANIFEST.json" "$OUT/RELEASE-NOTES.md" \
  "$OUT/review/main-hologram.png" "$OUT/review/small-hologram.png" \
  --repo zong-technology/DeskBox --target "$(git rev-parse HEAD)" \
  --prerelease --title 'DeskBox Mobile 0.8.0 · 全息语音与布局修复' \
  --notes-file "$OUT/RELEASE-NOTES.md"
```

本次云端连接器直接上传附件曾返回 401。本地应使用自己的有效授权；上传失败时保留已生成的包和日志，并说明错误，不将只有 Release 页面当作附件已上传。

## 请回传

实际源码提交、Xcode 版本、检查通过/失败结果、IPA 版本/build/平台/架构、文件大小、SHA-256、GitHub 下载链接及签名状态。若缺少 Mac、权限或签名条件，回传具体阻塞和已完成步骤。

若用户另外要求 Windows 包：在 Windows 源码目录运行 `build-update.cmd`，完整步骤见 [LOCAL-RELEASE.md](LOCAL-RELEASE.md)。本轮主要交付目标仍是 iPhone IPA。
