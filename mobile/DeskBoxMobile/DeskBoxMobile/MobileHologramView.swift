import SwiftUI
import AVFoundation

struct HologramCanvas: View {
    var motion: HologramMotion
    var reduced: Bool
    private static let palette: [Color] = [Color(red: 0.12, green: 0.90, blue: 0.82), Color(red: 0.30, green: 0.72, blue: 1), Color(red: 0.71, green: 0.44, blue: 0.98), Color(red: 0.94, green: 0.50, blue: 0.81), Color(red: 1, green: 0.80, blue: 0.49)]
    private struct Point3 { var x: Double; var y: Double; var z: Double; var phase: Double; var core: Bool }
    private static let cloud: [Point3] = (0..<2100).map { i in
        let y = 1 - 2 * (Double(i) + 0.5) / 2100, a = Double(i) * 2.39996323, radial = sqrt(1 - y * y)
        let core = i % 3 == 0, r = core ? 0.18 + Double(i % 103) / 103 * 0.33 : 0.76
        return Point3(x: cos(a) * radial * r, y: y * r, z: sin(a) * radial * r, phase: Double(i % 127) / 127, core: core)
    }
    var body: some View {
        Canvas(rendersAsynchronously: true) { context, size in
            var canvas = context
            render(&canvas, size: size)
        }.accessibilityHidden(true)
    }
    private func render(_ context: inout GraphicsContext, size: CGSize) {
            let flow = reduced ? 0 : motion.flow, energy = reduced ? 0 : motion.energy
            let scale = min(Double(size.width) * 0.28, Double(size.height) * 0.25), cx = Double(size.width) * 0.5, cy = Double(size.height) * 0.42
            let yaw = flow * 0.22, pitch = 0.23, sy = sin(yaw), co = cos(yaw), sx = sin(pitch), cp = cos(pitch)
            func project(_ x: Double, _ y: Double, _ z: Double) -> (CGPoint, Double) {
                let xx = x * co + z * sy, zz = z * co - x * sy, yy = y * cp - zz * sx, depth = y * sx + zz * cp
                let perspective = 4.8 / (4.8 - depth)
                return (CGPoint(x: cx + xx * scale * perspective, y: cy - yy * scale * perspective), depth)
            }
            context.blendMode = .plusLighter
            let halo = CGRect(x: cx - scale * 1.2, y: cy - scale * 1.2, width: scale * 2.4, height: scale * 2.4)
            context.fill(Path(ellipseIn: halo), with: .radialGradient(Gradient(colors: [Self.palette[1].opacity(0.09), .clear]), center: CGPoint(x: cx, y: cy), startRadius: 0, endRadius: scale * 1.2))
            var buckets = Array(repeating: Path(), count: 15)
            for point in Self.cloud {
                let band = sin(point.y * 7 - flow * 2.3 + point.phase * 4)
                let expansion = 1 + energy * 0.10 + (reduced ? 0 : motion.impulse) * 0.055
                let warp = band * (0.01 + energy * 0.055)
                let (p, z) = project(point.x * expansion + warp, point.y * expansion, point.z * expansion)
                let layer: Int = z < -0.15 ? 0 : z < 0.3 ? 1 : 2
                let tint: Int = point.core ? (point.phase > 0.55 ? 4 : 2) : (point.phase > 0.7 ? 1 : 0)
                let baseRadius: Double = 0.45 + Double(layer) * 0.18
                let magnification: Double = 1.0 + energy * 0.3
                let radius: CGFloat = CGFloat(baseRadius * magnification)
                buckets[tint * 3 + layer].addEllipse(in: CGRect(x: p.x - radius, y: p.y - radius, width: radius * 2, height: radius * 2))
            }
            context.drawLayer { glow in
                glow.addFilter(.blur(radius: 3))
                for index in buckets.indices { glow.fill(buckets[index], with: .color(Self.palette[index / 3].opacity(0.18 + energy * 0.12))) }
            }
            for index in buckets.indices { context.fill(buckets[index], with: .color(Self.palette[index / 3].opacity((0.24 + Double(index % 3) * 0.2) * (0.85 + energy * 0.35)))) }
            for ribbon in 0..<3 {
                var path = Path(); let roll = Double(ribbon) * 0.91 + flow * 0.08
                for i in 0...180 {
                    let a = Double(i) * .pi * 2 / 180, radius = (0.57 + 0.12 * cos(a * 3 + flow * 0.45 + Double(ribbon) * 2.094)) * (1 + energy * 0.1)
                    let x = radius * cos(a * 2), y = radius * sin(a * 2), z = 0.25 * sin(a * 3)
                    let p = project(x, y * cos(roll) - z * sin(roll), y * sin(roll) + z * cos(roll)).0
                    if i == 0 { path.move(to: p) } else { path.addLine(to: p) }
                }
                let tint = Self.palette[[0, 2, 4][ribbon]]
                context.drawLayer { glow in glow.addFilter(.blur(radius: 3)); glow.stroke(path, with: .color(tint.opacity(0.4)), lineWidth: 2.5) }
                context.stroke(path, with: .color(tint.opacity(0.65 + energy * 0.3)), lineWidth: 0.75 + energy * 0.3)
            }
            for orbit in 0..<6 {
                var path = Path(); let radius = (0.98 + Double(orbit) * 0.067) * (1 + energy * 0.045), tilt = 0.32 + Double(orbit) * 0.49
                for i in 0...128 {
                    let a = Double(i) * .pi * 2 / 128 + flow * (orbit % 2 == 0 ? 0.12 : -0.09)
                    let x = radius * cos(a), z = radius * sin(a), y = z * sin(tilt), turn = Double(orbit) * 0.81
                    let p = project(x * cos(turn) + z * cos(tilt) * sin(turn), y, z * cos(tilt) * cos(turn) - x * sin(turn)).0
                    if i % 32 == 0 { path.move(to: p) } else { path.addLine(to: p) }
                }
                context.stroke(path, with: .color(Self.palette[orbit % 4].opacity(orbit % 2 == 0 ? 0.32 : 0.17)), lineWidth: 0.65)
            }
            func floorRing(_ radius: Double) -> Path {
                var path = Path()
                for i in 0...128 { let a = Double(i) * .pi * 2 / 128, p = project(cos(a) * radius, -1.42, sin(a) * radius).0; if i == 0 { path.move(to: p) } else { path.addLine(to: p) } }
                return path
            }
            for ring in 0..<4 { context.stroke(floorRing(1.17 + Double(ring) * 0.12), with: .color(Self.palette[ring % 2 == 0 ? 0 : 2].opacity(0.22 + energy * 0.13)), lineWidth: ring == 1 ? 1.2 : 0.7) }
            if !reduced {
                for wave in motion.pulseAges.indices where motion.pulseAges[wave] < 1.25 {
                    let age = motion.pulseAges[wave], opacity = (1 - age / 1.25) * motion.pulseStrength[wave] * 0.6
                    context.stroke(floorRing(0.97 + age * 0.48), with: .color(Self.palette[motion.speaking > 0.5 ? 2 : 0].opacity(opacity)), lineWidth: 1)
                }
            }
    }
}

