import SwiftUI
import UniformTypeIdentifiers

struct MobileAIView: View {
    @EnvironmentObject var sync: SyncClient
    @EnvironmentObject var store: OfflineStore
    @EnvironmentObject var briefing: DailyBriefing
    @Environment(\.scenePhase) private var scenePhase
    @StateObject private var ai: MobileAIService
    init(preview: Bool = false, attachmentPreview: Bool = false) {
        _ai = StateObject(wrappedValue: MobileAIService(preview: preview))
        #if DEBUG
        if attachmentPreview { _attachment = State(initialValue: MobileAIAttachment(id: "qa-attachment", name: "超滤SIP步骤.pdf", text: "第1页\nSIP的FS需更新供应商操作步骤。\n复核压力与温度联锁，核对修订版本和验证证据。", note: "模拟附件预览 · PDF共2页，已提取文字")) }
        #endif
    }
    @StateObject private var voice = VoiceConversation()
    @StateObject private var history = HologramHistory()
    @State private var hologram = false
    @State private var voiceMode = false
    @State private var follow = true
    @State private var sheet: AISheet?
    @State private var callProfile = HologramProfile()
    @State private var draft = ""
    @State private var localError = ""
    @State private var attachment: MobileAIAttachment?
    @State private var importFile = false
    @State private var readingAttachment = false
    @State private var attachmentTask: Task<Void, Never>?
    @State private var importID = UUID()
    @FocusState private var composerFocused: Bool
    private enum AISheet: String, Identifiable { case settings, history, connection, diagnostics; var id: String { rawValue } }
    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                header
                conversation
            }.background(MobileStyle.background)
                .safeAreaInset(edge: .bottom, spacing: 0) { composer }
                .navigationTitle("AI 助手").navigationBarTitleDisplayMode(.inline)
                .toolbar {
                    ToolbarItem(placement: .topBarTrailing) { Button { chooseAttachment() } label: { Image(systemName: "paperclip").frame(width: 44, height: 44) }.disabled(readingAttachment).accessibilityLabel("添加附件").accessibilityIdentifier("ai.attach") }
                    ToolbarItem(placement: .topBarTrailing) { Menu { menu } label: { Image(systemName: "ellipsis").frame(width: 44, height: 44) }.accessibilityLabel("AI 更多操作") }
                }
                .fileImporter(isPresented: $importFile, allowedContentTypes: [.pdf, .image, .plainText, .commaSeparatedText, .json]) { result in
                    switch result { case .success(let url): readAttachment(url); case .failure(let error): if (error as NSError).code != NSUserCancelledError { localError = "附件选择失败：" + error.localizedDescription } }
                }
                .fullScreenCover(isPresented: $hologram, onDismiss: { briefing.voiceSessionActive = false; ai.resume(sync: sync) }) {
                    MobileHologramView(profile: callProfile, store: store, history: history, sync: sync)
                }
                .sheet(item: $sheet) { value in sheetContent(value) }
                .task {
                    ai.resume(sync: sync); voice.onFinal = { text in draft = text; if voiceMode { send() } }
                    #if DEBUG
                    if ProcessInfo.processInfo.arguments.contains("--qa-keyboard") { try? await Task.sleep(nanoseconds: 300_000_000); composerFocused = true }
                    #endif
                }
                .onChange(of: voice.transcript) { _, text in if voice.listening { draft = text } }
                .onChange(of: voiceMode) { _, value in
                    voice.continuous = value
                    if value && !ai.busy { composerFocused = false; Task { await voice.start() } } else if !value { voice.stop() }
                }
                .onChange(of: ai.completedReply) { _, value in readCompletedReply(value) }
                .onChange(of: scenePhase) { _, value in
                    if value == .active && !hologram { ai.resume(sync: sync) }
                    else if value == .background { ai.suspend(); voiceMode = false; voice.stop() }
                }
                .onDisappear { cancelAttachmentRead(); ai.suspend(); if !hologram { voice.stop() }; voice.onFinal = nil }
        }.tint(MobileStyle.accent)
    }
    private var header: some View {
        VStack(spacing: 8) {
            HStack(spacing: 10) {
                Button { openHologram() } label: { Label("全息语音", systemImage: "waveform.circle.fill").frame(maxWidth: .infinity) }.buttonStyle(MobilePrimaryButtonStyle()).accessibilityIdentifier("ai.hologram")
                Button { composerFocused = false; sheet = .history } label: { Image(systemName: "clock.arrow.circlepath").frame(width: 44, height: 44) }.background(MobileStyle.panel, in: RoundedRectangle(cornerRadius: 12)).accessibilityLabel("对话记录")
                Button { composerFocused = false; sheet = .settings } label: { Image(systemName: "slider.horizontal.3").frame(width: 44, height: 44) }.background(MobileStyle.panel, in: RoundedRectangle(cornerRadius: 12)).accessibilityLabel("全息语音设置")
            }
            if !composerFocused {
                HStack(spacing: 7) { if ai.busy { ProgressView().controlSize(.mini) }; Text(ai.stage).font(.caption).lineLimit(1); Spacer(); if ai.hasPendingRequest && !ai.busy { Button("恢复") { ai.resume(sync: sync) }.font(.caption.bold()) } }.foregroundStyle(.secondary)
            }
            if !ai.error.isEmpty || !history.error.isEmpty || !localError.isEmpty {
                Text([ai.error, history.error, localError].filter { !$0.isEmpty }.joined(separator: "\n")).font(.caption).foregroundStyle(.orange).frame(maxWidth: .infinity, alignment: .leading).lineLimit(3)
            }
        }.padding(.horizontal, 16).padding(.vertical, 10).overlay(alignment: .bottom) { Rectangle().fill(MobileStyle.line).frame(height: 1) }
    }
    private var conversation: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 14) {
                    if ai.messages.isEmpty { emptyConversation }
                    ForEach(ai.messages) { message in messageCard(message).id(message.id) }
                    Color.clear.frame(height: 1).id("ai-bottom")
                }.padding(16)
            }.scrollDismissesKeyboard(.interactively)
                .onChange(of: ai.messages.last?.content) { _, _ in if follow { proxy.scrollTo("ai-bottom", anchor: .bottom) } }
                .onChange(of: composerFocused) { _, focused in if focused && follow { proxy.scrollTo("ai-bottom", anchor: .bottom) } }
        }
    }
    private var emptyConversation: some View {
        VStack(alignment: .leading, spacing: 14) {
            Image(systemName: "sparkles").font(.system(size: 28)).foregroundStyle(MobileStyle.accent).padding(.top, 12)
            Text("让工作更清楚").font(.title2.weight(.semibold))
            Text("文字对话由电脑处理；全息语音直接连接已保存的实时服务。").font(.subheadline).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            ForEach(["整理今天的未完成任务", "查看当前项目进度", "分析最近的现场事件"], id: \.self) { text in
                Button { draft = text; composerFocused = true } label: { HStack(alignment: .top) { Text(text).multilineTextAlignment(.leading); Spacer(minLength: 8); Image(systemName: "arrow.up.left") }.font(.callout).padding(14).frame(maxWidth: .infinity, alignment: .leading).background(MobileStyle.panel, in: RoundedRectangle(cornerRadius: 13)) }.buttonStyle(.plain)
            }
            if sync.cloudCredential == nil { Button("连接账号与电脑") { sheet = .connection }.buttonStyle(.bordered).frame(minHeight: 44) }
        }.frame(maxWidth: .infinity, alignment: .leading)
    }
    private func messageCard(_ message: MobileAIMessage) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack { Label(message.role == "user" ? "我" : "工程 AI", systemImage: message.role == "user" ? "person.crop.circle" : "sparkles").font(.caption.weight(.semibold)).foregroundStyle(message.role == "user" ? MobileStyle.accent : Color.purple); Spacer(); if message.role == "assistant" && message.content.isEmpty && ai.busy { ProgressView().controlSize(.small) } }
            if let attachment = message.attachment { MobileAttachmentCard(attachment: attachment) }
            let presentation = AIToolPresentation.conversation(message.content)
            ForEach(presentation.tools) { tool in AIToolCard(tool: tool) }
            Text(presentation.text.isEmpty && ai.busy ? "正在等待电脑与模型回复…" : presentation.text).textSelection(.enabled).fixedSize(horizontal: false, vertical: true).frame(maxWidth: .infinity, alignment: .leading)
            if !message.error.isEmpty { Text(message.error).font(.caption).foregroundStyle(.orange) }
            if message.role == "assistant" && !message.content.isEmpty {
                HStack { Button { UIPasteboard.general.string = message.content } label: { Label("复制", systemImage: "doc.on.doc") }; Button { composerFocused = false; voice.speak(AIToolPresentation.conversation(message.content).text) } label: { Label("朗读", systemImage: "speaker.wave.2") } }.font(.caption).buttonStyle(.bordered).controlSize(.regular)
            }
        }.padding(15).background(message.role == "user" ? MobileStyle.elevated : MobileStyle.panel, in: RoundedRectangle(cornerRadius: 16))
    }
    private var composer: some View {
        VStack(spacing: 8) {
            if readingAttachment { HStack { ProgressView().controlSize(.small); Text("正在读取附件…").font(.caption); Spacer(); Button("取消") { cancelAttachmentRead() }.font(.caption).frame(minWidth: 44, minHeight: 44) } }
            if let attachment {
                HStack(alignment: .top, spacing: 4) {
                    if composerFocused { Label(attachment.name, systemImage: "paperclip").font(.caption).lineLimit(1).frame(maxWidth: .infinity, alignment: .leading).accessibilityIdentifier("ai.attachment.compact") }
                    else { MobileAttachmentCard(attachment: attachment) }
                    Button { self.attachment = nil } label: { Image(systemName: "xmark.circle.fill").frame(width: 44, height: 44) }.accessibilityLabel("移除附件").accessibilityIdentifier("ai.attachment.remove")
                }
            }
            if voiceMode || voice.listening { HStack { Text(voice.status).font(.caption).lineLimit(2); Spacer(); Button("停止") { voiceMode = false; voice.stop() }.font(.caption.bold()).frame(minWidth: 44, minHeight: 32) }.foregroundStyle(MobileStyle.accent) }
            HStack(alignment: .bottom, spacing: 8) {
                Button {
                    composerFocused = false
                    if voice.listening { voice.stopRecording() } else { Task { await voice.start() } }
                } label: { Image(systemName: voice.listening ? "stop.circle.fill" : "mic").font(.title3).frame(width: 44, height: 44) }.disabled(ai.busy || sync.cloudCredential == nil).accessibilityLabel(voice.listening ? "停止听写" : "语音听写")
                TextField("输入问题", text: $draft, axis: .vertical).focused($composerFocused).lineLimit(1...4).padding(12).frame(minHeight: 44).background(MobileStyle.elevated, in: RoundedRectangle(cornerRadius: 14)).accessibilityIdentifier("ai.composer")
                if ai.busy { Button { voice.stop(); ai.cancel(sync: sync) } label: { Image(systemName: "stop.fill").frame(width: 44, height: 44) }.accessibilityLabel("停止生成") }
                else { Button { send() } label: { Image(systemName: "arrow.up").font(.headline).frame(width: 44, height: 44).background(MobileStyle.accent, in: Circle()).foregroundStyle(MobileStyle.background) }.disabled((draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && attachment == nil) || readingAttachment || sync.cloudCredential == nil).accessibilityLabel("发送问题") }
            }
        }.padding(.horizontal, 12).padding(.vertical, 10).background(MobileStyle.background).overlay(alignment: .top) { Rectangle().fill(MobileStyle.line).frame(height: 1) }
    }
    @ViewBuilder private var menu: some View {
        Button("添加PDF、图片或文本附件") { chooseAttachment() }.disabled(readingAttachment)
        Button("全息语音设置") { sheet = .settings }
        Button("对话记录") { sheet = .history }
        Button("连接设置") { sheet = .connection }
        Button("通话诊断") { sheet = .diagnostics }
        Divider()
        Button("保存并新建对话") { archiveAndStart() }.disabled(!ai.canStartNewConversation)
        Toggle("连续语音对话", isOn: $voiceMode).disabled(sync.cloudCredential == nil)
        Toggle("跟随最新回复", isOn: $follow)
        Button("恢复当前请求") { ai.resume(sync: sync) }.disabled(!ai.hasPendingRequest)
        Button("停止朗读") { voice.stop() }
    }
    @ViewBuilder private func sheetContent(_ value: AISheet) -> some View {
        if value == .connection { ConnectionSettingsView() } else { NavigationStack {
            Group {
                switch value {
                case .settings: HologramSettingsView()
                case .history: HologramHistoryView(history: history)
                case .connection: ConnectionSettingsView()
                case .diagnostics: HologramDiagnosticsView()
                }
            }.toolbar { ToolbarItem(placement: .cancellationAction) { Button("关闭") { sheet = nil } } }
        } }
    }
    private func openHologram() {
        composerFocused = false; voiceMode = false; voice.stop(); briefing.stopSpeaking(); briefing.voiceSessionActive = true
        ai.suspend(); callProfile = HologramSettings.load(); hologram = true
    }
    private func archiveAndStart() {
        guard ai.canStartNewConversation else { return }; voiceMode = false; voice.stop()
        if !ai.messages.isEmpty {
            var archived = HologramSession(name: "文字对话", persona: "", model: ai.model); archived.kind = "text"
            archived.messages = ai.messages.map { HologramMessage(id: $0.id, role: $0.role, text: $0.content, partial: !$0.error.isEmpty, attachment: $0.attachment) }
            history.upsert(archived)
            guard history.error.isEmpty else { localError = "旧对话尚未保存，新对话未创建。"; return }
        }
        localError = ""; attachment = nil; ai.newConversation()
    }
    private func readCompletedReply(_ value: String?) {
        guard value != nil, voiceMode, !hologram, ai.error.isEmpty, let reply = ai.messages.last, reply.role == "assistant", !reply.content.isEmpty else { return }
        voice.continuous = true; voice.speak(AIToolPresentation.conversation(reply.content).text)
    }
    private func chooseAttachment() { composerFocused = false; voiceMode = false; voice.stop(); localError = ""; importFile = true }
    private func cancelAttachmentRead() { importID = UUID(); attachmentTask?.cancel(); attachmentTask = nil; readingAttachment = false }
    private func readAttachment(_ url: URL) {
        cancelAttachmentRead(); localError = ""; readingAttachment = true
        let ticket = UUID(); importID = ticket
        attachmentTask = Task {
            do {
                let value = try await MobileAttachmentReader.read(url)
                guard !Task.isCancelled, importID == ticket else { return }
                attachment = value; readingAttachment = false
            } catch {
                guard !Task.isCancelled, importID == ticket else { return }
                readingAttachment = false; localError = "附件读取失败：" + error.localizedDescription
            }
        }
    }
    private func send() {
        let text = draft.trimmingCharacters(in: .whitespacesAndNewlines); guard (!text.isEmpty || attachment != nil), !readingAttachment else { return }
        voice.stopRecording(); ai.send(text, sync: sync, attachment: attachment); if ai.busy { draft = ""; attachment = nil; composerFocused = false }
    }
}
