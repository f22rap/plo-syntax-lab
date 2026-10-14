using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
#if WINDOWS_GUI
using System.Windows.Forms;
#endif

namespace PloNativeMcp {
    public static class Program {
        public const string Usage = "PloMcp.Connect.exe [--setup | --stdio | --check] [--config <file>]\nDouble-click to open setup. No Node.js required. 17 tools for CSV research, user syntax, equity and export.";
        [STAThread]
        public static int Main(string[] args) {
            string mode = "--setup", configFile = Configuration.DefaultFile;
            bool modeSeen = false, configSeen = false;
            try {
                for (int i = 0; i < args.Length; i++) {
                    if (args[i] == "--help" && args.Length == 1) { Console.WriteLine(Usage); return 0; }
                    if (args[i] == "--config" && !configSeen && i + 1 < args.Length) { configSeen = true; configFile = Path.GetFullPath(args[++i]); }
                    else if ((args[i] == "--setup" || args[i] == "--stdio" || args[i] == "--check") && !modeSeen) { modeSeen = true; mode = args[i]; }
                    else throw new ArgumentException(Usage);
                }
                if (mode == "--setup") {
#if WINDOWS_GUI
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new SetupForm(configFile)); return 0;
#else
                    Console.Error.WriteLine("The setup screen is available in the Windows build."); return 2;
#endif
                }
                // A WinExe has no console window. On .NET Framework, setting
                // Console.OutputEncoding calls SetConsoleOutputCP and fails
                // with "The handle is invalid" even with redirected MCP pipes.
                // Encode the pipe writers directly, without a console API call.
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
                if (mode == "--stdio") {
                    var config = Configuration.Load(configFile);
                    using (var server = new McpServer(config)) return server.Run(Console.OpenStandardInput(), Console.Out, Console.Error);
                }
                Console.WriteLine(Json.Write(Check(configFile))); return 0;
            } catch (Exception e) {
                if (mode == "--setup") {
#if WINDOWS_GUI
                    MessageBox.Show(e.Message, "PLO 接続設定", MessageBoxButtons.OK, MessageBoxIcon.Error);
#else
                    Console.Error.WriteLine(e.Message);
#endif
                } else Console.Error.WriteLine(e.Message);
                return 1;
            }
        }
        public static System.Collections.Generic.Dictionary<string, object> Check(string config) {
#if WINDOWS_GUI
            return Diagnostics.Check(Assembly.GetExecutingAssembly().Location, config);
#else
            // Test harness only; the Windows product always launches its own EXE.
            return Diagnostics.Check(Process.GetCurrentProcess().MainModule.FileName, config, Diagnostics.Quote(Assembly.GetExecutingAssembly().Location) + " ");
#endif
        }
    }
}
