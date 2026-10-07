using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace FlopCommands {
    static partial class TestProgram {
        static void WaitDefaults(MainForm f){
            var timer=System.Diagnostics.Stopwatch.StartNew();while((f.DefaultsBusy||f.ChoiceCount!=185)&&timer.Elapsed.TotalSeconds<150){Application.DoEvents();System.Threading.Thread.Sleep(25);if(!f.DefaultsBusy&&f.DefaultsMessage.Contains("生成できません"))throw new Exception(f.DefaultsMessage);}
            Require(!f.DefaultsBusy&&f.ChoiceCount==185,"Default generation: "+f.DefaultsMessage);
        }
        static int ModesTest(string output){
            string a=Path.Combine(output,"modeA.csv"),b=Path.Combine(output,"modeB.csv");
            File.WriteAllText(a,"hand,weight\r\nJhJdAsKc,0.2\r\nJhJd9h8h,0.9\r\nThTsAhKh,0.3\r\n",new UTF8Encoding(true));
            File.WriteAllText(b,"combo,weight\r\nJhJdAsKc,0.6\r\nJhJd9h8h,0.1\r\nThTsAhKh,0.1\r\n",new UTF8Encoding(true));
            using(var f=new MainForm()){
                f.Show();Application.DoEvents();Require(f.UseDefaults,"Default radio initially selected");WaitDefaults(f);
                Require(f.CurrentCode.Contains("Tset all")&&f.CurrentCode.Contains("Mset all"),"Old CSV names and default selections");
                f.IncludeAll=false;f.SelectCells("L49");string before=f.CurrentCode;
                f.UseDefaults=false;Require(f.ChoiceCount==0&&f.CurrentCode=="","Generated mode has its own list");
                f.AcceptSelection(Payload("AsKd7s",Range("My second FD","AA:Qss:!ks",27)));Require(!f.UseDefaults&&f.ChoiceCount==1&&f.CurrentCode.Contains("My second FD"),"Manual generator imports into generated mode");
                string generated=f.CurrentCode;f.UseDefaults=true;WaitDefaults(f);Require(f.CurrentCode==before,"Default selection preserved across mode switch");
                f.UseDefaults=false;Require(f.CurrentCode==generated,"Generated selection preserved across mode switch");
                f.UseDefaults=true;f.ApplyInput("JsTd7c");WaitDefaults(f);Require(f.CurrentCode.Contains("JJ!(98)")&&!f.CurrentCode.Contains("AA:Qss"),"Default syntax adapts to board");
                f.UseDefaults=false;Require(f.ChoiceCount==0,"Board change clears stale generated conditions");f.UseDefaults=true;WaitDefaults(f);
                f.IncludeAll=true;f.SelectCells("L49","L61");f.LoadCsvs(a,b);f.SetCsvLabels("Check","Bet");Run(f);
                Require(f.LastResult.Rows.Count==3&&f.LastResult.Rows[1].Label=="Tset all"&&f.LastResult.Rows[1].Sources[0].Sum==0.2m,"Default excludes stronger straight from top set");
                Require(f.LastResult.Rows[1].Sources[0].Percent==25m&&f.LastResult.Rows[2].Sources[0].Percent==75m,"Default ratios");
                Require(f.GraphRowCount==3&&f.History.EntryCount==1,"Graph and history maintained");f.SaveSnapshot(Path.Combine(output,"default-preview.png"));f.ExportResultsTo(Path.Combine(output,"default-results.csv"));
                f.UseDefaults=false;Require(f.LastResult==null,"Switch mode clears stale result");f.AcceptSelection(Payload("JsTd7c",Range("生成したトップセット","JJ!(98)",3103)));Run(f);Require(f.LastResult.Rows.Count==2&&f.LastResult.Rows[1].Sources[0].Sum==0.2m,"Generated mode execution remains valid");f.SaveSnapshot(Path.Combine(output,"generated-preview.png"));
                f.UseDefaults=true;Require(f.ChoiceCount==185&&f.CurrentCode.Contains("Mset all"),"Return to default restores saved selections");
                f.LoadCsvs();f.ApplyInput("AsKd7s5h");f.UseDefaults=false;f.UseDefaults=true;f.ApplyInput("AsKd7s5h9c");WaitDefaults(f);
                Require(f.CurrentCode.Contains("AsKd7s5h9c")&&f.DefaultsMessage.Contains("AsKd7s5h9c"),"Cancel/switch and late results do not overwrite board");
                f.ApplyInput("AsAs7s");Require(f.CurrentCode==""&&!f.DefaultsBusy,"Invalid board clears commands and stops generation");
                f.ApplyInput("JsTd7c");WaitDefaults(f);Require(f.ChoiceCount==185,"Cached board restores defaults");
                f.Close();Application.DoEvents();
            }
            File.WriteAllText(Path.Combine(output,"modes-test-passed.txt"),"PASS: initial default radio; actual hidden-browser generation without CSV/Excel; 185 historical labels; board-adapted syntax; independent mode selections; generated transfers; real PowerShell sums/ratios/graph/history in both modes; cancellation and stale-result guards; turn/river; invalid board; cache.");return 0;
        }
    }
}
