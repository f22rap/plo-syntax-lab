using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FlopCommands {
    // Requires an actual Windows desktop; no browser or solver files are needed.
    static class EquityUiTests {
        static void Require(bool condition,string message){if(!condition)throw new Exception("EQ UI: "+message);}
        static T Find<T>(Control root,string name) where T:Control {return (T)root.Controls.Find(name,true).Single();}
        static void Pump(Task task){var timer=Stopwatch.StartNew();while(!task.IsCompleted){Application.DoEvents();System.Threading.Thread.Sleep(10);if(timer.Elapsed.TotalSeconds>120)throw new Exception("EQ UI timed out");}task.GetAwaiter().GetResult();Application.DoEvents();}
        public static void Run(string output){
            using(var form=new MainForm(new HistoryStore(Path.Combine(output,"eq-ui-history")))){
                form.UseDefaults=false;form.IncludeAll=false;
                var payload=new LabSelection{app="PLO Syntax Lab",game="PLO4",board=new[]{"2c","3d","7h","9s","Tc"},ranges=new[]{new LabRange{label="AA",syntax="AA",count=1},new LabRange{label="no kings",syntax="KK",count=1}}};
                form.AcceptSelection(new JavaScriptSerializer().Serialize(payload));form.Show();form.Controls.OfType<TabControl>().First().SelectedIndex=2;Application.DoEvents();
                var panel=form.EquityView;var heroMode=Find<ComboBox>(panel,"eqHeroMode");var hero=Find<TextBox>(panel,"eqHeroRange");var villain=Find<TextBox>(panel,"eqVillainRange");
                heroMode.SelectedIndex=1;hero.Text="AcAd4c5d";villain.Text="KcKd4h5h@25%,Jc8hKhQh@75%";Find<TextBox>(panel,"eqSeed").Text="123";
                Pump(panel.ExecuteAsync());Require(panel.LastResult!=null&&panel.ResultRowCount==2,"batch result rows");Require(Math.Abs(panel.LastResult.Rows[0].Result.Equity-25)<1e-10,"exact 25% displayed");Require(panel.LastResult.Rows[1].Result==null,"empty syntax remains empty");
                form.SaveSnapshot(Path.Combine(output,"equity-preview.png"));
                var actionPaths=new[]{Path.Combine(output,"eq-check.csv"),Path.Combine(output,"eq-bet.csv")};
                File.WriteAllText(actionPaths[0],"hand,weight\nAcAd4c5d,0.2");File.WriteAllText(actionPaths[1],"hand,weight\nAcAd4c5d,0.8");
                var graphContext=new Equity.EquityJobRequest{Board="2c3d7h9sTc",Conditions=new[]{new Equity.EquityCondition{Label="AA",Syntax="AA"},new Equity.EquityCondition{Label="same hand",Syntax="AcAd4c5d"}},ActionPaths=actionPaths,ActionLabels=new[]{"check","bet 75%"}};
                panel.SetContext(graphContext);Find<CheckBox>(panel,"eqBetAction1").Checked=true;Find<CheckBox>(panel,"eqCommonScale").Checked=true;
                Pump(panel.ExecuteAsync());Require(panel.PlotPointCount==2,"scatter includes both syntax groups");
                Require(Find<TabControl>(panel,"eqResultTabs").SelectedIndex==1,"completed plot automatically selected");
                Require(Math.Abs(panel.LastResult.Rows[0].Frequency.Percent[1]-80)<1e-9,"80% bet frequency");
                form.SaveSnapshot(Path.Combine(output,"equity-scatter-preview.png"));
                Require(Find<Button>(panel,"eqSaveCsv").Enabled&&Find<Button>(panel,"eqSaveSvg").Enabled,"completed exports enabled");
                panel.ExportTo(Path.Combine(output,"eq-ui-export.json"),".json");panel.ExportTo(Path.Combine(output,"eq-ui-export.csv"),".csv");panel.ExportTo(Path.Combine(output,"eq-ui-export.svg"),".svg");
                var archived=Equity.EquityFiles.Decode(File.ReadAllBytes(Path.Combine(output,"eq-ui-export.json")));Require(Equity.EquityPlot.Points(archived.Job).Length==2,"exported graph roundtrip");
                var eqHistory=new Equity.EquityHistoryStore(Path.Combine(output,"eq-ui-history","EQ"));int unreadable;Require(eqHistory.List(out unreadable).Count>=2,"completed jobs auto-saved");
                using(var historyWindow=new EquityHistoryForm(eqHistory)){historyWindow.ShowRecord(archived);Require(historyWindow.Current.Job.Input.VillainText==villain.Text,"history opponent input preserved");}
                Find<CheckBox>(panel,"eqBetAction1").Checked=false;Require(panel.LastResult==null&&panel.PlotPointCount==0,"action role change clears graph");Require(!Find<Button>(panel,"eqSaveJson").Enabled,"stale export disabled");
                Find<CheckBox>(panel,"eqBetAction1").Checked=true;Pump(panel.ExecuteAsync());
                Require(panel.PlotPointCount==2,"plot restored after recalculation");
                villain.Text="KcKd4h5h";Require(panel.LastResult==null&&panel.PlotPointCount==0,"opponent edit invalidates table and graph");Pump(panel.ExecuteAsync());Require(panel.LastResult.Rows[0].Result.Equity==100,"updated input uses new villain");
                Find<CheckBox>(panel,"eqExactRiver").Checked=false;Find<NumericUpDown>(panel,"eqTrials").Value=2000000;
                Task running=panel.ExecuteAsync();Require(panel.IsBusy,"busy status");Require(!Find<Button>(panel,"eqSaveJson").Enabled&&!Find<Button>(panel,"eqHistory").Enabled,"busy export and history disabled");Require(!Find<Button>(panel,"eqStart").Enabled&&Find<Button>(panel,"eqStop").Enabled,"start/stop state");
                bool rejected=false;try{form.AcceptSelection(new JavaScriptSerializer().Serialize(payload));}catch(Exception){rejected=true;}Require(rejected,"incoming syntax blocked while calculating");
                panel.Cancel();Pump(running);Require(panel.LastResult==null&&!panel.IsBusy&&panel.PlotPointCount==0,"cancel has no completed result or graph");
                running=panel.ExecuteAsync();form.ApplyInput("JsTs2d");Pump(running);Require(panel.LastResult==null&&panel.PlotPointCount==0,"late result cannot overwrite new board or graph");
                form.Close();Application.DoEvents();
            }
            File.WriteAllText(Path.Combine(output,"equity-ui-passed.txt"),"PASS: selected syntax and board; exact EQ; empty row; input invalidation; busy controls; incoming condition rejection; cancel; stale board guard; bet frequency; auto scatter; graph invalidation.");
        }
    }
}
