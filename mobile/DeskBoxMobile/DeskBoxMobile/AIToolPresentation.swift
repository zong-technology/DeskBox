import Foundation

struct AIToolPresentation: Identifiable {
    var id = UUID().uuidString
    let name: String
    let raw: String
    private var object: [String: Any] { Self.dictionary(raw) ?? [:] }
    private static func dictionary(_ text: String) -> [String: Any]? { (try? JSONSerialization.jsonObject(with: Data(text.utf8))) as? [String: Any] }
    private func value(_ root: [String: Any], _ key: String) -> Any? { root.first { $0.key.lowercased() == key.lowercased() }?.value }
    private func text(_ root: [String: Any], _ key: String) -> String { value(root, key) as? String ?? "" }
    private var payload: [String: Any] {
        if let nested = value(object, "result") as? [String: Any] { return nested }
        if let nested = value(object, "result") as? String, let decoded = Self.dictionary(nested) { return decoded }
        return object
    }
    var succeeded: Bool { (value(object, "ok") as? Bool) != false && text(object, "error").isEmpty }
    var title: String {
        let names = ["deskbox_tasks": "查询任务", "task.list": "查询任务", "deskbox_projects": "查询项目", "project.list": "查询项目", "deskbox_incidents": "查询现场问题", "incident.list": "查询现场问题", "deskbox_context": "读取工作状态", "app.context": "读取工作状态", "deskbox_task_create": "新增任务", "task.create": "新增任务", "deskbox_task_update": "更新任务", "task.update": "更新任务", "deskbox_incident_create": "记录现场问题", "incident.create": "记录现场问题", "deskbox_incident_update": "更新现场问题", "incident.update": "更新现场问题", "deskbox_execute": "执行电脑操作", "hologram_show": "展示全息信息", "hologram_clear": "收起全息信息", "workflow.run": "运行工作流", "doc.create": "创建文档", "doc.edit": "编辑文档", "fs.write": "保存文件"]
        return names[name] ?? "工具 · " + name
    }
    private var records: [[String: Any]] {
        for key in ["tasks", "projects", "incidents"] { if let rows = value(payload, key) as? [[String: Any]] { return rows } }
        for key in ["task", "incident"] { if let item = value(payload, key) as? [String: Any] { return [item] } }
        return []
    }
    private func rowTitle(_ row: [String: Any]) -> String {
        let item = value(row, "incident") as? [String: Any] ?? row
        for key in ["title", "name", "description"] { let label = text(item, key); if !label.isEmpty { return label } }
        return "记录"
    }
    var summary: String {
        if !succeeded { let error = text(object, "error"); return error.isEmpty ? String(raw.prefix(160)) : error }
        if value(payload, "saved") as? Bool == true { return "已保存" + (records.first.map { " · " + rowTitle($0) } ?? "") }
        if let count = value(payload, "total") as? Int { return "找到 \(count) 项" + (records.isEmpty ? "" : " · 当前显示 \(records.count) 项") }
        let message = text(payload, "message")
        if !message.isEmpty { return message }
        if !records.isEmpty { return "已完成 · \(records.count) 项" }
        if !object.isEmpty { return "已完成 · 展开查看详情" }
        return String(raw.prefix(100))
    }
    var details: String {
        var lines: [String] = []
        for row in records {
            let item = value(row, "incident") as? [String: Any] ?? row
            var line = "• " + rowTitle(item)
            let status = text(item, "status")
            if value(item, "done") as? Bool == true || status == "done" { line += " · 已完成" }
            else if status == "open" { line += " · 待处理" }
            let priority = text(item, "priority")
            if priority == "high" { line += " · 高优先级" }
            let due = text(item, "due"); if !due.isEmpty { line += " · 截止 " + due }
            let notes = text(item, "notes"); if !notes.isEmpty { line += "\n  " + notes }
            lines.append(line)
        }
        for key in ["sync", "source", "lastSyncedAt"] { let label = text(payload, key); if !label.isEmpty { lines.append(label) } }
        if lines.isEmpty { let result = text(object, "result"); if Self.dictionary(result) == nil && !result.isEmpty { lines.append(result) } }
        return lines.isEmpty ? summary : lines.joined(separator: "\n\n")
    }
    static func history(_ text: String) -> AIToolPresentation {
        let parts = text.split(separator: "\n", maxSplits: 1, omittingEmptySubsequences: false)
        return AIToolPresentation(name: parts.first.map(String.init) ?? "tool", raw: parts.count > 1 ? String(parts[1]) : text)
    }
    static func conversation(_ text: String) -> (text: String, tools: [AIToolPresentation]) {
        let prefix = "```deskbox-results\n"
        guard text.hasPrefix(prefix), let end = text.range(of: "\n```", range: text.index(text.startIndex, offsetBy: prefix.count)..<text.endIndex) else { return (text, []) }
        let data = Data(text[text.index(text.startIndex, offsetBy: prefix.count)..<end.lowerBound].utf8)
        guard let values = (try? JSONSerialization.jsonObject(with: data)) as? [[String: Any]] else { return (text, []) }
        let tools = values.enumerated().map { index, item -> AIToolPresentation in
            let json = (try? JSONSerialization.data(withJSONObject: item, options: [.sortedKeys])) ?? Data()
            return AIToolPresentation(id: String(index), name: item["name"] as? String ?? "tool", raw: String(decoding: json, as: UTF8.self))
        }
        return (String(text[end.upperBound...]).trimmingCharacters(in: .whitespacesAndNewlines), tools)
    }
}