struct MobileHologramView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(\.scenePhase) private var phase
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @StateObject private var call: MobileHologramCall
    init(profile: HologramProfile, store: OfflineStore, history: HologramHistory, sync: SyncClient, preview: Bool = false) {
        _call = StateObject(wrappedValue: MobileHologramCall(profile: profile, store: store, history: history, lastSync: { [weak sync] in sync?.lastSyncedAt }, preview: preview))
    }
    var body: some View {
        GeometryReader { geometry in
            VStack(spacing: 8) {
                HStack {
                    Button { call.toggleMute() } label: { Image(systemName: call.muted ? "mic.slash.fill" : "mic.fill").frame(width: 44, height: 44) }.accessibilityLabel(call.muted ? "打开麦克风" : "静音麦克风")
                    Spacer()
                    Button { call.close(); dismiss() } label: { Image(systemName: "xmark").frame(width: 44, height: 44) }.accessibilityLabel("退出全息通话")
                }.foregroundStyle(.white.opacity(0.75)).padding(.horizontal, 12)
                HologramCanvas(motion: call.motion, reduced: reduceMotion || call.profile.reducedMotion)
                    .contentShape(Rectangle()).onTapGesture { call.tapProjection() }
                    .accessibilityElement().accessibilityLabel("全息投影，轻点打断或恢复通话").accessibilityAddTraits(.isButton)
                    .accessibilityAction { call.tapProjection() }
                if let info = call.information {
                    ScrollView {
                        VStack(alignment: .leading, spacing: 8) { Text(info.title).font(.headline).foregroundStyle(.cyan); Text(info.content).font(.callout).textSelection(.enabled).foregroundStyle(.white.opacity(0.92)) }.frame(maxWidth: .infinity, alignment: .leading).padding(16)
                    }.frame(maxHeight: min(170, geometry.size.height * 0.28))
                        .background(.cyan.opacity(0.04)).overlay(Rectangle().stroke(.cyan.opacity(0.25), lineWidth: 0.5)).padding(.horizontal, 22)
                }
                VStack(spacing: 8) {
                    Text(call.caption).font(.body).foregroundStyle(.white.opacity(0.93)).multilineTextAlignment(.center).lineLimit(4).frame(maxWidth: .infinity, minHeight: 76, alignment: .top)
                    if !call.notice.isEmpty { Text(call.notice).font(.caption).foregroundStyle(.orange).multilineTextAlignment(.center) }
                    Button { call.tapProjection() } label: {
                        Label(call.mode == .failed || call.mode == .paused || call.mode == .idle ? "重新连接" : "打断后提问", systemImage: call.mode == .failed || call.mode == .paused || call.mode == .idle ? "arrow.clockwise" : "waveform")
                            .font(.caption.weight(.semibold)).frame(minWidth: 150, minHeight: 44)
                    }.buttonStyle(.plain).foregroundStyle(.cyan).opacity(call.mode == .speaking || call.mode == .thinking || call.mode == .failed || call.mode == .paused ? 1 : 0).disabled(call.mode != .speaking && call.mode != .thinking && call.mode != .failed && call.mode != .paused)
                }.padding(.horizontal, 22).padding(.bottom, 8)
            }.frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .background(Color(red: 0.008, green: 0.02, blue: 0.043).ignoresSafeArea()).preferredColorScheme(.dark)
        .task { call.start() }
        .onChange(of: phase) { _, value in if value == .background { call.suspend() } else if value == .active { call.animate() } }
        .onReceive(NotificationCenter.default.publisher(for: AVAudioSession.interruptionNotification)) { notice in
            if (notice.userInfo?[AVAudioSessionInterruptionTypeKey] as? UInt) == AVAudioSession.InterruptionType.began.rawValue { call.suspend(reason: "audio_interruption") }
        }
        .onReceive(NotificationCenter.default.publisher(for: AVAudioSession.routeChangeNotification)) { notice in
            if (notice.userInfo?[AVAudioSessionRouteChangeReasonKey] as? UInt) == AVAudioSession.RouteChangeReason.oldDeviceUnavailable.rawValue { call.suspend(reason: "route_disconnected") }
        }
        .onDisappear { call.close() }
    }
}
