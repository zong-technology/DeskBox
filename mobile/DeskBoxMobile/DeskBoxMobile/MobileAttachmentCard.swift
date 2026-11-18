import SwiftUI

struct MobileAttachmentCard: View {
    let attachment: MobileAIAttachment
    @State private var expanded = false
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Button { expanded.toggle() } label: {
                HStack(spacing: 8) {
                    Image(systemName: "doc.text")
                    Text(attachment.name).lineLimit(2).multilineTextAlignment(.leading)
                    Spacer(minLength: 4)
                    Image(systemName: expanded ? "chevron.down" : "chevron.right")
                }.font(.caption.bold()).foregroundStyle(.cyan).frame(minHeight: 44).contentShape(Rectangle())
            }.buttonStyle(.plain).accessibilityIdentifier("ai.attachment.preview." + attachment.id).accessibilityValue(expanded ? "已展开" : "已收起")
            Text(attachment.note).font(.caption).foregroundStyle(.secondary).lineLimit(3)
            if expanded {
                ScrollView { Text(attachment.text).font(.callout).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading) }.frame(maxHeight: 160).accessibilityIdentifier("ai.attachment.text." + attachment.id)
            }
        }.padding(10).background(MobileStyle.elevated, in: RoundedRectangle(cornerRadius: 12))
    }
}
