using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using FlopCommands.Research;

namespace FlopCommands {
    // Run only on a Windows desktop; compiling does not verify UI behavior.
    static class ResearchUiTests {
        static void Require(bool value,string why){if(!value)throw new Exception("Research UI: "+why);}
        static void Pump(Task task){var timer=Stopwatch.StartNew();while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(10);if(timer.Elapsed.TotalSeconds>120)throw new Exception("Research UI timed out");}task.GetAwaiter().GetResult();Application.DoEvents();}
        static ResearchCatalog SampleCatalog(string root){
            Directory.CreateDirectory(root);var catalog=new ResearchCatalog{Name="一括調査テスト",Scenario=new ResearchScenario{HeroPosition="BB",VillainPosition="BTN",StackBb="100",PotType="SRP",ActionHistory="BTN open, BB call",SolutionContext="synthetic"}};
            foreach(string board in new[]{"AsKd7c","Ah8h3c"}){
                string check=Path.Combine(root,board+"_check.csv"),bet=Path.Combine(root,board+"_bet.csv"),opponent=Path.Combine(root,board+"_opponent.csv");
                File.WriteAllText(check,"hand,weight\nAcAd4c5d,0.25\n");File.WriteAllText(bet,"hand,weight\nAcAd4c5d,0.75\n");File.WriteAllText(opponent,"hand,weight\nKcKh6h9d,1\n");
                catalog.Spots.Add(ResearchValidation.ValidateFiles(new ResearchSpot{Board=board,Node="BB first",AllActionsCommonScale=true,OpponentCommonScale=true,Actions=new List<ResearchAction>{new ResearchAction{Label="Check",Kind="check",Source=new ResearchSource{Path=check}},new ResearchAction{Label="Bet",Kind="bet",PotFraction="0.33",Source=new ResearchSource{Path=bet}}},Opponents=new List<ResearchSource>{new ResearchSource{Path=opponent}}},CancellationToken.None));
            }return catalog;
        }
        static void GeneratedSelection(MainForm owner){
            var draft=new ResearchConditionSet{Name="デフォルト選択のUIテスト"};
            using(var picker=new ResearchPresetPicker(draft)){
                picker.Show(owner);Application.DoEvents();var grid=(DataGridView)picker.Controls.Find("researchPresetGrid",true).Single();var search=(TextBox)picker.Controls.Find("researchPresetSearch",true).Single();
                Require(grid.Rows.Count==185,"185 defaults visible");Require(grid.Rows.Cast<DataGridViewRow>().Count(r=>r.Cells[0].ReadOnly)==26,"unsupported defaults disabled");
                search.Text="Msetall";Application.DoEvents();Require(grid.Rows.Count==1&&!grid.Rows[0].Cells[0].ReadOnly,"board-independent middle set selectable");grid.Rows[0].Cells[0].Value=true;
                search.Text="AA SD BDSD";Application.DoEvents();grid.Rows[0].Cells[0].Value=true;Require(picker.SelectedPresetIds.SequenceEqual(new[]{"set.middle.all","aa.sd.bdsd"}),"checks survive search in canonical order");
                search.Clear();Application.DoEvents();Require(grid.Rows.Cast<DataGridViewRow>().Count(r=>Convert.ToBoolean(r.Cells[0].Value))==2,"checked state restored after search");picker.Close();
            }
            Require(draft.Conditions.Count==0,"picker cancel leaves original unchanged");
            ResearchConditionSet saved;
            using(var editor=new ResearchConditionEditor(draft)){
                editor.Show(owner);Application.DoEvents();Require(editor.AddDefaults(new[]{"set.top.all","set.middle.all","aa.sd.bdsd"})==3,"bulk add defaults");string id=editor.Value.Conditions[0].Id;
                ((TextBox)editor.Controls.Find("researchConditionLabel",true).Single()).Text="改名したトップセット";((CheckBox)editor.Controls.Find("researchConditionEnabled",true).Single()).Checked=false;
                var list=(ListBox)editor.Controls.Find("researchConditionList",true).Single();list.SelectedIndex=1;Application.DoEvents();Require(editor.Value.Conditions[0].Id==id&&editor.Value.Conditions[0].Generator.PresetId=="set.top.all"&&!editor.Value.Conditions[0].Enabled,"edit retains stable identity and generator");
                Require(editor.AddDefaults(new[]{"set.top.all"})==0,"UI duplicate selection ignored");
                using(var picker=new ResearchPresetPicker(editor.Value)){picker.Show(editor);Application.DoEvents();var search=(TextBox)picker.Controls.Find("researchPresetSearch",true).Single();search.Text="Tsetall";var grid=(DataGridView)picker.Controls.Find("researchPresetGrid",true).Single();Require(grid.Rows.Count==1&&grid.Rows[0].Cells[0].ReadOnly&&Convert.ToBoolean(grid.Rows[0].Cells[0].Value),"renamed and disabled existing condition remains selected");picker.Close();}
                ((Button)editor.Controls.Find("researchConditionSave",true).Single()).PerformClick();Application.DoEvents();Require(editor.DialogResult==DialogResult.OK,"save accepts generated conditions");saved=editor.Value;
            }
            Require(draft.Conditions.Count==0,"editor does not mutate input object");owner.ResearchView.SaveSet(saved);
            using(var editor=new ResearchConditionEditor(saved)){editor.Show(owner);Application.DoEvents();((TextBox)editor.Controls.Find("researchConditionLabel",true).Single()).Text="キャンセルする編集";editor.Close();}
            Require(saved.Conditions[0].Label=="改名したトップセット","cancelled edit retains saved label");
        }
        public static void Run(string output){
            string history=Path.Combine(output,"research-ui-"+Guid.NewGuid().ToString("N"));var store=new ResearchStore(Path.Combine(history,"Research"));
            var set=new ResearchConditionSet{Name="自分の調査条件",Conditions=new List<ResearchCondition>{new ResearchCondition{Label="AA",Syntax=" AA "},new ResearchCondition{Label="ボード別",Kind="per_board",ByBoard=new Dictionary<string,string>{{"AsKd7c","AK"},{"Ah8h3c","A8"}}}}};
            using(var form=new MainForm(new HistoryStore(history))){
                form.UseDefaults=false;form.ApplyInput("7cAsKd");form.Show();Application.DoEvents();
                Require(form.ResearchView.Parent!=null,"registration controls in aggregation");
                var tabs=form.Controls.OfType<TabControl>().Single();Require(tabs.TabPages.Count==2&&tabs.TabPages[0].Text=="集計","no additional top-level tab");
                form.ResearchView.SaveSet(set);Require(form.ResearchView.SetCount==1&&store.Load().Sets.Count==1,"save set");
                form.ApplyResearchConditions(store.Load().Sets.Single());Require(!form.UseDefaults&&!form.IncludeAll&&form.ChoiceCount==2,"explicit set with no implicit all hands");
                var captured=form.CaptureResearchConditions();Require(captured.Conditions.Count==2&&captured.Conditions[0].ByBoard.Values.Single()==" AA ","syntax preserved");
                Require(form.CurrentCode.Contains("AK"),"resolved syntax reaches comparison command");
                form.ApplyInput("AhKd7c");string code=form.CurrentCode;bool rejected=false;try{form.ApplyResearchConditions(set);}catch(ArgumentException){rejected=true;}Require(rejected&&form.CurrentCode==code,"unmapped board not partially applied");
                form.ApplyInput("Ah8h3c");form.ApplyResearchConditions(set);Require(form.CaptureResearchConditions().Conditions[1].ByBoard.Values.Single()=="A8","second board definition");
                using(var editor=new ResearchConditionEditor(set)){editor.Show(form);Application.DoEvents();Require(editor.Value!=set,"isolated editor clone");editor.Close();}
                using(var editor=new ResearchCatalogEditor(new ResearchCatalog())){editor.Show(form);Application.DoEvents();editor.Close();}
                using(var editor=new ResearchSpotEditor(new ResearchSpot())){editor.Show(form);Application.DoEvents();editor.Close();}
                var catalog=SampleCatalog(Path.Combine(history,"csv"));form.ResearchView.SaveCatalog(catalog);int inputHeight=form.ResearchView.InputHeight;
                var batch=form.ResearchView.ExecuteResearchAsync(catalog,set,new ResearchRunSettings{Trials=1000,Seed=7123,GroupBy="pair"});Require(form.ResearchView.IsBusy,"batch busy state");Pump(batch);
                Require(form.ResearchView.LastRun!=null&&form.ResearchView.LastRun.CompletedSpots==2,"two boards completed");Require(form.ResearchView.ResultView.PlotPoints==4,"all actions plotted for both boards");Require(form.ResearchView.InputHeight>inputHeight,"results expand aggregation layout");Require(File.Exists(form.ResearchView.LastSavedPath),"batch result auto-saved");
                var mode=(ComboBox)form.ResearchView.Controls.Find("researchResultMode",true).Single();mode.SelectedIndex=1;Require(form.ResearchView.ResultView.PlotPoints==2,"category scatter uses grouped results");
                batch=form.ResearchView.ExecuteResearchAsync(catalog,set,new ResearchRunSettings{Trials=2000000,Seed=7123,GroupBy="pair"});form.ResearchView.Cancel();Pump(batch);Require(form.ResearchView.LastRun.Status=="cancelled"&&!form.ResearchView.IsBusy,"batch cancel and restore controls");
                GeneratedSelection(form);
                form.Close();Application.DoEvents();
            }
            using(var form=new MainForm(new HistoryStore(history))){Require(form.ResearchView.SetCount==2,"restored after reopening");var generated=store.Load().Sets.Single(s=>s.Conditions.Any(c=>c.Kind=="generated"));Require(generated.Conditions[0].Label=="改名したトップセット"&&!generated.Conditions[0].Enabled,"generated label and checkbox restored after reopening");}
            File.WriteAllText(Path.Combine(output,"research-ui-passed.txt"),"PASS: aggregation controls, persistence, explicit syntax, board mapping, missing-board rejection, editor construction, reopening.");
        }
    }
}
