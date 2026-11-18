# 本地构建与低成本更新

仓库已公开，2026-11-17 已实测 Windows/macOS 的 GitHub 标准云端构建恢复可用。日常开发仍可在自己的电脑打包，GitHub 保存源码和 Release 安装包。运行这些本地脚本不使用 GitHub Actions；上传 Release 本身也不需要启动 Actions。电脑、电力、设备和已有网络仍由构建机提供；千问等模型服务按各自账号计费。

## Windows：一键生成更新包

将最新源码下载或拉取到独立源码目录。双击根目录 `build-update.cmd`，或运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release-windows.ps1
```

需要 Windows 10/11 与 .NET Framework 4.8 的编译器，不需要 Visual Studio。脚本复用 `build.ps1`，产物放进新的 `dist\local-release\windows-版本-时间` 目录；不会替换正在使用的 DeskBox，也不会复制生产数据。

- 默认生成包含 `DeskBox.exe` 的 Portable ZIP、版本/源码/验证状态清单、SHA-256 校验和及发布说明。
- 若已安装 Inno Setup 6.5+，同时生成可安装升级的 Setup.exe。该工具只需安装一次；自定义路径用 `-Iscc 'C:\…\ISCC.exe'`。
- 默认仅构建候选包。清单明确记录 `NativeAndInstallerVerified=false`，不能把编译成功当作原生、安装或真实通话验收通过。
- 安装测试时先从托盘退出旧版，再运行 Setup 并选择原目录；原安装器的数据保留、旧 EXE 备份与回退逻辑继续有效。

完整原生、安装/升级/回退和 UI 验收在**独立 Windows 测试用户或 VM**进行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release-windows.ps1 -Verify -IsolatedTestAccount
```

需 Inno Setup、可交互桌面和符合 UI 脚本的实际 96 DPI/窗口尺寸。遵循仓库 [deskbox-release-review](../.agents/skills/deskbox-release-review/SKILL.md) 的要求：“Run the candidate in a disposable Windows profile or VM”。安装测试会写当前测试用户的卸载登记，并安装/卸载隔离目录中的候选程序，因此不能在正式使用的用户账户运行。脚本发现已有其他 DeskBox 安装登记时会在安装前停止。不要将自有 runner 作为服务装在生产桌面账户来运行这组 UI/安装测试。

## 可选：SDK 构建机生成 Windows 候选 EXE

没有 Windows 构建机时，已有 .NET SDK 的 Linux/Mac 也可编译 Windows .NET Framework 4.8 候选 EXE：

```bash
dotnet build DeskBox.Build.csproj -c Release -o dist/sdk-windows
```

编译期使用固定版本的 .NET Framework 参考程序集，应用运行仍需 Windows 与 .NET Framework 4.8，没有把软件迁移到 .NET 8。该路径不能执行 Windows 原生/安装验收，也不生成 Setup.exe。它适合先交付明确标注未验收的 EXE 测试包；正式安装包仍走上面的 Windows 流程。

## iPhone：用 Mac 本地打包

需要 Mac、完整 Xcode、首次启动时接受 Xcode 许可/安装 SDK，以及可用的 Python 3（现有模拟器脚本也使用它）。Windows 电脑不能运行本项目的 Xcode 真机编译；仅用 Windows 不能完成 iPhone 最终出包。

```bash
bash tools/release-ios.sh
# 完整的两尺寸 UI 交互和页面矩阵：
bash tools/release-ios.sh --verify-ui
```

脚本执行现有离线/协议/历史/音频检查和真实 iPhoneOS arm64 构建，保存日志、IPA、源码与版本清单、校验文件。可选 UI 验收复用原生模拟器脚本；未运行时清单明确为 `SimulatorUIVerified=false`。不连接真实付费模型或生产同步资料。

产物放入 `dist/local-release/ios-版本-时间`。当前生成**未签名 IPA**，需要沿用原 Apple 签名团队与 `com.deskbox.mobile` 签名覆盖安装；不要先卸载。Apple 签名/设备分发条件独立于 GitHub Actions。真实麦克风、扬声器/蓝牙和模型通话仍需目标手机验证。

## 上传到 GitHub，无需 Actions

Windows 构建目录可以用 GitHub CLI 上传为草稿：

```powershell
gh auth login
powershell -NoProfile -ExecutionPolicy Bypass -File tools\publish-local-release.ps1 -Directory 'dist\local-release\windows-实际目录'
```

