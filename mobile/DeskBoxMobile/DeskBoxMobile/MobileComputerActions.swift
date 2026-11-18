import Foundation

/// Uses the same paired-device job channel as text AI, without making another model request.
@MainActor enum MobileComputerActions {
    static func execute(_ arguments: [String: Any], sync: SyncClient) async -> [String: Any] {
        guard AIEnvironment.current == .network, UserDefaults.standard.bool(forKey: "aiAllowComputer") else { return ["ok": false, "error": "当前环境未开放电脑操作。手机任务与问题可独立保存。"] }
        guard sync.cloudCredential != nil else { return ["ok": false, "error": "电脑操作需先连接云账号，并让升级后的电脑端在线。手机任务与问题仍可直接保存。"] }
        guard let tool = arguments["tool"] as? String, let json = arguments["arguments"] as? String,
              !tool.isEmpty, json.utf8.count <= 32000, (try? JSONSerialization.jsonObject(with: Data(json.utf8))) is [String: Any] else { return ["ok": false, "error": "请提供工具名与JSON对象参数。"] }
        let id = UUID().uuidString.lowercased()
        var finished = false
        defer {
            if !finished {
                // Cancelling cannot undo an operation that already completed on the computer.
                Task { _ = try? await sync.mobileAIRequest("ai-cancel", body: JSONSerialization.data(withJSONObject: ["Id": id])) }
            }
        }
        do {
            try Task.checkCancellation()
            let envelope = "[DeskBoxTool:v1]\n" + (try HologramWire.json(["tool": tool, "arguments": json]))
            guard envelope.count <= 16000 else { finished = true; return ["ok": false, "error": "参数超过手机单次操作限制，请缩小文件内容或分步操作。"] }
            _ = try await sync.mobileAIRequest("ai-request", body: JSONSerialization.data(withJSONObject: ["Id": id, "Messages": [["Role": "user", "Content": envelope]]]))
            let deadline = Date().addingTimeInterval(90)
            while Date() < deadline {
                try Task.checkCancellation()
                let data = try await sync.mobileAIRequest("ai-request?id=" + id)
                try Task.checkCancellation()
                let result = try JSONDecoder().decode(CloudAIResult.self, from: data)
                if result.status == "completed" {
                    finished = true
                    guard let output = try JSONSerialization.jsonObject(with: Data(result.response.utf8)) as? [String: Any], output["ok"] is Bool else { return ["ok": false, "error": "电脑尚未支持此操作协议，请升级电脑端。未能确认执行结果，请先查询，勿重复写入。"] }
                    return output
                }
                if ["error", "cancelled"].contains(result.status) { finished = true; return ["ok": false, "error": result.error.isEmpty ? "电脑操作已取消；已完成的修改不会撤销，请先查询状态。" : result.error] }
                try await Task.sleep(nanoseconds: 700_000_000)
            }
            return ["ok": false, "error": "电脑暂未返回结果，可能离线或正在等待确认。已发送取消请求；请先查询目标状态，避免重复写入。"]
        } catch {
            return ["ok": false, "error": "电脑操作连接已中断或取消，未能确认结果。请先查询目标状态，避免重复写入。"]
        }
    }
}
