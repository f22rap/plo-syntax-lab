using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PloNativeMcp {
    public sealed class RpcFailure : Exception {
        public readonly int Code;
        public RpcFailure(int code, string message) : base(message) { Code = code; }
    }
    public sealed class McpServer : IDisposable {
        public const string Version = "0.2.0-preview.1";
        public const string Protocol = "2025-11-25";
        readonly Configuration config;
        readonly ToolDefinitions definitions = new ToolDefinitions();
        readonly NativeResearch research;
        bool initialized, ready;
        public McpServer(Configuration config) { this.config = config; research = new NativeResearch(config); }
        public void Dispose() { research.Dispose(); }
        public Dictionary<string, object> Status() {
            research.EnsureConfiguration();
            return Json.Obj("name", "PLO Syntax Lab Native MCP", "version", Version,
                "transport", "stdio", "requires_node", false, "stage", "research_preview",
                "research_ready", true, "tool_count", definitions.Tools.Length, "planned_tool_count", 17,
                "input_roots", config.InputRoots, "output_root", config.OutputRoot,
                "history_root", config.HistoryRoot, "config_file", config.FilePath,
                "history_delete_enabled", false, "chatgpt_connection", "not_tested",
                "note", "CSV調査・EQ・全アクション頻度・グラフ出力に対応。ブラウザChatGPTとの接続は別途確認が必要です。");
        }
        public static object Error(object id, int code, string message) { return Json.Obj("jsonrpc", "2.0", "id", id, "error", Json.Obj("code", code, "message", message)); }
        static object Result(object id, object value) { return Json.Obj("jsonrpc", "2.0", "id", id, "result", value); }
        static object ToolError(string message) { return Json.Obj("isError", true, "content", new object[] { Json.Obj("type", "text", "text", message) }); }
        public object Handle(string line) {
            object parsed;
            try { parsed = Json.Parse(line); } catch (Exception e) {
                if (!(e is FormatException || e is System.Xml.XmlException || e is System.Runtime.Serialization.SerializationException || e is ArgumentException)) throw;
                return Error(null, -32700, "Invalid JSON.");
            }
            var q = parsed as Dictionary<string, object>;
            if (q == null) return Error(null, -32600, "One JSON-RPC object is required.");
            bool hasId = q.ContainsKey("id"); object id = Json.Get(q, "id");
            if ((string)(Json.Get(q, "jsonrpc") as string) != "2.0" || (hasId && !(id is string) && !(id is JsonNumber))) return Error(null, -32600, "Invalid JSON-RPC version or request ID.");
            // No outbound requests in this preview; unsolicited responses are ignored.
            if (!q.ContainsKey("method") && (q.ContainsKey("result") || q.ContainsKey("error"))) return null;
            var method = Json.Get(q, "method") as string;
            if (string.IsNullOrEmpty(method)) return Error(hasId ? id : null, -32600, "Method is required.");
            try {
                var p = q.ContainsKey("params") ? Json.Object(q["params"], "params") : Json.Obj();
                if (!hasId) {
                    if (method == "notifications/initialized" && initialized) ready = true;
                    return null; // JSON-RPC notifications never receive replies.
                }
                if (method == "ping") return Result(id, Json.Obj());
                if (method == "initialize") {
                    if (initialized) throw new RpcFailure(-32600, "Already initialized.");
                    Json.String(Json.Get(p, "protocolVersion"), "protocolVersion", 100);
                    Json.Object(Json.Get(p, "capabilities"), "capabilities");
                    var client = Json.Object(Json.Get(p, "clientInfo"), "clientInfo");
                    Json.String(Json.Get(client, "name"), "clientInfo.name", 300);
                    Json.String(Json.Get(client, "version"), "clientInfo.version", 100);
                    initialized = true;
                    return Result(id, Json.Obj("protocolVersion", Protocol,
                        "capabilities", Json.Obj("tools", Json.Obj("listChanged", false), "resources", Json.Obj("subscribe", false, "listChanged", false)),
                        "serverInfo", Json.Obj("name", "plo-syntax-lab-native", "version", Version),
                        "instructions", definitions.Instructions + " Node.js is not required. Do not claim a browser connection from local diagnostics. Jobs time out cooperatively after configured timeoutSeconds; poll cancelled jobs for saved partial results."));
                }
                if (!ready) throw new RpcFailure(-32000, "Initialize and send notifications/initialized first.");
                if (method == "tools/list") {
                    Json.Keys(p, "cursor", "_meta");
                    if (p.ContainsKey("cursor")) throw new RpcFailure(-32602, "No cursor is supported for this one-page tool list.");
                    return Result(id, Json.Obj("tools", definitions.Tools));
                }
                if (method == "tools/call") {
                    Json.Keys(p, "name", "arguments", "_meta");
                    var name = Json.String(Json.Get(p, "name"), "name", 100);
                    if (!definitions.Contains(name)) throw new RpcFailure(-32602, "Unknown tool. Use tools/list for implemented tools.");
                    try {
                        var args = definitions.Arguments(name, p.ContainsKey("arguments") ? p["arguments"] : Json.Obj());
                        var status = name == "get_status" ? Status() : research.Invoke(name, args);
                        return Result(id, Json.Obj("content", new object[] { Json.Obj("type", "text", "text", Json.Write(status)) }, "structuredContent", status));
                    } catch (Exception e) { return Result(id, ToolError(e.Message)); }
                }
                if (method == "resources/list" || method == "resources/templates/list") {
                    Json.Keys(p, "cursor", "_meta");
                    if (p.ContainsKey("cursor")) throw new RpcFailure(-32602, "No resource cursor is supported.");
                    return Result(id, method == "resources/list" ? research.ResourceList() : Json.Obj("resourceTemplates", new object[0]));
                }
                if (method == "resources/read") {
                    Json.Keys(p, "uri", "_meta");
                    try { return Result(id, research.ResourceRead(Json.String(Json.Get(p, "uri"), "uri"))); }
                    catch (Exception e) { throw new RpcFailure(-32002, e.Message); }
                }
                throw new RpcFailure(-32601, "Method not found.");
            } catch (RpcFailure e) { return hasId ? Error(id, e.Code, e.Message) : null; }
            catch (ArgumentException e) { return hasId ? Error(id, -32602, e.Message) : null; }
            catch (Exception) { return hasId ? Error(id, -32603, "Internal error.") : null; }
        }
        public int Run(Stream input, TextWriter output, TextWriter errors) {
            using (var buffered = new BufferedStream(input, 8192)) {
                bool firstFrame = true;
                while (true) {
                    string line;
                    try { line = ReadLine(buffered); }
                    catch (Exception e) { output.WriteLine(Json.Write(Error(null, -32700, "Invalid or oversized UTF-8 frame."))); output.Flush(); errors.WriteLine(e.Message); return 1; }
                    if (line == null) return 0;
                    // Framework Process.StandardInput may emit an initial UTF-8
                    // preamble. Accept it only at byte zero of the transport;
                    // JSON bodies and later frames remain strict JSON.
                    if (firstFrame && line.Length > 0 && line[0] == '\uFEFF') line = line.Substring(1);
                    firstFrame = false;
                    if (line.Length == 0) continue;
                    var response = Handle(line);
                    if (response != null) { output.WriteLine(Json.Write(response)); output.Flush(); }
                }
            }
        }
        static string ReadLine(Stream input) {
            using (var memory = new MemoryStream()) {
                int b;
                while ((b = input.ReadByte()) >= 0) {
                    if (b == 10) return Json.Utf8.GetString(memory.ToArray()).TrimEnd('\r');
                    if (memory.Length >= Json.MaxBytes) throw new IOException("MCP request exceeds 4 MiB.");
                    memory.WriteByte((byte)b);
                }
                if (memory.Length != 0) throw new IOException("MCP request requires a final newline.");
                return null;
            }
        }
    }
}
