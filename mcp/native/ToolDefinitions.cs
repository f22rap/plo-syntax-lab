using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PloNativeMcp {
    // Deliberately limited to the schema vocabulary generated from the shared
    // research tools. Unsupported schema keywords fail at startup, not open.
    public sealed class ToolDefinitions {
        public readonly object[] Tools;
        public readonly string Instructions;
        readonly Dictionary<string, Dictionary<string, object>> schemas = new Dictionary<string, Dictionary<string, object>>();
        public ToolDefinitions() {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PloNativeMcp.ToolDefinitions.json")) {
                if (stream == null) throw new IOException("Embedded tool definitions are missing.");
                using (var reader = new StreamReader(stream, Json.Utf8)) {
                    var root = Json.Object(Json.Parse(reader.ReadToEnd()), "definitions");
                    Instructions = (string)root["instructions"]; Tools = (object[])root["tools"];
                }
            }
            foreach (var t in Tools) {
                var tool = Json.Object(t, "tool"); var schema = Json.Object(tool["inputSchema"], "schema");
                CheckSchema(schema); schemas.Add((string)tool["name"], schema);
            }
        }
        static void CheckSchema(Dictionary<string, object> s) {
            Json.Keys(s, "$schema", "type", "properties", "required", "additionalProperties", "items", "minItems", "maxItems", "minLength", "maxLength", "minimum", "maximum", "enum", "const", "pattern", "format", "default", "propertyNames", "anyOf");
            if(s.ContainsKey("anyOf")){foreach(var branch in (object[])s["anyOf"])CheckSchema(Json.Object(branch,"anyOf"));return;}
            if (!new[] { "object", "array", "string", "integer", "number", "boolean" }.Contains(Json.Get(s, "type") as string)) throw new ArgumentException("Unsupported schema type.");
            if (s.ContainsKey("properties")) foreach (var x in Json.Object(s["properties"], "properties").Values) CheckSchema(Json.Object(x, "property"));
            foreach (var key in new[] { "items", "additionalProperties", "propertyNames" }) if (Json.Get(s, key) is Dictionary<string, object>) CheckSchema(Json.Object(s[key], key));
            if (s.ContainsKey("format") && (string)s["format"] != "uuid") throw new ArgumentException("Unsupported schema format.");
        }
        public bool Contains(string name) { return schemas.ContainsKey(name); }
        public Dictionary<string, object> Arguments(string name, object args) { return Json.Object(Validate(schemas[name], args, name), "arguments"); }
        static long Bound(Dictionary<string, object> s, string key, long otherwise) { return s.ContainsKey(key) ? Json.Integer(s[key], key, -9007199254740991, 9007199254740991) : otherwise; }
        static void Need(bool ok, string path) { if (!ok) throw new ArgumentException("Invalid tool argument: " + path); }
        static object Validate(Dictionary<string, object> s, object v, string path) {
            if(s.ContainsKey("anyOf")){
                foreach(var branch in (object[])s["anyOf"]){try{return Validate(Json.Object(branch,"anyOf"),v,path);}catch(ArgumentException){}}
                throw new ArgumentException("Invalid tool argument: "+path+" does not match any allowed definition.");
            }
            var type = Json.Get(s, "type") as string;
            if (type == "object") {
                var input = Json.Object(v, path); var output = Json.Obj();
                var props = Json.Get(s, "properties") as Dictionary<string, object> ?? Json.Obj();
                var required = (Json.Get(s, "required") as object[] ?? new object[0]).Cast<string>().ToArray();
                foreach (var kv in input) {
                    if (props.ContainsKey(kv.Key)) output[kv.Key] = Validate(Json.Object(props[kv.Key], "schema"), kv.Value, path + "." + kv.Key);
                    else {
                        var additional = Json.Get(s, "additionalProperties"); Need(!object.Equals(additional, false), path + "." + kv.Key);
                        if (s.ContainsKey("propertyNames")) Validate(Json.Object(s["propertyNames"], "schema"), kv.Key, path);
                        output[kv.Key] = additional is Dictionary<string, object> ? Validate((Dictionary<string, object>)additional, kv.Value, path + "." + kv.Key) : kv.Value;
                    }
                }
                foreach (var kv in props) if (!input.ContainsKey(kv.Key)) {
                    var child = Json.Object(kv.Value, "schema");
                    if (child.ContainsKey("default")) output[kv.Key] = Validate(child, child["default"], path + "." + kv.Key);
                    else Need(!required.Contains(kv.Key), path + "." + kv.Key + " is required");
                }
                v = output;
            } else if (type == "array") {
                var list = v as object[]; Need(list != null, path);
                Need(list.Length >= Bound(s, "minItems", 0) && list.Length <= Bound(s, "maxItems", int.MaxValue), path);
                v = list.Select((x, i) => Validate(Json.Object(s["items"], "items"), x, path + "[" + i + "]")).ToArray();
            } else if (type == "string") {
                var str = v as string; Need(str != null, path);
                // JSON Schema lengths count Unicode scalar values, not UTF-16 units.
                int length = str.Length - str.Count(char.IsHighSurrogate);
                Need(length >= Bound(s, "minLength", 0) && length <= Bound(s, "maxLength", int.MaxValue) && str.IndexOf('\0') < 0, path);
                if (s.ContainsKey("pattern")) Need(Regex.IsMatch(str, "\\A(?:" + ((string)s["pattern"]).TrimStart('^').TrimEnd('$') + ")\\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), path);
            } else if (type == "integer") {
                v = Json.Integer(v, path, Bound(s, "minimum", -9007199254740991), Bound(s, "maximum", 9007199254740991));
            } else if (type == "number") {
                decimal number; var raw = v as JsonNumber;
                Need(v != null && !(v is string) && !(v is bool), path);
                Need(decimal.TryParse(raw == null ? Convert.ToString(v, CultureInfo.InvariantCulture) : raw.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out number), path);
                Need(number >= Bound(s, "minimum", -9007199254740991) && number <= Bound(s, "maximum", 9007199254740991), path); v = number;
            } else if (type == "boolean") Need(v is bool, path);
            else throw new ArgumentException("Unsupported schema type: " + type);
            if (s.ContainsKey("const")) {
                // JSON Schema numeric equality: 2, 2.0 and 2e0 are identical.
                if (type == "number" || type == "integer") {
                    decimal expected; Need(decimal.TryParse(Json.Write(s["const"]), NumberStyles.Float, CultureInfo.InvariantCulture, out expected) && Convert.ToDecimal(v, CultureInfo.InvariantCulture) == expected, path); v = expected;
                } else Need(Json.Write(v) == Json.Write(s["const"]), path);
            }
            if (s.ContainsKey("enum")) Need(((object[])s["enum"]).Any(x => Json.Write(x) == Json.Write(v)), path);
            return v;
        }
    }
}
