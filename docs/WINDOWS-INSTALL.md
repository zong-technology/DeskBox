# Windows 全息测试版：安装、升级和回退

本安装包面向 Windows 10/11 64 位，检查 .NET Framework 4.8 或更新版本。主程序、角色与两种全息风格均已打入包内，无需 Node、Electron 或浏览器。安装包是未签名的测试版本；Windows 可能显示发布者无法验证。

## 安装或升级

1. 从 GitHub Release 下载 `DeskBox-2.1.0-hologram.3-Setup.exe`。已有 DeskBox 时，先从系统托盘右键退出，结束当前通话。
2. 双击安装包。新电脑默认安装到当前用户的 `%LOCALAPPDATA%\Programs\DeskBox`，不要求管理员权限。已有 `D:\DeskBox\DeskBox.exe` 时默认选择该目录；已使用本安装器安装过时沿用原路径。升级时请核对目录指向正在使用的版本。
3. 安装器替换程序前保留 `DeskBox.previous.exe`。用户数据目录不会打入安装包，也不会在升级或卸载中删除。自定义安装目录需要当前用户有写入权限。
4. 从开始菜单启动 DeskBox，首次在「功能与设置 → 全息语音」保存实时服务与模型。打开 AI&SI / AI 助手 → 全息 AI 通话后直接进入投影与字幕；外观在常规设置中调整，Esc 返回。

程序的数据位置继续按既有规则解析：`DESKBOX_HOME` → `D:\DeskBox\data` → `%LOCALAPPDATA%\DeskBox\data`。安装目录与数据目录可以不同，升级不改变这个规则。

未配置服务时可查看纯投影，麦克风不会开启。实时通话需可用的千问 Omni Realtime 或 Gemini Live 服务、模型权限、网络和麦克风。密钥由本机 Windows 用户加密保存，换电脑或换 Windows 用户后需要重新配置。文档解析扩展另需 Python 3.12+，可用安装目录中的 `tools\install-document-skills.ps1` 配置；它不是全息预览和实时通话的前置条件。

## 回退

升级后如需回到升级前的程序，先从托盘退出 DeskBox，再运行开始菜单「DeskBox → 回退到升级前版本」，或安装目录的 `rollback.cmd`。它恢复 `DeskBox.previous.exe`，并将本次候选程序保留为 `DeskBox.before-rollback.exe`。回退只替换程序，不回退或删除用户数据；首次安装没有上一版时会直接提示。

## 卸载与测试反馈

从 Windows「已安装的应用」或开始菜单「卸载 DeskBox」移除程序，用户数据与升级前程序备份会保留。运行中会要求先退出，不会强杀桌面进程。

建议测试：首次启动、原画 / 光粒子切换、通话与打断、静音 / 挂断、升级后项目和配置保留、托盘退出及桌面图标恢复。反馈时提供安装包版本、Windows 版本、显示比例和现象；不要提交 API 密钥或用户数据文件。
