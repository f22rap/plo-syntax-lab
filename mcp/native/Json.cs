using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PloNativeMcp {
    // Retain number tokens (including request IDs) without floating point loss.
    public sealed class JsonNumber {
        public readonly string Text;
        public JsonNumber(string text) {
            if (!Regex.IsMatch(text, @"\A-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?\z")) throw new FormatException("Invalid JSON number.");
            Text = text;
        }
    }
    public static class Json {
        public const int MaxBytes = 4 * 1024 * 1024;
        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static object Parse(string text) {
            return ParseBounded(text, MaxBytes, 100000);
        }
        // Trusted bridge results can contain the full 16 MiB GUI workspace.
        // This does not enlarge the untrusted JSON-RPC request limit.
        internal static object ParseResult(string text) { return ParseBounded(text, 32 * 1024 * 1024, 2000000); }
        static object ParseBounded(string text, int maxBytes, int maxValues) {
            if (Utf8.GetByteCount(text) > maxBytes) throw new FormatException("JSON exceeds the size limit.");
            return new Parser(text, maxValues).Parse();
        }
        // Strict JSON, with no type materialization or permissive trailing commas.
        // .NET Framework's JSON-to-XML reader accepts trailing commas and treats
        // __type specially, so it cannot be used as the JSON-RPC envelope parser.
        sealed class Parser {
            readonly string text; readonly int maxValues; int pos, values;
            public Parser(string text, int maxValues) { this.text = text; this.maxValues = maxValues; }
            void Space() { while (pos < text.Length && (text[pos] == ' ' || text[pos] == '\t' || text[pos] == '\r' || text[pos] == '\n')) pos++; }
            bool Take(char c) { if (pos < text.Length && text[pos] == c) { pos++; return true; } return false; }
            void Need(char c) { if (!Take(c)) throw new FormatException("Invalid JSON punctuation."); }
            static bool Digit(char c) { return c >= '0' && c <= '9'; }
            public object Parse() { var value = Value(0); Space(); if (pos != text.Length) throw new FormatException("Trailing JSON content."); return value; }
            object Value(int depth) {
                if (depth > 64 || ++values > maxValues) throw new FormatException("JSON nesting or value limit exceeded.");
                Space(); if (pos == text.Length) throw new FormatException("Missing JSON value.");
                char c = text[pos];
                if (c == '"') return String();
                if (c == '{') {
                    pos++; Space(); var obj = Obj(); if (Take('}')) return obj;
                    while (true) {
                        Space(); string key = String(); Space(); Need(':');
                        if (obj.ContainsKey(key)) throw new FormatException("Duplicate JSON property: " + key);
                        obj.Add(key, Value(depth + 1)); Space(); if (Take('}')) return obj; Need(',');
                    }
                }
                if (c == '[') {
                    pos++; Space(); var list = new List<object>(); if (Take(']')) return list.ToArray();
                    while (true) { list.Add(Value(depth + 1)); Space(); if (Take(']')) return list.ToArray(); Need(','); }
                }
                if (c == 't') { Literal("true"); return true; }
                if (c == 'f') { Literal("false"); return false; }
                if (c == 'n') { Literal("null"); return null; }
                if (c == '-' || Digit(c)) return Number();
                throw new FormatException("Invalid JSON value.");
            }
            void Literal(string word) {
                if (pos + word.Length > text.Length || string.CompareOrdinal(text, pos, word, 0, word.Length) != 0) throw new FormatException("Invalid JSON literal.");
                pos += word.Length;
            }
            object Number() {
                int start = pos; Take('-');
                if (!Take('0')) { if (pos == text.Length || text[pos] < '1' || text[pos] > '9') throw new FormatException("Invalid JSON number."); while (pos < text.Length && Digit(text[pos])) pos++; }
                if (Take('.')) { int part = pos; while (pos < text.Length && Digit(text[pos])) pos++; if (pos == part) throw new FormatException("Missing fraction."); }
                if (Take('e') || Take('E')) { if (!Take('+')) Take('-'); int part = pos; while (pos < text.Length && Digit(text[pos])) pos++; if (pos == part) throw new FormatException("Missing exponent."); }
                return new JsonNumber(text.Substring(start, pos - start));
            }
            string String() {
                Need('"'); var b = new StringBuilder();
                while (pos < text.Length) {
                    char c = text[pos++];
                    if (c == '"') { var s = b.ToString(); Utf8.GetByteCount(s); return s; }
                    if (c < 32) throw new FormatException("Unescaped control character.");
                    if (c != '\\') { b.Append(c); continue; }
                    if (pos == text.Length) throw new FormatException("Missing escape character.");
                    c = text[pos++];
                    switch (c) {
                        case '"': case '\\': case '/': b.Append(c); break;
                        case 'b': b.Append('\b'); break;
                        case 'f': b.Append('\f'); break;
                        case 'n': b.Append('\n'); break;
                        case 'r': b.Append('\r'); break;
                        case 't': b.Append('\t'); break;
                        case 'u':
                            ushort number;
                            if (pos + 4 > text.Length || !ushort.TryParse(text.Substring(pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out number)) throw new FormatException("Invalid Unicode escape.");
                            b.Append((char)number); pos += 4; break;
                        default: throw new FormatException("Invalid JSON escape.");
                    }
                }
                throw new FormatException("Unterminated JSON string.");
            }
        }
        public static Dictionary<string, object> Obj(params object[] pairs) {
            if (pairs.Length % 2 != 0) throw new ArgumentException("Expected key/value pairs.");
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
            return result;
        }
        public static Dictionary<string, object> Object(object value, string name) {
            var obj = value as Dictionary<string, object>;
            if (obj == null) throw new ArgumentException(name + " must be an object."); return obj;
        }
        public static object Get(Dictionary<string, object> obj, string key) { object value; return obj.TryGetValue(key, out value) ? value : null; }
        public static string String(object value, string name, int max = 4096) {
            var s = value as string;
            if (string.IsNullOrEmpty(s) || s.Length > max || s.IndexOf('\0') >= 0) throw new ArgumentException(name + " must be a nonempty string."); return s;
        }
        public static long Integer(object value, string name, long min, long max) {
            decimal n; var number = value as JsonNumber;
            var raw = number != null ? number.Text : Convert.ToString(value, CultureInfo.InvariantCulture);
            if (value == null || value is string || value is bool || !decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out n) || n != decimal.Truncate(n) || n < min || n > max)
                throw new ArgumentException(name + " is outside the allowed integer range.");
            return (long)n;
        }
        public static void Keys(Dictionary<string, object> obj, params string[] allowed) {
            var keys = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach (var key in obj.Keys) if (!keys.Contains(key)) throw new ArgumentException("Unknown property: " + key);
        }
        public static string Write(object value) { var b = new StringBuilder(); Append(b, value); return b.ToString(); }
        static void Append(StringBuilder b, object value) {
            if (value == null) { b.Append("null"); return; }
            if (value is string) { Quote(b, (string)value); return; }
            if (value is bool) { b.Append((bool)value ? "true" : "false"); return; }
            var number = value as JsonNumber; if (number != null) { b.Append(number.Text); return; }
            var obj = value as IDictionary<string, object>;
            if (obj != null) {
                b.Append('{'); bool first = true;
                foreach (var kv in obj) { if (!first) b.Append(','); first = false; Quote(b, kv.Key); b.Append(':'); Append(b, kv.Value); }
                b.Append('}'); return;
            }
            var list = value as IEnumerable;
            if (list != null) { b.Append('['); bool first = true; foreach (var item in list) { if (!first) b.Append(','); first = false; Append(b, item); } b.Append(']'); return; }
            if (value is int || value is long || value is uint || value is decimal) { b.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
            throw new ArgumentException("Unsupported JSON value type: " + value.GetType().Name);
        }
        static void Quote(StringBuilder b, string s) {
            b.Append('"');
            foreach (char c in s) {
                switch (c) {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default: if (c < 32 || char.IsSurrogate(c)) b.Append("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture)); else b.Append(c); break;
                }
            }
            b.Append('"');
        }
    }
}
