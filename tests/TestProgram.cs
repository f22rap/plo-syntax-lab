using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FlopCommands {
    static partial class TestProgram {
        [STAThread] static int Main() {
            string output = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "test-results", "native"));
            Directory.CreateDirectory(output);
            HistoryStore.DefaultRoot = Path.Combine(output, "history-" + Guid.NewGuid().ToString("N"));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Control.CheckForIllegalCrossThreadCalls = true;
            try { return ModesTest(output); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "test-error.txt"), ex.ToString()); return 1; }
            finally { DefaultRunner.StopAll(); }
        }
        static void Require(bool value, string description) { if (!value) throw new Exception(description); }
        static void Pump(Task task) { while (!task.IsCompleted) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } task.GetAwaiter().GetResult(); }
        static void Run(MainForm form) { Pump(form.ExecuteAsync()); Require(form.LastResult != null, "Execution: " + form.ResultMessage); }
        static string Payload(string board, params LabRange[] ranges) {
            return new JavaScriptSerializer().Serialize(new LabSelection { app="PLO Syntax Lab", game="PLO4", board=Enumerable.Range(0,board.Length/2).Select(i=>board.Substring(i*2,2)).ToArray(), ranges=ranges });
        }
        static LabRange Range(string label, string syntax, int count=1) { return new LabRange { label=label, syntax=syntax, count=count }; }
    }
}
