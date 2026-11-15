# 没有本地 Mac 时验证 iOS 源码

仓库已提供 `.github/workflows/ios-build.yml` 和共享 Xcode Scheme。
工作流只允许手动触发，不会在本机自动上传代码或启动付费服务。

将仓库放入自己有权限的 GitHub 仓库后，打开 Actions → DeskBox iOS simulator build → Run workflow。
它在 macOS runner 上运行真实 xcodebuild，成功后产生 `DeskBoxMobile-simulator-not-iphone` 构建产物。
这能检查 SwiftUI 源码编译与资源打包，不需要在本地安装 macOS。

模拟器 `.app` 不能安装到 iPhone，不能当作 IPA 或 TestFlight 成品。
真机分发仍需 Apple 开发者账号、团队、证书/描述文件，以及相应的签名构建配置。
当前本地没有远程仓库地址，也没有执行此云端工作流；只完成 XML/YAML 及目标引用检查，不能声称 Xcode 已构建通过。

配置完成后应先处理云端实际编译错误，再进行签名和真机配对验收。
