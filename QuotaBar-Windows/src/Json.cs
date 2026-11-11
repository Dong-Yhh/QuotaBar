using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace QuotaBar
{
    /// <summary>
    /// 极简 JSON 解析 / 生成。
    /// 自己实现是为了让整个程序只依赖 Windows 自带的程序集，
    /// 不引入 System.Web.Extensions / Newtonsoft.Json 等需要额外下载的东西。
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("内容为空");
            Parser parser = new Parser(text);
            object value = parser.ParseValue();
            parser.SkipWhitespace();
            return value;
        }

        // ---------- 取值辅助（点号路径，支持数组下标） ----------

        public static object Get(object node, string path)
        {
            if (node == null || string.IsNullOrEmpty(path)) return node;
            object current = node;
            string[] parts = path.Split('.');
            for (int p = 0; p < parts.Length; p++)
            {
                string key = parts[p].Trim();
                if (key.Length == 0) continue;

                Dictionary<string, object> map = current as Dictionary<string, object>;
                if (map != null)
                {
                    if (!map.ContainsKey(key)) return null;
                    current = map[key];
                    continue;
                }

                List<object> list = current as List<object>;
                if (list != null)
                {
                    int index;
                    if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) return null;
                    if (index < 0 || index >= list.Count) return null;
                    current = list[index];
                    continue;
                }
                return null;
            }
            return current;
        }

        public static Dictionary<string, object> AsObject(object node)
        {
            return node as Dictionary<string, object>;
        }

        public static List<object> AsArray(object node)
        {
            return node as List<object>;
        }

        public static string Str(object node, string path)
        {
            object value = Get(node, path);
            if (value == null) return null;
            string text = value as string;
            if (text != null) return text;
            if (value is bool) return ((bool)value) ? "true" : "false";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static double? Num(object node, string path)
        {
            object value = Get(node, path);
            if (value == null) return null;
            if (value is double) return (double)value;
            if (value is bool) return null;
            double parsed;
            if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                                NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
            return null;
        }

        public static bool? Flag(object node, string path)
        {
            object value = Get(node, path);
            if (value == null) return null;
            if (value is bool) return (bool)value;
            string text = value as string;
            if (text != null)
            {
                if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return null;
        }

        /// <summary>把可能是毫秒时间戳或 ISO8601 字符串的字段解析成本地时间。</summary>
        public static DateTime? Time(object node, string path)
        {
            object value = Get(node, path);
            if (value == null) return null;
            if (value is double)
            {
                double seconds = (double)value;
                if (seconds > 1e12) seconds = seconds / 1000.0;
                return Epoch.AddSeconds(seconds).ToLocalTime();
            }
            string text = value as string;
            if (text == null) return null;
            DateTime parsed;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                                  DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
            {
                return parsed.ToLocalTime();
            }
            return null;
        }

        public static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // ---------- 生成 ----------

        public static string Dump(object value)
        {
            StringBuilder builder = new StringBuilder();
            Write(builder, value);
            return builder.ToString();
        }

        private static void Write(StringBuilder builder, object value)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            string text = value as string;
            if (text != null)
            {
                WriteString(builder, text);
                return;
            }

            if (value is bool)
            {
                builder.Append(((bool)value) ? "true" : "false");
                return;
            }

            if (value is double)
            {
                double number = (double)value;
                if (double.IsNaN(number) || double.IsInfinity(number)) { builder.Append("null"); return; }
                builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            if (value is int || value is long)
            {
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }

            IDictionary map = value as IDictionary;
            if (map != null)
            {
                builder.Append('{');
                bool first = true;
                foreach (DictionaryEntry entry in map)
                {
                    if (!first) builder.Append(',');
                    first = false;
                    WriteString(builder, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                    builder.Append(':');
                    Write(builder, entry.Value);
                }
                builder.Append('}');
                return;
            }

            IEnumerable list = value as IEnumerable;
            if (list != null)
            {
                builder.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) builder.Append(',');
                    first = false;
                    Write(builder, item);
                }
                builder.Append(']');
                return;
            }

            WriteString(builder, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder builder, string text)
        {
            if (text == null) { builder.Append("null"); return; }
            builder.Append('"');
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
        }

        // ---------- 解析器 ----------

        private sealed class Parser
        {
            private readonly string _text;
            private int _index;

            public Parser(string text)
            {
                _text = text;
                _index = 0;
            }

            public void SkipWhitespace()
            {
                while (_index < _text.Length)
                {
                    char c = _text[_index];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _index++;
                    else break;
                }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (_index >= _text.Length) throw new FormatException("JSON 意外结束");

                char c = _text[_index];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ParseNumber();
                }
            }

            private void Expect(string word)
            {
                if (_index + word.Length > _text.Length ||
                    string.CompareOrdinal(_text, _index, word, 0, word.Length) != 0)
                {
                    throw new FormatException("位置 " + _index + " 处不是预期的 " + word);
                }
                _index += word.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> map = new Dictionary<string, object>();
                _index++; // {
                SkipWhitespace();
                if (_index < _text.Length && _text[_index] == '}') { _index++; return map; }

                while (true)
                {
                    SkipWhitespace();
                    if (_index >= _text.Length || _text[_index] != '"')
                        throw new FormatException("对象键必须是字符串（位置 " + _index + "）");
                    string key = ParseString();
                    SkipWhitespace();
                    if (_index >= _text.Length || _text[_index] != ':')
                        throw new FormatException("对象键后面缺少冒号（位置 " + _index + "）");
                    _index++;
                    map[key] = ParseValue();
                    SkipWhitespace();
                    if (_index >= _text.Length) throw new FormatException("对象没有闭合");
                    char c = _text[_index];
                    if (c == ',') { _index++; continue; }
                    if (c == '}') { _index++; return map; }
                    throw new FormatException("对象里出现意外字符 '" + c + "'（位置 " + _index + "）");
                }
            }

            private List<object> ParseArray()
            {
                List<object> list = new List<object>();
                _index++; // [
                SkipWhitespace();
                if (_index < _text.Length && _text[_index] == ']') { _index++; return list; }

                while (true)
                {
                    list.Add(ParseValue());
                    SkipWhitespace();
                    if (_index >= _text.Length) throw new FormatException("数组没有闭合");
                    char c = _text[_index];
                    if (c == ',') { _index++; continue; }
                    if (c == ']') { _index++; return list; }
                    throw new FormatException("数组里出现意外字符 '" + c + "'（位置 " + _index + "）");
                }
            }

            private string ParseString()
            {
                _index++; // 开引号
                StringBuilder builder = new StringBuilder();
                while (true)
                {
                    if (_index >= _text.Length) throw new FormatException("字符串没有闭合");
                    char c = _text[_index++];
                    if (c == '"') return builder.ToString();
                    if (c != '\\') { builder.Append(c); continue; }

                    if (_index >= _text.Length) throw new FormatException("转义字符不完整");
                    char escape = _text[_index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _text.Length) throw new FormatException("\\u 转义不完整");
                            string hex = _text.Substring(_index, 4);
                            _index += 4;
                            builder.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            break;
                        default:
                            throw new FormatException("不认识的转义 \\" + escape);
                    }
                }
            }

            private object ParseNumber()
            {
                int start = _index;
                while (_index < _text.Length)
                {
                    char c = _text[_index];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                        _index++;
                    else
                        break;
                }
                if (start == _index) throw new FormatException("位置 " + start + " 处不是合法值");

                string token = _text.Substring(start, _index - start);
                double value;
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    throw new FormatException("不是合法数字：" + token);
                return value;
            }
        }
    }
}
