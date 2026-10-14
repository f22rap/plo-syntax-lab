using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using PloNativeMcp;
#if WINDOWS_GUI
using System.Windows.Forms;
#endif

namespace PloNativeMcpTests {
    public static class Program {
        static int checks;
        static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        static void Fails(Action action, string message) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, message); }
        static Dictionary<string, object> Obj(object value) { return Json.Object(value, "test object"); }
        static object Get(object value, string key) { return Json.Get(Obj(value), key); }
        static long Num(object value) { return Json.Integer(value, "number", long.MinValue, long.MaxValue); }
        static object Request(McpServer server, string method, object param, object id) { return server.Handle(Json.Write(Json.Obj("jsonrpc", "2.0", "id", id, "method", method, "params", param))); }
        static void Error(object response, int code) { Check(Num(Get(Get(response, "error"), "code")) == code, "Wrong error code: " + Json.Write(response)); }
        static object Initialize(McpServer server, string version) { return Request(server, "initialize", Json.Obj("protocolVersion", version, "capabilities", Json.Obj(), "clientInfo", Json.Obj("name", "test", "version", "1")), 1); }
        static void Ready(McpServer server) { Initialize(server, McpServer.Protocol); server.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"); }
        [STAThread]
        public static int Main(string[] args) {
            string dir = Path.Combine(Path.GetTempPath(), "plo-native-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                JsonChecks();
                var input = Path.Combine(dir, "CSV 日本語 with spaces"); Directory.CreateDirectory(input);
                var c = Configuration.New(Path.Combine(dir, "mcp.local.json"));
                c.InputRoots = new[] { input, input }; c.OutputRoot = Path.Combine(dir, "output"); c.HistoryRoot = Path.Combine(dir, "history");
                c = c.Save(); Check(c.InputRoots.Length == 1, "Deduplicate roots.");
                Check(Directory.Exists(c.OutputRoot) && Directory.Exists(c.HistoryRoot), "Create output and history on explicit save.");
                var before = File.ReadAllBytes(c.FilePath); var loaded = Configuration.Load(c.FilePath);
                Check(before.SequenceEqual(File.ReadAllBytes(c.FilePath)), "Read must preserve config bytes.");
                Check(c.Fingerprint == loaded.Fingerprint, "Fingerprint roundtrip.");
                var stale = Configuration.Load(c.FilePath);
                loaded.TimeoutSeconds = 55; loaded.MaxCsvBytes = 1024; loaded.AllowHistoryDelete = true; loaded = loaded.Save();
                Check(loaded.TimeoutSeconds == 55 && loaded.MaxCsvBytes == 1024 && loaded.AllowHistoryDelete, "Preserve advanced configuration.");
                Check(Directory.GetFiles(dir, "*.bak").Length == 1, "Keep previous config backup.");
                var current = File.ReadAllBytes(c.FilePath);
                Fails(() => stale.Save(), "Reject stale editor writes.");
                Check(current.SequenceEqual(File.ReadAllBytes(c.FilePath)), "Conflict preserves original file.");
                var collision = Configuration.New(c.FilePath); collision.InputRoots = new[] { input };
                Fails(() => collision.Save(), "New editor must not overwrite existing config.");
                var bad = Configuration.New(Path.Combine(dir, "bad.json")); bad.InputRoots = new[] { Path.Combine(dir, "missing") }; bad.OutputRoot = Path.Combine(dir, "bad-output"); bad.HistoryRoot = Path.Combine(dir, "bad-history");
                Fails(() => bad.Save(), "Missing root rejected."); Check(!File.Exists(bad.FilePath) && !Directory.Exists(bad.OutputRoot), "Invalid roots produce no configuration/output.");
                bad.InputRoots = new[] { input }; bad.OutputRoot = input; bad.HistoryRoot = input;
                Fails(() => bad.Save(), "Same output/history rejected.");
                var original = File.ReadAllBytes(c.FilePath);
                File.WriteAllText(c.FilePath, "{\"inputRoots\":[],\"outputRoot\":\"x\",\"surprise\":true}");
                Fails(() => Configuration.Load(c.FilePath), "Unknown config property rejected."); File.WriteAllBytes(c.FilePath, original);
                RelativeConfig(dir, input);
                loaded = Configuration.Load(c.FilePath); ProtocolChecks(loaded);
                ResearchChecks(loaded);
                var server = new McpServer(loaded); Ready(server);
                File.AppendAllText(c.FilePath, " ");
                var changed = Request(server, "tools/call", Json.Obj("name", "get_status", "arguments", Json.Obj()), 4);
                Check(object.Equals(Get(Get(changed, "result"), "isError"), true), "Changed config cannot be silently used."); File.WriteAllBytes(c.FilePath, original);
                ProcessChecks(dir, c.FilePath, args);
#if WINDOWS_GUI
                UiChecks(c.FilePath);
#endif
                Console.WriteLine("PASS: " + checks + " native MCP checks (including CSV research/EQ/export); Node.js unavailable to the child process."); return 0;
            } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
#if WINDOWS_GUI
        static IEnumerable<Control> Descendants(Control root) {
            foreach (Control control in root.Controls) { yield return control; foreach (var child in Descendants(control)) yield return child; }
        }
        static void UiChecks(string original) {
            var c = Configuration.Load(original); c.FilePath = Path.Combine(Path.GetDirectoryName(original), "ui-test.json"); c.Fingerprint = null; c = c.Save();
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new SetupForm(c.FilePath)) {
                form.Show(); Application.DoEvents();
                var controls = Descendants(form).ToArray();
                Check(form.Visible && form.IsHandleCreated, "Windows setup form opens.");
                Check(controls.OfType<ListBox>().Single().Items.Cast<string>().SequenceEqual(c.InputRoots), "Setup shows complete configured CSV paths.");
                var save = controls.OfType<Button>().Single(b => b.Text == "設定を保存");
                var check = controls.OfType<Button>().Single(b => b.Text == "接続を診断");
                Check(check.Enabled && !save.Enabled, "Saved setup enables connection diagnostics.");
                var output = controls.OfType<TextBox>().Single(b => b.Text == c.OutputRoot);
                output.Text = Path.Combine(Path.GetDirectoryName(original), "ui-output"); Application.DoEvents();
                Check(save.Enabled && !check.Enabled, "Edited setup requires saving before diagnostics.");
                save.PerformClick(); Application.DoEvents();
                Check(Configuration.Load(c.FilePath).OutputRoot == NativePaths.DirectoryPath(output.Text, false) && check.Enabled, "Setup save persists output path and re-enables diagnostics.");
                form.Close(); Application.DoEvents(); Check(!form.Visible, "Setup closes cleanly after saving.");
            }
        }
#endif
        static void RelativeConfig(string dir, string input) {
            var p = Path.Combine(dir, "relative.json");
            File.WriteAllText(p, "\uFEFF" + Json.Write(Json.Obj("inputRoots", new[] { Path.GetFileName(input) }, "outputRoot", "output", "historyRoot", "history", "maxCsvBytes", 268435456, "timeoutSeconds", 300)), new UTF8Encoding(false));
            var c = Configuration.Load(p); Check(NativePaths.Comparer.Equals(c.InputRoots[0], NativePaths.DirectoryPath(input, false)), "Relative paths and UTF-8 BOM.");
            Check(c.OutputRoot == NativePaths.DirectoryPath(Path.Combine(dir, "output"), false), "Resolve relative paths against config directory.");
            File.WriteAllText(p, Json.Write(Json.Obj("inputRoots", new[] { input }, "outputRoot", "output", "historyRoot", "history", "timeoutSeconds", "300")));
            Fails(() => Configuration.Load(p), "Reject string-valued numeric options.");
        }
        static void JsonChecks() {
            var sample = Json.Obj("日本語 key", "行1\n行2 \\ \" 😀", "a", new object[] { true, null, new JsonNumber("9007199254740993") }, "", "empty-key");
            var result = Obj(Json.Parse(Json.Write(sample)));
            Check((string)result["日本語 key"] == (string)sample["日本語 key"], "Unicode and escapes roundtrip.");
            Check((string)result[""] == "empty-key", "XML-invalid key roundtrip.");
            Check(((JsonNumber)((object[])result["a"])[2]).Text == "9007199254740993", "Preserve integer tokens.");
            Fails(() => Json.Parse("{\"x\":1,\"x\":2}"), "Duplicate properties rejected.");
            foreach (var bad in new[] { "{", "{\"x\":NaN}", "{\"x\":01}", "{\"x\":1,}", "{\"x\":true} garbage", "[1,]", "{\"a\":\"a\nb\"}" }) Fails(() => Json.Parse(bad), "Invalid JSON rejected: " + bad);
            Fails(() => Json.Parse(new string('[', 80) + "0" + new string(']', 80)), "Bounded nesting.");
            var type = Obj(Json.Parse("{\"__type\":\"label\",\"x\":1}")); Check((string)type["__type"] == "label", "No magic type properties.");
        }
        static object Tool(McpServer server, string name, object args) {
            var result = Get(Request(server, "tools/call", Json.Obj("name", name, "arguments", args), 50), "result");
            if (object.Equals(Get(result, "isError"), true)) throw new Exception(Json.Write(result));
            return Get(result, "structuredContent");
        }
        static object Finish(McpServer server, object job) {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 30000) {
                var state = Tool(server, "research_get_job", Json.Obj("job_id", Get(job, "job_id")));
                string name = (string)Get(state, "state");
                if (name == "completed") return Get(state, "result");
                if (name == "failed" || name == "cancelled") throw new Exception(Json.Write(state));
                Thread.Sleep(10);
            }
            throw new Exception("Research test timed out.");
        }
        static void ResearchChecks(Configuration config) {
            string tree = Path.Combine(config.InputRoots[0], "COvsBTN");
            foreach (var board in new[] { "AsKd7c", "KsQd2c" }) foreach (var side in new[] { "IP", "OOP" }) {
                string node = side == "IP" ? "vs_check" : "donk", position = side == "IP" ? "BTN" : "CO", history = side == "IP" ? "C" : "ROOT";
                string dir = Path.Combine(tree, board, side, node); Directory.CreateDirectory(dir);
                foreach (var action in new[] { "C", "R50" }) File.WriteAllText(Path.Combine(dir, board + "_" + position + "_" + history + "_" + action + ".csv"),
                    "hand,frequency,percent,weight,ev,ev_diff,variations\n" + (side == "IP" ? "AcAd4c5d" : "JcJd6c7d") + ",1,100," + (action == "C" ? "0.25" : "0.75") + ",0,0,1\n", Json.Utf8);
            }
            using (var server = new McpServer(config)) {
                Ready(server);
                var scan = Finish(server, Tool(server, "research_scan_directory", Json.Obj("directory", tree, "highest_rank", "K")));
                Check(Num(Get(scan, "board_count")) == 1 && Num(Get(scan, "excluded_board_count")) == 1, "Khi excludes Ahi.");
                var nodes = (object[])Get(Tool(server, "research_get_directory_scan", Json.Obj("scan_id", Get(scan, "scan_id"))), "items");
                var node = nodes.Single(x => (string)Get(x, "folder") == "vs_check");
                var scenario = Json.Obj("game", "PLO4", "players", new JsonNumber("2.0"), "hero_position", "BTN", "villain_position", "CO", "effective_stack_bb", "100", "pot_type", "SRP", "action_history", "CO check", "solution_context", "Synthetic test", "equity_reference", "Before actions");
                var imported = Finish(server, Tool(server, "research_import_directory", Json.Obj("scan_id", Get(scan, "scan_id"), "node_id", Get(node, "node_id"), "catalog_id", "native-k", "name", "Native Khi", "scenario", scenario, "confirm_profile", "plo-post-tree-v1", "all_actions_common_scale", true, "expected_workspace_revision", 0)));
                Check(Num(Get(imported, "spot_count")) == 1, "Directory import saves selected board.");
                var spot = ((object[])Get(Tool(server, "research_list_spots", Json.Obj("catalog_id", "native-k")), "items"))[0];
                Check(((object[])Get(spot, "opponent_sources")).Length == 2, "Opponent uses both pre-action CSVs.");
                var condition = Json.Obj("condition_id", "aa", "label", "User AA", "enabled", true, "kind", "literal", "syntax", "AA", "by_board", Json.Obj());
                var set = Json.Obj("set_id", "native-set", "revision", 0, "name", "User conditions", "conditions", new object[] { condition });
                var saved = Tool(server, "research_save_condition_set", Json.Obj("condition_set", set, "expected_workspace_revision", 1));
                Check((string)Get(((object[])Get(Get(saved, "condition_set"), "conditions"))[0], "syntax") == "AA", "User syntax is preserved.");
                var plan = Finish(server, Tool(server, "research_validate", Json.Obj("catalog_id", "native-k", "catalog_revision", 1, "set_id", "native-set", "set_revision", 1, "settings", Json.Obj("Trials", 1000))));
                Check((string)Get(Get(plan, "settings"), "HighestRank") == "K" && Num(Get(Get(plan, "settings"), "Seed")) == 7123, "Rank inheritance and nested schema defaults.");
                var started = Tool(server, "research_start", Json.Obj("plan_id", Get(plan, "plan_id"), "request_key", "native-run"));
                Check((string)Get(Tool(server, "research_start", Json.Obj("plan_id", Get(plan, "plan_id"), "request_key", "native-run")), "job_id") == (string)Get(started, "job_id"), "Request key is idempotent.");
                var run = Finish(server, started);
                var row = ((object[])Get(Tool(server, "research_get_result", Json.Obj("result_id", Get(run, "result_id"))), "items"))[0];
                Check((string)Get(row, "Status") == "completed" && Get(row, "Equity") != null, "EQ is computed by the shared engine.");
                var actions = (object[])Get(row, "Actions");
                Check(actions.Length == 3 && Num(Get(actions.Single(a => (string)Get(a, "Key") == "check"), "Frequency")) == 25 && Num(Get(actions.Single(a => (string)Get(a, "Key") == "total_bet"), "Frequency")) == 75, "Frequency uses all hero actions and retains total bet: " + Json.Write(actions));
                var art = Tool(server, "research_export", Json.Obj("result_id", Get(run, "result_id"), "format", "svg"));
                string svg = (string)Get(Tool(server, "get_artifact", Json.Obj("artifact_id", Get(art, "artifact_id"))), "text");
                Check(svg.Contains("User AA") && svg.Contains("Check") && svg.Contains("Bet"), "Graph contains the user condition and all actions.");
                Check(((object[])Get(Get(Request(server, "resources/list", Json.Obj(), 60), "result"), "resources")).Length == 1, "Export is discoverable as an MCP resource.");
                var invalid = Get(Request(server, "tools/call", Json.Obj("name", "research_list_workspace", "arguments", Json.Obj("limit", "10")), 51), "result");
                Check(object.Equals(Get(invalid, "isError"), true), "Numeric strings fail server-side schema validation.");
                var history = Tool(server, "research_list_history", Json.Obj()); Check(Num(Get(history, "total")) == 1, "Research result is persisted.");
            }
        }
        static void ProtocolChecks(Configuration config) {
            var server = new McpServer(config);
            Error(Request(server, "tools/list", Json.Obj(), 3), -32000);
            Error(server.Handle("[]"), -32600); Error(server.Handle("{broken"), -32700);
            Error(server.Handle("{\"jsonrpc\":\"2.0\",\"id\":null,\"method\":\"ping\"}"), -32600);
            Error(server.Handle("{\"jsonrpc\":\"1.0\",\"id\":1,\"method\":\"ping\"}"), -32600);
            var init = Initialize(server, "2099-01-01"); Check((string)Get(Get(init, "result"), "protocolVersion") == McpServer.Protocol, "Negotiate only implemented protocol.");
            Error(Initialize(server, McpServer.Protocol), -32600);
            Check(server.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}") == null, "No notification reply.");
            Check(server.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"unknown\",\"params\":[]}") == null, "Malformed notification has no reply.");
            Error(Request(server, "no_such_method", Json.Obj(), "unknown"), -32601);
            var listed = Get(Request(server, "tools/list", Json.Obj(), 2), "result"); var tools = (object[])Get(listed, "tools");
            Check(tools.Length == 17 && (string)Get(tools[0], "name") == "get_status", "Advertise the 17 implemented tools.");
            Error(Request(server, "tools/list", Json.Obj("cursor", "unexpected"), 2), -32602);
            Error(Request(server, "tools/call", Json.Obj("name", "not_a_tool"), 3), -32602);
            var bad = Get(Request(server, "tools/call", Json.Obj("name", "get_status", "arguments", Json.Obj("inputRoots", new[] { "C:/" })), 3), "result");
            Check(object.Equals(Get(bad, "isError"), true), "Tool arguments cannot alter configuration.");
            var response = Request(server, "tools/call", Json.Obj("name", "get_status", "arguments", Json.Obj()), "日本語-id");
            Check((string)Get(response, "id") == "日本語-id", "Echo string request IDs.");
            var status = Get(Get(response, "result"), "structuredContent");
            Check(object.Equals(Get(status, "requires_node"), false) && object.Equals(Get(status, "research_ready"), true), "Truthful research status.");
            Check(object.Equals(Get(status, "history_delete_enabled"), false), "No deletion tool in preview.");
            var exact = server.Handle("{\"jsonrpc\":\"2.0\",\"id\":9007199254740993,\"method\":\"ping\"}");
            Check(Json.Write(exact).Contains("9007199254740993"), "Echo large numeric ID exactly.");
            using (var input = new MemoryStream(Json.Utf8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"ping\"}\n"))) {
                var output = new StringWriter(); Check(server.Run(input, output, new StringWriter()) == 0, "Clean EOF exits.");
                Check(Num(Get(Json.Parse(output.ToString()), "id")) == 8, "Only JSON goes to stdout.");
            }
            using (var input = new MemoryStream(Json.Utf8.GetBytes("\uFEFF{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"ping\"}\n\uFEFF{\"jsonrpc\":\"2.0\",\"id\":10,\"method\":\"ping\"}\n"))) {
                var output = new StringWriter(); server.Run(input, output, new StringWriter());
                var lines = output.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                Check(Num(Get(Json.Parse(lines[0]), "id")) == 9, "Accept a Framework UTF-8 preamble only at transport start.");
                Error(Json.Parse(lines[1]), -32700);
            }
            foreach (var bytes in new[] { new byte[] { 255, 10 }, Json.Utf8.GetBytes("{\"jsonrpc\":\"2.0\"}"), new byte[Json.MaxBytes + 1] }) {
                var output = new StringWriter(); Check(server.Run(new MemoryStream(bytes), output, new StringWriter()) == 1, "Bad frame fails cleanly.");
                Error(Json.Parse(output.ToString()), -32700);
            }
        }
        static void ProcessChecks(string dir, string config, string[] args) {
            string exe = args.Length > 0 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PloMcp.Connect.exe");
            string command = args.Length > 1 ? args[1] : exe, prefix = args.Length > 1 ? Diagnostics.Quote(exe) + " " : "";
            var oldPath = Environment.GetEnvironmentVariable("PATH");
            var empty = Path.Combine(dir, "empty-path"); Directory.CreateDirectory(empty);
            try {
                Environment.SetEnvironmentVariable("PATH", empty);
                var result = Diagnostics.Check(command, config, prefix);
                Check((string)result["mcp_connection"] == "passed", "Real child-process stdio check with empty PATH.");
                Check((string)result["chatgpt_connection"] == "not_tested", "Local diagnosis never claims ChatGPT connectivity.");
                var quoted = Diagnostics.Quote("C:\\PLO 日本語\\"); Check(quoted.EndsWith("\\\\\""), "Quote trailing backslash.");
                var start = new ProcessStartInfo(command, prefix + "--stdio --config " + Diagnostics.Quote(Path.Combine(dir, "missing.json"))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var child = Process.Start(start)) {
                    child.StandardInput.Close(); var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
                    if (!child.WaitForExit(10000)) { child.Kill(); throw new Exception("Invalid configuration startup hung."); }
                    Check(child.ExitCode != 0 && stdout.Result.Length == 0 && stderr.Result.Length > 0, "Startup error is stderr-only.");
                }
            } finally { Environment.SetEnvironmentVariable("PATH", oldPath); }
        }
    }
}
