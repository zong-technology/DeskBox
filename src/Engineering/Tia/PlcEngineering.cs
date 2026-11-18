using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;

namespace DeskBox.Engineering.Tia
{
    public sealed class PlcObject
    {
        public string Kind = "", Name = "", Scope = "", Controller = "", Source = "", Language = "", Content = "", Address = "", DataType = "", Signal = "", OrderNumber = "", Interface = "", Fingerprint = "";
        public List<string> References = new List<string>();
        public string Identity { get { return Scope + " / " + Kind + " / " + Name; } }
    }

    public sealed class PlcSnapshot
    {
        public string Name = "", Origin = "导出文件";
        public List<PlcObject> Objects = new List<PlcObject>();
        public List<string> Notes = new List<string>();

        public string Report()
        {
            var report = new StringBuilder().AppendLine(Name + " · " + Origin);
            foreach (var group in Objects.GroupBy(x => x.Kind))
                report.AppendLine(group.Key + " · " + group.Count());
            foreach (var note in Notes.Concat(PlcEngineering.Check(this))) report.AppendLine("• " + note);
            report.AppendLine("检查范围为导出和已读取内容。编译、仿真、工程师审核和现场验证需分别记录。");
            return report.ToString();
        }
    }

    public static class PlcEngineering
    {
        private const int MaxFile = 8 * 1024 * 1024;
        private static readonly Regex Identifier = new Regex(@"\A[A-Za-z_][A-Za-z0-9_]{0,62}\z", RegexOptions.CultureInvariant);
        private static readonly Regex SclBlock = new Regex(@"(?im)^[ \t]*(FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|TYPE|ORGANIZATION_BLOCK)[ \t]+(?:""([^""\r\n]+)""|([A-Za-z_][A-Za-z0-9_]*))[^\r\n]*");
        private static readonly HashSet<string> XmlKinds = new HashSet<string> { "SW.Blocks.FB", "SW.Blocks.FC", "SW.Blocks.OB", "SW.Blocks.GlobalDB", "SW.Blocks.InstanceDB", "SW.Types.PlcStruct", "SW.Tags.PlcTagTable", "SW.Tags.PlcTag", "HW.Device", "HW.DeviceItem", "HW.Subnet" };

        public static string FullLocalPath(string path)
        {
            if ((path ?? "").StartsWith(@"\\", StringComparison.Ordinal))
                throw new InvalidOperationException("工程操作需要本机文件和目录。");
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (full.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(root).DriveType == DriveType.Network)
                throw new InvalidOperationException("工程操作需要本机文件和目录。");
            var current = root;
            foreach (var part in full.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("工程路径不能经过符号链接或重解析点：" + current);
                }
                catch (DirectoryNotFoundException) { break; }
                catch (FileNotFoundException) { break; }
            }
            return full;
        }

