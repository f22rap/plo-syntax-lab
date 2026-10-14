using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PloNativeMcp {
    public static class NativePaths {
        public static bool Windows { get { return Path.DirectorySeparatorChar == '\\'; } }
        public static StringComparer Comparer { get { return Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal; } }
        public static void Plain(string path) {
            for (var p = Path.GetFullPath(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p)) {
                try { if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("リンク・ジャンクションは指定できません: " + p); }
                catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
            }
        }
        public static string DirectoryPath(string path, bool create) {
            var full = Path.GetFullPath(path); Plain(full);
            if (create) Directory.CreateDirectory(full);
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("フォルダーがありません: " + full);
            Plain(full);
            return Canonical(full);
        }
        static string Canonical(string full) {
            if (!Windows) return full;
            using (var handle = CreateFile(full, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero)) {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                var text = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandle(handle, text, (uint)text.Capacity, 0);
                if (length == 0 || length >= text.Capacity) throw new IOException("フォルダーの実パスを取得できません: " + full);
                var resolved = text.ToString();
                if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) resolved = @"\\" + resolved.Substring(8);
                else if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) resolved = resolved.Substring(4);
                Plain(resolved); return resolved;
            }
        }
        public static bool Within(string root, string candidate) {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            candidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Comparer.Equals(root, candidate) || candidate.StartsWith(root + Path.DirectorySeparatorChar, Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        public static string InputFile(Configuration config, string path) {
            if (!Path.IsPathRooted(path) || Path.GetExtension(path).ToLowerInvariant() != ".csv") throw new ArgumentException("Use an absolute CSV path.");
            var full = Path.GetFullPath(path); Plain(full);
            if (!File.Exists(full)) throw new FileNotFoundException("CSV is missing.", full);
            var canonical = Canonical(full);
            if (!config.InputRoots.Any(r => Within(r, canonical))) throw new ArgumentException("CSV is outside configured inputRoots.");
            if (new FileInfo(canonical).Length > config.MaxCsvBytes) throw new ArgumentException("CSV exceeds maxCsvBytes.");
            return canonical;
        }
        public static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        static extern SafeFileHandle CreateFile(string file, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
        static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder name, uint size, uint flags);
    }
    public sealed class Configuration {
        public const int MaxConfigBytes = 1024 * 1024;
        public string FilePath, Fingerprint, OutputRoot, HistoryRoot;
        public string[] InputRoots = new string[0];
        public bool AllowHistoryDelete;
        public long MaxCsvBytes = 268435456;
        public int TimeoutSeconds = 300;
        public static string DataRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlopCommandApp"); } }
        public static string DefaultFile { get { return Path.Combine(DataRoot, "mcp-native.json"); } }
        public static Configuration New(string file) { return new Configuration { FilePath = Path.GetFullPath(file), OutputRoot = Path.Combine(DataRoot, "ResearchOutput"), HistoryRoot = Path.Combine(DataRoot, "History") }; }
        static byte[] Bytes(string file) {
            NativePaths.Plain(file);
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                if (stream.Length > MaxConfigBytes) throw new IOException("設定ファイルが大きすぎます。");
                using (var memory = new MemoryStream()) { stream.CopyTo(memory); return memory.ToArray(); }
            }
        }
        static string Resolve(string dir, object value, string field) { return Path.GetFullPath(Path.Combine(dir, Json.String(value, field))); }
        public static Configuration Load(string file) {
            file = Path.GetFullPath(file); var bytes = Bytes(file);
            var q = Json.Object(Json.Parse(Json.Utf8.GetString(bytes).TrimStart('\uFEFF')), "設定");
            Json.Keys(q, "inputRoots", "outputRoot", "historyRoot", "allowHistoryDelete", "maxCsvBytes", "timeoutSeconds");
            var roots = Json.Get(q, "inputRoots") as object[];
            if (roots == null || roots.Length == 0 || roots.Length > 100) throw new ArgumentException("CSVフォルダーを1〜100件指定してください。");
            var dir = Path.GetDirectoryName(file); var c = New(file); c.Fingerprint = NativePaths.Hash(bytes);
            c.InputRoots = roots.Select(x => NativePaths.DirectoryPath(Resolve(dir, x, "inputRoots"), false)).Distinct(NativePaths.Comparer).ToArray();
            c.OutputRoot = Resolve(dir, Json.Get(q, "outputRoot"), "outputRoot");
            if (q.ContainsKey("historyRoot")) c.HistoryRoot = Resolve(dir, q["historyRoot"], "historyRoot");
            if (q.ContainsKey("allowHistoryDelete")) { if (!(q["allowHistoryDelete"] is bool)) throw new ArgumentException("allowHistoryDeleteはtrue/falseで指定してください。"); c.AllowHistoryDelete = (bool)q["allowHistoryDelete"]; }
            if (q.ContainsKey("maxCsvBytes")) c.MaxCsvBytes = Json.Integer(q["maxCsvBytes"], "maxCsvBytes", 1, 1073741824);
            if (q.ContainsKey("timeoutSeconds")) c.TimeoutSeconds = (int)Json.Integer(q["timeoutSeconds"], "timeoutSeconds", 10, 3600);
            c.ValidateDirectories(false); return c;
        }
        void ValidateDirectories(bool create) {
            if (InputRoots == null || InputRoots.Length == 0 || InputRoots.Length > 100) throw new ArgumentException("CSVフォルダーを1〜100件追加してください。");
            InputRoots = InputRoots.Select(p => NativePaths.DirectoryPath(p, false)).Distinct(NativePaths.Comparer).ToArray();
            // Validate both before creating either, including linked ancestors.
            NativePaths.Plain(OutputRoot); NativePaths.Plain(HistoryRoot);
            if (NativePaths.Comparer.Equals(Path.GetFullPath(OutputRoot).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(HistoryRoot).TrimEnd(Path.DirectorySeparatorChar))) throw new ArgumentException("結果と履歴には別のフォルダーを指定してください。");
            OutputRoot = NativePaths.DirectoryPath(OutputRoot, create);
            HistoryRoot = NativePaths.DirectoryPath(HistoryRoot, create);
            if (NativePaths.Comparer.Equals(OutputRoot, HistoryRoot)) throw new ArgumentException("結果と履歴には別のフォルダーを指定してください。");
        }
        public Dictionary<string, object> ToJson() { return Json.Obj("inputRoots", InputRoots, "outputRoot", OutputRoot, "historyRoot", HistoryRoot, "allowHistoryDelete", AllowHistoryDelete, "maxCsvBytes", MaxCsvBytes, "timeoutSeconds", TimeoutSeconds); }
        public Configuration Save() {
            FilePath = Path.GetFullPath(FilePath); NativePaths.Plain(FilePath);
            var dir = Path.GetDirectoryName(FilePath); NativePaths.DirectoryPath(dir, true);
            var lockFile = FilePath + ".lock"; NativePaths.Plain(lockFile);
            using (var gate = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
                var exists = File.Exists(FilePath);
                if ((Fingerprint == null && exists) || (Fingerprint != null && (!exists || NativePaths.Hash(Bytes(FilePath)) != Fingerprint))) throw new IOException("設定が別の操作で変更されました。開き直してから保存してください。");
                if (MaxCsvBytes < 1 || MaxCsvBytes > 1073741824 || TimeoutSeconds < 10 || TimeoutSeconds > 3600) throw new ArgumentException("設定の上限値が不正です。");
                ValidateDirectories(true);
                var bytes = Json.Utf8.GetBytes(Json.Write(ToJson()) + "\n");
                var temp = Path.Combine(dir, ".plo-mcp-" + Guid.NewGuid().ToString("N") + ".tmp");
                try {
                    using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
                    NativePaths.Plain(FilePath);
                    if (exists) {
                        if (NativePaths.Hash(Bytes(FilePath)) != Fingerprint) throw new IOException("設定が変更されました。開き直してください。");
                        File.Replace(temp, FilePath, FilePath + "." + Guid.NewGuid().ToString("N") + ".bak");
                    } else File.Move(temp, FilePath);
                } finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            return Load(FilePath);
        }
        public void EnsureUnchanged() {
            if (NativePaths.Hash(Bytes(FilePath)) != Fingerprint) throw new IOException("設定が変更されました。接続を再起動してください。");
            ValidateDirectories(false);
        }
    }
}
