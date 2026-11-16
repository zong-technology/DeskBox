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
