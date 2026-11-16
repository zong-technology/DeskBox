#if DEBUG
import SwiftUI
import UIKit

// Used only by fresh CI simulator sandboxes. Never opens a microphone or a provider connection.
struct HologramDebugRoute: View {
    @EnvironmentObject var store: OfflineStore
    @EnvironmentObject var sync: SyncClient
    @StateObject private var history = HologramHistory(url: FileManager.default.temporaryDirectory.appendingPathComponent("hologram-preview-" + UUID().uuidString + ".json"))
    var route: String
    var body: some View {
        Group {
            if route == "settings" { NavigationStack { HologramSettingsView() } }
            else if route == "history" { NavigationStack { HologramHistoryView(history: history) } }
            else { MobileHologramView(profile: HologramProfile(), store: store, history: history, sync: sync, preview: true) }
        }.task {
            if route == "history" {
                var session = HologramSession(name: "星澜", persona: "工程助手", model: "模拟预览 · 无网络调用")
                session.messages = [HologramMessage(role: "user", text: "今天有哪些任务？"), HologramMessage(role: "assistant", text: "你有三项待办：审核项目文档、核对设备清单、确认测试进度。")]
                history.upsert(session)
            }
            try? await Task.sleep(nanoseconds: 1_000_000_000)
            guard let window = UIApplication.shared.connectedScenes.compactMap({ $0 as? UIWindowScene }).flatMap(\.windows).first(where: \.isKeyWindow), !window.isHidden else { return }
            let marker: [String: Any] = ["route": route, "active": UIApplication.shared.applicationState == .active,
                "width": window.bounds.width, "height": window.bounds.height, "scale": window.screen.scale,
                "simulatedVoice": true]
            let url = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appendingPathComponent("hologram-qa-" + route + ".json")
            if let data = try? JSONSerialization.data(withJSONObject: marker) { try? data.write(to: url, options: .atomic) }
        }
    }
}
#endif