        public static string Hash(string text)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).Replace("-", "").ToLowerInvariant();
        }

        public static PlcSnapshot ReadExports(IEnumerable<string> files, CancellationToken token)
        {
            var paths = files.Take(201).ToList();
            if (paths.Count == 0 || paths.Count > 200) throw new ArgumentException("每次选择 1–200 个导出文件。");
            var result = new PlcSnapshot { Name = paths.Count == 1 ? Path.GetFileName(paths[0]) : "工程导出资料（" + paths.Count + " 个文件）" };
            long size = 0;
            foreach (string path in paths)
            {
                token.ThrowIfCancellationRequested();
                var full = FullLocalPath(path);
                var file = new FileInfo(full);
                if (file.Length > MaxFile || (size += file.Length) > 32L * 1024 * 1024)
                    throw new InvalidDataException("每个文件最多 8 MB，一批最多 32 MB。");
                string ext = file.Extension.ToLowerInvariant();
                if (ext == ".xml") ReadXml(full, result);
                else if (ext == ".scl" || ext == ".udt" || ext == ".db") ReadScl(File.ReadAllText(full), file.Name, result);
                else if (ext == ".csv") ReadIo(full, result);
                else throw new InvalidDataException("没有对应的导出文件读取器：" + ext);
            }
            if (result.Objects.Count > 50000) throw new InvalidDataException("对象数量超过 50000。");
            if (result.Objects.Count == 0) throw new InvalidDataException("没有找到受支持的工程对象。请提供 Siemens SimaticML、SCL 源文件或带表头的 IO CSV。");
            return result;
        }

        private static void ReadXml(string path, PlcSnapshot snapshot)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxFile };
            XDocument doc;
            using (var reader = XmlReader.Create(path, settings)) doc = XDocument.Load(reader, LoadOptions.SetLineInfo);
            int count = snapshot.Objects.Count;
            foreach (var element in doc.Descendants().Where(x => XmlKinds.Contains(x.Name.LocalName)))
            {
                var attributes = element.Elements().FirstOrDefault(x => x.Name.LocalName == "AttributeList");
                var name = Value(attributes, "Name");
                if (name.Length == 0) continue;
                var line = (IXmlLineInfo)element;
                var item = new PlcObject { Kind = element.Name.LocalName.Split('.').Last(), Name = name, Source = Path.GetFileName(path) + ":" + line.LineNumber,
                    Language = Value(attributes, "ProgrammingLanguage"), Address = Value(attributes, "LogicalAddress"), DataType = Value(attributes, "DataTypeName") };
                item.Scope = string.Join("/", element.Ancestors().Where(x => XmlKinds.Contains(x.Name.LocalName)).Reverse().Select(x => Value(x.Elements().FirstOrDefault(a => a.Name.LocalName == "AttributeList"), "Name")));
                var members = element.Descendants().Where(x => x.Name.LocalName == "Member").ToList();
                item.Interface = string.Join("\n", members.Select(m => (string)m.Attribute("Name") + " : " + (string)m.Attribute("Datatype")));
                var networks = element.Descendants().Where(x => x.Name.LocalName == "CompileUnit" || x.Name.LocalName == "SW.Blocks.CompileUnit").ToList();
                if (networks.Count != 0)
                {
                    var details = new StringBuilder();
                    int number = 0;
                    foreach (var network in networks)
                    {
                        var symbols = network.Descendants().Where(x => x.Name.LocalName == "Symbol")
                            .Select(x => string.Join(".", x.Elements().Where(c => c.Name.LocalName == "Component").Select(c => (string)c.Attribute("Name"))))
                            .Where(x => x.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        var calls = network.Descendants().Where(x => x.Name.LocalName == "CallInfo").Select(x => (string)x.Attribute("Name")).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                        item.References.AddRange(symbols.Concat(calls));
                        var parts = network.Descendants().Where(x => x.Name.LocalName == "Part").Select(x => (string)x.Attribute("Name")).Where(x => x != null).ToArray();
                        details.AppendLine("网络 " + (++number) + " · " + string.Join(", ", parts)).AppendLine("引用 · " + string.Join(", ", symbols));
                        if (calls.Count != 0) details.AppendLine("块调用 · " + string.Join(", ", calls));
                        foreach (var wire in network.Descendants().Where(x => x.Name.LocalName == "Wire"))
                        {
                            var endpoints = wire.Elements().Select(x => x.Name.LocalName + " " + (string)x.Attribute("UId") + " " + (string)x.Attribute("Name"));
                            details.AppendLine("连线端点 · " + string.Join(" → ", endpoints));
                        }
                        var scl = network.Descendants().FirstOrDefault(x => x.Name.LocalName == "StructuredText");
                        if (scl != null) details.AppendLine(StructuredText(scl));
                    }
                    item.Content = details.ToString();
                }
                else item.Content = (attributes == null ? element : attributes).ToString(SaveOptions.DisableFormatting);
                item.References = item.References.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                item.Fingerprint = Hash(element.ToString(SaveOptions.DisableFormatting));
                snapshot.Objects.Add(item);
            }
            if (snapshot.Objects.Count == count) snapshot.Notes.Add(Path.GetFileName(path) + " 中没有可识别的 SimaticML 工程对象。");
        }

        private static string StructuredText(XElement element)
        {
            var text = new StringBuilder();
            foreach (var node in element.Descendants())
            {
                switch (node.Name.LocalName)
                {
                    case "Token": case "Text": text.Append((string)node.Attribute("Text") ?? node.Value); break;
                    case "NewLine": text.AppendLine(); break;
                    case "Blank": text.Append(' '); break;
                    case "Component": text.Append((string)node.Attribute("Name")); break;
                }
            }
            return text.ToString();
        }

        private static string Value(XElement element, string key)
        {
            var child = element == null ? null : element.Elements().FirstOrDefault(x => x.Name.LocalName == key);
            return child == null ? "" : child.Value.Trim();
        }

        public static PlcSnapshot ReviewScl(string text, string file)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 24000) throw new ArgumentException("SCL 草稿需为 1–24000 字。");
            var snapshot = new PlcSnapshot { Name = file, Origin = "SCL 草稿静态检查" };
            ReadScl(text, file, snapshot);
            if (snapshot.Objects.Count == 0) throw new InvalidDataException("未发现可识别的 SCL 工程对象。");
            return snapshot;
        }

        private static void ReadScl(string text, string file, PlcSnapshot snapshot)
        {
            var masked = Regex.Replace(text, @"\(\*[\s\S]*?\*\)|//[^\r\n]*|/\*[\s\S]*?\*/|'(?:''|[^'\r\n])*'", m => Regex.Replace(m.Value, @"[^\r\n]", " "));
            var blocks = SclBlock.Matches(masked).Cast<Match>().ToArray();
            for (int i = 0; i < blocks.Length; i++)
            {
                var block = blocks[i];
                var end = i + 1 < blocks.Length ? blocks[i + 1].Index : text.Length;
                var content = text.Substring(block.Index, end - block.Index).Trim();
                var code = masked.Substring(block.Index, end - block.Index);
                var header = block.Groups[1].Value.ToUpperInvariant();
                var item = new PlcObject { Kind = header == "FUNCTION_BLOCK" ? "FB" : header == "FUNCTION" ? "FC" : header == "DATA_BLOCK" ? "DB" : header == "TYPE" ? "UDT" : "OB",
                    Name = block.Groups[2].Success ? block.Groups[2].Value : block.Groups[3].Value, Language = "SCL", Content = content,
                    Source = file + ":" + (1 + text.Take(block.Index).Count(c => c == '\n')), Fingerprint = Hash(content) };
                item.References = Regex.Matches(code, @"#[A-Za-z_][A-Za-z0-9_]*|""[^""\r\n]+""").Cast<Match>().SkipWhile(m => m.Index < block.Length).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var declarations = Regex.Matches(code, @"(?ims)^\s*VAR(?:_[A-Z_]+)?\b(.*?)\bEND_VAR\b").Cast<Match>().ToList();
                item.Interface = string.Join("\n", declarations.Select(x => x.Value.Trim()));
                var declared = declarations.SelectMany(d => Regex.Matches(d.Groups[1].Value, @"(?im)^\s*([A-Za-z_][A-Za-z0-9_]*(?:\s*,\s*[A-Za-z_][A-Za-z0-9_]*)*)\s*:")
                    .Cast<Match>().SelectMany(m => m.Groups[1].Value.Split(',').Select(n => n.Trim()))).ToList();
                foreach (var duplicate in declared.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                    snapshot.Notes.Add(item.Source + " · 重复局部变量 " + duplicate.Key);
                if (declarations.Count != 0)
                {
                    var locals = item.References.Where(r => r.StartsWith("#", StringComparison.Ordinal)).Select(r => r.Substring(1)).ToList();
                    foreach (var missing in locals.Except(declared, StringComparer.OrdinalIgnoreCase))
                        snapshot.Notes.Add(item.Source + " · 未匹配到局部声明 #" + missing + "，核对接口或源文件范围。");
                    foreach (var unused in declared.Except(locals, StringComparer.OrdinalIgnoreCase))
                        snapshot.Notes.Add(item.Source + " · 正文未发现局部引用 #" + unused + "，核对外部使用和接口约定。");
                }

                if (!Regex.IsMatch(code, @"(?im)^\s*END_" + header + @"\b")) snapshot.Notes.Add(item.Source + " · 未找到 END_" + header);
                snapshot.Objects.Add(item);
            }
        }

        private static void ReadIo(string path, PlcSnapshot snapshot)
        {
            using (var csv = new TextFieldParser(path, Encoding.UTF8, true))
            {
                csv.SetDelimiters(","); csv.HasFieldsEnclosedInQuotes = true;
                if (csv.EndOfData) return;
                var header = csv.ReadFields();
                int name = Column(header, "name", "名称", "tag"), address = Column(header, "address", "地址"), type = Column(header, "type", "类型", "datatype"), device = Column(header, "device", "设备", "plc"), order = Column(header, "order", "订货号"), signal = Column(header, "signal", "信号");
                if (name < 0) throw new InvalidDataException("IO CSV 需要 name（或名称）列。");
                while (!csv.EndOfData)
                {
                    long row = csv.LineNumber;
                    var values = csv.ReadFields();
                    if (values.Length != header.Length) throw new InvalidDataException("CSV 列数不一致，第 " + row + " 行。");
                    if (string.IsNullOrWhiteSpace(values[name])) throw new InvalidDataException("CSV 名称为空，第 " + row + " 行。");
                    string addr = Cell(values, address), plc = Cell(values, device), part = Cell(values, order);
                    var item = new PlcObject { Name = values[name], Kind = "PlcTag", Scope = plc, Address = addr, DataType = Cell(values, type), Signal = Cell(values, signal),
                    Source = Path.GetFileName(path) + ":" + row, Controller = plc, OrderNumber = part,
                    Content = "信号 " + Cell(values, signal) + " · 订货号 " + (part.Length == 0 ? "待确认" : part) + " · 地址 " + (addr.Length == 0 ? "待分配" : addr),
                        Fingerprint = Hash(string.Join("\u001f", values)) };
                    snapshot.Objects.Add(item);
                    if (snapshot.Objects.Count > 50000) throw new InvalidDataException("对象数量超过 50000。");
                }
            }
        }

        private static int Column(string[] header, params string[] names) { return Array.FindIndex(header, h => names.Contains(h.Trim(), StringComparer.OrdinalIgnoreCase)); }
        private static string Cell(string[] row, int index) { return index < 0 ? "" : row[index].Trim(); }

        public static string Related(PlcSnapshot snapshot, PlcObject item)
        {
            var candidates = snapshot.Objects.Where(x => x != item &&
                (item.Controller.Length == 0 || x.Controller.Length == 0 || item.Controller == x.Controller));
            var references = new HashSet<string>(item.References.Select(GlobalSymbol).Where(x => x.Length != 0), StringComparer.OrdinalIgnoreCase);
            var links = candidates.Where(x => references.Contains(x.Name) || x.References.Any(r => string.Equals(GlobalSymbol(r), item.Name, StringComparison.OrdinalIgnoreCase)))
                .Take(21).Select(x => x.Identity + " · " + x.Source).ToList();
            bool truncated = links.Count > 20;
            if (truncated) links.RemoveAt(20);
            if (links.Count == 0) return "";
            return "工程交叉引用候选（按可见符号匹配）\r\n" + string.Join("\r\n", links) + (truncated ? "\r\n仅显示前 20 项。" : "");
        }

        private static string GlobalSymbol(string symbol)
        {
            return symbol.StartsWith("#", StringComparison.Ordinal) ? "" : symbol.Trim('"');
        }

        public static List<string> Check(PlcSnapshot snapshot)
        {
            var notes = new List<string>();
            foreach (var group in snapshot.Objects.GroupBy(x => x.Identity, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                notes.Add("重复对象：" + group.Key + " · " + string.Join(", ", group.Select(x => x.Source)));
            var spans = new List<Tuple<PlcObject, string, long, long>>();
            foreach (var item in snapshot.Objects.Where(x => x.Kind == "PlcTag"))
            {
                if (item.Address.Length == 0) { notes.Add(item.Source + " · " + item.Name + " 地址待确认"); continue; }
                S7AddressResult parsed;
                try { parsed = EngineeringTools.ParseS7Address(item.Address.TrimStart('%')); }
                catch (OverflowException) { notes.Add(item.Source + " · 地址超出检查范围：" + item.Address); continue; }
                if (!parsed.Ok) { notes.Add(item.Source + " · 暂无法校验地址 " + item.Address + " · " + parsed.Error); continue; }
                long start = 8L * parsed.ByteOffset + Math.Max(0, parsed.BitOffset);
                int width = TypeWidth(item.DataType);
                if (width != 0 && width != parsed.SizeBits) notes.Add(item.Source + " · " + item.Name + " 的类型与地址宽度不一致。");
                // CSV scope is a CPU, while XML tag tables may share the same CPU address space.
                string scope = item.Controller.Length == 0 ? item.Scope : item.Controller;
                if (item.Controller.Length == 0 && item.Source.IndexOf(".xml:", StringComparison.OrdinalIgnoreCase) >= 0) scope = "导出 PLC（CPU 归属待核对）";
                spans.Add(Tuple.Create(item, scope + ":" + parsed.Area + ":" + parsed.DbNumber, start, start + parsed.SizeBits));
            }
            foreach (var group in spans.GroupBy(x => x.Item2, StringComparer.OrdinalIgnoreCase))
            {
                Tuple<PlcObject, string, long, long> previous = null;
                foreach (var range in group.OrderBy(x => x.Item3))
                {
                    if (previous != null && range.Item3 < previous.Item4)
                        notes.Add("地址重叠：" + previous.Item1.Name + " " + previous.Item1.Address + " / " + range.Item1.Name + " " + range.Item1.Address + " · " + range.Item1.Source);
                    if (previous == null || range.Item4 > previous.Item4) previous = range;
                }
            }
            return notes;
        }

        public static string PlanIo(PlcSnapshot snapshot, int inputStart, int outputStart)
        {
            if (inputStart < 0 || outputStart < 0 || inputStart > 65535 || outputStart > 65535)
                throw new ArgumentException("起始字节应为 0–65535，分配结果须按实际 CPU 核对。");
            var rows = snapshot.Objects.Where(x => x.Kind == "PlcTag").ToList();
            if (rows.Count == 0) throw new InvalidOperationException("请加载 IO CSV 或 PLC 变量。");
            if (rows.Select(x => x.Controller.Length == 0 ? x.Scope : x.Controller).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                throw new InvalidOperationException("请为每个目标 PLC 分别提供一份 IO CSV。");
            var ranges = new Dictionary<char, List<Tuple<long, long>>> { { 'I', new List<Tuple<long, long>>() }, { 'Q', new List<Tuple<long, long>>() } };
            foreach (var row in rows.Where(x => x.Address.Length != 0))
            {
                var address = EngineeringTools.ParseS7Address(row.Address.TrimStart('%'));
                if (!address.Ok) throw new InvalidDataException("先修订无法识别的既有地址 " + row.Source + " " + row.Address);
                if (address.Area != "I" && address.Area != "Q") continue;
                long bit = 8L * address.ByteOffset + Math.Max(0, address.BitOffset);
                ranges[address.Area[0]].Add(Tuple.Create(bit, bit + address.SizeBits));
            }
            var position = new Dictionary<char, long> { { 'I', inputStart * 8L }, { 'Q', outputStart * 8L } };
            var output = new StringBuilder("IO 地址建议 · 依据手动起始字节与已填地址避让。需工程师核对实际模块位置。\r\n");
            foreach (var row in rows)
            {
                string proposed = row.Address;
                if (proposed.Length == 0)
                {
                    string signal = row.Signal.ToUpperInvariant();
                    char area = signal == "DI" || signal == "AI" ? 'I' : signal == "DO" || signal == "AO" ? 'Q' : '?';
                    int width = TypeWidth(row.DataType);
                    if (area == '?' || width == 0) proposed = "待确认信号方向或类型";
                    else
                    {
                        long bit = position[area], alignment = width == 1 ? 1 : 8;
                        foreach (var reserved in ranges[area].OrderBy(r => r.Item1))
                        {
                            bit = (bit + alignment - 1) / alignment * alignment;
                            if (bit + width <= reserved.Item1) break;
                            if (bit < reserved.Item2) bit = reserved.Item2;
                        }
                        bit = (bit + alignment - 1) / alignment * alignment;
                        if (bit + width > 65536L * 8) throw new InvalidOperationException("地址超过规划范围。");
                        position[area] = bit + width;
                        proposed = width == 1 ? area + (bit / 8).ToString(CultureInfo.InvariantCulture) + "." + (bit % 8) : area + (width == 8 ? "B" : width == 16 ? "W" : "D") + (bit / 8);
                    }
                }
                output.AppendLine(row.Name + " · " + row.DataType + " · " + proposed + " · " + row.Source +
                    " · 订货号 " + (row.OrderNumber.Length == 0 ? "待确认" : row.OrderNumber));
            }
            output.AppendLine().AppendLine("建议按信号类型核对通道容量、订货号、固件和槽位。");
            foreach (var group in rows.GroupBy(x => x.Signal.ToUpperInvariant()))
                output.AppendLine((group.Key.Length == 0 ? "信号类型待确认" : group.Key) + " · " + group.Count() + " 点");
            return output.ToString();
        }

        private static int TypeWidth(string type)
        {
            switch (type.ToUpperInvariant())
            {
                case "BOOL": return 1;
                case "BYTE": case "SINT": case "USINT": case "CHAR": return 8;
                case "WORD": case "INT": case "UINT": return 16;
                case "DWORD": case "DINT": case "UDINT": case "REAL": return 32;
                default: return 0;
            }
        }

        public static string Diff(PlcSnapshot before, PlcSnapshot after)
        {
            var output = new List<string> { "工程比较 · " + before.Name + " → " + after.Name };
            var old = before.Objects.ToLookup(o => o.Identity, StringComparer.OrdinalIgnoreCase);
            var next = after.Objects.ToLookup(o => o.Identity, StringComparer.OrdinalIgnoreCase);
            foreach (var id in old.Select(g => g.Key).Union(next.Select(g => g.Key), StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            {
                var a = old[id].ToList(); var b = next[id].ToList();
                if (a.Count > 1 || b.Count > 1) { output.Add("歧义 · " + id + "，先处理重复对象。"); continue; }
                if (a.Count == 0) output.Add("新增 · " + id);
                else if (b.Count == 0) output.Add("删除 · " + id);
                else if (a[0].Fingerprint != b[0].Fingerprint)
                {
                    var change = "修改 · " + id;
                    if (a[0].Address != b[0].Address) change += " · 地址 " + a[0].Address + " → " + b[0].Address;
                    if (a[0].DataType != b[0].DataType || a[0].Interface != b[0].Interface) change += " · 接口或类型变化";
                    var related = after.Objects.Where(x => x.References.Any(r => r.Trim('"', '#').Equals(b[0].Name, StringComparison.OrdinalIgnoreCase))).Take(12).Select(x => x.Name);
                    if (related.Any()) change += " · 引用线索 " + string.Join(", ", related);
                    output.Add(change);
                }
            }
            if (output.Count == 1) output.Add("所提供对象未发现指纹变化。");
            return string.Join(Environment.NewLine, output);
        }

        public static string AnalogScale(string name)
        {
            if (!Identifier.IsMatch(name ?? "")) throw new ArgumentException("块名需为 1–63 位 ASCII 字母、数字或下划线，且不能以数字开头。");
            return "FUNCTION_BLOCK \"" + name + "\"\r\nVERSION : 0.1\r\nVAR_INPUT\r\n    Raw : Real;\r\n    RawLow : Real;\r\n    RawHigh : Real;\r\n    EngLow : Real;\r\n    EngHigh : Real;\r\nEND_VAR\r\nVAR_OUTPUT\r\n    Value : Real;\r\n    Valid : Bool;\r\n    OutOfRange : Bool;\r\nEND_VAR\r\nBEGIN\r\n    #Valid := #RawHigh > #RawLow;\r\n    #OutOfRange := FALSE;\r\n    #Value := 0.0;\r\n    IF #Valid THEN\r\n        #OutOfRange := (#Raw < #RawLow) OR (#Raw > #RawHigh);\r\n        #Value := #EngLow + ((#Raw - #RawLow) / (#RawHigh - #RawLow)) * (#EngHigh - #EngLow);\r\n    END_IF;\r\nEND_FUNCTION_BLOCK\r\n";
        }

        public static string ScaleTests { get { return "人工离线测试（示例，不代表模块计数）：RawLow=4，RawHigh=20，EngLow=0，EngHigh=100。\r\n4 → 0，12 → 50，20 → 100，2 → -12.5 且 OutOfRange=TRUE。\r\n上下限相等时 Valid=FALSE、Value=0。反向工程量程应核对中点及端点。\r\n实际计数范围、故障值、单位和越界策略须确认。"; } }
    }
}
