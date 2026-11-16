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
    let profile: HologramProfile
    private let store: OfflineStore, history: HologramHistory
    private let lastSync: () -> Date?
    private let audio = HologramAudio()
    private let tools: HologramLocalTools
    private let preview: Bool
    private var recorder: HologramRecorder
    private var connection: URLSessionWebSocketTask?, transport: URLSession?
    private var receiver: Task<Void, Never>?, sender: Task<Void, Never>?, timeout: Task<Void, Never>?
    private var input: AsyncStream<Data>.Continuation?
    private var generation = UUID(), active = false, ready = false, waiting = false, responseActive = false, discardOutput = false
    private var timer: Timer?, lastFrame = CACurrentMediaTime(), lastSave = Date.distantPast
    private var micRMS = 0.0, lastMic = 0.0, started = CACurrentMediaTime()
    private var pending: [HologramFunction] = []
    private var userCaption = "", assistantCaption = "", captionFinished = true
    private var historyDirty = false
    init(profile: HologramProfile, store: OfflineStore, history: HologramHistory, lastSync: @escaping () -> Date?, preview: Bool = false) {
        self.profile = profile; self.store = store; self.history = history; self.lastSync = lastSync; self.preview = preview
        recorder = HologramRecorder(profile: profile); tools = HologramLocalTools(store: store, lastSync: lastSync)
    }
    func animate() {
        guard timer == nil else { return }; lastFrame = CACurrentMediaTime()
        let timer = Timer(timeInterval: 1.0 / 30, repeats: true) { [weak self] _ in Task { @MainActor in self?.tick() } }
        self.timer = timer; RunLoop.main.add(timer, forMode: .common)
    }
    private func tick() {
        let now = CACurrentMediaTime(), dt = now - lastFrame; lastFrame = now
        var target = 0.0
        if preview { mode = .speaking; target = 0.3 + 0.55 * pow(sin(now * 5), 2); caption = "模拟预览：已为你整理今天的任务。" }
        else if ready {
            let playback = audio.playbackLevel()
            mode = playback != nil && !discardOutput ? .speaking : muted ? .muted : waiting ? .thinking : .listening
            target = HologramMotion.level(playback ?? (muted || now - lastMic > 0.12 ? 0 : micRMS))
        }
        motion.advance(dt, mode: mode, target: target, reduced: profile.reducedMotion || UIAccessibility.isReduceMotionEnabled)
        if let information, information.expires <= Date() { self.information = nil; tools.clear() }
        if historyDirty && Date().timeIntervalSince(lastSave) > 1 && !recorder.session.messages.isEmpty { history.upsert(recorder.session); lastSave = Date(); historyDirty = false }
    }
    func start() {
        animate(); guard !active, !preview else { return }
        guard profile.ready else { mode = .idle; caption = "语音尚未连接。"; return }
        active = true; ready = false; mode = .connecting; caption = "正在与你连接…"; muted = false
        userCaption = ""; assistantCaption = ""; captionFinished = true; waiting = false; discardOutput = false
        let id = UUID(); generation = id
        receiver = Task { [weak self] in
            guard let self else { return }
            guard await HologramAudio.permission() else { if generation == id { stop(message: "麦克风未获授权。", mode: .failed) }; return }
            for _ in 0..<10 where UIApplication.shared.applicationState != .active { try? await Task.sleep(nanoseconds: 100_000_000); if generation != id { return } }
            guard generation == id, active else { return }
            guard UIApplication.shared.applicationState == .active else { stop(message: "通话已暂停，轻点投影继续。", mode: .paused); return }
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
                    let frame = try await socket.receive(); guard generation == id, active else { return }
                    let bytes: Data
                    switch frame { case .data(let data): bytes = data; case .string(let value): bytes = Data(value.utf8); @unknown default: continue }
                    try await consume(HologramWire.decode(bytes, service: profile.service), id: id)
                }
            } catch { if generation == id && active { stop(message: "连接暂时中断，轻点投影可重试。", mode: .failed) } }
        }
    }
    private func send(_ payload: [String: Any], id: UUID) async throws {
        guard generation == id, active, let connection else { throw CancellationError() }
        try await connection.send(.string(HologramWire.json(payload)))
    }
    private func beginAudio(id: UUID) throws {
        let pair = AsyncStream<Data>.makeStream(bufferingPolicy: .bufferingNewest(8)); input = pair.continuation
        sender = Task { [weak self] in
            for await data in pair.stream {
                guard let self, generation == id, active else { break }; if muted { continue }
                do { try await send(HologramWire.audio(data, service: profile.service), id: id) }
                catch { if generation == id && active { stop(message: "声音发送中断，轻点投影可重试。", mode: .failed) }; break }
            }
        }
        try audio.start { [weak self] data, rms in
            Task { @MainActor in
                guard let self, self.generation == id, self.active, !self.muted else { return }
                self.micRMS = rms; self.lastMic = CACurrentMediaTime()
                if case .dropped = pair.continuation.yield(data) { self.stop(message: "网络未能及时发送声音，轻点投影可重试。", mode: .failed) }
            }
        }
    }
    private func consume(_ incoming: HologramPacket, id: UUID) async throws {
        var packet = incoming
        if packet.ready && !ready {
            guard generation == id, UIApplication.shared.applicationState == .active else { return }
            timeout?.cancel(); try beginAudio(id: id); ready = true; mode = .listening; caption = "我在听。"
        }
        if packet.responseStarted { responseActive = true; discardOutput = false; pending.removeAll() }
        if packet.interrupted {
            audio.flush(); motion.interrupt(); discardOutput = true; waiting = false; pending.removeAll()
            userCaption = ""; assistantCaption = ""; captionFinished = true; caption = "我在听。"
            packet.audio = []; packet.caption = ""; historyDirty = true
        }
        if !packet.user.isEmpty {
            if profile.service == .gemini { discardOutput = false }
            userCaption = packet.userDelta ? userCaption + packet.user : packet.user
            caption = "你：" + String(userCaption.suffix(150))
        }
        if discardOutput { packet.audio = []; packet.caption = "" }
        recorder.accept(packet)
        if !packet.user.isEmpty || !packet.caption.isEmpty || packet.turnComplete { historyDirty = true }
        if packet.inputEnded { waiting = true }
        if !packet.caption.isEmpty {
            if captionFinished { assistantCaption = ""; userCaption = "" }
            assistantCaption += packet.caption; caption = String(assistantCaption.suffix(150)); captionFinished = false; waiting = true
        }
        if packet.turnComplete { waiting = false; responseActive = false; captionFinished = true }
        if packet.toolsCancelled { information = nil; tools.clear(); pending.removeAll() }
        if !packet.interrupted && !packet.toolsCancelled {
            if profile.service == .qwen {
                pending += packet.functions
                guard pending.count <= 8 else { throw HologramError.message("工具请求过多。") }
                if packet.turnComplete && !discardOutput { let calls = pending; pending.removeAll(); try await complete(calls, id: id) }
            } else { try await complete(packet.functions, id: id) }
        }
        for bytes in packet.audio { try audio.play(bytes) }
    }
    private func complete(_ calls: [HologramFunction], id: UUID) async throws {
        guard !calls.isEmpty else { return }; var results: [[String: Any]] = []
        for call in calls {
            guard generation == id, active else { throw CancellationError() }
            let result = tools.execute(call); let text = try HologramWire.json(result)
            if call.name == "hologram_show" || call.name == "hologram_clear" { information = tools.information; if result["ok"] as? Bool == true { motion.impulse = 1 } }
            recorder.tool(call.name, result: text)
            historyDirty = true
            if profile.service == .gemini { results.append(["id": call.id, "name": call.name, "response": result]) }
            else { try await send(["type": "conversation.item.create", "item": ["type": "function_call_output", "call_id": call.id, "output": text]], id: id) }
        }
        if profile.service == .gemini { try await send(["toolResponse": ["functionResponses": results]], id: id) }
        else { try await send(["type": "response.create"], id: id) }
        waiting = true
    }
    func toggleMute() {
        guard ready else { return }; muted.toggle()
        do { try audio.setMuted(muted); micRMS = 0 }
        catch { stop(message: "麦克风暂不可用，轻点投影可重试。", mode: .failed); return }
        let id = generation
        if muted { Task { try? await send(HologramWire.endInput(profile.service), id: id) } }
    }
    func tapProjection() {
        if mode == .failed || mode == .paused || mode == .idle { start(); return }
        guard ready, mode == .speaking || waiting else { return }
        audio.flush(); motion.interrupt(); discardOutput = true; waiting = false; caption = "我在听。"
        recorder.close(); historyDirty = true
        if profile.service == .qwen && responseActive { let id = generation; Task { try? await send(["type": "response.cancel"], id: id) } }
    }
    func suspend() { stop(message: "通话已暂停，轻点投影继续。", mode: .paused); timer?.invalidate(); timer = nil }
    func stop(message: String = "通话已结束。", mode: HologramMode = .idle) {
        generation = UUID(); active = false; ready = false
        receiver?.cancel(); sender?.cancel(); timeout?.cancel(); input?.finish(); input = nil
        connection?.cancel(with: .goingAway, reason: nil); transport?.invalidateAndCancel(); connection = nil; transport = nil
        audio.stop(); recorder.close(); history.upsert(recorder.session); motion.interrupt(); information = nil; tools.clear(); pending.removeAll(); micRMS = 0
        self.mode = mode; caption = message
    }
    func close() { stop(); timer?.invalidate(); timer = nil }
}
