using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DeskBox.Core;
using DeskBox.Domain;
using DeskBox.Engineering;
using DeskBox.Engineering.Tia;
using DeskBox.Integration;

namespace DeskBox.AppServices
{
    public static class EngineeringDocuments
    {
        private const long DocumentLimit = 128L * 1024 * 1024;
        private static readonly object CacheGate = new object();
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();
        private static readonly Queue<string> Cached = new Queue<string>();

        public static EngineeringDocument Read(ProjectContext project, string path, string version, CancellationToken token,
            string encoding = "auto", string worksheet = "")
        {
            string type = SourceType(path);
            EngineeringSource source;
            var output = new EngineeringDocument();
            long limit = IsPlc(type) ? 8 * 1024 * 1024 : DocumentLimit;
            using (var stream = EngineeringFiles.Open(project, path, type, version, limit, token, out source))
            {
                output.Source = source;
                try
                {
                    if (type == "fhx") return ReadFhx(project, stream, output, encoding, token);
                    if (IsTable(type)) return TableDocument(output, EngineeringTables.Read(stream, source, worksheet, encoding, token), token);
                    return ReadPlc(stream, output, token);
                }
                catch (DecoderFallbackException) { throw new InvalidDataException("资料字符无法按指定编码解码。请核对导出编码。"); }
            }
        }

        public static string[] Headers(ProjectContext project, string path, string worksheet, string encoding, CancellationToken token)
        {
            EngineeringSource source;
            return ReadTable(project, path, "", worksheet, encoding, token, out source).Headers;
        }


        internal static EngineeringTable ReadTable(ProjectContext project, string path, string version, string sheet, string encoding,
            CancellationToken token, out EngineeringSource source)
        {
            var kind = SourceType(path);
            if (!IsTable(kind)) throw new ArgumentException("请选择 CSV、TSV 或 XLSX 工程表。");
            using (var stream = EngineeringFiles.Open(project, path, kind, version, DocumentLimit, token, out source))
                return EngineeringTables.Read(stream, source, sheet, encoding, token);
        }

        public static string Excerpt(ProjectContext project, EngineeringSource source, int firstLine, int maxLines, CancellationToken token)
        {
            if (source == null || firstLine < 1 || maxLines < 1) throw new ArgumentException("资料位置无效。");
            if (source.Type == "xlsx") throw new InvalidOperationException("XLSX 使用工作表和行号定位。");
            EngineeringSource current;
            var file = Path.Combine(project.Root, source.Document);
            using (var stream = EngineeringFiles.Open(project, file, source.Type, source.Version, DocumentLimit, token, out current))
            {
                if (current.Sha256 != source.Sha256) throw new IOException("原始文件已更改，请重新读取资料。");
                using (var reader = EngineeringFiles.Text(stream, source.Encoding, current))
                {
                    var result = new StringBuilder();
                    for (int line = 1; line < firstLine + (long)Math.Min(200, maxLines); line++)
                    {
                        token.ThrowIfCancellationRequested();
                        int ch;
                        if (reader.Peek() < 0) break;
                        if (line >= firstLine) result.Append(line).Append("  ");
                        int count = 0;
                        while ((ch = reader.Read()) >= 0 && ch != '\n' && ch != '\r')
                        {
                            if ((++count & 4095) == 0) token.ThrowIfCancellationRequested();
                            if (line >= firstLine && count <= 4096) result.Append((char)ch);
                        }
                        if (ch == '\r' && reader.Peek() == '\n') reader.Read();
                        if (line >= firstLine)
                        {
                            if (count > 4096) result.Append(" […]");
                            result.AppendLine();
                        }
                        if (result.Length > 32000) break;
                    }
                    return result.ToString();
                }
            }
        }

        public static string SourceLocation(ProjectContext project, TagSourceLink link, CancellationToken token)
        {
            if (project == null || link == null || string.IsNullOrEmpty(link.SourceDocument))
                throw new ArgumentException("未指定可定位的工程来源。");
            string path = Path.Combine(project.Root, link.SourceDocument);
            EngineeringSource current;
            using (EngineeringFiles.Open(project, path, link.SourceType, link.SourceVersion, DocumentLimit, token, out current))
                if (current.Sha256 != link.Sha256) throw new IOException("原始来源已变化，请选用对应版本。");
            current.Encoding = link.Encoding;
            var details = current.Document + " · " + link.SourceLocation + "\r\nSHA-256 " + current.Sha256 + "\r\n";
            var marker = System.Text.RegularExpressions.Regex.Match(link.SourceLocation, @"(?:^|!)L(\d+)");
            int line;
            if (link.SourceType != "xlsx" && marker.Success && int.TryParse(marker.Groups[1].Value, out line))
                details += "\r\n" + Excerpt(project, current, line, 16, token);
            return details;
        }