上传前检查 Git 工作区干净、源码提交、文件校验和。草稿中的版本和验证状态可查看，验收后再发布为测试版。失败时保留本地包及错误，不覆盖已发布版本；如出现部分上传的草稿，在 Releases 页面补齐或删除该草稿后重试。当前连接器的附件上传曾返回 401，本机 `gh` 需要使用你自己的正常登录，不能保证当前连接器凭证可上传附件；不要将密钥发到聊天。

Mac 构建目录也可在 GitHub Releases 网页创建草稿，选择本次源码提交作为 tag 来源，上传 IPA、清单、校验文件和说明；或使用 `gh release create … --draft --prerelease`。安装包上传无需 Actions，仍需仓库写权限和正常的 GitHub 登录。

## 手动云端打包与可选自有构建机

本次将三个构建工作流改成仅 `workflow_dispatch` 手动触发，移除 PR 更新和发布分支 push 的自动构建；完整验证条件仍保留，不通过就不发布。每次源码更新不会自动启动 Windows/Mac 云端构建。

- `runner=self-hosted`：可选。需仓库管理员在 **Settings → Actions → Runners → New self-hosted runner** 按 GitHub 当页指令登记独立测试机，附加 `deskbox-qa` 标签。Windows 工作流需要 `Windows` 标签，iPhone 工作流需要 `macOS` 标签；构建机需在线。注册令牌不要提交进仓库。
- `runner=github-hosted`：默认选择 GitHub 标准 Windows/macOS 云端构建机。当前公开仓库已经实测运行成功；无需连接 Arthur，也不需要自己拥有 Mac。GitHub 对公开仓库标准 runner 的免费规则适用，未来转回私有仓库时需重新考虑账号额度。未启用付费大型 runner。
- `publish=false` 默认只构建；全部检查通过后，手动选择发布才生成新的测试 Release。已有发布 tag 不覆盖。
- 同平台工作流串行，避免同一自有电脑并发改写 `dist`。Windows 测试机需交互桌面、.NET Framework、Inno Setup；Mac 需 Xcode、对应模拟器与命令行工具。发布还需 `gh`。
- 安装包产物只保留 7 天，截图/原生预览保留 3 天。长期可下载版本放进 Releases，不依赖临时 Actions 产物链接。

手机原生检查通过、但发布或上传失败时，可复用该次安装包，不重新启动 Xcode：在同一 iPhone 工作流勾选 `publish`，将 `artifact_run_id` 填为原始运行 ID。恢复入口只使用原运行的 IPA 和截图，核对两个原生任务成功、安装包校验和、iPhoneOS/arm64 身份及已有标签的源码；只补传缺失附件，拒绝覆盖不同内容。先创建草稿，全部附件哈希核对后才公开，兼容 GitHub 不可变发布。若旧发布已锁定却缺少附件，使用同版本的 `-build构建号` 新标签，保留旧发布。原始产物必须仍在保留期内。发布权限错误与构建额度是不同问题，恢复上传仍需仓库允许写入 Releases。

```bash
gh workflow run ios-unsigned-ipa.yml --ref feat/holographic-ai-call \
  -f publish=true -f artifact_run_id=原始运行ID
```

自有 runner 不消耗 GitHub 托管机器的计算额度，但仍使用 Actions 调度，账号级限制可能影响调度。**直接运行上述本地脚本**完全不需要 Actions，是额度问题下的首选。当前会话没有登记构建机的管理员权限，也没有连接用户的 Windows 或 Mac，不能把工作流配置完成等同于机器已接通。

这些更改在开发分支 `feat/holographic-ai-call`。本地脚本直接使用该分支即可；默认分支的 Actions 页面需合入工作流更改后才能按新设置使用。

## 之后每次更新

1. 拉取已确认的源码，Windows 更新 `src/AssemblyInfo.cs` 的版本；手机更新 Xcode 项目的版本/build，并编写对应发布说明。
2. 在 Actions 手动选择对应工作流和 `feat/holographic-ai-call` 分支，保留 `runner=github-hosted`；需要上传到 Releases 时勾选 `publish`。也可用上述本地入口构建。检查源码、版本、哈希和未验项目。
3. 在独立测试环境完成升级保留、UI 和真机语音验证；未验收包保持候选/草稿状态。
4. 上传新的 Release，保留旧版供回退。无需为每次开发提交启动完整的云端打包。

## 发布工具自检

```powershell
pwsh -NoProfile -File tools/local-release-check.ps1
```

或在 Windows PowerShell 中运行同一脚本。该检查使用临时合成文件及假 `gh`/ISCC，验证脚本语法、草稿上传参数、校验失败和脏工作区拒绝、上传失败保留文件及从 PE 自动读取版本；不会实际上传或安装程序。它不能代替 Windows 原生验收或 Mac 的 Xcode 检查。
