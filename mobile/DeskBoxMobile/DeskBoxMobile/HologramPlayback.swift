import Foundation
import AVFoundation

// The player clock runs through gaps. Each buffer is anchored to the current clock, not to call start.
final class HologramPlayback {
    private let engine: AVAudioEngine
    private let player = AVAudioPlayerNode()
    private let format = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 24000, channels: 1, interleaved: false)!
    private struct Segment { let start: Int64; let frames: Int; let levels: [Double] }
    private var segments: [Segment] = []
    private var end: Int64 = 0
    static let maximumSeconds = 180.0 // About 17 MB of float PCM; exhaustion never closes the voice connection.
    init(engine: AVAudioEngine) {
        self.engine = engine; engine.attach(player); engine.connect(player, to: engine.mainMixerNode, format: format)
    }
    private var position: Int64 {
        guard let time = player.lastRenderTime, let current = player.playerTime(forNodeTime: time) else { return 0 }
        return max(0, current.sampleTime)
    }
    var queuedSeconds: Double { Double(max(0, end - position)) / 24000 }
    @discardableResult func enqueue(_ pcm: Data) throws -> Bool {
        guard !pcm.isEmpty else { return true }
        guard pcm.count % 2 == 0 else { throw HologramError.message("音频块不完整。") }
        let now = position; segments.removeAll { $0.start + Int64($0.frames) <= now }
        let start = max(end, now), count = pcm.count / 2
        guard Double(start - now + Int64(count)) / 24000 <= Self.maximumSeconds else { return false }
        guard let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(count)), let channel = buffer.floatChannelData?[0] else { throw HologramError.message("无法准备音频播放。") }
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
        segments.append(Segment(start: start, frames: count, levels: levels)); end = start + Int64(count)
        player.scheduleBuffer(buffer)
        if !player.isPlaying { player.play() }
        return true
    }
    func level() -> Double? {
        let now = position; segments.removeAll { $0.start + Int64($0.frames) <= now }
        guard let block = segments.first else { return nil }
        let offset = max(0, Int(now - block.start)) / 480
        return block.levels[min(offset, block.levels.count - 1)]
    }
    func flush() { player.stop(); segments.removeAll(); end = 0 }
}
