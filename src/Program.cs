using System;
using System.Windows.Forms;

namespace FlopCommands {
    static class Program {
        [STAThread] static int Main() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try {
                Application.Run(new MainForm());
                return 0;
            } catch (Exception ex) {
                MessageBox.Show(ex.Message, "PLO Syntax Lab", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            } finally {
                DefaultRunner.StopAll();
            }
        }
    }
}
