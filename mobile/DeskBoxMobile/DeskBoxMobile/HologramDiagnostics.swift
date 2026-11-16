import Foundation

struct HologramDiagnosticEvent: Codable {
    let date: Date
    let kind: String
    let details: [String: String]
}
@MainActor final class HologramDiagnostics {
    static var url: URL { FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appendingPathComponent("hologram-diagnostics.json") }
    private(set) var events: [HologramDiagnosticEvent] = []
    private static let fields: Set<String> = ["code", "provider", "reason", "queuedMs", "lost", "seconds", "inputStarted", "muted"]
    func note(_ kind: String, _ values: [String: String] = [:]) {
        let safe = values.filter { Self.fields.contains($0.key) && $0.value.count <= 64 && $0.value.range(of: "^[A-Za-z0-9_.:-]*$", options: .regularExpression) != nil }
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
