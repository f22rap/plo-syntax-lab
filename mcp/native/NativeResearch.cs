using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlopCommands.Research;
using PloMcp;

namespace PloNativeMcp {
    // The native EXE calls the same C# bridge and engine as the GUI/Node host.
    // Only transport, directory discovery and session orchestration live here.
    public sealed class NativeResearch : IDisposable {
        readonly Configuration config;
        readonly object gate = new object(), configGate = new object();
        readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();
        readonly Dictionary<string, Plan> plans = new Dictionary<string, Plan>();
        readonly Dictionary<string, RequestKey> keys = new Dictionary<string, RequestKey>();
        readonly Dictionary<string, ScanEntry> scans = new Dictionary<string, ScanEntry>();
        readonly Dictionary<string, Dictionary<string, object>> artifacts = new Dictionary<string, Dictionary<string, object>>();
        bool closed;
        sealed class Job {
            public string Id = Guid.NewGuid().ToString(), State = "queued", Error;
            public object Progress = Json.Obj("message", "Waiting"), Result;
            public readonly CancellationTokenSource Cancel = new CancellationTokenSource();
            public Task Task;
        }
        sealed class Plan { public ResearchRequest Request; public string Fingerprint; public DateTime Expires; }
        sealed class RequestKey { public string Id, Fingerprint; }
        sealed class ScanEntry { public DirectoryScan Scan; public DateTime Expires; }
        public NativeResearch(Configuration config) { this.config = config; }
        public static object Encode<T>(T value) { return Json.ParseResult(ResearchBridge.J(value)); }
        public static T Decode<T>(object value) { return ResearchJson.Decode<T>(Json.Utf8.GetBytes(Json.Write(value))); }
        static object Copy(object value) { return Json.ParseResult(Json.Write(value)); }
        static string S(Dictionary<string, object> a, string key) { return Json.Get(a, key) as string; }
        static int I(Dictionary<string, object> a, string key) { return (int)Json.Integer(a[key], key, 0, int.MaxValue); }
        public void EnsureConfiguration() { lock (configGate) config.EnsureUnchanged(); }
        object Call(ResearchRequest q, Job job = null) {
            EnsureConfiguration();
            q.historyRoot = config.HistoryRoot; q.outputRoot = config.OutputRoot;
            q.inputRoots = config.InputRoots; q.maxCsvBytes = config.MaxCsvBytes;
            var token = job == null ? CancellationToken.None : job.Cancel.Token;
            token.ThrowIfCancellationRequested();
            return Json.ParseResult(ResearchBridge.Execute(q, token, p => {
                if (job != null) Progress(job, Encode(p));
            }));
        }
        ResearchWorkspace Workspace() { return Decode<ResearchWorkspace>(Call(new ResearchRequest { action = "workspace" })); }
        void Scope(ResearchCatalog catalog) {
            foreach (var source in catalog.Spots.SelectMany(s => s.Actions.Select(a => a.Source).Concat(s.Opponents))) {
                var canonical = NativePaths.InputFile(config, source.Path);
                if (!string.Equals(canonical, source.Path, StringComparison.Ordinal)) throw new ArgumentException("Use canonical CSV paths from the directory scan.");
            }
        }
        static Dictionary<string, object> Page(IEnumerable<object> rows, Dictionary<string, object> a) {
            var all = rows.ToArray(); int offset = I(a, "offset"), limit = I(a, "limit");
            return Json.Obj("total", all.Length, "offset", offset, "items", all.Skip(offset).Take(limit).ToArray(), "next_offset", (long)offset + limit < all.Length ? (object)(offset + limit) : null);
        }
        static Dictionary<string, object> Merge(Dictionary<string, object> left, Dictionary<string, object> right) { foreach (var kv in right) left[kv.Key] = kv.Value; return left; }
        object Public(Job j) { return Copy(Json.Obj("job_id", j.Id, "state", j.State, "progress", j.Progress, "result", j.Result, "error", j.Error)); }
        void Progress(Job j, object p) { lock (gate) if (!j.Cancel.IsCancellationRequested) j.Progress = p; }
        object StartJob(Func<Job, object> run) {
            lock (gate) {
                if (closed) throw new InvalidOperationException("Server is closing.");
                if (jobs.Values.Any(j => j.State == "queued" || j.State == "running")) throw new InvalidOperationException("A research job is already active. Poll or cancel it first.");
                if (jobs.Count >= 200) throw new InvalidOperationException("Research session job limit reached. Restart the server.");
                var job = new Job(); jobs.Add(job.Id, job);
                job.Cancel.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
                job.Task = Task.Run(() => {
                    lock (gate) job.State = "running";
                    try {
                        job.Cancel.Token.ThrowIfCancellationRequested();
                        var result = run(job); var obj = result as Dictionary<string, object>;
                        string status = obj == null ? null : S(obj, "status");
                        lock (gate) { job.Result = result; job.State = status == "cancelled" ? "cancelled" : status == "failed" ? "failed" : "completed"; }
                    } catch (Exception e) {
                        lock (gate) { job.Error = e.Message; job.State = job.Cancel.IsCancellationRequested ? "cancelled" : "failed"; }
                    } finally { job.Cancel.CancelAfter(Timeout.Infinite); }
                });
                return Public(job);
            }
        }
        object GetJob(string id, bool cancel) {
            lock (gate) {
                Job j; if (!jobs.TryGetValue(id, out j)) throw new ArgumentException("Unknown research job_id.");
                if (cancel && (j.State == "queued" || j.State == "running")) { j.Cancel.Cancel(); j.Progress = Json.Obj("message", "Cancelling; completed boards will be saved when available."); }
                return Public(j);
            }
        }
        ScanEntry Scan(string id) {
            lock (gate) {
                ScanEntry entry; if (!scans.TryGetValue(id, out entry) || entry.Expires < DateTime.UtcNow) throw new ArgumentException("Unknown or expired scan_id; scan again.");
                return entry;
            }
        }
        object ScanDirectory(Dictionary<string, object> a) {
            return StartJob(j => {
                var rank = ResearchBoardFilter.Resolve(S(a, "highest_rank"), Json.Get(a, "ahi_only") as bool?, null);
                var scan = DirectoryImport.Scan(config, S(a, "directory"), rank, j.Cancel.Token, p => Progress(j, p));
                string id = Guid.NewGuid().ToString(); j.Cancel.Token.ThrowIfCancellationRequested();
                lock (gate) {
                    foreach (var key in scans.Where(x => x.Value.Expires < DateTime.UtcNow).Select(x => x.Key).ToArray()) scans.Remove(key);
                    if (scans.Count >= 8) scans.Remove(scans.OrderBy(x => x.Value.Expires).First().Key);
                    scans[id] = new ScanEntry { Scan = scan, Expires = DateTime.UtcNow.AddHours(1) };
                }
                return Json.Obj("scan_id", id, "profile", DirectoryScan.Profile, "directory", scan.Directory, "highest_rank", rank, "board_count", scan.BoardCount, "excluded_board_count", scan.ExcludedBoards, "file_count", scan.FileCount, "node_count", scan.Nodes.Count, "issue_count", scan.Issues.Count, "next_call", "research_get_directory_scan");
            });
        }
        object DirectoryPage(Dictionary<string, object> a) {
            var scan = Scan(S(a, "scan_id")).Scan; string view = S(a, "view"), node = S(a, "node_id");
            IEnumerable<object> rows = view == "issues" ? scan.Issues.Cast<object>() : view == "spots" ? scan.Spots.Where(s => node == null || s.NodeId == node).Select(s => s.Public()) : scan.Nodes.Cast<object>();
            return Merge(Page(rows, a), Json.Obj("scan_id", a["scan_id"], "directory", scan.Directory, "profile", DirectoryScan.Profile, "highest_rank", scan.HighestRank, "fingerprint", scan.Fingerprint));
        }
        object Import(Dictionary<string, object> a) {
            return StartJob(j => {
                var scan = Scan(S(a, "scan_id")).Scan; var catalog = DirectoryImport.Catalog(scan, a);
                var current = DirectoryImport.Scan(config, scan.Directory, scan.HighestRank, j.Cancel.Token, p => Progress(j, p));
                if (current.Fingerprint != scan.Fingerprint) throw new ArgumentException("Directory changed after preview; scan again before importing.");
                Scope(catalog);
                var saved = Json.Object(Call(new ResearchRequest { action = "save_catalog", catalog = catalog, expectedRevision = I(a, "expected_workspace_revision") }, j), "saved");
                var c = Decode<ResearchCatalog>(saved["catalog"]);
                return Json.Obj("workspace_revision", saved["workspace_revision"], "catalog_id", c.Id, "catalog_revision", c.Revision, "spot_count", c.Spots.Count, "source_directory", scan.Directory, "highest_rank", scan.HighestRank, "profile", DirectoryScan.Profile, "next_call", "research_list_spots");
            });
        }
        object Validate(Dictionary<string, object> a) {
            return StartJob(j => {
                var w = Workspace(); var c = w.Catalogs.SingleOrDefault(x => x.Id == S(a, "catalog_id")); var s = w.Sets.SingleOrDefault(x => x.Id == S(a, "set_id"));
                if (c == null || s == null) throw new ArgumentException("Select an existing catalog and condition set.");
                if (c.Revision != I(a, "catalog_revision") || s.Revision != I(a, "set_revision")) throw new ArgumentException("Definitions changed; reload their current revisions.");
                var settings = Decode<ResearchRunSettings>(a["settings"]); settings.HighestRank = ResearchBoardFilter.Resolve(settings.HighestRank, settings.AhiOnly, c.HighestRank);
                var request = new ResearchRequest { action = "validate", catalogId = c.Id, catalogRevision = c.Revision, setId = s.Id, setRevision = s.Revision, spotIds = ((object[])a["spot_ids"]).Cast<string>().ToArray(), settings = settings };
                var result = Json.Object(Call(request, j), "validation"); j.Cancel.Token.ThrowIfCancellationRequested();
                request.catalog = Decode<ResearchCatalog>(result["catalog"]); request.conditionSet = Decode<ResearchConditionSet>(result["condition_set"]); request.action = "run";
                var fingerprint = NativePaths.Hash(Json.Utf8.GetBytes(Json.Write(result))); var id = Guid.NewGuid().ToString(); var expires = DateTime.UtcNow.AddHours(1);
                lock (gate) {
                    if (plans.Count >= 200) throw new InvalidOperationException("Plan limit reached; restart server.");
                    plans[id] = new Plan { Request = request, Fingerprint = fingerprint, Expires = expires };
                }
                return Merge(result, Json.Obj("plan_id", id, "input_fingerprint", fingerprint, "expires_utc", expires.ToString("o")));
            });
        }
        object Start(Dictionary<string, object> a) {
            lock (gate) {
                Plan p; if (!plans.TryGetValue(S(a, "plan_id"), out p)) throw new ArgumentException("Unknown plan_id; validate first.");
                RequestKey old; string key = S(a, "request_key");
                if (keys.TryGetValue(key, out old)) {
                    if (old.Fingerprint != p.Fingerprint) throw new ArgumentException("request_key was already used with different inputs.");
                    return GetJob(old.Id, false);
                }
                if (p.Expires < DateTime.UtcNow) throw new ArgumentException("Plan expired; validate again.");
                var job = Json.Object(StartJob(j => {
                    var r = Json.Object(Call(ResearchJson.Clone(p.Request), j), "run");
                    return Merge(r, Json.Obj("input_fingerprint", p.Fingerprint, "next_call", "research_get_result"));
                }), "job");
                keys[key] = new RequestKey { Id = (string)job["job_id"], Fingerprint = p.Fingerprint }; return job;
            }
        }
        object Export(Dictionary<string, object> a) {
            if (artifacts.Count >= 200) throw new InvalidOperationException("Artifact session limit reached; restart server.");
            string id = Guid.NewGuid().ToString(), format = S(a, "format");
            var saved = Json.Object(Call(new ResearchRequest { action = "export", id = S(a, "result_id"), format = format, grouped = (bool)a["grouped"], artifactId = id }), "export");
            var mime = format == "svg" ? "image/svg+xml" : format == "csv" ? "text/csv" : format == "json" ? "application/json" : "text/markdown";
            var artifact = Merge(saved, Json.Obj("artifact_id", id, "result_id", a["result_id"], "uri", "plo://artifacts/" + id, "mimeType", mime));
            artifacts.Add(id, artifact); return Copy(artifact);
        }
        public object ReadArtifact(string id) {
            EnsureConfiguration(); Dictionary<string, object> artifact;
            if (!artifacts.TryGetValue(id, out artifact)) throw new ArgumentException("Unknown artifact_id (artifacts are session-scoped).");
            string path = (string)artifact["path"]; NativePaths.Plain(path);
            if (!NativePaths.Within(config.OutputRoot, path)) throw new ArgumentException("Artifact is outside output root.");
            var result = Json.Object(Copy(artifact), "artifact");
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                if (f.Length > 1024 * 1024) { result["inline"] = false; result["note"] = "Use the exported file path for artifacts over 1 MiB."; }
                else using (var reader = new StreamReader(f, Json.Utf8, true)) { result["inline"] = true; result["text"] = reader.ReadToEnd(); }
            }
            return result;
        }
        public object ResourceList() { EnsureConfiguration(); return Json.Obj("resources", artifacts.Values.Select(a => Json.Obj("uri", a["uri"], "name", Path.GetFileName((string)a["path"]), "mimeType", a["mimeType"], "size", a["bytes"])).ToArray()); }
        public object ResourceRead(string uri) {
            const string prefix = "plo://artifacts/";
            if (!uri.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException("Unknown resource URI.");
            var a = Json.Object(ReadArtifact(uri.Substring(prefix.Length)), "artifact");
            if (!(bool)a["inline"]) throw new ArgumentException("Artifact is too large to return inline; use get_artifact for its exported file path.");
            return Json.Obj("contents", new object[] { Json.Obj("uri", uri, "mimeType", a["mimeType"], "text", a["text"]) });
        }
        public object Invoke(string name, Dictionary<string, object> a) {
            EnsureConfiguration();
            switch (name) {
                case "get_artifact": return ReadArtifact(S(a, "artifact_id"));
                case "research_scan_directory": return ScanDirectory(a);
                case "research_get_directory_scan": return DirectoryPage(a);
                case "research_import_directory": return Import(a);
                case "research_get_job": return GetJob(S(a, "job_id"), false);
                case "research_cancel_job": return GetJob(S(a, "job_id"), true);
                case "research_validate": return Validate(a);
                case "research_start": return Start(a);
                case "research_export": return Export(a);
                case "research_save_condition_set": return Call(new ResearchRequest { action = "save_set", conditionSet = Decode<ResearchConditionSet>(a["condition_set"]), expectedRevision = I(a, "expected_workspace_revision") });
                case "research_save_catalog": {
                    var c = Decode<ResearchCatalog>(a["catalog"]); Scope(c);
                    return Call(new ResearchRequest { action = "save_catalog", catalog = c, expectedRevision = I(a, "expected_workspace_revision") });
                }
                case "research_list_history": return Call(new ResearchRequest { action = "history", query = S(a, "query"), offset = I(a, "offset"), limit = I(a, "limit") });
                case "research_get_result":
                    if (S(a, "view") != "board" && a.ContainsKey("spot_id")) throw new ArgumentException("spot_id applies only to board view.");
                    return Call(new ResearchRequest { action = "result", id = S(a, "result_id"), view = S(a, "view"), conditionId = S(a, "condition_id"), spotId = S(a, "spot_id"), offset = I(a, "offset"), limit = I(a, "limit") });
                case "research_list_workspace": {
                    var w = Workspace(); return Json.Obj("workspace_revision", w.Revision,
                        "condition_sets", Page(w.Sets.Select(s => Json.Obj("set_id", s.Id, "revision", s.Revision, "name", s.Name, "conditions", s.Conditions.Count, "enabled", s.Conditions.Count(c => c.Enabled))), a),
                        "catalogs", Page(w.Catalogs.Select(c => Json.Obj("catalog_id", c.Id, "revision", c.Revision, "name", c.Name, "highest_rank", ResearchBoardFilter.Resolve(c.HighestRank, null, null), "scenario", Encode(c.Scenario), "spot_count", c.Spots.Count)), a));
                }
                case "research_get_condition_set": {
                    var w = Workspace(); var s = w.Sets.SingleOrDefault(x => x.Id == S(a, "set_id"));
                    if (s == null) throw new ArgumentException("Unknown set_id.");
                    if (a.ContainsKey("revision") && s.Revision != I(a, "revision")) throw new ArgumentException("Condition set changed.");
                    return Json.Obj("workspace_revision", w.Revision, "condition_set", Encode(s));
                }
                case "research_list_spots": {
                    var w = Workspace(); var c = w.Catalogs.SingleOrDefault(x => x.Id == S(a, "catalog_id"));
                    if (c == null) throw new ArgumentException("Unknown catalog_id.");
                    string rank = ResearchBoardFilter.Resolve(S(a, "highest_rank"), Json.Get(a, "ahi_only") as bool?, c.HighestRank), node = S(a, "node");
                    var rows = c.Spots.Where(s => ResearchBoardFilter.Matches(s.Board, rank) && (string.IsNullOrEmpty(node) || s.Node.Trim() == node.Trim()));
                    return Merge(Page(rows.Select(s => Encode(s)), a), Json.Obj("workspace_revision", w.Revision, "catalog_id", c.Id, "revision", c.Revision, "name", c.Name, "highest_rank", rank, "scenario", Encode(c.Scenario)));
                }
                default: throw new ArgumentException("Unknown research tool.");
            }
        }
        public void Dispose() {
            Task[] pending;
            lock (gate) { closed = true; foreach (var j in jobs.Values) if (!j.Task.IsCompleted) j.Cancel.Cancel(); pending = jobs.Values.Select(j => j.Task).ToArray(); }
            // Cooperative engine cancellation persists completed boards on EOF.
            Task.WaitAll(pending);
            foreach (var j in jobs.Values) j.Cancel.Dispose();
        }
    }
}
