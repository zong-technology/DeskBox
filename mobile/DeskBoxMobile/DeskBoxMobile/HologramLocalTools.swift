import Foundation

@MainActor final class HologramLocalTools {
    private let store: OfflineStore
    private let lastSync: () -> Date?
    private(set) var information: HologramInformation?
    init(store: OfflineStore, lastSync: @escaping () -> Date?) { self.store = store; self.lastSync = lastSync }
    func clear() { information = nil }
    func execute(_ call: HologramFunction) -> [String: Any] {
        let args = call.arguments
        if call.name == "hologram_clear" { information = nil; return ["ok": true, "message": "已收起"] }
        if call.name == "hologram_show" {
            let title = (args["title"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            let content = (args["content"] as? String ?? "").replacingOccurrences(of: "\r\n", with: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
            guard !title.isEmpty, title.count <= 48, !content.isEmpty, content.count <= 480, content.components(separatedBy: "\n").count <= 8 else { return ["ok": false, "error": "请概括为48字内标题、480字内正文，最多8行。"] }
            let seconds = min(120, max(5, args["seconds"] as? Int ?? 30))
            information = HologramInformation(title: title, content: content, expires: Date().addingTimeInterval(Double(seconds)))
            return ["ok": true, "seconds": seconds, "message": "已展示临时投影"]
        }
        guard ["deskbox_context", "deskbox_tasks", "deskbox_projects", "deskbox_incidents"].contains(call.name), !store.loadFailed else { return ["ok": false, "error": "此工具未提供，或本机数据暂不可读取。"] }
        let query = args["query"] as? String ?? "", offset = max(0, args["offset"] as? Int ?? 0), limit = min(30, max(1, args["limit"] as? Int ?? 12))
        let stamp = lastSync().map { ISO8601DateFormatter().string(from: $0) } ?? "尚无成功同步时间"
        var result: [String: Any] = ["ok": true, "source": "iPhone本地同步缓存，不是设备实时测量", "lastSyncedAt": stamp, "pendingUploads": store.pending.count]
        do {
            func values<T: Encodable>(_ items: [T]) throws -> Any { try JSONSerialization.jsonObject(with: JSONEncoder().encode(Array(items.dropFirst(offset).prefix(limit)))) }
            switch call.name {
            case "deskbox_tasks":
                let status = args["status"] as? String ?? "all"
                let items = store.tasks.filter { (query.isEmpty || ($0.title + $0.notes).localizedCaseInsensitiveContains(query)) && (status != "open" || $0.status != "done") && (status != "done" || $0.status == "done") }
                result["total"] = items.count; result["tasks"] = try values(items)
            case "deskbox_projects":
                let items = store.projects.filter { query.isEmpty || ($0.name + $0.number + $0.equipment).localizedCaseInsensitiveContains(query) }
                result["total"] = items.count; result["projects"] = try values(items)
            case "deskbox_incidents":
                let items = store.incidents.filter { query.isEmpty || ($0.description + $0.equipment + $0.solution).localizedCaseInsensitiveContains(query) }
                result["total"] = items.count; result["incidents"] = try values(items)
            default: result["tasks"] = store.tasks.count; result["projects"] = store.projects.count; result["incidents"] = store.incidents.count
            }
            let text = try HologramWire.json(result)
            if text.count > 16000 { return ["ok": false, "error": "结果过长，请按关键词缩小查询或减少limit。", "source": "iPhone本地同步缓存"] }
            return result
        } catch { return ["ok": false, "error": "无法读取本机缓存。"] }
    }
}
