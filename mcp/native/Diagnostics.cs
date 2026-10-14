using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PloNativeMcp {
    public static class Diagnostics {
        // Windows CreateProcess argument escaping, not shell quoting.
        public static string Quote(string value) {
            var b = new System.Text.StringBuilder("\""); int slashes = 0;
            foreach (char c in value) {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { b.Append('\\', slashes * 2 + 1); b.Append(c); }
                else { b.Append('\\', slashes); b.Append(c); }
                slashes = 0;
            }
            b.Append('\\', slashes * 2); b.Append('"'); return b.ToString();
        }
        public static Dictionary<string, object> Check(string executable, string configFile, string prefix = "") {
            var config = Configuration.Load(configFile);
            var start = new ProcessStartInfo(executable, prefix + "--stdio --config " + Quote(config.FilePath)) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Json.Utf8, StandardErrorEncoding = Json.Utf8
            };
            using (var child = new Process { StartInfo = start }) {
                child.Start();
                var stderr = child.StandardError.ReadToEndAsync();
                try {
                    var lines = new List<string>();
                    lines.Add(Call(child, Json.Obj("jsonrpc", "2.0", "id", 1, "method", "initialize", "params", Json.Obj("protocolVersion", McpServer.Protocol, "capabilities", Json.Obj(), "clientInfo", Json.Obj("name", "plo-native-diagnostics", "version", McpServer.Version)))));
                    Write(child, Json.Obj("jsonrpc", "2.0", "method", "notifications/initialized"));
                    lines.Add(Call(child, Json.Obj("jsonrpc", "2.0", "id", 2, "method", "ping")));
                    lines.Add(Call(child, Json.Obj("jsonrpc", "2.0", "id", 3, "method", "tools/list")));
                    lines.Add(Call(child, Json.Obj("jsonrpc", "2.0", "id", 4, "method", "tools/call", "params", Json.Obj("name", "get_status", "arguments", Json.Obj()))));
                    child.StandardInput.Close();
                    var stdout = child.StandardOutput.ReadToEndAsync();
                    if (!child.WaitForExit(15000)) throw new TimeoutException("接続診断が15秒以内に完了しませんでした。");
                    if (!Task.WaitAll(new Task[] { stdout, stderr }, 5000)) throw new TimeoutException("診断結果の受信が完了しませんでした。");
                    if (child.ExitCode != 0) throw new IOException("接続EXEの起動に失敗しました: " + stderr.Result);
                    if (!string.IsNullOrWhiteSpace(stdout.Result)) throw new IOException("MCP以外の出力が混在しています。");
                    var replies = lines.Select(l => Json.Object(Json.Parse(l), "reply")).ToArray();
                    for (int i = 0; i < replies.Length; i++) {
                        if ((Json.Get(replies[i], "jsonrpc") as string) != "2.0" || replies[i].ContainsKey("error") || Json.Integer(Json.Get(replies[i], "id"), "id", 1, 4) != i + 1) throw new IOException("MCP応答が不正です。");
                    }
                    var init = Json.Object(replies[0]["result"], "initialize result");
                    if ((Json.Get(init, "protocolVersion") as string) != McpServer.Protocol) throw new IOException("MCPバージョンが一致しません。");
                    var tools = Json.Get(Json.Object(replies[2]["result"], "tools/list result"), "tools") as object[];
                    var expected = new ToolDefinitions().Tools.Select(x => (string)Json.Object(x, "tool")["name"]).OrderBy(x => x).ToArray();
                    if (tools == null || !tools.Select(x => (string)Json.Object(x, "tool")["name"]).OrderBy(x => x).SequenceEqual(expected)) throw new IOException("調査ツール一覧が一致しません。");
                    var result = Json.Object(replies[3]["result"], "tool result");
                    if (object.Equals(Json.Get(result, "isError"), true)) throw new IOException(Json.Write(result));
                    var status = Json.Object(Json.Get(result, "structuredContent"), "status");
                    if (!object.Equals(Json.Get(status, "requires_node"), false)) throw new IOException("Node.js不要版ではありません。");
                    return Json.Obj("mcp_connection", "passed", "chatgpt_connection", "not_tested", "research_ready", Json.Get(status, "research_ready"),
                        "tool_count", tools.Length, "protocol_version", McpServer.Protocol,
                        "checked_utc", DateTime.UtcNow.ToString("o"), "status", status);
                } finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } }
            }
        }
        static string Call(Process child, object request) {
            Write(child, request); var line = child.StandardOutput.ReadLineAsync();
            if (!line.Wait(5000)) throw new TimeoutException("MCP応答が5秒以内に返りませんでした。");
            if (line.Result == null) throw new IOException("MCP接続が応答前に終了しました。");
            return line.Result;
        }
        static void Write(Process child, object value) { child.StandardInput.WriteLine(Json.Write(value)); child.StandardInput.Flush(); }
    }
}
