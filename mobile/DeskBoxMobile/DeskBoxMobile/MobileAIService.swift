import Foundation
import Combine

struct MobileAIMessage: Codable, Identifiable {
    var id = UUID().uuidString
    var role: String
    var content: String
    var error = ""
    var attachment: MobileAIAttachment? = nil
    var requestContent: String { content + (attachment.map { "\n\n" + $0.prompt } ?? "") }
}
struct CloudAIResult: Decodable {
    var id: String
    var status: String
    var response: String
    var stage: String
    var error: String
    var provider: String
    var model: String
}

@MainActor final class MobileAIService: ObservableObject {
    @Published private(set) var messages: [MobileAIMessage] = []
    @Published private(set) var busy = false
    @Published private(set) var stage = "就绪 · 手机独立 AI"
    @Published private(set) var provider = ""
    @Published private(set) var model = ""
    @Published private(set) var error = ""
    @Published private(set) var completedReply: String?
    var hasPendingRequest: Bool { jobId != nil }
    var canStartNewConversation: Bool { !busy && jobId == nil && !loadFailed }
    private var loadFailed = false
    private var operationId = UUID()
    private var operation: Task<Void, Never>?
    private var environment = AIEnvironment.current
    private var direct = false
    private var historyURL: URL { FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appendingPathComponent("mobile-ai-history" + environment.historySuffix + ".json") }
    private var savedAt = Date.distantPast
    private var jobId: String? { get { environment == .network ? UserDefaults.standard.string(forKey: "mobileAIJob") : nil } set { UserDefaults.standard.set(newValue, forKey: "mobileAIJob") } }
    private var assistantId: String? { get { UserDefaults.standard.string(forKey: "mobileAIAssistant") } set { UserDefaults.standard.set(newValue, forKey: "mobileAIAssistant") } }
    init(preview: Bool = false) {
        #if DEBUG
        if preview {
            messages = [MobileAIMessage(role: "user", content: "帮我整理冻干机项目的待办，按优先级列出。"), MobileAIMessage(role: "assistant", content: "根据手机最近同步的项目记录，建议先处理以下事项：\n\n1. 复核 URS 与联锁因果关系矩阵，确认报警配置和验证边界。\n2. 核对设备清单与测试证据。\n3. 确认现场压力偏差事件的处理进度。\n\n这是隔离验收的模拟对话，不代表真实项目数据。")]
            stage = "模拟预览 · 无网络请求"; return
        }
        #endif
        guard FileManager.default.fileExists(atPath: historyURL.path) else { return }
        do { messages = try JSONDecoder().decode([MobileAIMessage].self, from: Data(contentsOf: historyURL)) }
        catch { loadFailed = true; self.error = "文字记录读取失败，原文件已保留，暂停覆盖。" }
    }
    private func save() { guard !loadFailed else { return }; do { try JSONEncoder().encode(Array(messages.suffix(60))).write(to: historyURL, options: .atomic); savedAt = Date() } catch { self.error = "AI历史保存失败，当前内容仍保留在内存" } }
    func send(_ text: String, sync: SyncClient, store: OfflineStore, attachment: MobileAIAttachment? = nil) {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        let value = trimmed.isEmpty && attachment != nil ? "请分析这份附件，说明依据和需要核实的地方。" : trimmed
        guard !value.isEmpty, !busy, !loadFailed else { return }
        guard jobId == nil else { error = "旧版电脑请求尚未确认，请先恢复查看或取消"; return }
        let user = MobileAIMessage(role: "user", content: value, attachment: attachment)
        let request: [[String: String]]
        do { request = try MobileAIRequest.history((messages + [user]).filter { !$0.content.isEmpty && $0.error.isEmpty }.map { ($0.role, AIToolPresentation.conversation($0.requestContent).text) }) }
        catch { self.error = error.localizedDescription; return }
        let profile = MobileAISettings.resolved(), scope = environment
        let assistant = MobileAIMessage(role: "assistant", content: "")
        messages.append(user); messages.append(assistant); error = ""; busy = true; direct = true; save()
        let token = UUID(); operationId = token
        let tools = HologramLocalTools(store: store, lastSync: { [weak sync] in sync?.lastSyncedAt })
        operation = Task {
            do {
                let execute: (HologramFunction) async -> [String: Any] = { call in
                    guard !Task.isCancelled, self.operationId == token, AIEnvironment.current == scope else { return ["ok": false, "error": "环境已切换，操作已停止。"] }
                    if call.name == "deskbox_execute" {
                        guard scope == .network, profile.allowComputer else { return ["ok": false, "error": "电脑操作未开启。"] }
                        return await MobileComputerActions.execute(call.arguments, sync: sync)
                    }
                    if call.name == "deskbox_web_read" { return await MobileWebTools.read(call.arguments) }
                    return tools.execute(call)
                }
                let progress: (String) -> Void = { text in
                    guard self.operationId == token else { return }
                    if text.hasPrefix("```deskbox-results"), let index = self.messages.firstIndex(where: { $0.id == assistant.id }) { self.messages[index].content = text; self.save() }
                    else { self.stage = text }
                }
                let instructions = HologramSettings.load().instructions + "\n你现在是手机独立文字助手；不要要求电脑版在线。只使用本次实际提供的工具。附件已由手机提取，不要通过电脑读取附件文件名。结果用中文概括，不粘贴原始JSON。"
                self.provider = scope == .local ? "本机模型" : profile.provider.rawValue; self.model = scope == .local ? "Apple 本机模型" : profile.model
                let reply: String
                if scope == .local { reply = try await MobileLocalAI.run(history: request, instructions: instructions, execute: execute, progress: progress) }
                else { reply = try await MobileDirectAI.run(profile: profile, history: request, instructions: instructions, execute: execute, progress: progress) }
                guard !Task.isCancelled, operationId == token, environment == scope else { return }
                if let index = messages.firstIndex(where: { $0.id == assistant.id }) { messages[index].content = reply }
                completedReply = assistant.id; stage = "已完成 · " + scope.title
            } catch {
                guard operationId == token else { return }
                self.error = error is CancellationError ? "已停止；已完成的修改不会撤销。" : (error as? HologramError)?.errorDescription ?? "模型连接中断，请重试；已保存的工具结果仍可查看。"
                if let index = messages.firstIndex(where: { $0.id == assistant.id }) { messages[index].error = self.error }
                stage = "未完成 · " + scope.title
            }
            guard operationId == token else { return }; busy = false; direct = false; save()
        }
    }
    func changeEnvironment() {
        guard environment != AIEnvironment.current else { return }
        suspend(); save(); environment = AIEnvironment.current
        messages = []; error = ""; completedReply = nil; provider = ""; model = ""; loadFailed = false; stage = "就绪 · " + environment.title
        guard FileManager.default.fileExists(atPath: historyURL.path) else { return }
        do { messages = try JSONDecoder().decode([MobileAIMessage].self, from: Data(contentsOf: historyURL)) }
        catch { loadFailed = true; self.error = "此环境的记录读取失败，原文件已保留。" }
    }
    func resume(sync: SyncClient) {
        guard !busy, let id = jobId, sync.cloudCredential != nil else { return }
        busy = true; error = ""; let token = UUID(); operationId = token
        operation = Task { await poll(id: id, token: token, sync: sync) }
    }
    private func poll(id: String, token: UUID, sync: SyncClient) async {
        do {
            while !Task.isCancelled {
                let data = try await sync.mobileAIRequest("ai-request?id=" + id)
                guard !Task.isCancelled, operationId == token, jobId == id else { return }
                let result = try JSONDecoder().decode(CloudAIResult.self, from: data)
                stage = result.stage; provider = result.provider; model = result.model
                if let index = messages.firstIndex(where: { $0.id == assistantId }) { messages[index].content = result.response; messages[index].error = result.error }
                if Date().timeIntervalSince(savedAt) >= 2 { save() }
                if ["completed", "error", "cancelled"].contains(result.status) { error = result.error; busy = false; jobId = nil; if result.status == "completed" { completedReply = assistantId }; save(); return }
                try await Task.sleep(nanoseconds: 1_000_000_000)
            }
        } catch { if !Task.isCancelled && operationId == token { self.error = error.localizedDescription; stage = "网络中断，已有内容保留"; busy = false; if (error as NSError).code == 404 { jobId = nil }; save() } }
    }
    func cancel(sync: SyncClient) {
        if direct { stopDirect(); return }
        guard let id = jobId, AIEnvironment.current == .network else { return }
        operation?.cancel(); let token = UUID(); operationId = token; busy = true; stage = "正在通知电脑取消"
        operation = Task {
            do {
                _ = try await sync.mobileAIRequest("ai-cancel", body: JSONSerialization.data(withJSONObject: ["Id": id]))
                guard !Task.isCancelled, operationId == token else { return }
                if jobId == id { jobId = nil }; stage = "已取消 · 已有内容保留"
            } catch { guard !Task.isCancelled, operationId == token else { return }; self.error = "取消未能确认，请联网后重试" }
            busy = false; save()
        }
    }
    private func stopDirect() {
        operationId = UUID(); operation?.cancel(); direct = false; busy = false; stage = "已停止 · 已有结果保留"
        if let index = messages.indices.last, messages[index].role == "assistant" { messages[index].error = "回复已停止，已完成的操作不会撤销。" }; save()
    }
    func suspend() { if direct { stopDirect(); return }; operationId = UUID(); operation?.cancel(); busy = false; if jobId != nil { stage = "旧版电脑请求已暂停查看" } }
    func newConversation() { guard canStartNewConversation else { return }; messages = []; error = ""; stage = "新对话"; save() }
}
