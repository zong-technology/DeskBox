import Foundation
import PDFKit
import Vision
import ImageIO
import CoreGraphics

/// Extracts only on the device; the user explicitly sends the reviewed text through the existing AI channel.
enum MobileAttachmentReader {
    static let maximumBytes = 10 * 1024 * 1024
    static let maximumUnits = 12000
    static func read(_ url: URL) async throws -> MobileAIAttachment {
        try Task.checkCancellation()
        let worker = Task.detached(priority: .userInitiated) { try extract(url) }
        return try await withTaskCancellationHandler(operation: { try await worker.value }, onCancel: { worker.cancel() })
    }
    static func extract(_ url: URL) throws -> MobileAIAttachment {
        try Task.checkCancellation()
        guard url.isFileURL else { throw HologramError.message("请选择本机或文件App中的附件。") }
        let access = url.startAccessingSecurityScopedResource()
        defer { if access { url.stopAccessingSecurityScopedResource() } }
        if let size = try url.resourceValues(forKeys: [.fileSizeKey]).fileSize, size > maximumBytes { throw HologramError.message("附件超过10MB，请选择较小文件。") }
        let data = try Data(contentsOf: url, options: .mappedIfSafe)
        guard data.count <= maximumBytes else { throw HologramError.message("附件超过10MB，请选择较小文件。") }
        var fragments: [String] = [], note = "文本内容", incomplete = false
        switch url.pathExtension.lowercased() {
        case "pdf":
            guard let document = PDFDocument(data: data), document.pageCount > 0 else { throw HologramError.message("无法读取此PDF，请确认文件未损坏或加密。") }
            note = "PDF共\(document.pageCount)页，最多读取前8页"
            incomplete = document.pageCount > 8
            var usedOCR = false
            for index in 0..<min(8, document.pageCount) {
                try Task.checkCancellation()
                let page = document.page(at: index)
                var text = page?.string?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
                if text.isEmpty, let image = try render(data, page: index + 1) { text = try recognize(image); usedOCR = true }
                if !text.isEmpty { fragments.append("第\(index + 1)页\n" + text) }
                if fragments.joined(separator: "\n\n").utf16.count > maximumUnits { incomplete = true; break }
            }
            if usedOCR { note += "；扫描页已识别文字，请核对数字和单位" }
        case "txt", "md", "csv", "json", "log":
            guard let text = String(data: data, encoding: .utf8) ?? String(data: data, encoding: .utf16) else { throw HologramError.message("文本编码无法识别，请转换为UTF-8文本或PDF。") }
            fragments = [text]
        default:
            guard let source = CGImageSourceCreateWithData(data as CFData, nil),
                  let image = CGImageSourceCreateThumbnailAtIndex(source, 0, [kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceCreateThumbnailWithTransform: true, kCGImageSourceThumbnailMaxPixelSize: 2048] as CFDictionary) else { throw HologramError.message("此文件格式无法读取，请选择PDF、图片或UTF-8文本。") }
            fragments = [try recognize(image)]; note = "图片文字识别，请核对数字和单位"
        }
        try Task.checkCancellation()
        let text = fragments.joined(separator: "\n\n").trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { throw HologramError.message("未提取到清晰文字，请换一份可读文件或更清晰的图片。") }
        incomplete = incomplete || text.utf16.count > maximumUnits
        if incomplete { note = "内容已截断，完整分析请分段发送；" + note }
        let name = String(url.lastPathComponent.filter { !$0.isNewline && !$0.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }) }.prefix(120))
        return MobileAIAttachment(name: name.isEmpty ? "附件" : name, text: MobileAIRequest.prefix(text, units: maximumUnits), note: note)
    }
    private static func recognize(_ image: CGImage) throws -> String {
        try Task.checkCancellation()
        let request = VNRecognizeTextRequest(); request.recognitionLevel = .accurate
        request.recognitionLanguages = ["zh-Hans", "en-US"]; request.usesLanguageCorrection = true
        try VNImageRequestHandler(cgImage: image, options: [:]).perform([request])
        try Task.checkCancellation()
        return (request.results ?? []).compactMap { $0.topCandidates(1).first?.string }.joined(separator: "\n")
    }
    private static func render(_ data: Data, page index: Int) throws -> CGImage? {
        try Task.checkCancellation()
        guard let provider = CGDataProvider(data: data as CFData), let document = CGPDFDocument(provider), let page = document.page(at: index) else { return nil }
        let bounds = page.getBoxRect(.mediaBox)
        guard bounds.width > 0, bounds.height > 0 else { return nil }
        let scale = min(2, 1800 / max(bounds.width, bounds.height))
        let width = max(1, Int(bounds.width * scale)), height = max(1, Int(bounds.height * scale))
        guard let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        let rect = CGRect(x: 0, y: 0, width: CGFloat(width), height: CGFloat(height))
        context.setFillColor(CGColor(gray: 1, alpha: 1)); context.fill(rect)
        context.concatenate(page.getDrawingTransform(.mediaBox, rect: rect, rotate: 0, preserveAspectRatio: true)); context.drawPDFPage(page)
        return context.makeImage()
    }
}
