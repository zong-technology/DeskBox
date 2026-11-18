import Foundation

@MainActor final class HologramLocalTools {
    private let store: OfflineStore
    private let lastSync: () -> Date?
    private var completed: [String: (String, [String: Any])] = [:]
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
        if Self.actionNames.contains(call.name) {
            guard !store.loadFailed else { return ["ok": false, "error": store.storageError] }
            let signature = (try? HologramWire.json(["name": call.name, "arguments": args])) ?? ""
            if let previous = completed[call.id] { return previous.0 == signature ? previous.1 : ["ok": false, "error": "重复工具ID的参数不同，请重新查询。"] }
            let result = mutate(call)
            if result["ok"] as? Bool == true { if completed.count >= 100 { completed.removeAll() }; completed[call.id] = (signature, result) }
            return result
        }
        guard ["deskbox_context", "deskbox_tasks", "deskbox_projects", "deskbox_incidents"].contains(call.name), !store.loadFailed else { return ["ok": false, "error": "此工具未提供，或本机数据暂不可读取。"] }
        let query = args["query"] as? String ?? "", offset = max(0, args["offset"] as? Int ?? 0), limit = min(30, max(1, args["limit"] as? Int ?? 12))
        let stamp = lastSync().map { ISO8601DateFormatter().string(from: $0) } ?? "尚无成功同步时间"
        var result: [String: Any] = ["ok": true, "source": "iPhone本地同步缓存，不是设备实时测量", "lastSyncedAt": stamp, "pendingUploads": store.pending.count]
        do {
            func values<T: Encodable>(_ items: [T]) throws -> Any { try JSONSerialization.jsonObject(with: JSONEncoder().encode(Array(items.dropFirst(offset).prefix(limit)))) }
            let project = try resolveProject(args)
            func projectText(_ id: String) -> String { store.projects.first(where: { $0.id == id }).map { $0.name + " " + $0.number + " " + $0.equipment } ?? "" }
            switch call.name {
            case "deskbox_tasks":
                let status = args["status"] as? String ?? "all"
                let items = store.tasks.filter { (project.isEmpty || $0.projectId == project) && (query.isEmpty || ($0.title + " " + $0.notes + " " + $0.owner + " " + projectText($0.projectId)).localizedCaseInsensitiveContains(query)) && (status != "open" || $0.status != "done") && (status != "done" || $0.status == "done") }
                result["total"] = items.count; result["tasks"] = try items.dropFirst(offset).prefix(limit).map { try stamped($0) }
            case "deskbox_projects":
                let items = store.projects.filter { query.isEmpty || ($0.name + $0.number + $0.equipment).localizedCaseInsensitiveContains(query) }
                result["total"] = items.count; result["projects"] = try values(items)
            case "deskbox_incidents":
                let items = store.incidents.filter { (project.isEmpty || $0.projectId == project) && (query.isEmpty || ($0.description + $0.equipment + $0.solution + projectText($0.projectId)).localizedCaseInsensitiveContains(query)) }
                result["total"] = items.count; result["incidents"] = try items.dropFirst(offset).prefix(limit).map { try stamped($0) }
            default: result["tools"] = HologramWire.declarations; result["computerTools"] = "deskbox_execute(tool: app.context, arguments: {}) 读取已连接电脑的完整工具目录；电脑须在线。"; result["tasks"] = store.tasks.count; result["projects"] = store.projects.count; result["incidents"] = store.incidents.count
            }
            let text = try HologramWire.json(result)
            if text.count > 16000 { return ["ok": false, "error": "结果过长，请按关键词缩小查询或减少limit。", "source": "iPhone本地同步缓存"] }
            return result
        } catch { return ["ok": false, "error": error.localizedDescription] }
    }

    static let actionNames = ["deskbox_task_create", "deskbox_task_update", "deskbox_incident_create", "deskbox_incident_update"]
    private func resolveProject(_ args: [String: Any]) throws -> String {
        let id = (args["project_id"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if !id.isEmpty {
            guard store.projects.contains(where: { $0.id == id }) else { throw HologramError.message("项目ID不存在，请先deskbox_projects查询真实项目。") }
            return id
        }
        let name = (args["project"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if name.isEmpty { return "" }
        let exact = store.projects.filter { $0.name == name || $0.number == name }
        let matches = exact.isEmpty ? store.projects.filter { ($0.name + " " + $0.number + " " + $0.equipment).localizedCaseInsensitiveContains(name) } : exact
        guard matches.count == 1 else { throw HologramError.message(matches.isEmpty ? "未找到对应项目，请先查询项目。" : "项目名称不唯一，请让用户从以下项目选择：" + matches.map { $0.name + "（" + $0.id + "）" }.joined(separator: "、")) }
        return matches[0].id
    }
    private func stamp<T: Encodable>(_ item: T) throws -> String {
        let encoder = JSONEncoder(); encoder.outputFormatting = [.sortedKeys]
        return String(decoding: try encoder.encode(item), as: UTF8.self)
    }
    private func stamped<T: Encodable>(_ item: T) throws -> [String: Any] {
        var result = try JSONSerialization.jsonObject(with: JSONEncoder().encode(item)) as! [String: Any]
        result["expected"] = try stamp(item); return result
    }
    private func mutate(_ call: HologramFunction) -> [String: Any] {
        do {
            let args = call.arguments
            func text(_ key: String) -> String { args[key] as? String ?? "" }
            var result: [String: Any] = ["ok": true, "saved": true, "synced": false, "sync": "已保存到手机，已加入同步队列；尚未确认电脑收到。"]
            if call.name.hasPrefix("deskbox_task_") {
                let updating = call.name == "deskbox_task_update"
                let existing = store.tasks.first { $0.id == text("id") }
                if updating {
                    guard let existing, try stamp(existing) == text("expected") else { throw HologramError.message("任务已变化、已删除或缺少expected；请先deskbox_tasks重新查询。") }
                }
                var task = updating ? existing! : MobileTask()
                if args["title"] != nil { task.title = text("title").trimmingCharacters(in: .whitespacesAndNewlines) }
                if args["notes"] != nil { task.notes = text("notes") }
                if args["due"] != nil { task.due = text("due") }
                if args["priority"] != nil { task.priority = text("priority") }
                if args["owner"] != nil { task.owner = text("owner") }
                if args["status"] != nil { task.status = text("status") }
                if let done = args["done"] as? Bool { task.status = done ? "done" : "open" }
                if args["project_id"] != nil || args["project"] != nil { task.projectId = try resolveProject(args) }
                if let error = store.updateTask(task, expected: updating ? existing : nil) { throw HologramError.message(error) }
                result["task"] = try stamped(store.tasks.first { $0.id == task.id }!)
            } else {
                let updating = call.name == "deskbox_incident_update"
                let existing = store.incidents.first { $0.id == text("id") }
                if updating {
                    guard let existing, try stamp(existing) == text("expected") else { throw HologramError.message("事件已变化、已删除或缺少expected；请先deskbox_incidents重新查询。") }
                }
                var item = updating ? existing! : Incident()
                if args["project_id"] != nil || args["project"] != nil { item.projectId = try resolveProject(args) }
                if args["description"] != nil { item.description = text("description") }
                if args["equipment"] != nil { item.equipment = text("equipment") }
                if args["owner"] != nil { item.owner = text("owner") }
                if args["severity"] != nil { item.severity = text("severity") }
                if args["status"] != nil { item.status = text("status") }
                if args["cause"] != nil { item.cause = text("cause") }
                if args["solution"] != nil { item.solution = text("solution") }
                if let error = store.updateIncident(item, expectedUpdatedAt: updating ? existing?.updatedAt : nil) { throw HologramError.message(error) }
                result["incident"] = try stamped(store.incidents.first { $0.id == item.id }!)
            }
            result["pendingUploads"] = store.pending.count
            return result
        } catch { return ["ok": false, "saved": false, "error": error.localizedDescription] }
    }
}
