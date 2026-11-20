using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DeskBox.Engineering.Tia
{
    public sealed class PlcDiagnostic
    {
        public string Severity = "", Source = "", Message = "";
        public int Line, Column;
        public override string ToString()
        {
            return Source + (Line > 0 ? ":" + Line + ":" + Column : "") + " · " + Message;
        }
    }

    public static class SclEditing
    {
        internal static string Mask(string source, bool keepQuotedNames)
        {
            var characters = source.ToCharArray();
            for (int i = 0; i < source.Length;)
            {
                int start = i;
                char c = source[i++];
                char next = i < source.Length ? source[i] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < source.Length && source[i] != '\r' && source[i] != '\n') i++;
                }
                else if ((c == '(' || c == '/') && next == '*')
                {
                    char close = c == '(' ? ')' : '/';
                    i++;
                    int depth = 1;
                    while (i + 1 < source.Length && depth > 0)
                    {
                        if (source[i] == '*' && source[i + 1] == close) { i += 2; depth--; }
                        else if (source[i] == c && source[i + 1] == '*') { i += 2; depth++; }
                        else i++;
                    }
                    if (depth > 0) i = source.Length;
                }
                else if (c == '\'' || c == '"')
                {
                    while (i < source.Length && source[i] != '\n' && source[i] != '\r')
                    {
                        char current = source[i++];
                        if (current == '$' && i < source.Length && source[i] != '\n' && source[i] != '\r') { i++; continue; }
                        if (current != c) continue;
                        if (i >= source.Length || source[i] != c) break;
                        i++;
                    }
                    if (c == '"' && keepQuotedNames) continue;
                }
                else continue;
                for (int j = start; j < i; j++) if (source[j] != '\r' && source[j] != '\n') characters[j] = ' ';
            }
            return new string(characters);
        }

        internal static int[] LineStarts(string source)
        {
            var starts = new List<int> { 0 };
            for (int i = 0; i < source.Length; i++) if (source[i] == '\n') starts.Add(i + 1);
            return starts.ToArray();
        }

        internal static int LineIndex(int[] starts, int offset)
        {
            int line = Array.BinarySearch(starts, offset);
            return line < 0 ? ~line - 1 : line;
        }

        internal static PlcDiagnostic At(string path, string severity, int[] starts, int offset, string message)
        {
            int line = LineIndex(starts, offset);
            return new PlcDiagnostic { Source = path, Severity = severity, Line = line + 1, Column = offset - starts[line] + 1, Message = message };
        }

        public static Dictionary<int, int> Brackets(string source)
        {
            return Brackets(source, null);
        }

        internal static Dictionary<int, int> Brackets(string source, Action<int, string> issue)
        {
            var pairs = new Dictionary<int, int>();
            var stack = new Stack<int>();
            var code = Mask(source, false);
            for (int i = 0; i < code.Length; i++)
            {
                char symbol = code[i];
                if (symbol == '(' || symbol == '[' || symbol == '{') { stack.Push(i); continue; }
                int index = ")]}".IndexOf(symbol);
                if (index < 0) continue;
                if (stack.Count == 0 || code[stack.Peek()] != "([{"[index])
                {
                    if (issue != null) issue(i, "找不到配对的 " + symbol);
                    continue;
                }
                int open = stack.Pop();
                pairs[open] = i;
                pairs[i] = open;
            }
            if (issue != null)
                foreach (int open in stack) issue(open, "未闭合的 " + code[open]);
            return pairs;
        }

        public static string ReplaceLiteral(string source, string search, string replacement)
        {
            if (string.IsNullOrEmpty(search)) return source;
            return Regex.Replace(source, Regex.Escape(search), match => replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public static string Diff(string before, string after)
        {
            var a = before.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var b = after.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int start = 0, endA = a.Length, endB = b.Length;
            while (start < endA && start < endB && a[start] == b[start]) start++;
            while (endA > start && endB > start && a[endA - 1] == b[endB - 1]) { endA--; endB--; }
            if (endA == start && endB == start) return "未发现行差异。";
            int countA = endA - start, countB = endB - start;
            var result = new StringBuilder().AppendLine("--- 当前草稿").AppendLine("+++ 建议草稿");
            result.AppendLine("@@ -" + (countA == 0 ? start : start + 1) + "," + countA +
                " +" + (countB == 0 ? start : start + 1) + "," + countB + " @@");
            // Keep preview memory bounded for unusually large replacements.
            if ((long)(countA + 1) * (countB + 1) > 1000000)
            {
                foreach (string line in a.Skip(start).Take(countA)) result.AppendLine("-" + line);
                foreach (string line in b.Skip(start).Take(countB)) result.AppendLine("+" + line);
                return result.ToString();
            }
            var common = new int[countA + 1, countB + 1];
            for (int i = countA - 1; i >= 0; i--)
                for (int j = countB - 1; j >= 0; j--)
                    common[i, j] = a[start + i] == b[start + j] ? 1 + common[i + 1, j + 1] : Math.Max(common[i + 1, j], common[i, j + 1]);
            for (int i = 0, j = 0; i < countA || j < countB;)
            {
                if (i < countA && j < countB && a[start + i] == b[start + j])
                {
                    result.AppendLine(" " + a[start + i]); i++; j++;
                }
                else if (i < countA && (j == countB || common[i + 1, j] >= common[i, j + 1]))
                    result.AppendLine("-" + a[start + i++]);
                else result.AppendLine("+" + b[start + j++]);
            }
            return result.ToString();
        }
    }
}
