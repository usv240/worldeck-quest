using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Worldeck.Core
{
    /// <summary>
    /// A small, strict JSON reader and writer for recipes, saved programs, the AI's replies and the play
    /// log. The engine-free core can't use Unity's JsonUtility, and the AI's output must be read
    /// defensively: anything malformed comes back as null, never an exception thrown into a frame.
    /// Objects are Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double, plus string, bool, null.
    /// </summary>
    public static class Json
    {
        public const int MaxDepth = 32;
        public const int MaxLength = 64 * 1024;

        /// <summary>Parses <paramref name="text"/>; null if it is not valid JSON (or too long or deep).</summary>
        public static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > MaxLength) return null;
            var p = new Parser(text);
            try
            {
                p.SkipWs();
                object v = p.Value(0);
                p.SkipWs();
                return p.AtEnd ? v : null;
            }
            catch (FormatException) { return null; }
        }

        /// <summary>
        /// The first complete JSON object inside <paramref name="text"/> (models sometimes wrap JSON in prose
        /// or a code fence). Null if none parses.
        /// </summary>
        public static Dictionary<string, object> FirstObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (Parse(text) is Dictionary<string, object> whole) return whole;
            for (int start = text.IndexOf('{'); start >= 0; start = text.IndexOf('{', start + 1))
            {
                int depth = 0; bool inStr = false, esc = false;
                for (int i = start; i < text.Length; i++)
                {
                    char c = text[i];
                    if (inStr) { if (esc) esc = false; else if (c == '\\') esc = true; else if (c == '"') inStr = false; continue; }
                    if (c == '"') inStr = true;
                    else if (c == '{') depth++;
                    else if (c == '}' && --depth == 0)
                    {
                        if (Parse(text.Substring(start, i - start + 1)) is Dictionary<string, object> obj) return obj;
                        break;
                    }
                }
            }
            return null;
        }

        // ---------- typed helpers for reading untrusted objects ----------

        public static string Str(Dictionary<string, object> o, string key, string fallback = "")
            => o != null && o.TryGetValue(key, out var v) && v is string s ? s : fallback;

        public static double Num(Dictionary<string, object> o, string key, double fallback)
            => o != null && o.TryGetValue(key, out var v) && v is double d && !double.IsNaN(d) && !double.IsInfinity(d) ? d : fallback;

        public static bool Bool(Dictionary<string, object> o, string key, bool fallback)
            => o != null && o.TryGetValue(key, out var v) && v is bool b ? b : fallback;

        public static Dictionary<string, object> Obj(Dictionary<string, object> o, string key)
            => o != null && o.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        public static List<object> Arr(Dictionary<string, object> o, string key)
            => o != null && o.TryGetValue(key, out var v) ? v as List<object> : null;

        /// <summary>The strings in an array (non-strings skipped), at most <paramref name="max"/>.</summary>
        public static List<string> Strings(Dictionary<string, object> o, string key, int max = 16)
        {
            var list = new List<string>();
            var a = Arr(o, key);
            if (a == null) return list;
            foreach (var v in a)
            {
                if (list.Count >= max) break;
                if (v is string s) list.Add(s);
            }
            return list;
        }

        // ---------- writing ----------

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v, int depth)
        {
            if (depth > MaxDepth) { sb.Append("null"); return; }
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: Quote(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: Number(sb, d); break;
                case float f: Number(sb, f); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> obj:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in obj)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Quote(sb, kv.Key);
                        sb.Append(':');
                        WriteValue(sb, kv.Value, depth + 1);
                    }
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    bool f0 = true;
                    foreach (var item in list)
                    {
                        if (!f0) sb.Append(',');
                        f0 = false;
                        WriteValue(sb, item, depth + 1);
                    }
                    sb.Append(']');
                    break;
                default: Quote(sb, v.ToString()); break;
            }
        }

        static void Number(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(Math.Round(d, 4).ToString("0.####", CultureInfo.InvariantCulture));
        }

        public static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------- parser ----------

        sealed class Parser
        {
            readonly string s;
            int i;
            public Parser(string text) { s = text; }
            public bool AtEnd => i >= s.Length;

            public void SkipWs() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            char Peek() => i < s.Length ? s[i] : '\0';

            public object Value(int depth)
            {
                if (depth > MaxDepth) throw new FormatException("too deep");
                SkipWs();
                char c = Peek();
                switch (c)
                {
                    case '{': return Object(depth);
                    case '[': return Array(depth);
                    case '"': return String();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return Number();
                        throw new FormatException("unexpected " + c);
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException("expected " + word);
                i += word.Length;
            }

            Dictionary<string, object> Object(int depth)
            {
                var o = new Dictionary<string, object>();
                i++;
                SkipWs();
                if (Peek() == '}') { i++; return o; }
                while (true)
                {
                    SkipWs();
                    if (Peek() != '"') throw new FormatException("key");
                    string key = String();
                    SkipWs();
                    if (Peek() != ':') throw new FormatException(":");
                    i++;
                    o[key] = Value(depth + 1);
                    SkipWs();
                    char c = Peek();
                    i++;
                    if (c == ',') continue;
                    if (c == '}') return o;
                    throw new FormatException("object");
                }
            }

            List<object> Array(int depth)
            {
                var a = new List<object>();
                i++;
                SkipWs();
                if (Peek() == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(depth + 1));
                    SkipWs();
                    char c = Peek();
                    i++;
                    if (c == ',') continue;
                    if (c == ']') return a;
                    throw new FormatException("array");
                }
            }

            string String()
            {
                var sb = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= s.Length) throw new FormatException("unterminated");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) throw new FormatException("escape");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw new FormatException("\\u");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw new FormatException("escape " + e);
                    }
                }
            }

            double Number()
            {
                int start = i;
                if (Peek() == '-') i++;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
                if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw new FormatException("number");
                return d;
            }
        }
    }
}
