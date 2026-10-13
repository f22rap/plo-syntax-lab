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
        static void WaitForEq(MainForm form,EquityPanel panel,Task task){var timer=Stopwatch.StartNew();while(!panel.IsBusy&&!task.IsCompleted){Application.DoEvents();System.Threading.Thread.Sleep(10);if(timer.Elapsed.TotalSeconds>120)throw new Exception("EQ phase did not start");}}
        public static void Run(string output){
            using(var form=new MainForm(new HistoryStore(Path.Combine(output,"eq-ui-history")))){
                form.UseDefaults=false;form.IncludeAll=false;
                var payload=new LabSelection{app="PLO Syntax Lab",game="PLO4",board=new[]{"2c","3d","7h","9s","Tc"},ranges=new[]{new LabRange{label="AA",syntax="AA",count=1},new LabRange{label="no kings",syntax="KK",count=1}}};
                form.AcceptSelection(new JavaScriptSerializer().Serialize(payload));form.Show();var mainTabs=form.Controls.OfType<TabControl>().First();mainTabs.SelectedIndex=0;Application.DoEvents();Require(mainTabs.TabPages.Count==2&&!mainTabs.TabPages.Cast<TabPage>().Any(p=>p.Text=="EQ"),"no separate EQ tab");
                var panel=form.EquityView;var villain=Find<TextBox>(panel,"eqVillainRange");
                var pathBox=Find<TextBox>(panel,"eqVillainFilePath");Require(pathBox.Text.Contains("未読込")&&pathBox.Visible,"source path placeholder visible before import");
                Require(panel.Controls.Find("eqHeroMode",true).Length==0&&panel.Controls.Find("eqHeroRange",true).Length==0&&panel.Controls.Find("eqImportHero",true).Length==0,"hero source is fixed with no manual or uniform controls");
                villain.Text="KcKd4h5h@25%,Jc8hKhQh@75%";Find<TextBox>(panel,"eqSeed").Text="123";
                var actionPaths=new[]{Path.Combine(output,"eq-check.csv"),Path.Combine(output,"eq-bet.csv")};
                File.WriteAllText(actionPaths[0],"hand,weight\nAcAd4c5d,0.2");File.WriteAllText(actionPaths[1],"hand,weight\nAcAd4c5d,0.8");
                string sourceFolder=Path.Combine(output,"相手レンジ 保存先","long-folder-name-for-path-display");Directory.CreateDirectory(sourceFolder);
                string sourceFile=Path.Combine(sourceFolder,"opponent-weighted-range.csv");File.WriteAllText(sourceFile,"hand,frequency,percent,weight,ev,ev_diff,variations\nKcKd4h5h,1,100,0.25,0,0,1\nJc8hKhQh,1,100,0.75,0,0,1");
                Pump(panel.ImportRangeFileAsync(sourceFile));Require(pathBox.Text==Path.GetFullPath(sourceFile)&&pathBox.ReadOnly&&pathBox.Multiline&&pathBox.WordWrap,"successful import displays full source path in wrapped copyable field");
                Require(Find<Label>(panel,"eqVillainImportStatus").Text.Contains("読込済み")&&villain.Text.Contains("@0.25"),"import status and weighted range updated together");
                var initialSize=form.Size;form.Size=form.MinimumSize;Application.DoEvents();
                for(Control child=pathBox;child!=panel;child=child.Parent)Require(child.Parent.ClientRectangle.Contains(child.Bounds),"source path is not clipped by its layout ancestors at minimum window size");
                Require(pathBox.Height>=2*pathBox.Font.Height,"path field has room for wrapped lines");form.Size=initialSize;Application.DoEvents();
                string originalRange=villain.Text;string invalidFile=Path.Combine(sourceFolder,"invalid.csv");File.WriteAllText(invalidFile,"hand,weight\nBAD,1");Pump(panel.ImportRangeFileAsync(invalidFile));
                Require(pathBox.Text==Path.GetFullPath(sourceFile)&&villain.Text==originalRange&&Find<Label>(panel,"eqVillainImportStatus").Text.Contains("読み込めません"),"failed import keeps last successful path and shows local error");
                Pump(panel.ImportRangeFileAsync(sourceFile));
                string sourceFile2=Path.Combine(sourceFolder,"opponent-second.csv");File.WriteAllText(sourceFile2,File.ReadAllText(sourceFile));
                Pump(panel.ImportRangeFilesAsync(new[]{sourceFile,sourceFile2}));
                Require(pathBox.Text==string.Join(Environment.NewLine,new[]{Path.GetFullPath(sourceFile),Path.GetFullPath(sourceFile2)})&&Find<Label>(panel,"eqVillainImportStatus").Text.Contains("2ファイル読込済み"),"multiple opponent CSV paths and file count visible");
                string mergedRange=villain.Text,mergedPaths=pathBox.Text;Pump(panel.ImportRangeFilesAsync(new[]{sourceFile,invalidFile}));
                Require(villain.Text==mergedRange&&pathBox.Text==mergedPaths,"failure in second file cannot partially replace merged input");
                Pump(panel.ImportRangeFilesAsync(new[]{sourceFile,sourceFile2}));
                form.LoadCsvs(actionPaths);form.SetCsvLabels("check","bet 75%");Find<CheckBox>(panel,"eqCommonScale").Checked=true;
                var request=panel.CaptureForComparison();Require(request.HeroMode==Equity.EquityHeroMode.AllActions&&request.HeroText==null&&request.ActionPaths.SequenceEqual(actionPaths),"request always uses all comparison CSVs");
                Require(request.RangeInputPaths.SequenceEqual(new[]{Path.GetFullPath(sourceFile),Path.GetFullPath(sourceFile2)}),"every opponent source is included in snapshot and export protection");
                Require(Find<Label>(panel,"eqHeroSummary").Text.Contains("2ファイル"),"summed CSV source is visible");
                Require(panel.Controls.Find("eqBetAction1",true).Length==0&&request.BetActions.Length==0,"all actions automatic with no bet selection controls");
                Require(Find<Label>(panel,"eqAction0").Visible&&Find<Label>(panel,"eqAction1").Visible&&Find<Label>(panel,"eqAction0").ForeColor!=Find<Label>(panel,"eqAction1").ForeColor,"action legend uses separate colors");
                Pump(panel.ExecuteAsync());Require(panel.LastResult!=null&&panel.ResultRowCount==2,"batch result rows without any bet selection");Require(Math.Abs(panel.LastResult.Rows[0].Result.Equity-25)<1e-10,"exact 25% displayed");Require(panel.LastResult.Rows[1].Result==null,"empty syntax remains empty");
                form.SaveSnapshot(Path.Combine(output,"equity-preview.png"));
                var extra=new LabSelection{app="PLO Syntax Lab",game="PLO4",board=payload.board,ranges=new[]{new LabRange{label="same hand",syntax="AcAd4c5d",count=1}}};
                form.AcceptSelection(new JavaScriptSerializer().Serialize(extra));Find<CheckBox>(panel,"eqCommonScale").Checked=true;
                form.IncludeAll=true;Require(Find<CheckBox>(panel,"eqCommonScale").Checked,"condition changes preserve common-scale confirmation");form.IncludeAll=false;
                int initialHeight=panel.InputHeight;Find<Button>(panel,"eqDetailsToggle").PerformClick();Require(panel.InputHeight>initialHeight,"detail settings expand");Find<Button>(panel,"eqDetailsToggle").PerformClick();Require(panel.InputHeight==initialHeight,"details collapse");
                Pump(form.ExecuteAsync());Require(form.LastResult!=null&&panel.LastResult!=null,"one execution calculates both results");Require(panel.PlotPointCount==4,"two conditions times two action series");
                Require(mainTabs.SelectedIndex==0&&Find<TableLayoutPanel>(form,"comparisonAndEqGraphs").Visible&&panel.PlotView.Visible,"comparison and EQ charts visible together");
                Require(Math.Abs(panel.LastResult.Rows[0].Frequency.Percent[1]-80)<1e-9,"80% bet frequency");
                form.SaveSnapshot(Path.Combine(output,"equity-scatter-preview.png"));
                Require(Find<Button>(form,"eqSaveCsv").Enabled&&Find<Button>(form,"eqSaveSvg").Enabled,"completed exports enabled");
                panel.ExportTo(Path.Combine(output,"eq-ui-export.json"),".json");panel.ExportTo(Path.Combine(output,"eq-ui-export.csv"),".csv");panel.ExportTo(Path.Combine(output,"eq-ui-export.svg"),".svg");
                var archived=Equity.EquityFiles.Decode(File.ReadAllBytes(Path.Combine(output,"eq-ui-export.json")));Require(Equity.EquityPlot.Points(archived.Job).Length==4,"exported graph roundtrip");
                var eqHistory=new Equity.EquityHistoryStore(Path.Combine(output,"eq-ui-history","EQ"));int unreadable;Require(eqHistory.List(out unreadable).Count>=2,"completed jobs auto-saved");
                using(var historyWindow=new EquityHistoryForm(eqHistory)){historyWindow.ShowRecord(archived);Require(historyWindow.Current.Job.Input.VillainText==villain.Text,"history opponent input preserved");}
                Find<TextBox>(panel,"eqSeed").Text="124";Require(panel.LastResult==null&&panel.PlotPointCount==0,"seed change clears graph");Require(!Find<Button>(form,"eqSaveJson").Enabled,"stale export disabled");
                Pump(panel.ExecuteAsync());
                Require(panel.PlotPointCount==4,"plot restored after recalculation");
                villain.Text="KcKd4h5h";Require(panel.LastResult==null&&panel.PlotPointCount==0,"opponent edit invalidates table and graph");Pump(panel.ExecuteAsync());Require(panel.LastResult.Rows[0].Result.Equity==100,"updated input uses new villain");
                Find<CheckBox>(panel,"eqExactRiver").Checked=false;Find<NumericUpDown>(panel,"eqTrials").Value=2000000;
                Task running=form.ExecuteAsync();Require(form.IsBusy&&panel.PlotPointCount==0,"new comparison immediately clears old EQ graph");WaitForEq(form,panel,running);Require(panel.IsBusy,"busy status");Require(!Find<Button>(form,"eqSaveJson").Enabled&&!Find<Button>(form,"eqHistory").Enabled,"busy export and history disabled");Require(!Find<Button>(form,"runComparisonEq").Enabled&&Find<Button>(form,"cancelComparisonEq").Enabled,"shared start/stop state");
                bool rejected=false;try{form.AcceptSelection(new JavaScriptSerializer().Serialize(payload));}catch(Exception){rejected=true;}Require(rejected,"incoming syntax blocked while calculating");
                form.CancelExecution();Pump(running);Require(panel.LastResult==null&&!panel.IsBusy&&panel.PlotPointCount==0&&form.LastResult!=null,"shared cancel stops EQ and preserves completed comparison");
                running=form.ExecuteAsync();form.ApplyInput("JsTs2d");Pump(running);Require(panel.LastResult==null&&panel.PlotPointCount==0,"late result cannot overwrite new board or graph");
                Require(!form.IsBusy&&villain.Enabled,"inputs restored after canceled sequence");
                form.AcceptSelection(new JavaScriptSerializer().Serialize(payload));form.LoadCsvs(actionPaths);Find<CheckBox>(panel,"eqCommonScale").Checked=true;
                Find<CheckBox>(panel,"eqExactRiver").Checked=true;
                File.WriteAllText(actionPaths[1],"hand,weight\nBAD,1");Pump(form.ExecuteAsync());Require(form.LastResult==null&&panel.LastResult==null&&panel.PlotPointCount==0,"comparison error prevents EQ calculation");
                File.WriteAllText(actionPaths[1],"hand,weight\nAcAd4c5d,0.8");villain.Text="invalid";Pump(form.ExecuteAsync());Require(form.LastResult!=null&&panel.LastResult==null&&panel.PlotPointCount==0,"EQ failure retains comparison with no stale graph");
                form.Close();Application.DoEvents();
            }
            File.WriteAllText(Path.Combine(output,"equity-ui-passed.txt"),"PASS: selected syntax and board; exact EQ; empty row; input invalidation; busy controls; incoming condition rejection; cancel; stale board guard; all action frequencies; auto scatter; graph invalidation; unified input; fixed CSV-sum hero; one-click comparison and EQ; shared cancel; comparison failure; EQ failure; automatic colored action series; collapsible settings.");
        }
    }
}
