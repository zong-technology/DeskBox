import Foundation

struct HologramDiagnosticEvent: Codable {
    let date: Date
    let kind: String
    let details: [String: String]
}
@MainActor final class HologramDiagnostics {
    static var url: URL { FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appendingPathComponent("hologram-diagnostics.json") }
    private(set) var events: [HologramDiagnosticEvent] = []
    private static let kinds: Set<String> = ["call.start", "call.failure", "call.pause", "call.stop", "input.jitter", "input.failure", "input.mute", "response.failure", "response.interrupted", "response.user_interrupt", "output.limit"]
    private static func safe(_ key: String, _ value: String) -> Bool {
        switch key {
        case "code": return value.range(of: "^-?[0-9]{1,8}$", options: .regularExpression) != nil
        case "queuedMs", "lost", "seconds": return value.range(of: "^[0-9]{1,9}$", options: .regularExpression) != nil
        case "provider": return ["gemini", "qwen"].contains(value)
        case "reason": return ["connect", "receive", "decode", "handle", "background", "audio_interruption", "route_disconnected", "idle", "failed", "paused"].contains(value)
        case "inputStarted", "muted": return ["true", "false"].contains(value)
        default: return false
        }
    }
    func note(_ kind: String, _ values: [String: String] = [:]) {
        guard Self.kinds.contains(kind) else { return }
        let safe = values.filter { Self.safe($0.key, $0.value) }
        events.append(HologramDiagnosticEvent(date: Date(), kind: kind, details: safe)); events = Array(events.suffix(200))
    }
    func save() {
        guard let data = try? JSONEncoder().encode(events) else { return }
        try? data.write(to: Self.url, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
    }
    static func text() -> String {
        guard let data = try? Data(contentsOf: url), let events = try? JSONDecoder().decode([HologramDiagnosticEvent].self, from: data) else { return "还没有已保存的通话诊断。诊断不包含音频、转写或密钥。" }
        return events.map { ISO8601DateFormatter().string(from: $0.date) + "  " + $0.kind + "  " + $0.details.sorted { $0.key < $1.key }.map { $0.key + "=" + $0.value }.joined(separator: " ") }.joined(separator: "\n")
    }
}
