import SwiftUI

enum HologramSettings {
    static var account: String { "DeskBoxMobileHologramProfile" + AIEnvironment.current.historySuffix }
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
                Toggle("允许免按键打断", isOn: $profile.allowInterruption)
                Text("默认在 AI 播报时抑制收音回声；需要提问时点投影或下方打断按钮。戴耳机时可开启免按键打断，直接说话接续。").font(.caption).foregroundStyle(.secondary)
            }
            if AIEnvironment.current == .network { Section("实时语音服务") {
                Picker("服务", selection: $profile.service) { Text("Google · Gemini Live").tag(HologramService.gemini); Text("千问百炼 · Omni Realtime").tag(HologramService.qwen) }
                TextField("WSS 服务地址", text: $profile.endpoint).textInputAutocapitalization(.never).autocorrectionDisabled().keyboardType(.URL)
                TextField("实时语音模型", text: $profile.model).textInputAutocapitalization(.never).autocorrectionDisabled()
                SecureField(saved.key.isEmpty || profile.service != saved.service ? "API 密钥" : "已保存，留空保留", text: $secret).textInputAutocapitalization(.never).autocorrectionDisabled()
                Text("在这里配置一次，通话中只显示全息与字幕。手机密钥保存在系统钥匙串，不通过私人云同步；文字AI也可在手机独立配置。").font(.caption).foregroundStyle(.secondary)
            } }
            Section {
                if !status.isEmpty { Text(status).font(.caption).foregroundStyle(.cyan) }
            }
            Section("使用") { Text("轻点投影可打断播报；左上角可静音。切到后台或音频被系统打断时停止通话，返回后轻点投影继续。项目与任务查询来自手机同步缓存；新建与修改任务、记录现场问题可直接保存并等待同步。电脑文件、文档和工作流操作需已连接的电脑在线，保留原有权限与确认。").font(.caption) }
        }.workspacePage().scrollDismissesKeyboard(.interactively).navigationTitle("全息语音设置")
        .toolbar { ToolbarItem(placement: .confirmationAction) { Button("保存") { save() } } }
        .onChange(of: profile.service) { _, service in
            profile.endpoint = service == .gemini ? HologramProfile().endpoint : "wss://YOUR-WORKSPACE.cn-beijing.maas.aliyuncs.com/api-ws/v1/realtime"
            profile.model = service == .gemini ? "gemini-3.1-flash-live-preview" : "qwen3.8-omni-flash-realtime"; secret = ""
        }
    }
    private func save() {
                    var value = profile
                    value.name = value.name.trimmingCharacters(in: .whitespacesAndNewlines); value.persona = value.persona.trimmingCharacters(in: .whitespacesAndNewlines)
                    value.key = secret.isEmpty && value.service == saved.service ? saved.key : secret.trimmingCharacters(in: .whitespacesAndNewlines)
                    do {
                        try value.validate(requireKey: !value.key.isEmpty)
                        guard Keychain.save(value, account: HologramSettings.account) else { status = "钥匙串保存失败，请稍后重试。"; return }
                        profile = value; saved = value; secret = ""; status = AIEnvironment.current == .local ? "本地角色已保存。" : value.ready ? "已保存，下次进入全息通话会直接连接。" : "偏好已保存，语音尚未启用。"
                    } catch { status = error.localizedDescription }

    }
}
struct HologramHistoryView: View {
    @ObservedObject var history: HologramHistory
    var body: some View {
        VStack(spacing: 0) {
            if !history.error.isEmpty { Text(history.error).font(.caption).foregroundStyle(.orange).padding().frame(maxWidth: .infinity, alignment: .leading) }
            if history.sessions.isEmpty { ContentUnavailableView("还没有对话记录", systemImage: "waveform", description: Text("全息通话自动保存；文字对话可从 AI 菜单保存。")) }
            else {
                List {
                    ForEach(history.sessions) { session in
                        NavigationLink {
                            HologramTranscriptView(session: session)
                        } label: {
                            VStack(alignment: .leading, spacing: 6) { Label(session.name, systemImage: session.kind == "text" ? "text.bubble" : "waveform").font(.headline); HStack { Text(session.date, style: .date); Text(session.date, style: .time) }.font(.caption).foregroundStyle(.secondary); Text(session.messages.first(where: { $0.role == "user" })?.text ?? "全息语音对话").font(.caption).lineLimit(2).foregroundStyle(.secondary) }
                        }
                    }.onDelete { indices in history.delete(Set(indices.map { history.sessions[$0].id })) }
                }
            }
        }.workspacePage().navigationTitle("对话记录")
    }
}

struct HologramDiagnosticsView: View {
    private let report = HologramDiagnostics.text()
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Label("本机通话诊断", systemImage: "waveform.path.ecg").font(.headline)
                Text("记录连接、缓冲、系统打断及上传丢帧的时间和状态，不包含音频、对话内容或密钥。遇到卡断可导出这份记录定位。").font(.callout).foregroundStyle(.secondary)
                Text(report).font(.caption.monospaced()).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading).workspaceCard()
            }.padding(16)
        }.workspacePage().navigationTitle("通话诊断")
        .toolbar { ToolbarItem(placement: .topBarTrailing) { ShareLink(item: report) { Image(systemName: "square.and.arrow.up").frame(width: 44, height: 44) }.accessibilityLabel("导出通话诊断") } }
    }
}

struct HologramTranscriptView: View {
    let session: HologramSession
    var body: some View {
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 18) {
                Text(session.model).font(.caption).foregroundStyle(.secondary)
                ForEach(session.messages) { message in
                    VStack(alignment: .leading, spacing: 6) {
                        if message.role == "tool" { AIToolCard(tool: .history(message.text)) }
                        else {
                            Text((message.role == "user" ? "我" : session.name) + (message.partial ? " · 已打断" : "")).font(.caption.bold()).foregroundStyle(.cyan)
                            if let attachment = message.attachment { MobileAttachmentCard(attachment: attachment) }
                            let presentation = AIToolPresentation.conversation(message.text)
                            ForEach(presentation.tools) { tool in AIToolCard(tool: tool) }
                            Text(presentation.text).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
                        }
                    }.padding().background(.white.opacity(0.04), in: RoundedRectangle(cornerRadius: 14))
                }
            }.padding()
        }.workspacePage().navigationTitle(session.name)
        .toolbar { ShareLink(item: session.messages.map { ($0.role == "user" ? "我" : $0.role == "tool" ? "工具" : session.name) + "：" + $0.text + ($0.attachment.map { "\n\n" + $0.prompt } ?? "") }.joined(separator: "\n\n")) { Image(systemName: "square.and.arrow.up") } }
    }
}
