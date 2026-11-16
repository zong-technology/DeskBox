import Foundation

struct HologramCaptureFrame: Sendable { let data: Data; let capturedAt: Double }
// Shared only by the real-time tap and uploader. No audio data waits for UI drawing or history I/O.
final class HologramInputGate: @unchecked Sendable {
    static func stream() -> (stream: AsyncStream<HologramCaptureFrame>, continuation: AsyncStream<HologramCaptureFrame>.Continuation) { AsyncStream.makeStream(bufferingPolicy: .bufferingNewest(96)) }
    private let lock = NSLock()
    private var token: UUID?, muted = false, allowInterruption = false, playing = false
    private var tailUntil = 0.0, rms = 0.0, capturedAt = 0.0
    private var lost = 0
    func activate(_ token: UUID, allowInterruption: Bool) {
        lock.lock(); defer { lock.unlock() }
        self.token = token; self.allowInterruption = allowInterruption; muted = false; playing = false; tailUntil = 0; lost = 0; rms = 0
    }
    func isActive(_ id: UUID) -> Bool { lock.lock(); defer { lock.unlock() }; return token == id }
    func setMuted(_ value: Bool) { lock.lock(); muted = value; rms = 0; lock.unlock() }
    func updatePlayback(_ value: Bool, now: Double) {
        lock.lock(); defer { lock.unlock() }
        if playing && !value { tailUntil = now + 0.25 }
        playing = value
    }
    func interrupt(now: Double) { lock.lock(); playing = false; tailUntil = now + 0.10; rms = 0; lock.unlock() }
    func frame(_ data: Data, rms: Double, now: Double, token: UUID) -> HologramCaptureFrame? {
        lock.lock(); defer { lock.unlock() }
        guard self.token == token, !muted else { return nil }
        let suppressEcho = !allowInterruption && (playing || now < tailUntil)
        self.rms = suppressEcho ? 0 : rms; capturedAt = now
        return HologramCaptureFrame(data: suppressEcho ? Data(repeating: 0, count: data.count) : data, capturedAt: now)
    }
    func outgoing(_ frame: HologramCaptureFrame, token: UUID, now: Double) -> Data? {
        lock.lock(); defer { lock.unlock() }
        guard self.token == token, !muted else { return nil }
        return !allowInterruption && (playing || now < tailUntil) ? Data(repeating: 0, count: frame.data.count) : frame.data
    }
    func noteLoss() { lock.lock(); lost += 1; lock.unlock() }
    func snapshot(now: Double) -> (level: Double, lost: Int) { lock.lock(); defer { lock.unlock() }; return (now - capturedAt < 0.15 ? rms : 0, lost) }
    func stop() { lock.lock(); token = nil; rms = 0; playing = false; lock.unlock() }
}
