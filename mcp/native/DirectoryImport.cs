using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using FlopCommands.Research;

namespace PloNativeMcp {
    public sealed class DirectoryScan {
        public const string Profile = "plo-post-tree-v1";
        public string Directory, HighestRank, Fingerprint;
        public int BoardCount, ExcludedBoards, FileCount;
        public readonly List<Dictionary<string, object>> Issues = new List<Dictionary<string, object>>();
        public readonly List<Dictionary<string, object>> Nodes = new List<Dictionary<string, object>>();
        public readonly List<DirectorySpot> Spots = new List<DirectorySpot>();
    }
    public sealed class DirectorySpot {
        public string NodeId, Board, Side, Folder, History, Position, Villain, EqStatus, OpponentNode;
        public List<string> Problems;
        public ResearchSpot Spot;
        public object Public() { return Json.Obj("node_id", NodeId, "board", Board, "side", Side, "folder", Folder, "history", History,
            "position", Position, "villain_position", Villain, "problems", Problems, "eq_status", EqStatus,
            "opponent_node", OpponentNode, "spot", NativeResearch.Encode(Spot)); }
    }
    public static class DirectoryImport {
        sealed class Csv {
            public string Board, Side, Folder, Position, History, Token, Path, Sha;
            public ResearchSource Source() { return new ResearchSource { Path = Path, Sha256 = Sha }; }
        }
        sealed class Node {
            public string Board, Side, Folder, Position, History, Id;
            public readonly List<Csv> Files = new List<Csv>();
            public readonly List<string> Problems = new List<string>();
            public List<ResearchAction> Actions;
        }
        static string Id(string value) { return NativePaths.Hash(Json.Utf8.GetBytes(value)).Substring(0, 24); }
        static string[] Tokens(string history) { return history == "ROOT" ? new string[0] : history.Split('-'); }
        static readonly Regex BoardPattern = new Regex(@"\A(?:[2-9TJQKA][shdc]){3}\z", RegexOptions.CultureInvariant);
        static readonly Regex TokenPattern = new Regex(@"\A(?:C|CALL|F|A|R[1-9][0-9]*(?:\.[0-9]+)?)\z", RegexOptions.CultureInvariant);
        static string BoardKey(string board) { return string.Concat(Enumerable.Range(0, 3).Select(i => board.Substring(i * 2, 2)).OrderBy(x => x, StringComparer.Ordinal)); }
        static FileSystemInfo[] Children(string path, ref int visited, CancellationToken token) {
            token.ThrowIfCancellationRequested(); NativePaths.Plain(path);
            var rows = new List<FileSystemInfo>();
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos()) {
                token.ThrowIfCancellationRequested();
                if (++visited > 100000) throw new ArgumentException("Directory exceeds 100,000 entries; select a smaller root.");
                rows.Add(entry);
            }
            return rows.OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
        }
        static bool PlainDirectory(FileSystemInfo file) { return (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == FileAttributes.Directory; }
        public static DirectoryScan Scan(Configuration config, string directory, string rank, CancellationToken token, Action<object> progress) {
            if (!Path.IsPathRooted(directory)) throw new ArgumentException("Use an absolute directory.");
            var root = NativePaths.DirectoryPath(directory, false);
            if (!config.InputRoots.Any(r => NativePaths.Within(r, root))) throw new ArgumentException("Directory is outside configured inputRoots. Register it in setup first.");
            var scan = new DirectoryScan { Directory = root, HighestRank = ResearchBoardFilter.Resolve(rank, null, null) };
            var files = new List<Csv>(); var boards = new List<string>(); var boardDirs = new List<object>(); int visited = 0;
            Action<string, string> issue = (p, reason) => scan.Issues.Add(Json.Obj("path", p, "reason", reason));
            foreach (var board in Children(root, ref visited, token)) {
                if ((board.Attributes & FileAttributes.ReparsePoint) != 0) { issue(board.FullName, "linked_path"); continue; }
                if (!PlainDirectory(board)) continue;
                if (!BoardPattern.IsMatch(board.Name) || Enumerable.Range(0, 3).Select(i => board.Name.Substring(2 * i, 2)).Distinct().Count() != 3) { issue(board.FullName, "not_a_physical_flop_directory"); continue; }
                if (!ResearchBoardFilter.Matches(board.Name, scan.HighestRank)) { scan.ExcludedBoards++; continue; }
                boards.Add(board.Name); boardDirs.Add(Json.Obj("board", board.Name, "path", board.FullName));
                int filesBeforeBoard = files.Count;
                foreach (var side in Children(board.FullName, ref visited, token)) {
                    if (!PlainDirectory(side) || (side.Name != "IP" && side.Name != "OOP")) { issue(side.FullName, "expected_IP_or_OOP_directory"); continue; }
                    foreach (var node in Children(side.FullName, ref visited, token)) {
                        if (!PlainDirectory(node)) { issue(node.FullName, "expected_node_directory"); continue; }
                        var entries = Children(node.FullName, ref visited, token);
                        if (entries.Length == 0) issue(node.FullName, "empty_node_directory");
                        foreach (var file in entries) {
                            token.ThrowIfCancellationRequested();
                            if ((file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || !file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) { issue(file.FullName, "expected_plain_csv"); continue; }
                            var parts = file.Name.Substring(0, file.Name.Length - 4).Split('_');
                            if (parts.Length != 4 || parts[0] != board.Name || !Regex.IsMatch(parts[1], @"\A[a-zA-Z0-9_]{1,30}\z") || !TokenPattern.IsMatch(parts[3]) || (parts[2] != "ROOT" && !Tokens(parts[2]).All(TokenPattern.IsMatch))) { issue(file.FullName, "filename_does_not_match_board_position_history_action"); continue; }
                            try {
                                var canonical = NativePaths.InputFile(config, file.FullName);
                                files.Add(new Csv { Board = board.Name, Side = side.Name, Folder = node.Name, Position = parts[1], History = parts[2], Token = parts[3], Path = canonical, Sha = ResearchValidation.Hash(canonical, token) });
                            } catch (OperationCanceledException) { throw; }
                            catch (Exception e) { issue(file.FullName, e.Message); }
                        }
                    }
                }
                if (files.Count == filesBeforeBoard) issue(board.FullName, "no_recognized_csv_for_board");
                progress(Json.Obj("message", "Scanning " + board.Name, "boards", boards.Count, "files", files.Count));
            }
            var nodes = files.GroupBy(f => f.Board + "/" + f.Side + "/" + f.Folder).Select(g => {
                var first = g.First(); var n = new Node { Board = first.Board, Side = first.Side, Folder = first.Folder, Position = first.Position, History = first.History, Id = Id(first.Side + "/" + first.Folder + "/" + first.Position + "/" + first.History) };
                n.Files.AddRange(g); return n;
            }).ToList();
            var duplicateBoards = new HashSet<string>(boards.GroupBy(BoardKey).Where(g => g.Count() > 1).SelectMany(g => g));
            var issueParents = new HashSet<string>(scan.Issues.Select(x => Path.GetDirectoryName((string)x["path"])), NativePaths.Comparer);
            var ip = files.Where(f => f.Side == "IP").Select(f => f.Position).Distinct().ToArray();
            var oop = files.Where(f => f.Side == "OOP").Select(f => f.Position).Distinct().ToArray();
            foreach (var n in nodes) {
                token.ThrowIfCancellationRequested();
                if (n.Files.Any(f => f.Position != n.Position || f.History != n.History)) n.Problems.Add("mixed_position_or_history_in_node");
                if (ip.Length != 1 || oop.Length != 1 || ip.Intersect(oop).Any()) n.Problems.Add("ambiguous_positions");
                if (duplicateBoards.Contains(n.Board)) n.Problems.Add("duplicate_physical_board");
                if (n.Files.Count > 3) n.Problems.Add("more_than_3_actions");
                if (n.Files.Select(f => f.Token).Distinct().Count() != n.Files.Count) n.Problems.Add("duplicate_action");
                var hist = Tokens(n.History);
                if (n.Side != (hist.Length % 2 == 0 ? "OOP" : "IP")) n.Problems.Add("history_actor_mismatch");
                if (hist.Any(t => t == "F" || t == "CALL") || hist.Contains("A") && hist.Last() != "A" || hist.Skip(1).Contains("C")) n.Problems.Add("terminal_or_invalid_history");
                bool facing = hist.Any(t => t == "A" || t.StartsWith("R", StringComparison.Ordinal));
                if (n.Files.Any(f => f.Token == "C" && facing || (f.Token == "CALL" || f.Token == "F") && !facing)) n.Problems.Add("action_incompatible_with_history");
                if (issueParents.Contains(Path.GetDirectoryName(n.Files[0].Path))) n.Problems.Add("unreadable_or_unknown_sibling");
                n.Actions = n.Files.Select(f => {
                    string kind = f.Token == "C" ? "check" : f.Token == "CALL" ? "call" : f.Token == "F" ? "fold" : facing ? "raise" : "bet";
                    string fraction = "";
                    if (f.Token == "A") fraction = "allin";
                    else if (f.Token.StartsWith("R", StringComparison.Ordinal)) {
                        decimal amount;
                        if (!decimal.TryParse(f.Token.Substring(1), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)) { n.Problems.Add("invalid_bet_size"); }
                        else fraction = (amount / 100m).ToString("0.############################", CultureInfo.InvariantCulture);
                    }
                    return new ResearchAction { Id = Id(f.Path), Kind = kind, PotFraction = fraction, Source = f.Source(), Label = f.Token == "A" ? "All-in" : f.Token == "C" ? "Check" : f.Token == "CALL" ? "Call" : f.Token == "F" ? "Fold" : (facing ? "Raise " : "Bet ") + f.Token.Substring(1) + "% pot" };
                }).ToList();
            }
            var historyIndex = nodes.ToLookup(n => n.Board + "/" + n.Side + "/" + n.History, StringComparer.Ordinal);
            foreach (var n in nodes) {
                token.ThrowIfCancellationRequested();
                var hist = Tokens(n.History); string previous = hist.Length == 1 ? "ROOT" : string.Join("-", hist.Take(Math.Max(0, hist.Length - 1)));
                string previousKey = n.Board + "/" + (n.Side == "IP" ? "OOP" : "IP") + "/" + previous;
                var candidates = historyIndex[previousKey].Where(o => hist.Length > 0 && o.Files.Any(f => f.Token == hist.Last())).ToArray();
                var opponent = hist.Length > 0 && candidates.Length == 1 && candidates[0].Problems.Count == 0 ? candidates[0] : null;
                // Explicit CO/BTN convention, shared with the Node directory importer.
                if (n.Side == "OOP" && n.Folder == "donk" && n.History == "ROOT" && n.Position == "CO" && n.Files.Any(f => f.Token == "C")) {
                    var roots = historyIndex[n.Board + "/IP/C"].ToArray();
                    if (roots.Length == 1 && roots[0].Folder == "vs_check" && roots[0].Position == "BTN" && roots[0].Problems.Count == 0) opponent = roots[0];
                }
                var villain = n.Side == "IP" ? oop : ip;
                scan.Spots.Add(new DirectorySpot { NodeId = n.Id, Board = n.Board, Side = n.Side, Folder = n.Folder, History = n.History, Position = n.Position, Villain = villain.Length == 1 ? villain[0] : null, Problems = n.Problems,
                    EqStatus = opponent != null ? "ready" : hist.Length > 0 ? "opponent_pre_action_node_missing_or_ambiguous" : "opponent_root_range_not_exported", OpponentNode = opponent == null ? null : opponent.Side + "/" + opponent.Folder + "/" + opponent.History,
                    Spot = new ResearchSpot { Id = Id(root + "/" + n.Board + "/" + n.Id), Board = n.Board, Node = n.Side + "/" + n.Folder + "/" + n.History, Actions = n.Actions, Opponents = opponent == null ? new List<ResearchSource>() : opponent.Files.Select(f => f.Source()).ToList(), AllActionsCommonScale = true, OpponentCommonScale = opponent != null } });
            }
            foreach (var g in scan.Spots.GroupBy(s => s.NodeId)) {
                var s = g.First(); scan.Nodes.Add(Json.Obj("node_id", s.NodeId, "side", s.Side, "folder", s.Folder, "history", s.History, "position", s.Position, "villain_position", s.Villain, "boards", g.Count(), "ready", g.Count(x => x.Problems.Count == 0 && x.EqStatus == "ready"), "blocked", g.Count(x => x.Problems.Count > 0), "missing_opponent", g.Count(x => x.Problems.Count == 0 && x.EqStatus != "ready")));
            }
            scan.BoardCount = boards.Count; scan.FileCount = files.Count;
            scan.Fingerprint = NativePaths.Hash(Json.Utf8.GetBytes(Json.Write(Json.Obj("root", root, "highest_rank", scan.HighestRank, "root_opponent_policy", "co-btn-vs-check-v1", "files", files.Select(f => Json.Obj("path", f.Path, "sha256", f.Sha)), "issues", scan.Issues, "boards", boardDirs))));
            return scan;
        }
        public static ResearchCatalog Catalog(DirectoryScan scan, Dictionary<string, object> a) {
            if ((string)a["confirm_profile"] != DirectoryScan.Profile || !(bool)a["all_actions_common_scale"]) throw new ArgumentException("Confirm profile and complete common-scale weights.");
            var boards = ((object[])a["boards"]).Cast<string>().ToArray();
            var rows = scan.Spots.Where(s => s.NodeId == (string)a["node_id"] && (boards.Length == 0 || boards.Contains(s.Board))).ToArray();
            if (rows.Length == 0 || rows.Length > 200) throw new ArgumentException("Select 1–200 boards for this node.");
            if (boards.Length > 0 && (boards.Distinct().Count() != boards.Length || rows.Length != boards.Length)) throw new ArgumentException("Unknown or duplicate boards.");
            var blocked = rows.Where(s => s.Problems.Count > 0 || !(bool)a["allow_frequency_only"] && s.EqStatus != "ready").ToArray();
            if (blocked.Length > 0) throw new ArgumentException("Resolve blocked boards: " + string.Join("; ", blocked.Select(s => s.Board + ": " + string.Join(",", s.Problems.Concat(s.EqStatus == "ready" ? new string[0] : new[] { s.EqStatus })))));
            var scenario = NativeResearch.Decode<ResearchScenario>(a["scenario"]);
            if (rows.Any(s => s.Position != scenario.HeroPosition || s.Villain != scenario.VillainPosition)) throw new ArgumentException("Scenario positions do not match the CSVs.");
            scenario.EquityReference = "Hero: sum of all actions at " + rows[0].Spot.Node + "; opponent: sum of all actions BEFORE their immediately preceding action. Prior hero actions remain conditioned. Root opponent range is not inferred.";
            if (rows.All(s => s.Side == "OOP" && s.Folder == "donk" && s.History == "ROOT" && s.Position == "CO" && s.Villain == "BTN"))
                scenario.EquityReference = "Hero CO: sum of all actions at OOP/donk/ROOT. User-defined BTN starting range: sum of ALL actions at IP/vs_check/C (after CO checks), adopted as the root opponent range; not an independently exported preflop range. Missing or ambiguous sources remain unavailable. Policy: co-btn-vs-check-v1.";
            return new ResearchCatalog { Id = (string)a["catalog_id"], Revision = 0, Name = (string)a["name"], HighestRank = scan.HighestRank, Scenario = scenario, Spots = rows.Select(s => ResearchJson.Clone(s.Spot)).ToList() };
        }
    }
}
