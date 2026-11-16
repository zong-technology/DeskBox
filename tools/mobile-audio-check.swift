import Foundation
import AVFoundation

@main struct MobileAudioCheck {
    static func main() async throws {
        var checks = 0
        func check(_ ok: Bool, _ message: String) { precondition(ok, message); checks += 1; print("PASS " + message) }
        let engine = AVAudioEngine(), format = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 24000, channels: 1, interleaved: false)!
        let player = HologramPlayback(engine: engine)
        try engine.enableManualRenderingMode(.offline, format: format, maximumFrameCount: 480); try engine.start(); defer { engine.stop() }
        let rendered = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: 480)!
        func render(_ frames: Int) throws -> Int {
            var remaining = frames, audible = 0
            while remaining > 0 {
                let count = min(remaining, 480)
                let status = try engine.renderOffline(AVAudioFrameCount(count), to: rendered)
                guard status == .success else { throw NSError(domain: "OfflineRender", code: Int(status.rawValue)) }
                for i in 0..<Int(rendered.frameLength) where abs(rendered.floatChannelData![0][i]) > 0.01 { audible += 1 }
                remaining -= count
            }
            return audible
        }
        let samples = [Int16](repeating: 8192, count: 24000), pcm = samples.withUnsafeBytes { Data($0) }
        check(try render(5 * 24000) == 0, "Waiting for a tool result renders silence without moving a buffer origin")
        for _ in 0..<30 { check(try player.enqueue(pcm), "Queue long task reply PCM") }
        check(player.queuedSeconds > 29.9, "A 30-second reply is retained instead of closing at the old 12-second threshold")
        let first = try render(30 * 24000)
        check(first >= 30 * 24000 - 480, "Actual offline AVAudioEngine renders the complete long reply")
        check(player.queuedSeconds < 0.03, "Completed playback drains its queue")
        _ = try render(5 * 24000)
        check(try player.enqueue(pcm), "A new packet is accepted after an idle gap")
        check(player.level() != nil && player.queuedSeconds > 0.95, "The meter follows the new buffer after the player clock has advanced through silence")
        check(try render(24000) > 23500, "Speech after a gap is not lost or scheduled in the past")
        _ = try player.enqueue(pcm); player.flush()
        check(player.queuedSeconds == 0 && player.level() == nil, "Manual interruption clears queued audio and its meter together")
        check(try render(24000) < 480, "Flushed speech does not resume on the next render")
        for _ in 0..<180 { precondition(try player.enqueue(pcm)) }
        check(try !player.enqueue(pcm) && player.queuedSeconds <= 180, "The memory limit refuses only excess PCM without throwing or resetting queued speech")
        check(try render(24000) > 23500, "Previously queued speech still plays after capacity is reached")
        check(try player.enqueue(pcm), "New audio can enter as playback frees capacity")
        player.flush()
        let gate = HologramInputGate(), id = UUID(), input = Data([1, 2, 3, 4])
        gate.activate(id, allowInterruption: false); gate.updatePlayback(true, now: 0)
        check(gate.frame(input, rms: 0.2, now: 1, token: id)!.data.allSatisfy { $0 == 0 }, "Default stable playback never sends the assistant speaker echo as user speech")
        gate.updatePlayback(false, now: 1)
        check(gate.frame(input, rms: 0.2, now: 1.1, token: id)!.data.allSatisfy { $0 == 0 }, "Echo suppression includes the acoustic tail after playback")
        check(gate.frame(input, rms: 0.2, now: 1.3, token: id)!.data == input, "Listening resumes after the echo tail")
        gate.activate(id, allowInterruption: true); gate.updatePlayback(true, now: 0)
        check(gate.frame(input, rms: 0.2, now: 1, token: id)!.data == input, "Optional hands-free interruption preserves duplex input")
        let captured = gate.frame(input, rms: 0.2, now: 1, token: id)!
        gate.setMuted(true)
        check(gate.outgoing(captured, token: id, now: 1.1) == nil, "Muting also removes frames that were captured before mute")
        gate.stop(); check(gate.frame(input, rms: 0.2, now: 1, token: id) == nil, "Late audio callbacks cannot restart a stopped call")
        let pair = HologramInputGate.stream(); var dropped = 0
        for n in 0..<32 { if case .dropped = pair.continuation.yield(HologramCaptureFrame(data: Data([UInt8(n)]), capturedAt: Double(n) * 0.02)) { dropped += 1 } }
        pair.continuation.finish(); var values: [UInt8] = []
        for await frame in pair.stream { values.append(frame.data.first!) }
        check(dropped == 0 && values == Array(0..<32).map(UInt8.init), "A normal 640ms upload stall preserves every input frame in order")
        print("PASS \(checks) native audio checks; manual offline rendering, no microphone or paid service")
    }
}
