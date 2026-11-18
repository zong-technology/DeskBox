import Foundation

enum AIEnvironment: String, CaseIterable, Codable, Sendable {
    case local, network
    static let key = "aiEnvironment"
    static var current: Self { guard UserDefaults.standard.object(forKey: key) != nil else { return .network }; return Self(rawValue: UserDefaults.standard.string(forKey: key) ?? "") ?? .local }
    var title: String { self == .local ? "本地隔离" : "网络开放" }
    var historySuffix: String { self == .local ? "-local" : "" }
    static let changed = Notification.Name("DeskBoxAIEnvironmentChanged")
    private static let lock = NSLock()
    private static var revision = UUID()
    static var generation: UUID { lock.lock(); defer { lock.unlock() }; return revision }
    static func select(_ mode: Self) {
        guard mode != current else { return }
        UserDefaults.standard.set(mode.rawValue, forKey: key)
        lock.lock(); revision = UUID(); lock.unlock()
        NotificationCenter.default.post(name: changed, object: nil)
    }
    func requireNetwork() throws {
        guard self == .network, Self.current == self else { throw HologramError.message("本地隔离模式禁止联网，请在 AI 设置中切换环境。") }
    }
}

enum MobileAIProvider: String, Codable, CaseIterable { case compatible, gemini }
struct MobileAIProfile: Codable {
    var provider = MobileAIProvider.compatible
    var endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions"
    var model = "qwen-plus"
    var key = ""
    var useVoiceKey = true
    var allowComputer = false
    var allowWeb = true
    func url() throws -> URL {
        guard let parts = URLComponents(string: endpoint), parts.scheme == "https", parts.host?.isEmpty == false,
              parts.user == nil, parts.password == nil, parts.query == nil, parts.fragment == nil,
              let url = parts.url else { throw HologramError.message("模型地址需为不含密钥或参数的 HTTPS 地址。") }
        guard !model.isEmpty, model.count <= 120, model.range(of: "^[A-Za-z0-9._/-]+$", options: .regularExpression) != nil else { throw HologramError.message("请填写有效的文字模型名称。") }
        if provider == .gemini { return url.appendingPathComponent("models").appendingPathComponent(model.replacingOccurrences(of: "models/", with: "") + ":generateContent") }
        return url
    }
}

/// No redirects: model credentials and selected documents stay with the saved endpoint.
final class AIRedirectBlocker: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
}
