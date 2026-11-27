using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DeskBox.Ai
{
    /// <summary>AI 返回的一个工具调用请求（来自 ```tool 代码块）。</summary>
    public class ToolCall
    {
        public string Name = "";
        public Dictionary<string, string> Args = new Dictionary<string, string>();
        public string RawJson = "";

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("工具：").Append(Name);
            if (Args != null && Args.Count > 0)
            {
                sb.Append("\r\n参数：");
                foreach (var kv in Args) sb.Append("\r\n  ").Append(kv.Key).Append(" = ").Append(kv.Value);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 从 AI 回复中解析工具调用块。
    /// 约定格式（对任何 OpenAI 兼容模型都可用，不依赖 function calling 支持）：
    /// ```tool
    /// {"name":"fs.search","args":{"root":"D:\\资料","keyword":"校准","limit":"10"}}
    /// ```
    /// 同一回复可包含多个块；解析失败的块被忽略并记录，不抛异常。
    /// </summary>
    public static class ToolCallParser
    {
        private static readonly Regex Block = new Regex("```tool\\s*([\\s\\S]*?)```", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<ToolCall> Parse(string content)
        {
            var list = new List<ToolCall>();
            if (string.IsNullOrEmpty(content)) return list;
            try
            {
                foreach (Match m in Block.Matches(content))
                {
                    var json = (m.Groups[1].Value ?? "").Trim();
                    if (json.Length == 0) continue;
                    var call = ParseOne(json);
                    if (call != null && !string.IsNullOrEmpty(call.Name)) list.Add(call);
                }
            }
            catch (Exception ex)
            {
                DeskBox.Core.AppLog.Write("warn", "ai", "工具调用解析失败: " + ex.Message);
            }
            return list;
        }

        internal static bool TryParseComplete(string text, out List<ToolCall> calls)
        {
            calls = new List<ToolCall>();
            var openings = Regex.Matches(text ?? "", @"(?im)^\s*```tool\b").Count;
            foreach (Match match in Block.Matches(text ?? ""))
            {
                var json = match.Groups[1].Value.Trim();
                var call = ParseOne(json);
                if (call == null || string.IsNullOrWhiteSpace(call.Name)) return false;
                calls.Add(call);
            }
            return calls.Count == openings;
        }

        internal static ToolCall ParseOne(string json)
        {
            try
            {
                var root = DeskBox.Core.Json.FromJson<Dictionary<string, object>>(json);
                if (root == null) return null;
                var call = new ToolCall { RawJson = json };
                object nameObj;
                if (!root.TryGetValue("name", out nameObj) || nameObj == null) return null;
                call.Name = Convert.ToString(nameObj).Trim();

                object argsObj;
                if (root.TryGetValue("args", out argsObj) && argsObj != null)
                {
                    var dict = argsObj as Dictionary<string, object>;
                    if (dict != null)
                    {
                        foreach (var kv in dict)
                            call.Args[kv.Key] = kv.Value is System.Collections.IDictionary || kv.Value is System.Collections.IList
                                ? DeskBox.Core.Json.ToJson(kv.Value) : kv.Value == null ? "" : Convert.ToString(kv.Value);
                    }
                }
                return call;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把工具执行结果整理成回灌给模型的文本。</summary>
        public static string BuildToolResultText(List<ToolCall> calls, List<string> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("以下是你请求的工具执行结果，请基于结果继续回答用户（不要再重复调用同样的工具）：");
            for (int i = 0; i < calls.Count && i < results.Count; i++)
            {
                sb.AppendLine();
                sb.AppendLine("[" + (i + 1) + "] " + calls[i].Name);
                sb.AppendLine(results[i] ?? "");
            }
            return sb.ToString();
        }
    }
}
