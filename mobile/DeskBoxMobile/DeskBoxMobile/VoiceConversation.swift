import Foundation
import Speech
import AVFoundation
import Combine
import UIKit

@MainActor final class VoiceConversation: NSObject, ObservableObject, AVSpeechSynthesizerDelegate {
    @Published private(set) var listening = false
    @Published private(set) var transcript = ""
    @Published private(set) var status = "语音就绪"
    @Published private(set) var speaking = false
    var onSpeechPulse: (() -> Void)?
    var continuous = false
    var onFinal: ((String) -> Void)?
    private let engine = AVAudioEngine()
    private let speaker = AVSpeechSynthesizer()
    private let recognizer = SFSpeechRecognizer(locale: Locale(identifier: "zh-CN"))
    private var recognition: SFSpeechRecognitionTask?
    private var request: SFSpeechAudioBufferRecognitionRequest?
    private var silence: Task<Void, Never>?
    private var session = UUID()
    private var tapInstalled = false
    private var starting: UUID?
    private var ownsSession = false
    private var spokenID: ObjectIdentifier?
    override init() { super.init(); speaker.delegate = self }
    func start() async {
        guard !listening, starting == nil else { return }
        let attempt = UUID(); starting = attempt
        defer { if starting == attempt { starting = nil } }
        speaker.stopSpeaking(at: .immediate); spokenID = nil; releaseSession()
        let speech = await withCheckedContinuation { continuation in SFSpeechRecognizer.requestAuthorization { continuation.resume(returning: $0 == .authorized) } }
        guard starting == attempt else { return }
        let microphone = await withCheckedContinuation { continuation in AVAudioSession.sharedInstance().requestRecordPermission { continuation.resume(returning: $0) } }
        for _ in 0..<10 where UIApplication.shared.applicationState != .active { try? await Task.sleep(nanoseconds: 100_000_000); if starting != attempt { return } }
        guard starting == attempt, UIApplication.shared.applicationState == .active else { return }
        guard speech && microphone else { status = "请在系统设置允许麦克风与语音识别"; return }
        guard let recognizer, recognizer.isAvailable else { status = "语音识别暂不可用，请检查网络或使用文字"; return }
        guard AIEnvironment.current != .local || recognizer.supportsOnDeviceRecognition else { status = "本地隔离模式仅允许设备内听写，当前设备不支持，请使用文字。"; return }
        do {
            stopRecording(); try AVAudioSession.sharedInstance().setCategory(.playAndRecord, mode: .measurement, options: [.defaultToSpeaker, .allowBluetooth])
            try AVAudioSession.sharedInstance().setActive(true); ownsSession = true
            let request = SFSpeechAudioBufferRecognitionRequest(); request.shouldReportPartialResults = true
            if recognizer.supportsOnDeviceRecognition { request.requiresOnDeviceRecognition = true }
            self.request = request; transcript = ""; let current = UUID(); session = current
            let node = engine.inputNode; let format = node.outputFormat(forBus: 0)
            guard format.sampleRate > 0 && format.channelCount > 0 else { status = "未找到可用麦克风"; return }
            node.installTap(onBus: 0, bufferSize: 1024, format: format) { buffer, _ in request.append(buffer) }; tapInstalled = true
            recognition = recognizer.recognitionTask(with: request) { [weak self] result, error in
                Task { @MainActor in
                    guard let self, self.session == current else { return }
                    if let result {
                        self.transcript = result.bestTranscription.formattedString
                        self.silence?.cancel()
                        if result.isFinal { self.finish() }
                        else { self.silence = Task { try? await Task.sleep(nanoseconds: 1_300_000_000); if !Task.isCancelled && self.session == current { self.finish() } } }
                    } else if let error { self.status = "识别中断：" + error.localizedDescription; self.stopRecording() }
                }
            }
            engine.prepare(); try engine.start(); listening = true; status = "正在聆听 · 停顿后完成识别"
        } catch { stopRecording(); status = "麦克风启动失败：" + error.localizedDescription }
    }
    private func finish() { let value = transcript.trimmingCharacters(in: .whitespacesAndNewlines); stopRecording(); status = value.isEmpty ? "没有识别到内容" : "识别完成"; if !value.isEmpty { onFinal?(value) } }
    func stopRecording() { starting = nil; session = UUID(); silence?.cancel(); engine.stop(); if tapInstalled { engine.inputNode.removeTap(onBus: 0); tapInstalled = false }; request?.endAudio(); recognition?.cancel(); recognition = nil; request = nil; listening = false; releaseSession() }
    func speak(_ text: String) {
        speaker.stopSpeaking(at: .immediate); speaking = false; spokenID = nil; stopRecording()
        let clean = text.replacingOccurrences(of: "(?s)```.*?```", with: "代码块请查看屏幕。", options: .regularExpression).replacingOccurrences(of: "[#*`]", with: "", options: .regularExpression)
        do { try AVAudioSession.sharedInstance().setCategory(.playback, mode: .spokenAudio, options: .duckOthers); try AVAudioSession.sharedInstance().setActive(true); ownsSession = true; let utterance = AVSpeechUtterance(string: String(clean.prefix(6000))); utterance.voice = AVSpeechSynthesisVoice(language: "zh-CN"); spokenID = ObjectIdentifier(utterance); speaking = true; speaker.speak(utterance); status = "正在朗读回复" } catch { status = "朗读失败：" + error.localizedDescription }
    }
    func stop() { continuous = false; speaker.stopSpeaking(at: .immediate); speaking = false; spokenID = nil; stopRecording() }
    private func releaseSession() { if ownsSession { ownsSession = false; try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation) } }
    nonisolated func speechSynthesizer(_ synthesizer: AVSpeechSynthesizer, willSpeakRangeOfSpeechString characterRange: NSRange, utterance: AVSpeechUtterance) { Task { @MainActor in guard self.spokenID == ObjectIdentifier(utterance) else { return }; self.onSpeechPulse?() } }
    nonisolated func speechSynthesizer(_ synthesizer: AVSpeechSynthesizer, didFinish utterance: AVSpeechUtterance) { Task { @MainActor in guard self.spokenID == ObjectIdentifier(utterance) else { return }; self.speaking = false; self.spokenID = nil; self.releaseSession(); if self.continuous && UIApplication.shared.applicationState == .active { await self.start() } } }
}
