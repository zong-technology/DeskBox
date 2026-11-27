using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace DeskBox.Integration
{
    internal static class ValidationWord
    {
        private const long MaxExpanded = 96L * 1024 * 1024, MaxXml = 16L * 1024 * 1024;
        private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly Regex Label = new Regex(@"\{\{([^{}\r\n]{1,80})\}\}", RegexOptions.CultureInvariant);
        private static readonly Regex Requirement = new Regex(@"^\s*(?<number>[\p{L}\p{N}][\p{L}\p{N}_.-]{0,119})[ \t]*(?:[：:]|[ \t]+[–—-][ \t]+|\t)(?<detail>.+)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

        internal static EngineeringTable Extract(Stream source, int table, CancellationToken token)
        {
            if (table < 0) throw new ArgumentOutOfRangeException("table");
            using (var archive = new ZipArchive(source, ZipArchiveMode.Read, true))
            {
                CheckPackage(archive);
                var document = ReadXml(archive.GetEntry("word/document.xml"));
                var body = document.Root == null ? null : document.Root.Element(Word + "body");
                if (body == null) throw new InvalidDataException("DOCX 缺少 Word 正文。");
                var result = new EngineeringTable();
                int row = 0;
                if (table == 0)
                {
                    result.Headers = new[] { "Number", "Description" };
                    foreach (var paragraph in body.Elements(Word + "p"))
                    {
                        token.ThrowIfCancellationRequested();
                        row++;
                        var text = Text(paragraph);
                        var match = Requirement.Match(text);
                        if (match.Success && match.Groups["number"].Value.Any(char.IsDigit))
                            Add(result, new[] { match.Groups["number"].Value.Trim(), match.Groups["detail"].Value.Trim() }, "段落 " + row, row);
                    }
                    return result;
                }

                var selected = body.Descendants(Word + "tbl").Skip(table - 1).FirstOrDefault();
                if (selected == null) throw new InvalidDataException("DOCX 找不到指定的正文表格。");
                foreach (var element in selected.Elements(Word + "tr"))
                {
                    token.ThrowIfCancellationRequested();
                    row++;
                    var values = element.Elements(Word + "tc").Select(item =>
                        string.Join("\n", item.Elements(Word + "p").Select(Text))).ToArray();
                    if (values.All(string.IsNullOrWhiteSpace)) continue;
                    if (values.Length > 256) throw new InvalidDataException("Word 表格超过 256 列。");
                    if (result.Headers.Length == 0) result.Headers = values;
                    else Add(result, values, "表格 " + table + " 行 " + row, row);
                }
                return result;
            }
        }

        private static void Add(EngineeringTable table, string[] values, string position, int line)
        {
            if (table.Rows.Count >= 30000) throw new InvalidDataException("Word 提取最多 30000 条候选记录。");
            if (values.Skip(table.Headers.Length).Any(value => !string.IsNullOrWhiteSpace(value)))
                throw new InvalidDataException("Word 表格列数与表头不一致，位置 " + position + "。");
            var row = new DeskBox.Domain.EngineeringRecord { Kind = "WordRow", Location = position, StartLine = line, EndLine = line };
            for (var column = 0; column < table.Headers.Length; column++)
                row.Fields[EngineeringTables.Index(column)] = column < values.Length ? values[column] : "";
            table.Rows.Add(row);
        }

        private static string Text(XElement paragraph)
        {
            var parts = paragraph.Descendants()
                .Where(item => item.Name == Word + "t" || item.Name == Word + "tab" || item.Name == Word + "br")
                .Where(item => !item.Ancestors(Word + "del").Any() && !item.Ancestors(Word + "moveFrom").Any());
            return string.Concat(parts.Select(item => item.Name == Word + "tab" ? "\t" : item.Name == Word + "br" ? "\n" : item.Value));
        }

        internal static void Write(Stream output, IEnumerable<string> paragraphs, CancellationToken token)
        {
            var body = new XElement(Word + "body", paragraphs.Select(line =>
            {
                token.ThrowIfCancellationRequested();
                return Paragraph(line);
            }));
            var doc = new XDocument(new XElement(Word + "document", new XAttribute(XNamespace.Xmlns + "w", Word), body));
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                WriteText(archive, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
                WriteText(archive, "_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
                WriteXml(archive, "word/document.xml", doc);
            }
        }

        internal static void FillTemplate(Stream output, Stream template, IDictionary<string, string> fields,
            IEnumerable<string> bodyLines, CancellationToken token)
        {
            using (var input = new ZipArchive(template, ZipArchiveMode.Read, true))
            using (var target = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                CheckPackage(input);
                var contentTypes = ReadXml(input.GetEntry("[Content_Types].xml"));
                if (contentTypes.Descendants().Any(element =>
                    ((string)element.Attribute("ContentType") ?? "").IndexOf("macro", StringComparison.OrdinalIgnoreCase) >= 0))
                    throw new InvalidDataException("报告模板必须是无宏的 .docx。");
                foreach (var relation in input.Entries.Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
                {
                    token.ThrowIfCancellationRequested();
                    if (ReadXml(relation).Descendants(Relationships + "Relationship").Any(link =>
                        string.Equals((string)link.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException("报告模板不能包含外部文件或链接关系。");
                }
                var main = ReadXml(input.GetEntry("word/document.xml"));
                var documentBody = main.Root == null ? null : main.Root.Element(Word + "body");
                if (documentBody == null) throw new InvalidDataException("DOCX 模板没有正文。");
                var placeholders = documentBody.Descendants(Word + "p").ToList();
                bool hasBody = false;
                foreach (var paragraph in placeholders)
                {
                    token.ThrowIfCancellationRequested();
                    var text = Text(paragraph);
                    if (!Label.IsMatch(text)) continue;
                    var single = Label.Match(text.Trim());
                    string fieldName;
                    if (single.Success && single.Value == text.Trim() &&
                        fields.TryGetValue(single.Groups[1].Value.Trim(), out fieldName) && fieldName == "$validation-body")
                    {
                        if (hasBody) throw new InvalidDataException("企业模板只能有一个正文占位符。");
                        hasBody = true;
                        var formatting = paragraph.Element(Word + "pPr");
                        foreach (var line in bodyLines)
                        {
                            token.ThrowIfCancellationRequested();
                            var added = Paragraph(line);
                            if (formatting != null) added.AddFirst(new XElement(formatting));
                            paragraph.AddBeforeSelf(added);
                        }
                        paragraph.Remove();
                        continue;
                    }
                    var replaced = Label.Replace(text, match =>
                    {
                        string value;
                        if (!fields.TryGetValue(match.Groups[1].Value.Trim(), out value))
                            throw new InvalidDataException("没有模板占位符的字段映射：" + match.Value);
                        if (value == "$validation-body")
                            throw new InvalidDataException("正文占位符须占用一个独立段落。");
                        return value ?? "";
                    });
                    var children = paragraph.Elements().Where(item => item.Name != Word + "pPr").ToList();
                    var runProps = children.Select(item => item.DescendantsAndSelf(Word + "rPr").FirstOrDefault()).FirstOrDefault(item => item != null);
                    foreach (var item in children) item.Remove();
                    var run = Run(replaced);
                    if (runProps != null) run.AddFirst(new XElement(runProps));
                    paragraph.Add(run);
                }
                if (!hasBody) throw new InvalidDataException("模板正文需要独立的 {{Body}} 或映射后占位符。");
                documentBody.AddFirst(Paragraph("待审核草稿 · 未签核"));
                foreach (var entry in input.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                    if (entry.FullName == "word/document.xml") WriteXml(target, entry.FullName, main);
                    else
                    {
                        using (var read = entry.Open())
                        using (var write = target.CreateEntry(entry.FullName).Open())
                        {
                            int count;
                            var buffer = new byte[65536];
                            while ((count = read.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                token.ThrowIfCancellationRequested();
                                write.Write(buffer, 0, count);
                            }
                        }
                    }
                }
            }
        }

        private static void CheckPackage(ZipArchive archive)
        {
            if (archive.Entries.Count > 10000) throw new InvalidDataException("Word 压缩包条目过多。");
            long size = 0;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in archive.Entries)
            {
                if (!paths.Add(part.FullName) || part.FullName.StartsWith("/", StringComparison.Ordinal) ||
                    part.FullName.Contains("\\") || part.FullName.Split('/').Any(piece => piece == ".."))
                    throw new InvalidDataException("Word 压缩包含有不安全或重复路径。");
                if (part.Length > MaxExpanded - size) throw new InvalidDataException("Word 压缩包展开后超过限额。");
                size += part.Length;
                if (part.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase) ||
                    part.FullName.StartsWith("word/embeddings/", StringComparison.OrdinalIgnoreCase) ||
                    part.FullName.StartsWith("word/activeX/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("验证 Word 文档不能包含宏、嵌入对象或 ActiveX。");
            }
        }

        private static XDocument ReadXml(ZipArchiveEntry part)
        {
            if (part == null || part.Length > MaxXml) throw new InvalidDataException("缺少 Word 文档部件或部件过大。");
            using (var stream = part.Open())
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXml
            }))
                return XDocument.Load(reader);
        }

        private static XElement Paragraph(string text) { return new XElement(Word + "p", Run(text)); }

        private static XElement Run(string text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var element = new XElement(Word + "r");
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) element.Add(new XElement(Word + "br"));
                var safe = new StringBuilder(lines[i].Length);
                for (int index = 0; index < lines[i].Length; index++)
                {
                    var character = lines[i][index];
                    if (char.IsHighSurrogate(character) && index + 1 < lines[i].Length && char.IsLowSurrogate(lines[i][index + 1]))
                    {
                        safe.Append(character).Append(lines[i][++index]);
                    }
                    else safe.Append(XmlConvert.IsXmlChar(character) ? character : '\uFFFD');
                }
                element.Add(new XElement(Word + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), safe.ToString()));
            }
            return element;
        }

        private static void WriteText(ZipArchive archive, string path, string content)
        {
            using (var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false))) writer.Write(content);
        }

        private static void WriteXml(ZipArchive archive, string path, XDocument document)
        {
            using (var writer = XmlWriter.Create(archive.CreateEntry(path).Open(), new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = true }))
                document.Save(writer);
        }
    }
}
