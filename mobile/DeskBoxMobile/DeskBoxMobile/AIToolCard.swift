import SwiftUI

struct AIToolCard: View {
    let tool: AIToolPresentation
    @State private var expanded = false
    var body: some View {
        DisclosureGroup(isExpanded: $expanded) {
            VStack(alignment: .leading, spacing: 12) {
                Text(tool.details).font(.callout).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
                DisclosureGroup("原始结果") {
                    Text(tool.raw).accessibilityIdentifier("ai.tool.raw").font(.caption.monospaced()).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
                }.font(.caption).foregroundStyle(.secondary)
            }.padding(.top, 10)
        } label: {
            VStack(alignment: .leading, spacing: 5) {
                Label(tool.title, systemImage: tool.succeeded ? "checkmark.circle" : "exclamationmark.circle").font(.caption.bold()).foregroundStyle(tool.succeeded ? Color.cyan : Color.orange)
                Text(tool.summary).font(.callout).foregroundStyle(tool.succeeded ? Color.secondary : Color.orange).lineLimit(expanded ? nil : 2)
            }.frame(maxWidth: .infinity, alignment: .leading).padding(.vertical, 4)
        }.tint(.cyan).accessibilityHint(expanded ? "收起工具详情" : "展开工具详情")
    }
}
