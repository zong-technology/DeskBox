import SwiftUI

enum MobileStyle {
    static let background = Color(red: 0.025, green: 0.04, blue: 0.07)
    static let panel = Color(red: 0.055, green: 0.08, blue: 0.125)
    static let elevated = Color(red: 0.075, green: 0.115, blue: 0.17)
    static let accent = Color(red: 0.32, green: 0.85, blue: 0.92)
    static let line = Color.white.opacity(0.085)
}
struct MobilePrimaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label.font(.callout.weight(.semibold)).padding(.horizontal, 14).frame(minHeight: 44)
            .foregroundStyle(Color(red: 0.025, green: 0.07, blue: 0.10)).background(MobileStyle.accent.opacity(configuration.isPressed ? 0.7 : 1), in: RoundedRectangle(cornerRadius: 13))
    }
}
extension View {
    func workspacePage() -> some View { self.scrollContentBackground(.hidden).background(MobileStyle.background).tint(MobileStyle.accent).navigationBarTitleDisplayMode(.inline) }
    func workspaceCard() -> some View { self.padding(16).background(MobileStyle.panel, in: RoundedRectangle(cornerRadius: 17)).overlay(RoundedRectangle(cornerRadius: 17).stroke(MobileStyle.line, lineWidth: 1)) }
}
