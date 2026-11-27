using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using DeskBox.Core;

namespace DeskBox.Integration
{
    // The Agent accepts a bounded subset of the advertised JSON Schema. Unsupported assertions fail closed.
    internal static class McpInputSchema
    {
        private static readonly HashSet<string> Supported = new HashSet<string>
        {
            "$schema", "title", "description", "default", "examples", "deprecated", "readOnly", "writeOnly",
            "type", "properties", "required", "additionalProperties", "items", "enum", "const",
            "minLength", "maxLength", "minItems", "maxItems", "minProperties", "maxProperties",
            "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "pattern", "anyOf", "oneOf", "allOf"
        };
        private static readonly HashSet<string> Types = new HashSet<string> { "object", "array", "string", "integer", "number", "boolean", "null" };

        internal static bool CanValidate(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 16000) return false;
            var schema = Json.FromJson<Dictionary<string, object>>(json);
            return schema != null && McpProtocol.ReadString(schema, "type") == "object" && ValidSchema(schema, 0);
        }

        internal static void Require(string json, Dictionary<string, object> arguments)
        {
            if (!CanValidate(json)) throw new McpFault("schema", "该工具的输入 Schema 超出 Agent 支持的验证范围。");
            string error = Validate(Json.FromJson<Dictionary<string, object>>(json), arguments, "$", 0, Stopwatch.StartNew());
            if (error != null) throw new McpFault("arguments", "工具参数与输入 Schema 不符：" + error);
        }

