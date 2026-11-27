using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DeskBox.Domain;

namespace DeskBox.Integration
{
    // Parses a supported FHX export grammar: statements/assignments, named nested blocks and source spans.
    internal sealed class FhxParser
    {
        private enum Kind { Text, Quoted, Equals, Open, Close, Break, End }
        private sealed class Token
        {
            internal Kind Kind;
            internal string Text;
            internal int Line, Column;
            internal string Position { get { return "L" + Line + ":" + Column; } }
        }

        private static readonly Dictionary<string, string> ObjectKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"MODULE", "Module"}, {"MODULE_INSTANCE", "Module"}, {"MODULE_DEFINITION", "ModuleDefinition"},
            {"MODULE_CLASS", "ModuleClass"}, {"MODULE_CLASS_DEFINITION", "ModuleClass"},
            {"PARAMETER", "Parameter"}, {"ATTRIBUTE_INSTANCE", "Parameter"}, {"ATTRIBUTE_DEFINITION", "ParameterDefinition"},
            {"ALARM", "Alarm"}, {"ALARM_DEFINITION", "Alarm"}, {"ALARM_INSTANCE", "Alarm"},
            {"AREA", "Area"}, {"AREA_DEFINITION", "Area"}, {"PHASE", "Phase"}, {"PHASE_CLASS", "Phase"},
            {"PHASE_DEFINITION", "Phase"}, {"RECIPE", "Recipe"}, {"RECIPE_DEFINITION", "Recipe"},
            {"CONTROL_RECIPE", "Recipe"}, {"UNIT_CLASS", "UnitClass"},
            {"TAG", "Tag"}, {"TAG_DEFINITION", "Tag"}
        };

        private readonly TextReader reader;
        private readonly CancellationToken token;
        private readonly EngineeringDocument document;
        private readonly Dictionary<string, int> anonymous = new Dictionary<string, int>(StringComparer.Ordinal);
        private int line = 1, column = 1, chars, fieldCharacters, structured;
        private Token current;

        private FhxParser(TextReader reader, EngineeringDocument result, CancellationToken token)
        {
            this.reader = reader;
            document = result;
            this.token = token;
            current = Next();
        }

        internal static void Read(TextReader reader, EngineeringDocument result, CancellationToken token)
        {
            var parser = new FhxParser(reader, result, token);
            parser.Block(null, 0);
            if (parser.structured == 0) parser.Issue("L1:1", "未找到带花括号的 FHX 对象。", true);
        }

        private void Block(EngineeringRecord parent, int depth)
        {
            if (depth > 96) throw new InvalidDataException("FHX 对象嵌套超过 96 层。");
            while (current.Kind != Kind.End)
            {
                token.ThrowIfCancellationRequested();
                if (current.Kind == Kind.Break) { Advance(); continue; }
                if (current.Kind == Kind.Close)
                {
                    var close = current;
                    Advance();
                    if (parent != null)
                    {
                        parent.EndLine = close.Line;
                        parent.Location += "–L" + close.Line + ":" + close.Column;
                        return;
                    }
                    Issue(close.Position, "没有对应起始对象的右花括号。", true);
                    continue;
                }
                var start = current;
                var header = new List<Token>();
                while (current.Kind == Kind.Text || current.Kind == Kind.Quoted || current.Kind == Kind.Equals)
                {
                    if (header.Count >= 2048) throw new InvalidDataException("FHX 语句过长，位置 " + start.Position + "。");
                    header.Add(current);
                    Advance();
                }
                while (current.Kind == Kind.Break) Advance();
                if (current.Kind == Kind.Open)
                {
                    Advance();
                    structured++;
                    var item = MakeRecord(header, start, parent);
                    document.Records.Add(item);
                    if (document.Records.Count > 50000) throw new InvalidDataException("FHX 对象超过 50000。");
                    Block(item, depth + 1);
                }
                else if (header.Count != 0)
                {
                    if (parent == null)
                    {
                        var record = MakeRecord(header, start, null, true);
                        record.EndLine = header.Last().Line;
                        document.Records.Add(record);
                        if (document.Records.Count > 50000) throw new InvalidDataException("FHX 对象超过 50000。");
                        Issue(start.Position, "顶层语句已保留，未识别为对象。", false);
                    }
                    else
                    {
                        var field = ParseFields(header, 0, parent.Fields);
                        if (field != "")
                        {
                            Put(parent.Fields, "$statement", field);
                            Issue(start.Position, "对象包含未识别语句，已保留。", false);
                        }
                    }
                }
            }
            if (parent != null)
            {
                parent.EndLine = line;
                Issue(parent.Location, "对象在文件结束前缺少右花括号。", true);
            }
        }

        private EngineeringRecord MakeRecord(List<Token> header, Token start, EngineeringRecord parent, bool loose = false)
        {
            string rawKind = header.Count == 0 ? "$anonymous" : header[0].Text;
            string kind = rawKind;
            bool recognized = !loose && ObjectKinds.TryGetValue(rawKind, out kind);
            if (!recognized) kind = loose ? "$statement" : rawKind;
            var row = new EngineeringRecord { Kind = kind, Parent = parent == null ? "" : parent.Key,
                Recognized = recognized, Location = start.Position, StartLine = start.Line, EndLine = start.Line };
            var positional = ParseFields(header, loose ? 0 : 1, row.Fields);
            string name;
            if (!row.Fields.TryGetValue("NAME", out name)) row.Fields.TryGetValue("TAG_NAME", out name);
            if (string.IsNullOrWhiteSpace(name) && positional != "") name = positional;
            row.Name = name ?? "";
            if (positional.Length != 0) row.Fields["$header"] = positional;
            row.Fields["$object"] = rawKind;
            string stem = row.Parent + "/" + Escape(rawKind.ToUpperInvariant()) + ":";
            if (stem.Length > 12000) throw new InvalidDataException("FHX 对象路径过长。");
            if (row.Name.Length == 0 || loose)
            {
                int index;
                anonymous.TryGetValue(stem, out index);
                anonymous[stem] = index + 1;
                row.Key = stem + "#" + (index + 1).ToString(CultureInfo.InvariantCulture);
                if (recognized) Issue(start.Position, "识别到对象类型，但未找到名称。将按匿名对象保留。", false);
            }
            else row.Key = stem + Escape(row.Name);
            if (header.Count == 0) Issue(start.Position, "匿名花括号块已保留。", false);
            return row;
        }

        private string ParseFields(List<Token> input, int first, Dictionary<string, string> fields)
        {
            var extra = new List<string>();
            for (int i = first; i < input.Count;)
            {
                if (i + 2 < input.Count && input[i].Kind == Kind.Text && input[i + 1].Kind == Kind.Equals
                    && (input[i + 2].Kind == Kind.Text || input[i + 2].Kind == Kind.Quoted))
                {
                    Put(fields, input[i].Text, input[i + 2].Text);
                    i += 3;
                }
                else extra.Add(input[i++].Text);
            }
            return string.Join(" ", extra);
        }

        private void Put(Dictionary<string, string> fields, string key, string value)
        {
            fieldCharacters += key.Length + value.Length;
            if (fieldCharacters > 24 * 1024 * 1024 || fields.Count > 2048)
                throw new InvalidDataException("FHX 解析后的字段超过限额。");
            string original = key;
            int part = 1;
            while (fields.ContainsKey(key)) key = original + "[" + (++part) + "]";
            fields.Add(key, value);
        }

        private static string Escape(string value) { return value.Replace("%", "%25").Replace("/", "%2F").Replace(":", "%3A").Replace("#", "%23"); }

        private void Issue(string at, string message, bool blocking)
        {
            if (document.Issues.Count == 4999)
                document.Issues.Add(new EngineeringIssue { Location = at, Message = "更多诊断已省略。", Blocking = blocking });
            else if (document.Issues.Count < 4999)
                document.Issues.Add(new EngineeringIssue { Location = at, Message = message, Blocking = blocking });
            else if (blocking) document.Issues[4999].Blocking = true;
        }

        private void Advance() { current = Next(); }
        private int Read()
        {
            int c = reader.Read();
            if ((++chars & 4095) == 0) token.ThrowIfCancellationRequested();
            if (c == '\r') { if (reader.Peek() == '\n') reader.Read(); c = '\n'; }
            if (c == '\n') { line++; column = 1; }
            else if (c >= 0) column++;
            return c;
        }

        private Token Next()
        {
            while (true)
            {
                int at = line, col = column, c = Read();
                if (c == ' ' || c == '\t' || c == '\r') continue;
                var next = new Token { Line = at, Column = col, Text = c < 0 ? "" : ((char)c).ToString() };
                switch (c)
                {
                    case -1: next.Kind = Kind.End; return next;
                    case '\n': case ';': next.Kind = Kind.Break; return next;
                    case '{': next.Kind = Kind.Open; return next;
                    case '}': next.Kind = Kind.Close; return next;
                    case '=': next.Kind = Kind.Equals; return next;
                    case '"': return Quoted(next);
                    case '/':
                        if (reader.Peek() == '/')
                        {
                            while (reader.Peek() >= 0 && reader.Peek() != '\n' && reader.Peek() != '\r') Read();
                            continue;
                        }
                        if (reader.Peek() == '*')
                        {
                            Read();
                            bool closed = false;
                            int comment;
                            while ((comment = Read()) >= 0)
                                if (comment == '*' && reader.Peek() == '/') { Read(); closed = true; break; }
                            if (!closed) Issue(next.Position, "多行注释未闭合。", true);
                            continue;
                        }
                        break;
                }
                var text = new StringBuilder(next.Text);
                while ((c = reader.Peek()) >= 0 && !char.IsWhiteSpace((char)c) && "{}=;\"".IndexOf((char)c) < 0)
                {
                    if (text.Length >= 65536) throw new InvalidDataException("FHX 单个词元超过限额。");
                    text.Append((char)Read());
                }
                next.Text = text.ToString();
                return next;
            }
        }

        private Token Quoted(Token start)
        {
            var text = new StringBuilder();
            int c;
            while ((c = Read()) >= 0)
            {
                if (text.Length >= 65536) throw new InvalidDataException("FHX 引号值超过限额。");
                if (c == '"')
                {
                    if (reader.Peek() == '"') { Read(); text.Append('"'); continue; }
                    start.Kind = Kind.Quoted;
                    start.Text = text.ToString();
                    return start;
                }
                if (c == '\\' && (reader.Peek() == '"' || reader.Peek() == '\\')) c = Read();
                text.Append((char)c);
            }
            Issue(start.Position, "引号值未闭合。", true);
            start.Kind = Kind.Quoted;
            start.Text = text.ToString();
            return start;
        }
    }
}
