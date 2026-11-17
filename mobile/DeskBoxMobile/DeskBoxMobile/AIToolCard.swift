import SwiftUI

struct AIToolCard: View {
    let tool: AIToolPresentation
    @State private var expanded = false
    @State private var rawExpanded = false
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Button {
                expanded.toggle()
                if !expanded { rawExpanded = false }
            } label: {
                HStack(spacing: 12) {
                    VStack(alignment: .leading, spacing: 5) {
                        Label(tool.title, systemImage: tool.succeeded ? "checkmark.circle" : "exclamationmark.circle").font(.caption.bold()).foregroundStyle(tool.succeeded ? Color.cyan : Color.orange)
                        Text(tool.summary).font(.callout).foregroundStyle(tool.succeeded ? Color.secondary : Color.orange).lineLimit(expanded ? nil : 2)
                    }.frame(maxWidth: .infinity, alignment: .leading)
                    Image(systemName: expanded ? "chevron.down" : "chevron.right").font(.caption.bold()).foregroundStyle(.cyan)
                }.frame(minHeight: 44).contentShape(Rectangle())
            }.buttonStyle(.plain)
                .accessibilityIdentifier("ai.tool.toggle." + tool.name)
                .accessibilityLabel(tool.title)
                .accessibilityValue((expanded ? "已展开" : "已收起") + "，" + tool.summary)
                .accessibilityHint(expanded ? "收起工具详情" : "展开工具详情")
            if expanded {
                Text(tool.details).font(.callout).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
                Button { rawExpanded.toggle() } label: {
                    HStack {
                        Text("原始结果"); Spacer()
                        Image(systemName: rawExpanded ? "chevron.down" : "chevron.right")
                    }.font(.caption).foregroundStyle(.secondary).frame(minHeight: 44).contentShape(Rectangle())
                }.buttonStyle(.plain).accessibilityLabel("原始结果")
                    .accessibilityIdentifier("ai.tool.raw-toggle." + tool.name)
                    .accessibilityValue(rawExpanded ? "已展开" : "已收起")
                if rawExpanded {
                    Text(tool.raw).accessibilityIdentifier("ai.tool.raw").font(.caption.monospaced()).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
                }
            }
        }
    }
}
