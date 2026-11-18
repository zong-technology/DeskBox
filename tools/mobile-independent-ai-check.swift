import Foundation

final class ModelFixture: URLProtocol {
    static var handler: ((URLRequest) throws -> Data)?
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        do {
            let data = try Self.handler!(request)
            let response = HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "application/json"])!
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed); client?.urlProtocol(self, didLoad: data); client?.urlProtocolDidFinishLoading(self)
        } catch { client?.urlProtocol(self, didFailWithError: error) }
    }
    override func stopLoading() { }
}

@main struct IndependentAICheck {
    @MainActor static func main() async throws {
        var checks = 0
        func check(_ value: Bool, _ label: String) { precondition(value, label); checks += 1; print("PASS " + label) }
        let defaults = UserDefaults.standard, previous = defaults.object(forKey: AIEnvironment.key)
        defer { if let previous { defaults.set(previous, forKey: AIEnvironment.key) } else { defaults.removeObject(forKey: AIEnvironment.key) }; ModelFixture.handler = nil }
        AIEnvironment.select(.network)
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("deskbox-independent-ai-" + UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true); defer { try? FileManager.default.removeItem(at: directory) }
        let store = OfflineStore(fileURL: directory.appendingPathComponent("sync.json"))
        let tools = HologramLocalTools(store: store, lastSync: { nil })
        let config = URLSessionConfiguration.ephemeral; config.protocolClasses = [ModelFixture.self]; config.timeoutIntervalForResource = 5
        var profile = MobileAIProfile(); profile.endpoint = "https://model.fixture.invalid/v1/chat/completions"; profile.key = "fixture-key-never-send-to-PC"; profile.useVoiceKey = false
        let history = [["Role": "user", "Content": "请记录一条任务：SIP的FS需要供应商更新步骤。"]]
        var requests = 0, progress = ""
        ModelFixture.handler = { request in
            requests += 1
            check(request.value(forHTTPHeaderField: "Authorization") == "Bearer " + profile.key, "The phone authenticates directly with its selected model")
            if requests == 1 { return Data("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"create-1\",\"type\":\"function\",\"function\":{\"name\":\"deskbox_task_create\",\"arguments\":\"{\\\"title\\\":\\\"SIP的FS需要供应商更新步骤\\\"}\"}}]}}]}".utf8) }
            return Data("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"已保存到手机，等待同步。\"}}]}".utf8)
        }
        let reply = try await MobileDirectAI.run(profile: profile, history: history, instructions: "只在工具成功后声称已保存。", configuration: config, execute: { tools.execute($0) }, progress: { progress = $0 })
        check(requests == 2 && store.tasks.count == 1, "A text AI request creates a real phone task without PC or cloud credentials")
        check(reply.contains("```deskbox-results") && reply.contains("等待同步"), "Tool evidence remains folded beside the final answer")
        check(OfflineStore(fileURL: directory.appendingPathComponent("sync.json")).tasks.count == 1 && store.pending.count == 1, "Phone operations survive restart with their actual pending queue")
        check(!reply.contains(profile.key) && !progress.contains(profile.key), "Credentials are absent from history and progress")
        check(!MobileDirectAI.declarations(profile).contains { $0["name"] as? String == "deskbox_execute" }, "Computer operations are optional and disabled by default")
        profile.provider = .gemini; profile.endpoint = "https://model.fixture.invalid/v1beta"; profile.model = "gemini-fixture"
        requests = 0
        ModelFixture.handler = { request in
            requests += 1
            check(request.url?.query == nil && request.value(forHTTPHeaderField: "x-goog-api-key") == profile.key, "Gemini direct requests keep the key out of URLs")
            if requests == 1 { return Data("{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"thoughtSignature\":\"fixture-signature\",\"functionCall\":{\"id\":\"gemini-call-1\",\"name\":\"deskbox_tasks\",\"args\":{}}}]}}]}".utf8) }
            return Data("{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"已读取手机任务。\"}]}}]}".utf8)
        }
        let gemini = try await MobileDirectAI.run(profile: profile, history: history, instructions: "查询任务", configuration: config, execute: { tools.execute($0) }, progress: { _ in })
        check(requests == 2 && gemini.contains("已读取"), "Gemini uses the same actual phone tools without a computer")
        let generation = AIEnvironment.generation; AIEnvironment.select(.local)
        check(generation != AIEnvironment.generation && AIEnvironment.current.historySuffix == "-local", "Switching environment changes the lifetime and separates history files")
        let count = requests
        do { _ = try await MobileDirectAI.run(profile: profile, history: history, instructions: "must not send", configuration: config, execute: { _ in preconditionFailure("Offline request executed a network tool") }, progress: { _ in }); preconditionFailure("Offline request reached a network model") }
        catch { check(requests == count, "Local isolation refuses requests before invoking any network transport") }
        var voice = HologramProfile(); voice.key = "fixture"
        do { _ = try HologramWire.request(voice); preconditionFailure("Offline realtime request allowed") }
        catch { check(true, "Cloud holographic voice cannot bypass local isolation") }
        check(!HologramWire.availableDeclarations.contains { ["deskbox_execute", "deskbox_web_read"].contains($0["name"] as? String ?? "") }, "Offline tool discovery excludes computer and web tools")
        defaults.set("unknown-mode", forKey: AIEnvironment.key)
        check(AIEnvironment.current == .local, "Unknown environment preferences fail closed")
        print("PASS \(checks) independent mobile AI checks; native mocked HTTP, real offline writes, no paid model")
    }
}
