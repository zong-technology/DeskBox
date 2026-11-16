import SwiftUI

enum HologramSettings {
    static let account = "DeskBoxMobileHologramProfile"
    static func load() -> HologramProfile { Keychain.load(account: account) ?? HologramProfile() }
}
struct HologramSettingsView: View {
    @Environment(\.dismiss) private var dismiss
    @State private var profile = HologramSettings.load()
    @State private var saved = HologramSettings.load()
    @State private var secret = ""
    @State private var status = ""
    var body: some View {
        Form {
            Section("全息助手") {
                TextField("助手名称", text: $profile.name)
                TextField("角色、性格、专长与说话方式", text: $profile.persona, axis: .vertical).lineLimit(4...8)
                Toggle("减少动态效果", isOn: $profile.reducedMotion)
            }
            Section("实时语音服务") {
                Picker("服务", selection: $profile.service) { Text("Google · Gemini Live").tag(HologramService.gemini); Text("千问百炼 · Omni Realtime").tag(HologramService.qwen) }
                TextField("WSS 服务地址", text: $profile.endpoint).textInputAutocapitalization(.never).autocorrectionDisabled().keyboardType(.URL)
                TextField("实时语音模型", text: $profile.model).textInputAutocapitalization(.never).autocorrectionDisabled()
                SecureField(saved.key.isEmpty ? "API 密钥" : "已保存，留空保留", text: $secret).textInputAutocapitalization(.never).autocorrectionDisabled()
                Text("在这里配置一次，通话中只显示全息与字幕。手机密钥保存在系统钥匙串，不通过私人云同步；电脑中继文字AI的密钥仍留在电脑。").font(.caption).foregroundStyle(.secondary)
            }
            Section {
                Button("保存全息语音") {
                    var value = profile
                    value.name = value.name.trimmingCharacters(in: .whitespacesAndNewlines); value.persona = value.persona.trimmingCharacters(in: .whitespacesAndNewlines)
                    value.key = secret.isEmpty && value.service == saved.service ? saved.key : secret.trimmingCharacters(in: .whitespacesAndNewlines)
                    do {
                        try value.validate(requireKey: !value.key.isEmpty)
                        guard Keychain.save(value, account: HologramSettings.account) else { status = "钥匙串保存失败，请稍后重试。"; return }
                        profile = value; saved = value; secret = ""; status = value.ready ? "已保存，下次进入全息通话会直接连接。" : "偏好已保存，语音尚未启用。"
                    } catch { status = error.localizedDescription }
                }
                if !status.isEmpty { Text(status).font(.caption).foregroundStyle(.cyan) }
            }
            Section("使用") { Text("轻点投影可打断播报；左上角可静音。切到后台或音频被系统打断时停止通话，返回后轻点投影继续。任务、项目与事件查询来自手机已同步缓存。").font(.caption) }
        }.navigationTitle("全息语音")
        .onChange(of: profile.service) { _, service in
            profile.endpoint = service == .gemini ? HologramProfile().endpoint : "wss://YOUR-WORKSPACE.cn-beijing.maas.aliyuncs.com/api-ws/v1/realtime"
            profile.model = service == .gemini ? "gemini-3.1-flash-live-preview" : "qwen3.8-omni-flash-realtime"; secret = ""
        }
    }
}
struct HologramHistoryView: View {
    @ObservedObject var history: HologramHistory
    var body: some View {
        VStack(spacing: 0) {
            if !history.error.isEmpty { Text(history.error).font(.caption).foregroundStyle(.orange).padding().frame(maxWidth: .infinity, alignment: .leading) }
            if history.sessions.isEmpty { ContentUnavailableView("还没有全息通话记录", systemImage: "waveform", description: Text("通话转写自动保存在本机，不录制原始音频。")) }
            else {
                List {
                    ForEach(history.sessions) { session in
                        NavigationLink {
                            ScrollView {
                                LazyVStack(alignment: .leading, spacing: 18) {
                                    Text(session.model).font(.caption).foregroundStyle(.secondary)
                                    ForEach(session.messages) { message in
                                        VStack(alignment: .leading, spacing: 6) {
                                            Text((message.role == "user" ? "我" : message.role == "tool" ? "工具结果" : session.name) + (message.partial ? " · 已打断" : "")).font(.caption.bold()).foregroundStyle(.cyan)
                                            Text(message.text).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
                                        }.padding().background(.white.opacity(0.04), in: RoundedRectangle(cornerRadius: 14))
                                    }
                                }.padding()
                            }.navigationTitle(session.name).toolbar { ShareLink(item: session.messages.map { ($0.role == "user" ? "我" : session.name) + "：" + $0.text }.joined(separator: "\n\n")) { Image(systemName: "square.and.arrow.up") } }
                        } label: {
                            VStack(alignment: .leading, spacing: 6) { Text(session.name).font(.headline); HStack { Text(session.date, style: .date); Text(session.date, style: .time) }.font(.caption).foregroundStyle(.secondary); Text(session.messages.first(where: { $0.role == "user" })?.text ?? "全息语音对话").font(.caption).lineLimit(2).foregroundStyle(.secondary) }
                        }
                    }.onDelete { indices in history.delete(Set(indices.map { history.sessions[$0].id })) }
                }
            }
        }.navigationTitle("全息通话记录")
    }
}
