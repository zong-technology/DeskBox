import Foundation

@main struct MobileHologramCheck {
    @MainActor static func main() throws {
        var checks = 0
        func check(_ condition: Bool, _ text: String) { precondition(condition, text); checks += 1; print("PASS " + text) }
        var profile = HologramProfile(); profile.key = "fixture-secret-never-send"; profile.name = "星璃"; profile.persona = "温柔严谨的工程伙伴"
        try profile.validate(); check(profile.ready, "Configured voice profile is ready")
        var invalid = profile; invalid.endpoint += "?key=must-not-embed"; check(!invalid.ready, "Endpoint authentication cannot be embedded in a saved URL")
        invalid = profile; invalid.endpoint = "http://fixture.invalid"; check(!invalid.ready, "Non-TLS endpoints are rejected")
        invalid = profile; invalid.name = String(repeating: "字", count: 25); check(!invalid.ready, "Assistant identity is bounded")
        var oldJSON = try JSONSerialization.jsonObject(with: JSONEncoder().encode(profile)) as! [String: Any]
        oldJSON.removeValue(forKey: "allowInterruption")
        let migrated = try JSONDecoder().decode(HologramProfile.self, from: JSONSerialization.data(withJSONObject: oldJSON))
        check(migrated.key == profile.key && migrated.name == profile.name && !migrated.allowInterruption, "Upgrade keeps the existing voice key and role while enabling stable playback")
        let setup = try HologramWire.json(HologramWire.setup(profile))
        check(setup.contains(profile.name) && setup.contains(profile.persona) && setup.contains("inputAudioTranscription"), "Gemini setup contains role and both transcripts")
        check(!setup.contains(profile.key), "Session instructions never contain the API key")
        let request = try HologramWire.request(profile); check(URLComponents(url: request.url!, resolvingAgainstBaseURL: false)?.queryItems?.first?.value == profile.key, "Gemini authentication uses its required request field")
        profile.service = .qwen; profile.endpoint = "wss://fixture.invalid/live"; profile.model = "qwen3.8-omni-flash-realtime"
        let qwen = try HologramWire.json(HologramWire.setup(profile))
        check(qwen.contains("\"function\":{") && qwen.contains("qwen3-asr-flash-realtime"), "Qwen uses nested functions and explicitly enabled input transcription")
        check(try HologramWire.request(profile).value(forHTTPHeaderField: "Authorization") == "Bearer " + profile.key, "Qwen authentication stays in the header")
        func packet(_ json: String, _ service: HologramService = .gemini) throws -> HologramPacket { try HologramWire.decode(Data(json.utf8), service: service) }
        let ready = try packet("{\"setupComplete\":{}}")
        check(ready.ready, "A provider acknowledgement gates microphone start")
        let input = try packet("{\"serverContent\":{\"inputTranscription\":{\"text\":\"查询任务\",\"finished\":true}}}")
        check(input.inputEnded && input.userDelta && input.user == "查询任务", "User transcription marks a real input boundary")
        let voice = try packet("{\"serverContent\":{\"modelTurn\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"audio/pcm;rate=24000\",\"data\":\"AQACAA==\"}}]}}}")
        check(voice.audio.first?.count == 4, "Output PCM is decoded with the negotiated sample rate")
        let interrupted = try packet("{\"serverContent\":{\"interrupted\":true,\"modelTurn\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"audio/pcm;rate=24000\",\"data\":\"AQACAA==\"}}]}}}")
        check(interrupted.interrupted && interrupted.audio.isEmpty, "Interrupted frames do not queue stale sound")
        let tool = try packet("{\"toolCall\":{\"functionCalls\":[{\"id\":\"g1\",\"name\":\"hologram_clear\",\"args\":{}}]}}")
        check(tool.functions.first?.id == "g1", "Function calls keep their response correlation ID")
        let q = try packet("{\"type\":\"conversation.item.input_audio_transcription.completed\",\"item_id\":\"u1\",\"transcript\":\"你好\"}", .qwen)
        check(q.userID == "u1" && !q.userDelta, "Qwen completed transcription has a stable user turn ID")
        let failedTurn = try packet("{\"type\":\"response.done\",\"response\":{\"status\":\"failed\"}}", .qwen)
        check(failedTurn.turnComplete && failedTurn.responseFailed, "Qwen failed turns are reported without treating the WebSocket as disconnected")
        check(try packet("{\"type\":\"error\",\"error\":{\"code\":\"response_cancel_not_active\"}}", .qwen).audio.isEmpty, "A finished-response cancellation race cannot terminate a Qwen call")
        do { _ = try packet("{\"error\":{\"message\":\"fixture-secret-never-send\"}}"); preconditionFailure() }
        catch { check(!error.localizedDescription.contains(profile.key), "Upstream errors do not reveal authentication details") }
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("deskbox-holo-test-" + UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true); defer { try? FileManager.default.removeItem(at: directory) }
        let store = OfflineStore(fileURL: directory.appendingPathComponent("sync.json")); store.tasks = [MobileTask(title: "审核设备清单")]
        let tools = HologramLocalTools(store: store, lastSync: { Date(timeIntervalSince1970: 100) })
        let read = tools.execute(HologramFunction(id: "r1", name: "deskbox_tasks", arguments: ["query": "设备"]))
        check(try HologramWire.json(read).contains("审核设备清单") && read["source"] != nil && read["lastSyncedAt"] != nil, "AI reads actual cached tasks and receives the cache source and timestamp")
        check(tools.execute(HologramFunction(id: "no", name: "fs.write", arguments: [:]))["ok"] as? Bool == false, "Unregistered file and action tools cannot execute")
        _ = tools.execute(HologramFunction(id: "s", name: "hologram_show", arguments: ["title": "今日任务", "content": "审核设备清单", "seconds": 999]))
        check(tools.information != nil && tools.information!.expires < Date().addingTimeInterval(121), "Temporary projections have a bounded lifetime")
        check(tools.execute(HologramFunction(id: "bad", name: "hologram_show", arguments: ["title": "标题", "content": String(repeating: "字", count: 481)]))["ok"] as? Bool == false, "Oversized projections return a tool error")
        _ = tools.execute(HologramFunction(id: "c", name: "hologram_clear", arguments: [:])); check(tools.information == nil, "The AI can clear temporary information")
        store.projects = [MobileProject(id: "UF", name: "超滤项目"), MobileProject(id: "PAS", name: "PAS平台")]
        store.tasks.append(MobileTask(id: "pas-task", projectId: "PAS", title: "其他项目记录"))
        let created = tools.execute(HologramFunction(id: "create", name: "deskbox_task_create", arguments: ["project": "超滤", "title": "SIP的FS需要更新步骤", "notes": "请供应商补充最新操作步骤", "owner": "王工"]))
        check(created["saved"] as? Bool == true && created["synced"] as? Bool == false && store.tasks.last?.projectId == "UF" && store.pending.count == 1, "A spoken project issue is actually saved to the right project and queued, never falsely reported as synced")
        _ = tools.execute(HologramFunction(id: "create", name: "deskbox_task_create", arguments: ["project": "超滤", "title": "SIP的FS需要更新步骤", "notes": "请供应商补充最新操作步骤", "owner": "王工"]))
        check(store.pending.count == 1 && store.tasks.count == 3, "Replayed provider function IDs cannot create duplicate tasks")
        check(tools.execute(HologramFunction(id: "owner-query", name: "deskbox_tasks", arguments: ["query": "王工"]))["total"] as? Int == 1, "Hologram task queries can find the actual assigned owner")
        let scoped = tools.execute(HologramFunction(id: "scoped", name: "deskbox_tasks", arguments: ["project_id": "UF"]))
        let ufTasks = scoped["tasks"] as! [[String: Any]]
        check(ufTasks.count == 1 && ufTasks[0]["ProjectId"] as? String == "UF", "Task queries stay within the requested project")
        check(tools.execute(HologramFunction(id: "byname", name: "deskbox_tasks", arguments: ["query": "超滤"]))["total"] as? Int == 1, "Project-name task queries also match the linked project")
        let taskID = ufTasks[0]["Id"] as! String, expected = ufTasks[0]["expected"] as! String
        let updated = tools.execute(HologramFunction(id: "update", name: "deskbox_task_update", arguments: ["id": taskID, "expected": expected, "notes": "需要供应商提供修订版FS", "done": true]))
        check(updated["ok"] as? Bool == true && store.tasks.first(where: { $0.id == taskID })?.status == "done", "Voice task updates preserve real completion state and notes")
        check(tools.execute(HologramFunction(id: "stale", name: "deskbox_task_update", arguments: ["id": taskID, "expected": expected, "title": "stale overwrite"]))["ok"] as? Bool == false, "Stale AI edits cannot overwrite a newer task")
        check(tools.execute(HologramFunction(id: "bad-project", name: "deskbox_task_create", arguments: ["project_id": "missing", "title": "must not save"]))["ok"] as? Bool == false && store.tasks.count == 3, "Unknown project IDs cannot produce misplaced tasks")
        store.projects.append(MobileProject(id: "UF2", name: "超滤二期"))
        check(tools.execute(HologramFunction(id: "ambiguous", name: "deskbox_task_create", arguments: ["project": "超滤", "title": "ambiguous"]))["ok"] as? Bool == false, "Ambiguous project names require selection before writing")
        check(tools.execute(HologramFunction(id: "date", name: "deskbox_task_create", arguments: ["title": "bad date", "due": "2026-02-30"]))["ok"] as? Bool == false, "Invalid calendar dates cannot enter the offline task queue")
        let incident = tools.execute(HologramFunction(id: "issue", name: "deskbox_incident_create", arguments: ["project_id": "UF", "description": "SIP的FS需更新步骤", "severity": "S2"]))
        check(incident["ok"] as? Bool == true && store.incidents.last?.projectId == "UF", "Voice issues use the real incident store and sync queue")
        let issue = incident["incident"] as! [String: Any]
        check(tools.execute(HologramFunction(id: "resolve", name: "deskbox_incident_update", arguments: ["id": issue["Id"]!, "expected": issue["expected"]!, "status": "resolved"]))["ok"] as? Bool == false && store.incidents.last?.status == "open", "Resolving issues requires actual handling measures")
        let reloaded = OfflineStore(fileURL: directory.appendingPathComponent("sync.json"))
        check(reloaded.tasks.contains(where: { $0.id == taskID && $0.status == "done" }) && reloaded.incidents.count == 1 && reloaded.pending.count == 2, "AI changes survive process restart with their upload queue intact")
        let presentation = AIToolPresentation(name: "deskbox_tasks", raw: try HologramWire.json(scoped))
        check(presentation.summary.contains("1") && presentation.details.contains("SIP") && !presentation.details.contains("expected"), "Readable tool cards show a task summary instead of protocol fields")
        let cards = AIToolPresentation.conversation("```deskbox-results\n[{\"name\":\"task.create\",\"ok\":true,\"result\":\"saved\"}]\n```\n已记录。")
        check(cards.text == "已记录。" && cards.tools.count == 1, "Text AI tool activity is separated from spoken and visible answers")
        check(AIToolPresentation.history("deskbox_execute\n{\"ok\":false,\"error\":\"电脑离线\"}").summary == "电脑离线", "Existing tool history keeps actionable failures visible")
        var recorder = HologramRecorder(profile: profile)
        var speech = HologramPacket(); speech.inputStarted = true; speech.userID = "late"; recorder.accept(speech)
        Thread.sleep(forTimeInterval: 0.003)
        var answer = HologramPacket(); answer.caption = "收到，"; recorder.accept(answer)
        var late = HologramPacket(); late.userID = "late"; late.user = String(repeating: "详", count: 340); recorder.accept(late)
        answer.caption = "马上查看。"; answer.turnComplete = true; recorder.accept(answer); recorder.accept(late)
        check(recorder.session.messages.count == 2 && recorder.session.messages.first?.role == "user", "Delayed and repeated transcriptions keep the correct turn and chronological order")
        var partial = HologramPacket(); partial.caption = "未完成的回答"; recorder.accept(partial)
        let path = directory.appendingPathComponent("history.json"), history = HologramHistory(url: path); history.upsert(recorder.session)
        check(HologramHistory(url: path).sessions.first?.messages.last?.partial == true, "Process restart recovers an unfinished streamed reply")
        recorder.close(); history.upsert(recorder.session)
        let restored = HologramHistory(url: path)
        check(restored.sessions.first?.messages.first?.text.count == 340 && restored.sessions.first?.messages.last?.partial == true, "Complete text and interrupted reply state survive reopening")
        check(restored.sessions.first?.name == profile.name && restored.sessions.first?.persona == profile.persona, "Each call preserves the selected assistant identity")
        check(!(try String(contentsOf: path, encoding: .utf8)).contains(profile.key), "Conversation history never serializes the voice profile secret")
        let corrupt = directory.appendingPathComponent("corrupt.json"); try Data("broken original".utf8).write(to: corrupt)
        let blocked = HologramHistory(url: corrupt); blocked.upsert(recorder.session)
        let corruptText = try String(contentsOf: corrupt, encoding: .utf8)
        check(!blocked.error.isEmpty && corruptText == "broken original", "Unreadable historical data is preserved instead of overwritten")
        let diagnostics = HologramDiagnostics()
        diagnostics.note("call.failure", ["code": profile.key, "reason": profile.key, "url": profile.key])
        diagnostics.note(profile.key, ["code": "1"])
        diagnostics.note("input.jitter", ["lost": "32"])
        check(diagnostics.events.count == 2 && diagnostics.events[0].details.isEmpty && diagnostics.events[1].details["lost"] == "32", "Diagnostics accept only fixed event names and typed numeric values, never arbitrary secrets")
        var failedRecorder = HologramRecorder(profile: profile)
        var startedReply = HologramPacket(); startedReply.caption = "尚未读完的任务"
        failedRecorder.accept(startedReply); failedRecorder.accept(failedTurn)
        check(failedRecorder.session.messages.last?.partial == true, "A failed Qwen turn preserves its partial transcript rather than marking it complete")
        do { _ = try packet("{\"type\":\"error\",\"error\":{\"code\":\"invalid_api_key\"}}", .qwen); preconditionFailure() }
        catch { check(true, "Qwen authentication failures still require explicit configuration recovery") }
        var attachmentSession = recorder.session
        attachmentSession.messages.append(HologramMessage(role: "user", text: "分析附件", attachment: MobileAIAttachment(name: "SIP步骤.pdf", text: "供应商需更新步骤", note: "第1页")))
        history.upsert(attachmentSession)
        check(HologramHistory(url: path).sessions.first?.messages.last?.attachment?.text == "供应商需更新步骤", "Archived conversations retain the actual attachment content and its source")
        var textSession = recorder.session; textSession.kind = "text"; history.upsert(textSession)
        check(HologramHistory(url: path).sessions.first?.kind == "text", "Archived text conversations survive relaunch beside voice history")
        var motion = HologramMotion(); for _ in 0..<30 { motion.advance(1.0 / 30, mode: .listening, target: 0, reduced: false) }
        check(motion.energy == 0 && motion.pulseAges.allSatisfy { $0 > 1.25 }, "Quiet time does not fabricate speech pulses")
        for _ in 0..<20 { motion.advance(1.0 / 30, mode: .speaking, target: 0.85, reduced: false) }
        check(motion.energy > 0.8 && motion.speaking > 0.95, "Voice energy has a responsive visual attack")
        for _ in 0..<30 { motion.advance(1.0 / 30, mode: .speaking, target: 0, reduced: false) }
        check(motion.energy < 0.02, "Silent audio settles the particle motion")
        motion.interrupt(); check(motion.energy == 0 && motion.pulseAges.allSatisfy { $0 > 1.25 }, "Interrupting clears previous voice wavefronts")
        var reduced = HologramMotion(); reduced.advance(1, mode: .speaking, target: 0.8, reduced: true)
        check(reduced.flow == 0 && reduced.pulseAges.allSatisfy { $0 > 1.25 }, "Reduced motion disables rotation and travelling pulses")
        print("PASS \(checks) mobile hologram checks; isolated data, no network or microphone")
    }
}