        private static bool ValidSchema(Dictionary<string, object> schema, int depth)
        {
            if (depth > 24 || schema.Keys.Any(key => !Supported.Contains(key))) return false;
            foreach (var pair in schema)
            {
                switch (pair.Key)
                {
                    case "type":
                        var name = pair.Value as string;
                        var names = pair.Value as IList;
                        if (name == null ? names == null || names.Count == 0 || names.Cast<object>().Any(item => !(item is string) || !Types.Contains((string)item)) : !Types.Contains(name))
                            return false;
                        break;
                    case "properties":
                        var props = pair.Value as Dictionary<string, object>;
                        if (props == null || props.Values.Any(value => !ValidNode(value, depth + 1))) return false;
                        break;
                    case "required":
                        var required = pair.Value as IList;
                        if (required == null || required.Cast<object>().Any(item => !(item is string))) return false;
                        break;
                    case "additionalProperties":
                        if (!(pair.Value is bool) && !ValidNode(pair.Value, depth + 1)) return false;
                        break;
                    case "items":
                        if (!ValidNode(pair.Value, depth + 1)) return false;
                        break;
                    case "enum":
                        var allowed = pair.Value as IList;
                        if (allowed == null || allowed.Count == 0) return false;
                        break;
                    case "anyOf": case "oneOf": case "allOf":
                        var choices = pair.Value as IList;
                        if (choices == null || choices.Count == 0 || choices.Count > 16 || choices.Cast<object>().Any(value => !ValidNode(value, depth + 1))) return false;
                        break;
                    case "pattern":
                        var pattern = pair.Value as string;
                        if (pattern == null || pattern.Length > 512) return false;
                        try { new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)); }
                        catch (ArgumentException) { return false; }
                        break;
                    case "minLength": case "maxLength": case "minItems": case "maxItems": case "minProperties": case "maxProperties":
                        if (!(pair.Value is int) || (int)pair.Value < 0) return false;
                        break;
                    case "minimum": case "maximum": case "exclusiveMinimum": case "exclusiveMaximum":
                        decimal valueAsNumber;
                        if (!Number(pair.Value, out valueAsNumber)) return false;
                        break;
                }
            }
            return true;
        }

        private static bool ValidNode(object node, int depth)
        {
            var schema = node as Dictionary<string, object>;
            return schema != null && ValidSchema(schema, depth);
        }

        private static string Validate(Dictionary<string, object> schema, object value, string at, int depth, Stopwatch timeout)
        {
            if (timeout.ElapsedMilliseconds > 500) throw new McpFault("arguments", "参数验证超过时间预算。");
            if (depth > 24) return at + " 超过允许的嵌套层数";
            object rule;
            if (schema.TryGetValue("type", out rule))
            {
                var types = rule as IList;
                if (types == null ? !MatchesType((string)rule, value) : !types.Cast<string>().Any(type => MatchesType(type, value)))
                    return at + " 类型无效";
            }
            if (schema.TryGetValue("enum", out rule) && !((IList)rule).Cast<object>().Any(item => Same(item, value)) ||
                schema.TryGetValue("const", out rule) && !Same(rule, value)) return at + " 不在允许值内";
            foreach (var op in new[] { "anyOf", "oneOf", "allOf" })
            {
                if (!schema.TryGetValue(op, out rule)) continue;
                var options = ((IList)rule).Cast<Dictionary<string, object>>().ToArray();
                int matched = options.Count(option => Validate(option, value, at, depth + 1, timeout) == null);
                if (op == "anyOf" ? matched == 0 : op == "oneOf" ? matched != 1 : matched != options.Length) return at + " 不符合 " + op;
            }
            var map = value as Dictionary<string, object>;
            if (map != null)
            {
                if (!InBounds(map.Count, schema, "minProperties", "maxProperties")) return at + " 字段数量无效";
                if (schema.TryGetValue("required", out rule))
                    foreach (var key in ((IList)rule).Cast<string>())
                        if (!map.ContainsKey(key)) return at + " 缺少字段 " + key;
                var properties = McpProtocol.Object(schema, "properties");
                foreach (var pair in map)
                {
                    object field;
                    var next = at + "." + pair.Key;
                    if (properties == null || !properties.TryGetValue(pair.Key, out field))
                    {
                        field = McpProtocol.Value(schema, "additionalProperties");
                        if (Equals(field, true)) continue;
                        if (field == null || Equals(field, false))
                        {
                            if (properties == null && field == null) continue;
                            return next + " 字段未经 Schema 声明";
                        }
                    }
                    var error = Validate((Dictionary<string, object>)field, pair.Value, next, depth + 1, timeout);
                    if (error != null) return error;
                }
            }
            var list = value as IList;
            if (list != null)
            {
                if (!InBounds(list.Count, schema, "minItems", "maxItems")) return at + " 数组长度无效";
                var items = McpProtocol.Object(schema, "items");
                if (items != null)
                    for (int i = 0; i < list.Count; i++)
                    {
                        var error = Validate(items, list[i], at + "[" + i + "]", depth + 1, timeout);
                        if (error != null) return error;
                    }
            }
            var text = value as string;
            if (text != null)
            {
                int length = text.Length - text.Count(char.IsLowSurrogate);
                if (!InBounds(length, schema, "minLength", "maxLength")) return at + " 文本长度无效";
                var pattern = McpProtocol.ReadString(schema, "pattern");
                if (pattern != null)
                    try { if (!Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))) return at + " 不符合格式"; }
                    catch (RegexMatchTimeoutException) { return at + " 格式校验超时"; }
            }
            decimal number, bound;
            if (Number(value, out number))
            {
                if (Number(McpProtocol.Value(schema, "minimum"), out bound) && number < bound ||
                    Number(McpProtocol.Value(schema, "maximum"), out bound) && number > bound ||
                    Number(McpProtocol.Value(schema, "exclusiveMinimum"), out bound) && number <= bound ||
                    Number(McpProtocol.Value(schema, "exclusiveMaximum"), out bound) && number >= bound) return at + " 数值超出范围";
            }
            return null;
        }

        private static bool InBounds(int size, Dictionary<string, object> schema, string minimum, string maximum)
        {
            var min = McpProtocol.Value(schema, minimum) as int?;
            var max = McpProtocol.Value(schema, maximum) as int?;
            return (!min.HasValue || size >= min) && (!max.HasValue || size <= max);
        }

        private static bool Same(object a, object b)
        {
            decimal left, right;
            if (Number(a, out left) && Number(b, out right)) return left == right;
            var first = a as Dictionary<string, object>;
            var second = b as Dictionary<string, object>;
            if (first != null && second != null)
                return first.Count == second.Count && first.All(pair => second.ContainsKey(pair.Key) && Same(pair.Value, second[pair.Key]));
            var one = a as IList;
            var two = b as IList;
            if (one != null && two != null) return one.Count == two.Count && Enumerable.Range(0, one.Count).All(i => Same(one[i], two[i]));
            return Equals(a, b);
        }

        private static bool MatchesType(string type, object value)
        {
            decimal number;
            switch (type)
            {
                case "object": return value is Dictionary<string, object>;
                case "array": return value is IList;
                case "string": return value is string;
                case "boolean": return value is bool;
                case "null": return value == null;
                case "integer": return Number(value, out number) && number == decimal.Truncate(number);
                case "number": return Number(value, out number);
                default: return false;
            }
        }

        private static bool Number(object value, out decimal number)
        {
            number = 0;
            return (value is int || value is long || value is decimal || value is double) &&
                decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }
    }
}
