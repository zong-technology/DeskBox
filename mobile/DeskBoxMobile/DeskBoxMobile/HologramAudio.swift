import Foundation
import AVFoundation

// The call owns this on the main actor. The input tap only converts PCM and yields immutable data.
final class HologramAudio {
    private let engine = AVAudioEngine()
    private let player = AVAudioPlayerNode()
    private let output = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 24000, channels: 1, interleaved: false)!
    private var tap = false, attached = false
    private var written: Int64 = 0
    private struct Segment { let start: Int64; let frames: Int; let levels: [Double] }
    private var segments: [Segment] = []
    private var capture: ((Data, Double) -> Void)?
    static func permission() async -> Bool {
        await withCheckedContinuation { continuation in AVAudioSession.sharedInstance().requestRecordPermission { continuation.resume(returning: $0) } }
    }
    func start(capture: @escaping (Data, Double) -> Void) throws {
        self.capture = capture
        let session = AVAudioSession.sharedInstance()
        try session.setCategory(.playAndRecord, mode: .voiceChat, options: [.defaultToSpeaker, .allowBluetooth])
        try session.setActive(true)
        if !attached { engine.attach(player); engine.connect(player, to: engine.mainMixerNode, format: output); attached = true }
        try engine.inputNode.setVoiceProcessingEnabled(true)
        try installTap(); engine.prepare(); try engine.start(); player.play()
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
    func play(_ pcm: Data) throws {
        guard !pcm.isEmpty else { return }
        guard pcm.count % 2 == 0 else { throw HologramError.message("音频块不完整。") }
        _ = playbackLevel()
        let position = playbackPosition()
        guard written - position + Int64(pcm.count / 2) <= 24000 * 12 else { throw HologramError.message("声音缓冲积压，轻点投影重新连接。") }
        let count = pcm.count / 2
        guard let buffer = AVAudioPCMBuffer(pcmFormat: output, frameCapacity: AVAudioFrameCount(count)), let channel = buffer.floatChannelData?[0] else { return }
        buffer.frameLength = AVAudioFrameCount(count)
        var levels: [Double] = [], sum = 0.0, samples = 0
        pcm.withUnsafeBytes { raw in
            let bytes = raw.bindMemory(to: UInt8.self)
            for index in 0..<count {
                let word = UInt16(bytes[index * 2]) | UInt16(bytes[index * 2 + 1]) << 8
                let value = Double(Int16(bitPattern: word)) / 32768
                channel[index] = Float(value); sum += value * value; samples += 1
                if samples == 480 || index == count - 1 { levels.append(sqrt(sum / Double(samples))); sum = 0; samples = 0 }
            }
        }
        segments.append(Segment(start: written, frames: count, levels: levels)); written += Int64(count)
        player.scheduleBuffer(buffer); if !player.isPlaying { player.play() }
    }
    private func playbackPosition() -> Int64 {
        guard let time = player.lastRenderTime, let current = player.playerTime(forNodeTime: time) else { return 0 }
        return max(0, current.sampleTime)
    }
    func playbackLevel() -> Double? {
        let position = playbackPosition()
        segments.removeAll { $0.start + Int64($0.frames) <= position }
        guard let block = segments.first else { return nil }
        let offset = max(0, Int(position - block.start)) / 480
        return block.levels[min(offset, block.levels.count - 1)]
    }
    func flush() { player.stop(); segments.removeAll(); written = 0; if engine.isRunning { player.play() } }
    func stop() {
        engine.stop(); if tap { engine.inputNode.removeTap(onBus: 0); tap = false }
        player.stop(); segments.removeAll(); written = 0; capture = nil
        try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
    }
}
