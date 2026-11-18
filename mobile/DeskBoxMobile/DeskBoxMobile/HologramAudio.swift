import Foundation
import AVFoundation

// The call owns this on the main actor. The input tap only converts PCM and yields immutable data.
final class HologramAudio {
    private let engine = AVAudioEngine()
    private lazy var playback = HologramPlayback(engine: engine)
    private var tap = false, ownsSession = false
    private var capture: ((Data, Double) -> Void)?
    static func permission() async -> Bool {
        await withCheckedContinuation { continuation in AVAudioSession.sharedInstance().requestRecordPermission { continuation.resume(returning: $0) } }
    }
    func start(capture: @escaping (Data, Double) -> Void) throws {
        self.capture = capture
        let session = AVAudioSession.sharedInstance()
        try session.setCategory(.playAndRecord, mode: .voiceChat, options: [.defaultToSpeaker, .allowBluetooth])
        try session.setActive(true); ownsSession = true
        _ = playback
        try engine.inputNode.setVoiceProcessingEnabled(true)
        try installTap(); engine.prepare(); try engine.start()
    }
    private func installTap() throws {
        guard !tap, let capture else { return }
        let input = engine.inputNode.outputFormat(forBus: 0)
        guard input.sampleRate > 0, input.channelCount > 0,
              let target = AVAudioFormat(commonFormat: .pcmFormatInt16, sampleRate: 16000, channels: 1, interleaved: false),
              let converter = AVAudioConverter(from: input, to: target) else { throw HologramError.message("没有可用的麦克风输入。") }
        engine.inputNode.installTap(onBus: 0, bufferSize: 1024, format: input) { buffer, _ in
            let capacity = AVAudioFrameCount(ceil(Double(buffer.frameLength) * 16000 / input.sampleRate) + 16)
            guard let converted = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: capacity) else { return }
            var supplied = false, error: NSError?
            _ = converter.convert(to: converted, error: &error) { _, status in
                if supplied { status.pointee = .noDataNow; return nil }
                supplied = true; status.pointee = .haveData; return buffer
            }
            guard error == nil, converted.frameLength > 0, let samples = converted.int16ChannelData?[0] else { return }
            let count = Int(converted.frameLength); var sum = 0.0
            for index in 0..<count { let value = Double(samples[index]) / 32768; sum += value * value }
            capture(Data(bytes: samples, count: count * 2), sqrt(sum / Double(count)))
        }
        tap = true
    }
    func setMuted(_ muted: Bool) throws {
        if muted { if tap { engine.inputNode.removeTap(onBus: 0); tap = false } }
        else { try installTap() }
    }
    @discardableResult func play(_ pcm: Data) throws -> Bool { try playback.enqueue(pcm) }
    func playbackLevel() -> Double? { playback.level() }
    var queuedSeconds: Double { playback.queuedSeconds }
    func flush() { playback.flush() }
    func stop() {
        engine.stop(); if tap { engine.inputNode.removeTap(onBus: 0); tap = false }
        playback.flush(); capture = nil
        if ownsSession { ownsSession = false; try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation) }
    }
}
