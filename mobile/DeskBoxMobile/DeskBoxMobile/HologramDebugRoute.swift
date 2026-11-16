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
            switch route.replacingOccurrences(of: "-keyboard", with: "").replacingOccurrences(of: "-large", with: "") {
            case "workspace": RootView()
            case "project-detail": NavigationStack { if let project = store.projects.first { ProjectDetailView(project: project) } }
            case "diagnostics": NavigationStack { HologramDiagnosticsView() }
            case "home": HomeView(onNavigate: { _ in })
            case "tasks": TasksView()
            case "task-editor": TaskEditor(task: MobileTask(title: "复核冻干机自动化控制系统联锁与验证文档", notes: "核对设备清单、工艺配方与测试证据，保留所有待上传修改。"), isNew: true)
            case "projects": ProjectsView()
            case "incidents": IncidentView()
            case "incident-editor": IncidentEditor(incident: Incident(equipment: "PT-101 冻干机压力变送器", description: "现场压力读数偏差，复核联锁及趋势记录。"), isNew: true)
            case "ai": MobileAIView(preview: true)
            case "ai-empty": MobileAIView()
            case "connection": ConnectionSettingsView()
            case "briefing": DailyBriefingSettingsView()
            case "conflicts": SyncConflictView()
            case "settings": NavigationStack { HologramSettingsView() }
            case "history": NavigationStack { HologramHistoryView(history: history) }
            default: MobileHologramView(profile: HologramProfile(), store: store, history: history, sync: sync, preview: true)
            }
        }.environment(\.dynamicTypeSize, route.hasSuffix("-large") ? .accessibility2 : .large)
        .task {
            store.pending = []
            store.projects = [MobileProject(id: "qa-project", name: "冻干机自动化控制系统与计算机化验证项目", number: "DB-2026-016", customer: "制药设备项目客户", equipment: "冻干机 / CIP清洗设备", owner: "项目工程师", deadline: "2026-12-30", progress: 68)]
            store.tasks = [MobileTask(id: "qa-task-1", projectId: "qa-project", title: "审核URS与联锁因果关系矩阵，确认报警配置与验证边界", priority: "high", due: "2026-11-18", owner: "项目工程师", notes: "包含长标题与工程术语的同步缓存样例。"), MobileTask(id: "qa-task-2", projectId: "qa-project", title: "核对设备清单与测试证据", due: "2026-11-20"), MobileTask(id: "qa-task-3", title: "已完成的校准记录", status: "done")]
            store.incidents = [Incident(id: "qa-incident", projectId: "qa-project", equipment: "PT-101", severity: "S2", description: "压力读数与现场校验记录存在偏差，需要复核联锁和历史趋势。", owner: "现场工程师")]

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
            try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            if let data = try? JSONSerialization.data(withJSONObject: marker) { try? data.write(to: url, options: .atomic) }
        }
    }
}
#endif
