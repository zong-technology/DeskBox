import SwiftUI

enum MobileAISettings {
    private static let account = "DeskBoxMobileDirectAIProfile"
    static func load() -> MobileAIProfile {
        var profile: MobileAIProfile = Keychain.load(account: account) ?? MobileAIProfile()
        profile.allowComputer = UserDefaults.standard.object(forKey: "aiAllowComputer") as? Bool ?? profile.allowComputer
        profile.allowWeb = UserDefaults.standard.object(forKey: "aiAllowWeb") as? Bool ?? profile.allowWeb
        return profile
    }
    static func resolved() -> MobileAIProfile {
        var profile = load()
        if profile.useVoiceKey {
            let voice = HologramSettings.load()
            let host = URL(string: profile.endpoint)?.host
            if (profile.provider == .gemini && voice.service == .gemini && host == "generativelanguage.googleapis.com") || (profile.provider == .compatible && voice.service == .qwen && host == "dashscope.aliyuncs.com") { profile.key = voice.key }
        }
        return profile
    }
    static func save(_ value: MobileAIProfile) -> Bool { Keychain.save(value, account: account) }
}

struct MobileAISettingsView: View {
    init(previewEnvironment: AIEnvironment? = nil) { _environment = State(initialValue: previewEnvironment ?? AIEnvironment.current) }
    @State private var environment = AIEnvironment.current
    @State private var profile = MobileAISettings.load()
    @State private var saved = MobileAISettings.load()
    @State private var secret = ""
    @State private var status = ""
    var body: some View {
        Form {
            Section("所有 AI 的运行环境") {
                Picker("环境", selection: $environment) { ForEach(AIEnvironment.allCases, id: \.self) { Text($0.title).tag($0) } }.accessibilityIdentifier("ai.environment")
                Text(environment == .local ? "禁止外部模型、网络工具及电脑操作；停止 AI 请求并暂停同步。本地和网络对话分别保存，切换不会带出原对话。" : "手机直接连接已保存模型，不需要电脑版或同步账号。只有开启的网络能力可执行，工具结果默认折叠。").font(.caption).foregroundStyle(.secondary)
                if environment == .local { Text(MobileLocalAI.status).font(.caption) }
            }
            if environment == .network {
                Section("手机独立文字模型") {
                    Picker("协议", selection: $profile.provider) { Text("千问 / OpenAI 兼容").tag(MobileAIProvider.compatible); Text("Gemini").tag(MobileAIProvider.gemini) }
                    TextField("HTTPS 模型地址", text: $profile.endpoint).keyboardType(.URL).textInputAutocapitalization(.never).autocorrectionDisabled()
                    Text(profile.provider == .gemini ? "Gemini 填写 API 根地址，例如 https://generativelanguage.googleapis.com/v1beta。" : "填写完整的 chat/completions 接口地址。千问默认地址已填入。").font(.caption).foregroundStyle(.secondary)
                    TextField("文字模型", text: $profile.model).textInputAutocapitalization(.never).autocorrectionDisabled()
                    Toggle("沿用匹配的全息服务密钥", isOn: $profile.useVoiceKey)
                    if !profile.useVoiceKey { SecureField(saved.key.isEmpty ? "模型 API 密钥" : "已保存，留空保留", text: $secret).textInputAutocapitalization(.never).autocorrectionDisabled() }
                    Text("千问默认使用百炼兼容接口；沿用密钥仅适用于默认千问地址或 Gemini。自定义服务请填写它自己的密钥。密钥只存手机钥匙串。").font(.caption).foregroundStyle(.secondary)
                }
                Section("可选能力") {
                    Toggle("允许读取网络网页", isOn: $profile.allowWeb)
                    Toggle("允许调用已配对电脑", isOn: $profile.allowComputer)
                    Text("默认关闭。手机任务、问题、附件分析与对话可独立运行；电脑文件和工作流仅在主动开启、电脑在线且已有授权时使用。").font(.caption).foregroundStyle(.secondary)
                }
            }
            Section { if !status.isEmpty { Text(status).font(.caption).foregroundStyle(.cyan).accessibilityIdentifier("ai.settings.status") }; NavigationLink("全息名称、角色和实时语音") { HologramSettingsView() } }
        }.workspacePage().navigationTitle("AI 环境与模型").scrollDismissesKeyboard(.interactively)
        .toolbar { ToolbarItem(placement: .confirmationAction) { Button("保存") { save() }.accessibilityIdentifier("ai.settings.save") } }
        .onChange(of: profile.provider) { _, provider in
            profile.endpoint = provider == .gemini ? "https://generativelanguage.googleapis.com/v1beta" : MobileAIProfile().endpoint
            profile.model = provider == .gemini ? "gemini-2.5-flash" : "qwen-plus"; secret = ""
        }
    }
    private func save() {
        var value = profile
        value.key = secret.isEmpty && profile.provider == saved.provider && profile.endpoint == saved.endpoint ? saved.key : secret.trimmingCharacters(in: .whitespacesAndNewlines)
        do {
            // Turning networking off must not depend on an available credential store.
            if environment == .local { AIEnvironment.select(.local); status = "已保存 · 本地隔离"; return }
            if environment == .network { _ = try value.url() }
            var credentials = value; credentials.allowComputer = saved.allowComputer; credentials.allowWeb = saved.allowWeb
            guard credentials == saved || MobileAISettings.save(value) else { status = "钥匙串保存失败，原环境保持不变。"; return }
            UserDefaults.standard.set(value.allowComputer, forKey: "aiAllowComputer")
            UserDefaults.standard.set(value.allowWeb, forKey: "aiAllowWeb")
            AIEnvironment.select(environment); profile = value; saved = value; secret = ""; status = "已保存 · " + environment.title
        } catch { status = error.localizedDescription }
    }
}
