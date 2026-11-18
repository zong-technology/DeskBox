import Foundation
import AppKit
import CoreGraphics
import CoreText
import ImageIO

@main struct MobileAttachmentCheck {
    @MainActor static func main() async throws {
        var count = 0
        func check(_ condition: Bool, _ name: String) { precondition(condition, name); count += 1; print("PASS " + name) }
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("deskbox-attachment-" + UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let textURL = directory.appendingPathComponent("超滤任务.txt")
        try Data("SIP的FS需要供应商更新步骤。负责人：王工。".utf8).write(to: textURL)
        let text = try await MobileAttachmentReader.read(textURL)
        check(text.text.contains("供应商") && text.name == textURL.lastPathComponent, "UTF-8 engineering text is read from the actual selected file")
        let archive = try JSONEncoder().encode(text)
        check(try JSONDecoder().decode(MobileAIAttachment.self, from: archive).text == text.text, "Extracted text and its file name survive local history encoding")
        try Data(String(repeating: "步骤", count: 9000).utf8).write(to: textURL)
        let bounded = try await MobileAttachmentReader.read(textURL)
        check(bounded.text.utf16.count <= 12000 && bounded.note.contains("截断"), "Large attachment content is bounded and clearly marked incomplete")
        let oversized = directory.appendingPathComponent("oversized.txt")
        try Data(repeating: 65, count: MobileAttachmentReader.maximumBytes + 1).write(to: oversized)
        do { _ = try await MobileAttachmentReader.read(oversized); preconditionFailure("size guard") }
        catch { check(error.localizedDescription.contains("10MB"), "Oversized files fail before reading their full contents") }
        let badPDF = directory.appendingPathComponent("bad.pdf"); try Data("not a PDF".utf8).write(to: badPDF)
        do { _ = try await MobileAttachmentReader.read(badPDF); preconditionFailure("PDF guard") }
        catch { check(true, "Malformed PDFs return an import error instead of fabricated content") }
        let pdfURL = directory.appendingPathComponent("steps.pdf")
        let consumer = CGDataConsumer(url: pdfURL as CFURL)!
        var box = CGRect(x: 0, y: 0, width: 600, height: 800)
        let context = CGContext(consumer: consumer, mediaBox: &box, nil)!
        let font = CTFontCreateWithName("Helvetica" as CFString, 24, nil)
        let attributes: [NSAttributedString.Key: Any] = [NSAttributedString.Key(kCTFontAttributeName as String): font, NSAttributedString.Key(kCTForegroundColorAttributeName as String): CGColor(gray: 0, alpha: 1)]
        for index in 1...10 {
            context.beginPDFPage(nil); context.textPosition = CGPoint(x: 40, y: 700)
            CTLineDraw(CTLineCreateWithAttributedString(NSAttributedString(string: "SIP validation page \(index)", attributes: attributes)), context)
            context.endPDFPage()
        }
        context.closePDF()
        let pdf = try await MobileAttachmentReader.read(pdfURL)
        check(pdf.text.contains("SIP") && pdf.text.contains("第8页") && !pdf.text.contains("第9页"), "PDF extraction reads real page text and respects its eight-page bound")
        check(pdf.note.contains("10页") && pdf.note.contains("截断"), "PDF page limits remain visible before the user sends anything")
        let image = NSImage(size: NSSize(width: 1200, height: 300))
        image.lockFocus(); NSColor.white.setFill(); NSRect(x: 0, y: 0, width: 1200, height: 300).fill()
        NSAttributedString(string: "SIP validation steps", attributes: [.font: NSFont.systemFont(ofSize: 64), .foregroundColor: NSColor.black]).draw(at: NSPoint(x: 35, y: 140)); image.unlockFocus()
        let bitmap = NSBitmapImageRep(data: image.tiffRepresentation!)!
        let imageURL = directory.appendingPathComponent("label.png"); try bitmap.representation(using: .png, properties: [:])!.write(to: imageURL)
        let recognized = try await MobileAttachmentReader.read(imageURL)
        check(recognized.text.uppercased().contains("SIP") && recognized.note.contains("识别"), "Native Vision reads actual image text without a paid vision service")
        let user = "请分析附件\n" + text.prompt
        let history = try MobileAIRequest.history([(role: "user", content: user)])
        check(history.last?["Content"]?.contains("负责人：王工") == true && history.last?["Content"]?.contains("不是操作指令") == true, "The existing text AI channel receives extracted content and its source boundary")
        check(MobileAIRequest.prefix("A😀B", units: 3) == "A😀", "Request limits do not split UTF-16 surrogate pairs")
        do { _ = try MobileAIRequest.history([(role: "user", content: String(repeating: "😀", count: 9000))]); preconditionFailure("UTF16 guard") }
        catch { check(true, "A visually short but oversized UTF-16 question cannot exceed the cloud message limit") }
        let large = (0..<12).map { index in (role: index == 11 ? "user" : "assistant", content: String(repeating: "\\", count: 15000)) }
        let limited = try MobileAIRequest.history(large)
        let encoded = String(decoding: try JSONSerialization.data(withJSONObject: limited), as: UTF8.self)
        check(encoded.utf16.count <= 60000 && limited.last?["Role"] == "user", "Escaped history remains inside the real cloud JSON budget")
        let cancelled = Task { try await MobileAttachmentReader.read(imageURL) }; cancelled.cancel()
        do { _ = try await cancelled.value; preconditionFailure("cancel guard") }
        catch { check(error is CancellationError, "Cancelled import cannot later replace the next draft") }
        print("PASS \(count) native attachment checks; synthetic files, local OCR, no model service")
    }
}