        public static EngineeringDiff Compare(ProjectContext project, string beforePath, string afterPath, CancellationToken token,
            string worksheet = "", string encoding = "auto", string beforeVersion = "", string afterVersion = "",
            IDictionary<string, int> beforeMapping = null, IDictionary<string, int> afterMapping = null)
        {
            string firstType = SourceType(beforePath), secondType = SourceType(afterPath);
            if (firstType != secondType && !(IsTable(firstType) && IsTable(secondType)) &&
                !((firstType == "scl" || firstType == "udt" || firstType == "db") &&
                  (secondType == "scl" || secondType == "udt" || secondType == "db")))
                throw new ArgumentException("请选择同类导出资料比较。CSV、TSV 与 XLSX 工程表可互相比较。");
            var before = ForComparison(project, beforePath, beforeVersion, token, worksheet, encoding, beforeMapping);
            var after = ForComparison(project, afterPath, afterVersion, token, worksheet, encoding, afterMapping);
            var output = CompareRecords(project.Id, before, after, token);
            return output;
        }

        private static EngineeringDocument ForComparison(ProjectContext project, string path, string version, CancellationToken token,
            string sheet, string encoding, IDictionary<string, int> mapping)
        {
            if (!IsTable(SourceType(path))) return Read(project, path, version, token, encoding, sheet);
            EngineeringSource source;
            return TableDocument(new EngineeringDocument(), ReadTable(project, path, version, sheet, encoding, token, out source), token, source, mapping);
        }

