import Foundation

enum MobileDirectAI {
    static func declarations(_ profile: MobileAIProfile) -> [[String: Any]] {
        HologramWire.declarations.filter { ($0["name"] as? String != "deskbox_execute" || profile.allowComputer) && ($0["name"] as? String != "deskbox_web_read" || profile.allowWeb) && !(($0["name"] as? String ?? "").hasPrefix("hologram_")) }
    }
    @MainActor static func run(profile: MobileAIProfile, history: [[String: String]], instructions: String,
                               configuration: URLSessionConfiguration? = nil, execute: (HologramFunction) async -> [String: Any], progress: (String) -> Void) async throws -> String {
        let environment = AIEnvironment.current
        let generation = AIEnvironment.generation
        try environment.requireNetwork()
        let url = try profile.url()
        guard !profile.key.isEmpty else { throw HologramError.message("请在 AI 设置保存手机直连的模型密钥，不需要电脑或云同步账号。") }
        let config = configuration ?? URLSessionConfiguration.ephemeral; config.httpShouldSetCookies = false; config.urlCache = nil
        let session = URLSession(configuration: config, delegate: AIRedirectBlocker(), delegateQueue: nil)
        let observer = NotificationCenter.default.addObserver(forName: AIEnvironment.changed, object: nil, queue: nil) { _ in session.invalidateAndCancel() }
        defer { NotificationCenter.default.removeObserver(observer); session.invalidateAndCancel() }
        var messages: [[String: Any]] = [["role": "system", "content": instructions]] + history.map { ["role": $0["Role"] ?? "user", "content": $0["Content"] ?? ""] }
        var contents: [[String: Any]] = history.map { ["role": $0["Role"] == "assistant" ? "model" : "user", "parts": [["text": $0["Content"] ?? ""]]] }
        var activities: [[String: Any]] = []
        func visible(_ text: String) throws -> String { activities.isEmpty ? text : "```deskbox-results\n" + (try HologramWire.json(Array(activities.suffix(8)))) + "\n```\n" + text }
        for round in 0..<8 {
            guard generation == AIEnvironment.generation else { throw CancellationError() }
            try Task.checkCancellation(); try environment.requireNetwork()
            let canCall = round < 7
            var body: [String: Any]
            if profile.provider == .compatible {
                body = ["model": profile.model, "messages": messages, "stream": false]
                if canCall { body["tools"] = declarations(profile).map { ["type": "function", "function": $0] }; body["tool_choice"] = "auto" }
            } else {
                body = ["contents": contents, "systemInstruction": ["parts": [["text": instructions]]]]
                if canCall { body["tools"] = [["functionDeclarations": declarations(profile)]] }
            }
            var request = URLRequest(url: url, timeoutInterval: 120); request.httpMethod = "POST"
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.setValue(profile.provider == .gemini ? profile.key : "Bearer " + profile.key, forHTTPHeaderField: profile.provider == .gemini ? "x-goog-api-key" : "Authorization")
            request.httpBody = try JSONSerialization.data(withJSONObject: body)
            guard request.httpBody!.count <= 512_000 else { throw HologramError.message("对话上下文过长，请保存记录后新建对话。") }
            progress("手机直连 · 正在思考")
            let (stream, response) = try await session.bytes(for: request)
            guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
                let code = (response as? HTTPURLResponse)?.statusCode ?? 0
                throw HologramError.message("模型请求失败（\(code)），请核对手机密钥、模型权限及额度。")
            }
            var data = Data()
            for try await byte in stream { if data.count >= 2_097_152 { throw HologramError.message("模型返回内容过大，请缩小问题范围。") }; data.append(byte) }
            try Task.checkCancellation(); try environment.requireNetwork()
            guard let root = try JSONSerialization.jsonObject(with: data) as? [String: Any] else { throw HologramError.message("模型响应格式无法识别。") }
            guard generation == AIEnvironment.generation else { throw CancellationError() }
            let text: String, calls: [HologramFunction]
            var providerIDs = Set<String>()
            if profile.provider == .compatible {
                guard let message = (root["choices"] as? [[String: Any]])?.first?["message"] as? [String: Any] else { throw HologramError.message("模型未返回可用回复。") }
                text = message["content"] as? String ?? ""
                calls = try (message["tool_calls"] as? [[String: Any]] ?? []).map { value in
                    guard let id = value["id"] as? String, let function = value["function"] as? [String: Any], let name = function["name"] as? String, let json = function["arguments"] as? String, json.utf8.count <= 32000, let args = try JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any] else { throw HologramError.message("工具参数无效，未执行操作。") }
                    return HologramFunction(id: id, name: name, arguments: args)
                }
                messages.append(message)
            } else {
                guard let content = (root["candidates"] as? [[String: Any]])?.first?["content"] as? [String: Any], let parts = content["parts"] as? [[String: Any]] else { throw HologramError.message("模型未返回可用回复。") }
                text = parts.compactMap { $0["text"] as? String }.joined()
                calls = parts.compactMap { part in
                    guard let call = part["functionCall"] as? [String: Any], let name = call["name"] as? String, let args = call["args"] as? [String: Any] else { return nil }
                    if let id = call["id"] as? String { providerIDs.insert(id) }
                    return HologramFunction(id: call["id"] as? String ?? UUID().uuidString, name: name, arguments: args)
                }
                contents.append(content) // Preserve thought signatures and the provider's original parts.
            }
            if calls.isEmpty { guard !text.isEmpty else { throw HologramError.message("模型返回空回复，请重试。") }; return try visible(text) }
            guard canCall, calls.count <= 8 else { throw HologramError.message("工具调用达到本轮上限，请先查看已有结果再继续。") }
            var responses: [[String: Any]] = []
            for call in calls {
                guard generation == AIEnvironment.generation else { throw CancellationError() }
                try Task.checkCancellation(); try environment.requireNetwork()
                progress("手机操作 · " + AIToolPresentation(name: call.name, raw: "{}").title)
                let allowed = declarations(profile).contains { $0["name"] as? String == call.name }
                let result = allowed ? await execute(call) : ["ok": false, "error": "当前环境未开放此工具。"]
                let json = try HologramWire.json(result)
                activities.append(["name": call.name, "ok": result["ok"] as? Bool ?? false, "result": String(MobileAIRequest.prefix(json, units: 6000))])
                progress(try visible("")) // Persist completed work even if a later request fails or is cancelled.
                if profile.provider == .compatible { messages.append(["role": "tool", "tool_call_id": call.id, "content": json]) }
                else { var response: [String: Any] = ["name": call.name, "response": result]; if providerIDs.contains(call.id) { response["id"] = call.id }; responses.append(["functionResponse": response]) }
            }
            if !responses.isEmpty { contents.append(["role": "user", "parts": responses]) }
        }
        throw HologramError.message("已达到本轮操作上限，请继续提问。")
    }
}
