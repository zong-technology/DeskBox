using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using DeskBox.Domain;

namespace DeskBox.Integration
{
    internal sealed class EngineeringTable
    {
        internal string Sheet = "";
        internal List<EngineeringRecord> Rows = new List<EngineeringRecord>();
        internal string[] Headers = new string[0];
    }

    internal static class EngineeringTables
    {
        private const int MaxRows = 30000, MaxColumns = 256, MaxCharacters = 24 * 1024 * 1024;
        private const string Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string OfficeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        internal static EngineeringTable Read(Stream stream, EngineeringSource source, string sheet, string encoding, CancellationToken token)
        {
            if (source.Type == "xlsx") return ReadWorkbook(stream, sheet, token);
            var table = new EngineeringTable();
            int line = 1, characters = 0;
            using (var reader = EngineeringFiles.Text(stream, encoding, source))
            {
                while (reader.Peek() >= 0)
                {
                    token.ThrowIfCancellationRequested();
                    int start = line;
                    var row = ReadDelimitedRow(reader, source.Type == "tsv" ? '\t' : ',', ref line, ref characters, token);
                    AddRow(table, row, "L" + start, start);
                }
            }
            return table;
        }

        private static string[] ReadDelimitedRow(TextReader reader, char separator, ref int line, ref int characters, CancellationToken token)
        {
            var cells = new List<string>();
            var text = new StringBuilder();
            bool quoted = false, closed = false, started = false;
            int c;
            while ((c = reader.Read()) >= 0)
            {
                if ((++characters & 4095) == 0) token.ThrowIfCancellationRequested();
                if (characters > MaxCharacters) throw new InvalidDataException("CSV/TSV 内容超出解析限额。");
                bool newline = c == '\r' || c == '\n';
                if (newline)
                {
                    if (c == '\r' && reader.Peek() == '\n') reader.Read();
                    line++;
                }
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (reader.Peek() == '"') { reader.Read(); text.Append('"'); }
                        else { quoted = false; closed = true; }
                    }
                    else text.Append(newline ? '\n' : (char)c);
                }
                else if (newline || c == separator)
                {
                    cells.Add(text.ToString());
                    if (newline) return cells.ToArray();
                    if (cells.Count >= MaxColumns) throw new InvalidDataException("表格列数超过 256。");
                    text.Clear();
                    started = closed = false;
                }
                else if (c == '"' && !started) quoted = started = true;
                else
                {
                    if (closed || c == '"') throw new InvalidDataException("CSV 引号字段格式无效，位置 L" + line + "。");
                    text.Append((char)c);
                    started = true;
                }
                if (text.Length > 32767) throw new InvalidDataException("表格单元格超过 32767 字符。");
            }
            if (quoted) throw new InvalidDataException("CSV 引号字段未闭合，位置 L" + line + "。");
            cells.Add(text.ToString());
            return cells.ToArray();
        }

        private static void AddRow(EngineeringTable table, string[] values, string location, int row)
        {
            if (values.All(string.IsNullOrWhiteSpace)) return;
            if (table.Headers.Length == 0) { table.Headers = values.Select(value => value.Trim()).ToArray(); return; }
            if (table.Rows.Count >= MaxRows) throw new InvalidDataException("一份工程表最多 30000 条数据。");
            if (values.Skip(table.Headers.Length).Any(value => !string.IsNullOrWhiteSpace(value)))
                throw new InvalidDataException("数据超过表头列数，位置 " + location + "。");
            var record = new EngineeringRecord { Kind = "TableRow", Location = location, StartLine = row, EndLine = row };
            for (int index = 0; index < table.Headers.Length; index++)
                record.Fields[Index(index)] = index < values.Length ? values[index] : "";
            table.Rows.Add(record);
        }

        internal static string Index(int index) { return index.ToString(CultureInfo.InvariantCulture); }

        private static EngineeringTable ReadWorkbook(Stream input, string selected, CancellationToken token)
        {
            using (var archive = new ZipArchive(input, ZipArchiveMode.Read, true))
            {
                var workbook = ReadXml(archive, "xl/workbook.xml", 4 * 1024 * 1024);
                var relationships = ReadXml(archive, "xl/_rels/workbook.xml.rels", 4 * 1024 * 1024);
                XNamespace ns = Spreadsheet, rel = OfficeRel, package = PackageRel;
                var sheets = workbook.Descendants(ns + "sheet").ToArray();
                var sheet = string.IsNullOrWhiteSpace(selected) ? sheets.FirstOrDefault() :
                    sheets.FirstOrDefault(item => (string)item.Attribute("name") == selected);
                if (sheet == null)
                    throw new InvalidDataException("找不到工作表。可用工作表：" + string.Join(", ", sheets.Select(item => (string)item.Attribute("name"))));
                var relationship = relationships.Descendants(package + "Relationship").FirstOrDefault(item => (string)item.Attribute("Id") == (string)sheet.Attribute(rel + "id"));
                string target = relationship == null ? null : (string)relationship.Attribute("Target");
                if (relationship == null || !((string)relationship.Attribute("Type") ?? "").EndsWith("/worksheet", StringComparison.Ordinal)
                    || (string)relationship.Attribute("TargetMode") == "External" || string.IsNullOrEmpty(target))
                    throw new InvalidDataException("工作表不指向本地 XLSX worksheet。");
                string path = target.StartsWith("/", StringComparison.Ordinal) ? target.TrimStart('/') : "xl/" + target;
                if (!path.StartsWith("xl/worksheets/", StringComparison.Ordinal) || path.Contains("..") || path.Contains("\\"))
                    throw new InvalidDataException("XLSX 工作表路径超出受支持范围。");
                var strings = SharedStrings(archive, token);
                var table = new EngineeringTable { Sheet = (string)sheet.Attribute("name") ?? "" };
                int last = 0, characters = 0;
                using (var stream = Entry(archive, path, 64 * 1024 * 1024).Open())
                using (var reader = XmlReader.Create(stream, XmlSettings(64 * 1024 * 1024)))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row" || reader.NamespaceURI != Spreadsheet) continue;
                        token.ThrowIfCancellationRequested();
                        if (last >= 1048576) throw new InvalidDataException("XLSX 行编号超出工作表范围。");
                        XElement row;
                        using (var part = reader.ReadSubtree()) row = XElement.Load(part);
                        int number = (int?)row.Attribute("r") ?? last + 1;
                        if (number <= last || number > 1048576) throw new InvalidDataException("XLSX 行号无效。");
                        last = number;
                        var cells = new List<string>();
                        foreach (var cell in row.Elements(ns + "c"))
                        {
                            string reference = (string)cell.Attribute("r");
                            int column = reference == null ? cells.Count : ColumnNumber(reference);
                            if (column >= MaxColumns || column < cells.Count) throw new InvalidDataException("XLSX 列序无效或超过前 256 列。");
                            while (cells.Count < column) cells.Add("");
                            string type = (string)cell.Attribute("t");
                            string value = (string)cell.Element(ns + "v") ?? "";
                            if (cell.Element(ns + "f") != null)
                                throw new InvalidDataException("工程表含公式单元格 " + table.Sheet + "!" + (reference ?? "行 " + number) + "，请另存值后导入。");
                            if (type == "s")
                            {
                                int id;
                                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) || id < 0 || id >= strings.Count)
                                    throw new InvalidDataException("XLSX 共享字符串索引无效。");
                                value = strings[id];
                            }
                            if (type == "inlineStr") value = string.Concat(cell.Descendants(ns + "t").Select(item => item.Value));
                            if (type == "e") throw new InvalidDataException("工程表包含 Excel 错误单元格 " + reference + "。");
                            characters += value.Length;
                            if (value.Length > 32767 || characters > MaxCharacters) throw new InvalidDataException("XLSX 单元格或总文本超过限额。");
                            cells.Add(value);
                        }
                        AddRow(table, cells.ToArray(), table.Sheet + "!L" + number, number);
                    }
                }
                return table;
            }
        }

        private static int ColumnNumber(string reference)
        {
            int result = 0, index = 0;
            while (index < reference.Length && reference[index] >= 'A' && reference[index] <= 'Z')
            {
                result = result * 26 + reference[index++] - 'A' + 1;
                if (result > MaxColumns) return MaxColumns;
            }
            if (index == 0 || index == reference.Length || reference.Skip(index).Any(c => c < '0' || c > '9'))
                throw new InvalidDataException("XLSX 单元格地址无效。");
            return result - 1;
        }

        private static List<string> SharedStrings(ZipArchive archive, CancellationToken token)
        {
            XNamespace ns = Spreadsheet;
            var output = new List<string>();
            if (archive.GetEntry("xl/sharedStrings.xml") == null) return output;
            using (var stream = Entry(archive, "xl/sharedStrings.xml", 32 * 1024 * 1024).Open())
            using (var reader = XmlReader.Create(stream, XmlSettings(32 * 1024 * 1024)))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "si" || reader.NamespaceURI != Spreadsheet) continue;
                    token.ThrowIfCancellationRequested();
                    if (output.Count >= 500000) throw new InvalidDataException("XLSX 共享字符串超过限额。");
                    using (var item = reader.ReadSubtree()) output.Add(string.Concat(XElement.Load(item).Descendants(ns + "t").Select(value => value.Value)));
                }
            }
            return output;
        }

        private static ZipArchiveEntry Entry(ZipArchive archive, string name, long max)
        {
            var entry = archive.GetEntry(name);
            if (entry == null || entry.Length > max) throw new InvalidDataException("XLSX 缺少或超出包文件限额：" + name);
            return entry;
        }

        private static XDocument ReadXml(ZipArchive archive, string name, long max)
        {
            using (var stream = Entry(archive, name, max).Open())
            using (var reader = XmlReader.Create(stream, XmlSettings(max))) return XDocument.Load(reader);
        }

        private static XmlReaderSettings XmlSettings(long max)
        {
            return new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = max };
        }

        internal static void WriteCsv(Stream stream, IEnumerable<string[]> rows, CancellationToken token)
        {
            using (var writer = new StreamWriter(stream, new UTF8Encoding(true), 32768, true))
            {
                foreach (var cells in rows)
                {
                    token.ThrowIfCancellationRequested();
                    writer.WriteLine(string.Join(",", cells.Select(CsvCell)));
                }
            }
        }

        private static string CsvCell(string value)
        {
            value = value ?? "";
            string first = value.TrimStart();
            decimal number;
            if (first.Length > 0 && "=+-@".Contains(first[0]) &&
                !decimal.TryParse(first, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number))
                value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        internal static void WriteWorkbook(Stream stream, IEnumerable<string[]> rows, CancellationToken token)
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                WritePart(archive, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
                WritePart(archive, "_rels/.rels", Relationship("officeDocument", "xl/workbook.xml"));
                WritePart(archive, "xl/_rels/workbook.xml.rels", Relationship("worksheet", "worksheets/sheet1.xml"));
                WritePart(archive, "xl/workbook.xml", "<workbook xmlns=\"" + Spreadsheet + "\" xmlns:r=\"" + OfficeRel +
                    "\"><sheets><sheet name=\"Tags\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                using (var output = archive.CreateEntry("xl/worksheets/sheet1.xml").Open())
                using (var writer = XmlWriter.Create(output))
                {
                    writer.WriteStartElement("worksheet", Spreadsheet);
                    writer.WriteStartElement("sheetData", Spreadsheet);
                    foreach (var cells in rows)
                    {
                        token.ThrowIfCancellationRequested();
                        writer.WriteStartElement("row", Spreadsheet);
                        foreach (var value in cells)
                        {
                            writer.WriteStartElement("c", Spreadsheet);
                            writer.WriteAttributeString("t", "inlineStr");
                            writer.WriteStartElement("is", Spreadsheet);
                            writer.WriteStartElement("t", Spreadsheet);
                            writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                            writer.WriteString(value ?? "");
                            writer.WriteEndElement();
                            writer.WriteEndElement();
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
            }
        }

        private static string Relationship(string kind, string target)
        {
            return "<Relationships xmlns=\"" + PackageRel + "\"><Relationship Id=\"rId1\" Type=\"" + OfficeRel + "/" +
                kind + "\" Target=\"" + target + "\"/></Relationships>";
        }

        private static void WritePart(ZipArchive archive, string path, string text)
        {
            using (var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false))) writer.Write(text);
        }
    }
}