        internal static EngineeringDiff CompareRecords(string projectId, EngineeringDocument before, EngineeringDocument after, CancellationToken token)
        {
            var output = new EngineeringDiff { ProjectId = projectId, Before = before.Source, After = after.Source };
            output.Issues.AddRange(before.Issues.Select(issue => Qualified(before.Source, issue)));
            output.Issues.AddRange(after.Issues.Select(issue => Qualified(after.Source, issue)));
            var first = Index(before, token);
            var second = Index(after, token);
            output.Unclassified = before.Records.Count(row => !row.Recognized) + after.Records.Count(row => !row.Recognized);
            // Partial grammars still expose raw objects and their fields. Invalid or ambiguous graphs cannot yield a trustworthy diff.
            if (output.Issues.Any(issue => issue.Blocking)) return output;
            foreach (var key in first.Keys.Union(second.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                EngineeringRecord a, b;
                first.TryGetValue(key, out a);
                second.TryGetValue(key, out b);
                if (a == null || b == null)
                {
                    if (a == null) output.Added++; else output.Removed++;
                    Add(output, Delta(key, a == null ? "added" : "removed", "", a, b, a == null ? "" : a.Name, b == null ? "" : b.Name));
                    continue;
                }
                bool modified = false;
                foreach (var field in a.Fields.Keys.Union(b.Fields.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    string old, now;
                    a.Fields.TryGetValue(field, out old);
                    b.Fields.TryGetValue(field, out now);
                    if (string.Equals(old, now, StringComparison.Ordinal)) continue;
                    modified = true;
                    Add(output, Delta(key, "modified", field, a, b, old, now));
                }
                if (modified) output.Modified++;
            }
            return output;
        }

        public static void ExportDiff(ProjectContext project, EngineeringDiff diff, string destination, CancellationToken token)
        {
            if (project == null || diff == null || diff.ProjectId != project.Id) throw new UnauthorizedAccessException("报告超出当前工程范围。");
            if (!".json".Equals(Path.GetExtension(destination), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("差异报告应保存为 .json。");
            EngineeringFiles.Export(project, destination, stream =>
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 32768, true))
                    writer.Write(Json.ToJson(diff));
            }, token);
        }

        public static string Summarize(EngineeringDiff diff, int limit)
        {
            var text = new StringBuilder();
            text.AppendLine(diff.Before.Document + " · " + diff.Before.Sha256);
            text.AppendLine(diff.After.Document + " · " + diff.After.Sha256);
            if (diff.Issues.Any(item => item.Blocking)) text.AppendLine("存在阻塞诊断。未生成差异结论。");
            else text.AppendLine("新增 " + diff.Added + " · 删除 " + diff.Removed + " · 修改 " + diff.Modified +
                " · 未识别对象 " + diff.Unclassified + " · 省略字段明细 " + diff.Unavailable);
            foreach (var delta in diff.Changes.Take(limit))
            {
                if (text.Length > 12500) break;
                text.AppendLine(delta.State + " · " + delta.Key + " · " + delta.Field + " · " +
                    delta.BeforeLocation + " → " + delta.AfterLocation + " · " + Clip(delta.Before, 150) + " → " + Clip(delta.After, 150));
            }
            foreach (var issue in diff.Issues.Take(16)) text.AppendLine(issue.Location + " · " + issue.Message);
            if (diff.Changes.Count > limit || text.Length > 12500) text.AppendLine("其余字段明细请查看工作台导出的 JSON 报告。");
            return text.ToString();
        }

        private static EngineeringDocument ReadFhx(ProjectContext project, Stream stream, EngineeringDocument output, string encoding, CancellationToken token)
        {
            var source = output.Source;
            string key = project.Id + "|" + source.Sha256 + "|" + encoding;
            string cached;
            lock (CacheGate) Cache.TryGetValue(key, out cached);
            if (cached != null)
            {
                var previous = Json.FromJson<EngineeringDocument>(cached);
                if (previous != null)
                {
                    token.ThrowIfCancellationRequested();
                    source.Encoding = previous.Source.Encoding;
                    previous.Source = source;
                    return previous;
                }
            }
            using (var reader = EngineeringFiles.Text(stream, encoding, source)) FhxParser.Read(reader, output, token);
            CheckIdentities(output);
            if (source.Bytes <= 768 * 1024)
            {
                var json = Json.ToJson(output);
                if (json.Length < 2 * 1024 * 1024)
                    lock (CacheGate)
                        if (!Cache.ContainsKey(key))
                        {
                            Cache.Add(key, json);
                            Cached.Enqueue(key);
                            if (Cached.Count > 4) Cache.Remove(Cached.Dequeue());
                        }
            }
            return output;
        }

        private static EngineeringDocument ReadPlc(Stream stream, EngineeringDocument output, CancellationToken token)
        {
            if (output.Source.Type == "tia-diagnostics")
            {
                using (var reader = EngineeringFiles.Text(stream, "auto", output.Source))
                {
                    var lines = Json.FromJson<List<PlcDiagnostic>>(reader.ReadToEnd());
                    if (lines == null || lines.Any(item => item == null) || lines.Count > 10000)
                        throw new InvalidDataException("诊断 JSON 应为 DeskBox PlcDiagnostic 数组，最多 10000 条。");
                    for (int i = 0; i < lines.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var item = lines[i];
                        var row = new EngineeringRecord { Key = "diagnostic:" + (i + 1), Kind = "Diagnostic", Name = item.Severity,
                            Location = "JSON[" + i + "] / " + item.Source + ":" + item.Line };
                        row.Fields["Message"] = item.Message;
                        row.Fields["Column"] = item.Column.ToString(CultureInfo.InvariantCulture);
                        output.Records.Add(row);
                    }
                }
                return output;
            }
            var snapshot = new PlcSnapshot { Name = output.Source.Document };
            PlcEngineering.ReadExport(stream, Path.GetFileName(output.Source.Document), snapshot, token);
            foreach (var item in snapshot.Objects)
            {
                token.ThrowIfCancellationRequested();
                int line = SourceLine(item.Source);
                var row = new EngineeringRecord { Key = item.Identity, Kind = item.Kind, Name = item.Name,
                    Parent = item.Scope, StartLine = line, EndLine = line, Location = line == 0 ? item.Source : "L" + line };
                row.Fields["Controller"] = item.Controller;
                row.Fields["Address"] = item.Address;
                row.Fields["DataType"] = item.DataType;
                row.Fields["SignalType"] = item.Signal;
                row.Fields["Interface"] = item.Interface;
                row.Fields["References"] = string.Join(", ", item.References);
                row.Fields["Calls"] = string.Join(", ", item.Calls);
                row.Fields["Fingerprint"] = item.Fingerprint;
                if (!string.IsNullOrWhiteSpace(item.Content))
                    row.Fields["Content"] = Clip(item.Content, 8192);
                output.Records.Add(row);
            }
            output.Issues.AddRange(snapshot.Diagnostics.Select(item => new EngineeringIssue { Location = "L" + item.Line, Message = item.ToString() }));
            output.Issues.AddRange(snapshot.Notes.Take(500).Select(note => new EngineeringIssue { Location = output.Source.Document, Message = note }));
            if (output.Records.Count == 0)
                output.Issues.Add(new EngineeringIssue { Location = output.Source.Document, Message = "没有可识别的 PLC 导出对象。", Blocking = true });
            CheckIdentities(output);
            return output;
        }

        private static EngineeringDocument TableDocument(EngineeringDocument output, EngineeringTable table, CancellationToken token,
            EngineeringSource source = null, IDictionary<string, int> mapping = null)
        {
            if (source != null) output.Source = source;
            var mapped = TagCenter.Map(table.Headers, mapping);
            int name, system;
            bool identify = mapped.TryGetValue("TagName", out name);
            bool scope = mapped.TryGetValue("SystemId", out system);
            if (!identify)
                output.Issues.Add(new EngineeringIssue { Location = output.Source.Document, Message = "未映射 TagName 列，无法建立比较键。", Blocking = true });
            var assigned = new HashSet<int>(mapped.Values);
            foreach (var row in table.Rows)
            {
                token.ThrowIfCancellationRequested();
                string tag = identify ? row.Fields[EngineeringTables.Index(name)].Trim() : "";
                if (tag.Length == 0 && identify)
                    output.Issues.Add(new EngineeringIssue { Location = row.Location, Message = "TagName 为空。", Blocking = true });
                var record = new EngineeringRecord { Kind = "Tag", Name = tag, StartLine = row.StartLine, EndLine = row.EndLine,
                    Location = row.Location, Key = TagCenter.Identity(scope ? row.Fields[EngineeringTables.Index(system)] : "", tag) };
                foreach (var pair in mapped)
                    record.Fields[pair.Key] = row.Fields[EngineeringTables.Index(pair.Value)];
                for (int i = 0; i < table.Headers.Length; i++)
                    if (!assigned.Contains(i)) record.Fields["Column " + (i + 1) + " " + table.Headers[i]] = row.Fields[EngineeringTables.Index(i)];
                output.Records.Add(record);
            }
            CheckIdentities(output);
            return output;
        }

        private static Dictionary<string, EngineeringRecord> Index(EngineeringDocument doc, CancellationToken token)
        {
            var index = new Dictionary<string, EngineeringRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in doc.Records)
            {
                token.ThrowIfCancellationRequested();
                if (!index.ContainsKey(row.Key)) index.Add(row.Key, row);
            }
            return index;
        }

