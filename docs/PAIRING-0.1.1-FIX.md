# 签名后配对失败修复（0.1.1 / build 2）

## 本机证据

- 正在运行的旧版为 `dist/mobile-ux-final/DeskBox.exe`，TCP 47831 在 0.0.0.0 监听。
- 按旧程序实际 NetworkInterface 枚举顺序，首个可用地址是 VMware VMnet1 的 192.168.209.1，而实际 WLAN 地址是 10.176.213.120。
- 旧 PairingUri 直接选择第一个地址，未区分物理 WLAN 与虚拟网卡。
- 通过本机 WLAN 地址访问 /health 返回 TLS 200；返回的 SHA-256 指纹尾部与截图一致。
- 本机当前专用/公用防火墙未启用，不能把这次问题笼统归咎于防火墙或签名。
- 没有手机端详细错误码，尚不能排除本地网络权限、Wi-Fi 隔离等其他因素。

## 修复

- Windows 优先 WLAN/有网关的物理网卡，降低 VMware/VPN/TAP 优先级，排除169.254自动地址。
- 配对页增加地址下拉选择，复制链接使用所选的当前地址。
- iOS 增加 NSAllowsLocalNetworking；保留 HTTPS 和 SHA-256 指纹校验，没有全局放宽证书校验。
- 手机显示实际目标 IP:port，分别提示连接超时、无法连接、本地网络不可用、TLS失败、指纹不匹配、HTTP401配对码失效以及数据格式问题。
- 粘贴链接禁用自动更正，点击配对收起键盘；显示实际版本号以辨别重签的是新包。

## 产物和验证

- 电脑：`dist/pairing-review/DeskBox.exe`。
- 手机：`dist/ios/DeskBoxMobile-0.1.1-unsigned.ipa`，需用户重新签名安装。
- iOS 17+，iPhoneOS arm64；Info.plist 本地网络配置与版本 0.1.1(2) 验证通过。
- IPA SHA-256：`0cb3049fa5cb8597d67b943948172716eafee4f221a6f2d5df3cab3d8c503c14`。
- 真机编译/打包：https://github.com/zong-technology/dsh-desktop/actions/runs/41403374454
- 地址专项3项、设备可靠性8项、TLS服务15项、工程UI32项通过。
- 尚未在用户 iPhone 上复测成功，不能声称实机配对已通过。

## 重新配对

从托盘退出旧电脑程序，启动上述电脑新版；确认下拉地址为 WLAN 地址，生成全新链接。
不要复用重启之前或已过期的链接；手机重签安装0.1.1(2)后，粘贴完整链接再配对。
若仍超时，检查两台设备是否能在同一网络互访，企业/访客 Wi-Fi 可能开启客户端隔离。
新手机版显示的错误码和目标地址可用于下一步定位；不要关闭指纹校验或安装未知根证书来绕过错误。
