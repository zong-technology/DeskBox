import Foundation
import Combine

struct HologramMessage: Codable, Identifiable {
    var id = UUID().uuidString
    var role: String
    var text: String
    var date = Date()
    var partial = false
    var streaming: Bool? = nil
    var attachment: MobileAIAttachment? = nil
}
struct HologramSession: Codable, Identifiable {
    var id = UUID().uuidString
    var name: String, persona: String, model: String
    var date = Date()
    var messages: [HologramMessage] = []
    var kind: String? = nil
}
@MainActor final class HologramHistory: ObservableObject {
    @Published private(set) var sessions: [HologramSession] = []
    @Published private(set) var error = ""
    private let url: URL
    private var loadFailed = false
    init(url: URL? = nil) {
        self.url = url ?? FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appendingPathComponent("hologram-history.json")
        guard FileManager.default.fileExists(atPath: self.url.path) else { return }
        do {
            sessions = try JSONDecoder().decode([HologramSession].self, from: Data(contentsOf: self.url))
            for session in sessions.indices { for message in sessions[session].messages.indices where sessions[session].messages[message].streaming == true {
                sessions[session].messages[message].streaming = false; sessions[session].messages[message].partial = true
            } }
        }
        catch { loadFailed = true; self.error = "全息记录读取失败，原文件已保留，暂停覆盖。" }
    }
    func upsert(_ session: HologramSession) {
        guard !session.messages.isEmpty, !loadFailed else { return }
        var value = session; value.messages = Array(value.messages.suffix(200))
        sessions.removeAll { $0.id == value.id }; sessions.insert(value, at: 0); sessions = Array(sessions.prefix(50)); save()
    }
    func delete(_ ids: Set<String>) { guard !loadFailed else { return }; sessions.removeAll { ids.contains($0.id) }; save() }
    private func save() {
        do { try JSONEncoder().encode(sessions).write(to: url, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication]); error = "" }
        catch { self.error = "记录暂未保存，请检查手机存储空间。" }
    }
}
struct HologramRecorder {
    private(set) var session: HologramSession
    private var userID: String?, assistantID: String?
    private var started: [String: Date] = [:]
    init(profile: HologramProfile) { session = HologramSession(name: profile.name, persona: profile.persona, model: profile.model) }
    mutating func accept(_ packet: HologramPacket) {
        if packet.interrupted || packet.responseFailed { markAssistantPartial(); userID = nil }
        if packet.inputStarted { userID = packet.userID ?? UUID().uuidString; started[userID!] = Date() }
        if !packet.user.isEmpty {
            let id = packet.userID ?? userID ?? UUID().uuidString; userID = id
            put(id: id, role: "user", text: packet.user, append: packet.userDelta)
        }
        if !packet.caption.isEmpty {
            let id = assistantID ?? UUID().uuidString; assistantID = id
            put(id: id, role: "assistant", text: packet.caption, append: true)
        }
        if packet.turnComplete {
            if let id = assistantID, let index = session.messages.firstIndex(where: { $0.id == id }) { session.messages[index].streaming = false }
            assistantID = nil; userID = nil
        }
    }
    mutating func tool(_ name: String, result: String) { put(id: UUID().uuidString, role: "tool", text: name + "\n" + result, append: false) }
    mutating func close() { markAssistantPartial(); userID = nil }
    private mutating func markAssistantPartial() {
        if let id = assistantID, let index = session.messages.firstIndex(where: { $0.id == id }) { session.messages[index].partial = true; session.messages[index].streaming = false }; assistantID = nil
    }
    private mutating func put(id: String, role: String, text: String, append: Bool) {
        if let index = session.messages.firstIndex(where: { $0.id == id }) { session.messages[index].text = append ? session.messages[index].text + text : text }
        else { session.messages.append(HologramMessage(id: id, role: role, text: text, date: started[id] ?? Date())); session.messages.sort { $0.date < $1.date } }
        if role == "assistant", let index = session.messages.firstIndex(where: { $0.id == id }) { session.messages[index].streaming = true }
    }
}

enum HologramMode: String { case idle, connecting, listening, thinking, speaking, muted, paused, failed }
struct HologramMotion {
    var energy = 0.0, speaking = 0.0, thinking = 0.0, flow = 0.0, impulse = 0.0
    var pulseAges = [10.0, 10.0, 10.0], pulseStrength = [0.0, 0.0, 0.0]
    private var previous = 0.0, sincePulse = 1.0, nextPulse = 0
    static func level(_ rms: Double) -> Double { min(1, sqrt(max(0, rms - 0.004) * 5.5)) }
    mutating func advance(_ elapsed: Double, mode: HologramMode, target: Double, reduced: Bool) {
        let dt = min(0.1, max(0, elapsed)), target = min(1, max(0, target))
        energy += (target - energy) * (1 - exp(-dt / (target > energy ? 0.045 : 0.23)))
        speaking += ((mode == .speaking ? 1 : 0) - speaking) * (1 - exp(-dt / 0.16))
        thinking += ((mode == .thinking || mode == .connecting ? 1 : 0) - thinking) * (1 - exp(-dt / 0.24))
        impulse *= exp(-dt / 0.18); sincePulse += dt
        for index in pulseAges.indices { pulseAges[index] += dt }
        if target - previous > 0.075 && target > 0.12 && sincePulse > 0.14 && !reduced {
            pulseAges[nextPulse] = 0; pulseStrength[nextPulse] = 0.3 + target * 0.7; impulse = min(1, (target - previous) * 2.8)
            nextPulse = (nextPulse + 1) % 3; sincePulse = 0
        }
        previous = target
        if !reduced { flow += dt * ((mode == .idle || mode == .muted || mode == .paused ? 0.10 : 0.28) + thinking * 0.55 + energy * (0.8 + speaking * 1.4)) }
    }
    mutating func interrupt() { energy = 0; speaking = 0; impulse = 0; previous = 0; pulseAges = [10, 10, 10] }
}