        private static void CheckIdentities(EngineeringDocument doc)
        {
            foreach (var group in doc.Records.GroupBy(row => row.Key, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Take(200))
                doc.Issues.Add(new EngineeringIssue { Location = string.Join(", ", group.Take(4).Select(row => row.Location)),
                    Message = "对象标识重复：" + group.Key, Blocking = true });
        }

        private static EngineeringIssue Qualified(EngineeringSource source, EngineeringIssue issue)
        {
            return new EngineeringIssue { Location = source.Document + " · " + issue.Location, Message = issue.Message, Blocking = issue.Blocking };
        }

        private static EngineeringDelta Delta(string key, string state, string field, EngineeringRecord a, EngineeringRecord b, string before, string after)
        {
            return new EngineeringDelta { Key = key, State = state, Field = field, Before = Clip(before, 4096), After = Clip(after, 4096),
                BeforeLocation = a == null ? "" : a.Location, AfterLocation = b == null ? "" : b.Location };
        }

        private static void Add(EngineeringDiff report, EngineeringDelta delta)
        {
            int characters = delta.Key.Length + delta.Field.Length + delta.Before.Length + delta.After.Length +
                delta.BeforeLocation.Length + delta.AfterLocation.Length;
            if (report.Changes.Count < 10000 && report.DetailCharacters + characters < 6 * 1024 * 1024)
            {
                report.DetailCharacters += characters;
                report.Changes.Add(delta);
            }
            else report.Unavailable++;
        }

        internal static string Clip(string value, int max) { return value == null ? "" : value.Length > max ? value.Substring(0, max) + " […]" : value; }

        private static int SourceLine(string source)
        {
            int colon = (source ?? "").LastIndexOf(':'), line;
            return colon >= 0 && int.TryParse(source.Substring(colon + 1), out line) ? line : 0;
        }

        internal static bool IsTable(string type) { return type == "csv" || type == "xlsx" || type == "tsv"; }
        internal static bool IsPlc(string type) { return type == "xml" || type == "aml" || type == "scl" || type == "udt" || type == "db" || type == "tia-diagnostics"; }

        internal static string SourceType(string path)
        {
            var ext = Path.GetExtension(path ?? "").TrimStart('.').ToLowerInvariant();
            if (ext == "json") return "tia-diagnostics";
            if (ext == "fhx" || IsTable(ext) || IsPlc(ext)) return ext;
            throw new ArgumentException("不支持该资料格式。");
        }
    }
}
