import Foundation
import Combine
import UIKit
import AVFoundation
import QuartzCore

@MainActor final class MobileHologramCall: ObservableObject {
    @Published private(set) var mode = HologramMode.idle
    @Published private(set) var caption = ""
    @Published private(set) var motion = HologramMotion()
    @Published private(set) var information: HologramInformation?
    @Published private(set) var muted = false
    @Published private(set) var notice = ""
    let profile: HologramProfile
    private let history: HologramHistory
    private let inputGate = HologramInputGate()
    private let diagnostics = HologramDiagnostics()
    private let audio = HologramAudio()
    private let tools: HologramLocalTools
    private let computer: (([String: Any]) async -> [String: Any])?
    private var toolTasks: [(UUID, Task<Void, Never>)] = []
    private let preview: Bool
    private var recorder: HologramRecorder
    private var connection: URLSessionWebSocketTask?, transport: URLSession?
    private var receiver: Task<Void, Never>?, sender: Task<Void, Never>?, timeout: Task<Void, Never>?
    private var input: AsyncStream<HologramCaptureFrame>.Continuation?
    private var generation = UUID(), active = false, ready = false, waiting = false, responseActive = false, discardOutput = false
    private var timer: Timer?, lastFrame = CACurrentMediaTime(), lastSave = Date.distantPast
    private var observedLoss = 0
    private var outputCapacityReached = false
    private var pending: [HologramFunction] = []
    private var userCaption = "", assistantCaption = "", captionFinished = true
    private var historyDirty = false
    init(profile: HologramProfile, store: OfflineStore, history: HologramHistory, lastSync: @escaping () -> Date?, computer: (([String: Any]) async -> [String: Any])? = nil, preview: Bool = false) {
        self.profile = profile; self.history = history; self.preview = preview; self.computer = computer
        recorder = HologramRecorder(profile: profile); tools = HologramLocalTools(store: store, lastSync: lastSync)
    }
    func animate() {
        guard timer == nil else { return }; lastFrame = CACurrentMediaTime()
        let interval = profile.reducedMotion || UIAccessibility.isReduceMotionEnabled ? 0.15 : 1.0 / 30
        let timer = Timer(timeInterval: interval, repeats: true) { [weak self] _ in Task { @MainActor in self?.tick() } }
        self.timer = timer; RunLoop.main.add(timer, forMode: .common)
    }
    private func tick() {
        let desired = profile.reducedMotion || UIAccessibility.isReduceMotionEnabled ? 0.15 : 1.0 / 30
        if let timer, abs(timer.timeInterval - desired) > 0.01 { timer.invalidate(); self.timer = nil; animate(); return }
        let now = CACurrentMediaTime(), dt = now - lastFrame; lastFrame = now
        var target = 0.0
        if preview { mode = .speaking; target = 0.3 + 0.55 * pow(sin(now * 5), 2); caption = "模拟预览：已为你整理今天的任务。" }
        else if ready {
            let playback = audio.playbackLevel()
            mode = playback != nil && !discardOutput ? .speaking : muted ? .muted : waiting ? .thinking : .listening
            inputGate.updatePlayback(playback != nil, now: now)
            let capture = inputGate.snapshot(now: now)
            target = HologramMotion.level(playback ?? (muted ? 0 : capture.level))
            if capture.lost > observedLoss {
                observedLoss = capture.lost; diagnostics.note("input.jitter", ["lost": String(capture.lost)])
                if capture.lost >= 10 { notice = "网络较慢，部分收音未送达，请重复最后一句。" }
            }
        }
        motion.advance(dt, mode: mode, target: target, reduced: profile.reducedMotion || UIAccessibility.isReduceMotionEnabled)
        if let information, information.expires <= Date() { self.information = nil; tools.clear() }
        if historyDirty && Date().timeIntervalSince(lastSave) > 1 && !recorder.session.messages.isEmpty { history.upsert(recorder.session); lastSave = Date(); historyDirty = false }
    }
    func start() {
        animate(); guard !active, !preview else { return }
        guard profile.ready else { mode = .idle; caption = "语音尚未连接。"; return }
        active = true; ready = false; mode = .connecting; caption = "正在与你连接…"; muted = false
        notice = ""; observedLoss = 0; outputCapacityReached = false
        diagnostics.note("call.start", ["provider": profile.service.rawValue])
        userCaption = ""; assistantCaption = ""; captionFinished = true; waiting = false; discardOutput = false
        let id = UUID(); generation = id
        receiver = Task { [weak self] in
            guard let self else { return }
            guard await HologramAudio.permission() else { if generation == id { stop(message: "麦克风未获授权。", mode: .failed) }; return }
            for _ in 0..<10 where UIApplication.shared.applicationState != .active { try? await Task.sleep(nanoseconds: 100_000_000); if generation != id { return } }
            guard generation == id, active else { return }
            guard UIApplication.shared.applicationState == .active else { stop(message: "通话已暂停，轻点投影继续。", mode: .paused); return }
            var phase = "connect"
            do {
                let config = URLSessionConfiguration.ephemeral; config.httpShouldSetCookies = false
                let session = URLSession(configuration: config); transport = session
                let socket = session.webSocketTask(with: try HologramWire.request(profile)); socket.maximumMessageSize = 2_097_152; connection = socket; socket.resume()
                try await send(HologramWire.setup(profile), id: id)
                timeout = Task { [weak self] in
                    try? await Task.sleep(nanoseconds: 20_000_000_000)
                    guard !Task.isCancelled, let self, generation == id, !ready else { return }
                    stop(message: "连接超时，轻点投影可重试。", mode: .failed)
                }
                while !Task.isCancelled {
                    phase = "receive"
                    let frame = try await socket.receive(); guard generation == id, active else { return }
                    let bytes: Data
                    switch frame { case .data(let data): bytes = data; case .string(let value): bytes = Data(value.utf8); @unknown default: continue }
                    phase = "decode"
                    let packet = try HologramWire.decode(bytes, service: profile.service)
                    phase = "handle"
                    try await consume(packet, id: id)
                }
            } catch { if generation == id && active {
                diagnostics.note("call.failure", ["reason": phase, "code": String((error as NSError).code), "queuedMs": String(Int(audio.queuedSeconds * 1000))])
                let message = (error as? HologramError)?.errorDescription ?? "网络连接中断，轻点投影可重试。"
                stop(message: message, mode: .failed)
            } }
        }
    }
    private func send(_ payload: [String: Any], id: UUID) async throws {
        guard generation == id, active, let connection else { throw CancellationError() }
        try await connection.send(.string(HologramWire.json(payload)))
    }
    private func beginAudio(id: UUID) throws {
        guard let socket = connection else { throw CancellationError() }
        let pair = HologramInputGate.stream(), gate = inputGate, service = profile.service
        input = pair.continuation; gate.activate(id, allowInterruption: profile.allowInterruption)
        sender = Task.detached(priority: .userInitiated) { [weak self] in
            var resetNeeded = false
            for await frame in pair.stream {
                guard !Task.isCancelled, gate.isActive(id) else { break }
                let now = CACurrentMediaTime()
                if now - frame.capturedAt > 1 { gate.noteLoss(); resetNeeded = true; continue }
                guard let data = gate.outgoing(frame, token: id, now: now) else { continue }
                do {
                    if resetNeeded { try await socket.send(.string(HologramWire.json(HologramWire.endInput(service)))); resetNeeded = false }
                    try await socket.send(.string(HologramWire.json(HologramWire.audio(data, service: service))))
                } catch {
                    await self?.uploadFailed(id: id, code: (error as NSError).code); break
                }
            }
        }
        try audio.start { data, rms in
            guard let frame = gate.frame(data, rms: rms, now: CACurrentMediaTime(), token: id) else { return }
            if case .dropped = pair.continuation.yield(frame) { gate.noteLoss() }
        }
    }
    private func uploadFailed(id: UUID, code: Int) {
        guard generation == id, active else { return }
        diagnostics.note("input.failure", ["code": String(code)])
        stop(message: "网络未能继续发送声音，轻点投影可重试。", mode: .failed)
    }
    private func consume(_ incoming: HologramPacket, id: UUID) async throws {
        var packet = incoming
        if packet.ready && !ready {
            guard generation == id, UIApplication.shared.applicationState == .active else { return }
            timeout?.cancel(); try beginAudio(id: id); ready = true; mode = .listening; caption = "我在听。"
        }
        if packet.responseFailed {
            cancelTools(); diagnostics.note("response.failure"); notice = "千问本轮回复未完成，可直接重新提问；若反复出现，请检查服务配额。"
            audio.flush(); inputGate.interrupt(now: CACurrentMediaTime()); motion.interrupt(); historyDirty = true
            discardOutput = true; pending.removeAll(); information = nil; tools.clear(); caption = muted ? "麦克风仍静音。" : "我在听。"
        }
        if packet.responseStarted { outputCapacityReached = false; responseActive = true; discardOutput = false; pending.removeAll() }
        if packet.interrupted {
            cancelTools(); diagnostics.note("response.interrupted", ["inputStarted": String(packet.inputStarted), "queuedMs": String(Int(audio.queuedSeconds * 1000))])
            inputGate.interrupt(now: CACurrentMediaTime()); outputCapacityReached = false
            audio.flush(); motion.interrupt(); discardOutput = true; waiting = false; pending.removeAll()
            userCaption = ""; assistantCaption = ""; captionFinished = true; caption = "我在听。"
            packet.audio = []; packet.caption = ""; historyDirty = true
        }
        if !packet.user.isEmpty {
            if profile.service == .gemini { discardOutput = false; outputCapacityReached = false }
            userCaption = packet.userDelta ? userCaption + packet.user : packet.user
            caption = "你：" + String(userCaption.suffix(150))
        }
        if discardOutput { packet.audio = []; packet.caption = ""; packet.functions = [] }
        recorder.accept(packet)
        if !packet.user.isEmpty || !packet.caption.isEmpty || packet.turnComplete { historyDirty = true }
        if packet.inputEnded { waiting = true }
        if !packet.caption.isEmpty {
            if captionFinished { assistantCaption = ""; userCaption = "" }
            assistantCaption += packet.caption; caption = String(assistantCaption.suffix(150)); captionFinished = false; waiting = true
        }
        if packet.turnComplete { waiting = false; responseActive = false; captionFinished = true }
        if packet.toolsCancelled { cancelTools(); information = nil; tools.clear(); pending.removeAll() }
        if !packet.interrupted && !packet.toolsCancelled {
            if profile.service == .qwen {
                pending += packet.functions
                guard pending.count <= 8 else { throw HologramError.message("工具请求过多。") }
                if packet.turnComplete && !discardOutput { let calls = pending; pending.removeAll(); queueTools(calls, id: id) }
            } else { queueTools(packet.functions, id: id) }
        }
        for bytes in packet.audio where !outputCapacityReached {
            inputGate.updatePlayback(true, now: CACurrentMediaTime())
            if try !audio.play(bytes) {
                outputCapacityReached = true; notice = "本轮语音较长，完整内容保存在通话记录中。"
                diagnostics.note("output.limit", ["seconds": String(Int(HologramPlayback.maximumSeconds))])
            }
        }
    }
    private func cancelTools() { for (_, task) in toolTasks { task.cancel() }; toolTasks.removeAll() }
    private func queueTools(_ calls: [HologramFunction], id: UUID) {
        guard !calls.isEmpty else { return }
        let previous = toolTasks.last?.1
        let ticket = UUID()
        let task = Task { [weak self] in
            if let previous { await previous.value }
            guard let self else { return }
            defer { toolTasks.removeAll { $0.0 == ticket } }
            guard !Task.isCancelled, generation == id, active else { return }
            do { try await complete(calls, id: id) }
            catch { if !Task.isCancelled, generation == id, active { stop(message: "工具结果发送失败，轻点投影可重试。", mode: .failed) } }
        }
        // Retain the chain until interruption/stop so all queued work can be cancelled together.
        toolTasks.append((ticket, task))
    }
    private func complete(_ calls: [HologramFunction], id: UUID) async throws {
        guard !calls.isEmpty, !Task.isCancelled else { return }; waiting = true; var results: [[String: Any]] = []
        for call in calls {
            try Task.checkCancellation()
            guard generation == id, active else { throw CancellationError() }
            let result: [String: Any]
            if call.name == "deskbox_execute" {
                result = await computer?(call.arguments) ?? ["ok": false, "error": "电脑操作通道未连接。"]
            } else { result = tools.execute(call) }
            // Keep evidence of a completed operation even if speech was interrupted while awaiting it.
            let text = try HologramWire.json(result)
            if call.name == "hologram_show" || call.name == "hologram_clear" { information = tools.information; if result["ok"] as? Bool == true { motion.impulse = 1 } }
            recorder.tool(call.name, result: text)
            historyDirty = true
            if Task.isCancelled || !active || generation != id { history.upsert(recorder.session) }
            try Task.checkCancellation()
            guard generation == id, active else { throw CancellationError() }
            if profile.service == .gemini { results.append(["id": call.id, "name": call.name, "response": result]) }
            else { try await send(["type": "conversation.item.create", "item": ["type": "function_call_output", "call_id": call.id, "output": text]], id: id) }
        }
        if profile.service == .gemini { try await send(["toolResponse": ["functionResponses": results]], id: id) }
        else { try await send(["type": "response.create"], id: id) }
        waiting = true
    }
    func toggleMute() {
        guard ready else { return }; muted.toggle()
        do { inputGate.setMuted(muted); try audio.setMuted(muted); diagnostics.note("input.mute", ["muted": String(muted)]) }
        catch { stop(message: "麦克风暂不可用，轻点投影可重试。", mode: .failed); return }
        let id = generation
        if muted { Task { try? await send(HologramWire.endInput(profile.service), id: id) } }
    }
    func tapProjection() {
        if mode == .failed || mode == .paused || mode == .idle { start(); return }
        guard ready, mode == .speaking || waiting else { return }
        cancelTools(); diagnostics.note("response.user_interrupt"); inputGate.interrupt(now: CACurrentMediaTime()); outputCapacityReached = false
        audio.flush(); motion.interrupt(); discardOutput = true; waiting = false; caption = muted ? "播报已停止，麦克风仍静音。" : "我在听。"
        recorder.close(); historyDirty = true; pending.removeAll(); information = nil; tools.clear()
        if profile.service == .qwen && responseActive { let id = generation; Task { try? await send(["type": "response.cancel"], id: id) } }
    }
    func suspend(reason: String = "background") { diagnostics.note("call.pause", ["reason": reason]); stop(message: "通话已暂停，轻点投影继续。", mode: .paused); timer?.invalidate(); timer = nil }
    func stop(message: String = "通话已结束。", mode: HologramMode = .idle) {
        diagnostics.note("call.stop", ["reason": mode.rawValue]); diagnostics.save()
        cancelTools(); inputGate.stop(); generation = UUID(); active = false; ready = false
        receiver?.cancel(); sender?.cancel(); timeout?.cancel(); input?.finish(); input = nil
        connection?.cancel(with: .goingAway, reason: nil); transport?.invalidateAndCancel(); connection = nil; transport = nil
        audio.stop(); recorder.close(); history.upsert(recorder.session); motion.interrupt(); information = nil; tools.clear(); pending.removeAll()
        self.mode = mode; caption = message
    }
    func close() { stop(); timer?.invalidate(); timer = nil }
}
