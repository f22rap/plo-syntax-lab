using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlopCommands.Research;

namespace FlopCommands {
    public sealed partial class ResearchPanel {
        TableLayoutPanel researchRoot;
        readonly FlowLayoutPanel runOptions=ResearchUi.Bar(),runButtons=ResearchUi.Bar(),exports=ResearchUi.Bar();
        readonly CheckBox ahi=new CheckBox{Text="Ahiのみ",Checked=true,AutoSize=true,Name="researchAhiOnly"},missing=new CheckBox{Text="syntax未設定を欠損として続行",AutoSize=true},frequencyOnly=new CheckBox{Text="相手未登録は頻度のみ",AutoSize=true};
        readonly NumericUpDown trials=new NumericUpDown{Minimum=1,Maximum=2000000,Value=50000,Width=95,Name="researchTrials"};
        readonly TextBox seed=new TextBox{Text="7123",Width=86,Name="researchSeed"};
        readonly ComboBox grouping=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=165,Name="researchGrouping"};
        readonly ResearchResultsView results=new ResearchResultsView();
        Button startRun,stopRun,toggleResults;
        CancellationTokenSource researchCancellation;
        bool externalBusy,showResults;
        public event Action<bool> BusyChanged;
        public event Action InputHeightChanged;
        public bool IsBusy {get{return researchCancellation!=null;}}
        public int InputHeight {get{return researchRoot==null?196:(int)researchRoot.RowStyles.Cast<RowStyle>().Sum(r=>r.Height);}}
        public ResearchRunResult LastRun {get;private set;}
        public ResearchResultsView ResultView {get{return results;}}
        public string LastSavedPath {get;private set;}
        void InstallRunControls(TableLayoutPanel root){
            researchRoot=root;var settings=ResearchUi.Rows(38,38);settings.Padding=System.Windows.Forms.Padding.Empty;
            grouping.Items.AddRange(new object[]{"スート × ペア","スート構成","ペア構成","A以外のランク構成"});grouping.SelectedIndex=0;
            runOptions.Controls.Add(ahi);runOptions.Controls.Add(grouping);runOptions.Controls.Add(new Label{Text="試行数",AutoSize=true,Padding=new Padding(5,5,0,0)});runOptions.Controls.Add(trials);runOptions.Controls.Add(new Label{Text="シード",AutoSize=true,Padding=new Padding(5,5,0,0)});runOptions.Controls.Add(seed);runOptions.Controls.Add(frequencyOnly);runOptions.Controls.Add(missing);settings.Controls.Add(runOptions,0,0);
            startRun=new Button{Text="登録ボードを一括調査",AutoSize=true,Height=32,Name="researchStart"};startRun.Click+=async(s,e)=>await ExecuteSelectedAsync();stopRun=ResearchUi.Button("調査を中止",()=>Cancel());stopRun.Name="researchCancel";toggleResults=ResearchUi.Button("結果を表示",()=>{showResults=!showResults;ResizeResults();});
            runButtons.Controls.Add(startRun);runButtons.Controls.Add(stopRun);runButtons.Controls.Add(toggleResults);runButtons.Controls.Add(ResearchUi.Button("調査結果JSONを開く",LoadResult));settings.Controls.Add(runButtons,0,1);root.Controls.Add(settings,0,2);root.Controls.Add(results,0,4);
            foreach(string format in new[]{".csv",".json",".svg",".md"}){string ext=format;exports.Controls.Add(ResearchUi.Button(ext.ToUpperInvariant()+"保存",()=>ExportRun(ext)));}root.Controls.Add(exports,0,5);
            sets.SelectedIndexChanged+=(s,e)=>InputUpdated();catalogs.SelectedIndexChanged+=(s,e)=>InputUpdated();ahi.CheckedChanged+=(s,e)=>InputUpdated();missing.CheckedChanged+=(s,e)=>InputUpdated();frequencyOnly.CheckedChanged+=(s,e)=>InputUpdated();grouping.SelectedIndexChanged+=(s,e)=>InputUpdated();trials.ValueChanged+=(s,e)=>InputUpdated();seed.TextChanged+=(s,e)=>InputUpdated();ResizeResults();RefreshRunControls();
        }
        void InputUpdated(){if(IsBusy)return;LastRun=null;LastSavedPath=null;results.SetData(null);showResults=false;ResizeResults();RefreshRunControls();}
        void ResizeResults(){if(researchRoot==null)return;bool visible=showResults&&LastRun!=null;results.Visible=visible;exports.Visible=visible;researchRoot.RowStyles[4].Height=visible?620:0;researchRoot.RowStyles[5].Height=visible?38:0;toggleResults.Text=visible?"結果を閉じる":"結果を表示";if(InputHeightChanged!=null)InputHeightChanged();}
        void RefreshRunControls(){if(startRun==null)return;bool free=!IsBusy&&!externalBusy;setBar.Enabled=free&&workspace!=null;catalogBar.Enabled=free;runOptions.Enabled=free;startRun.Enabled=free&&workspace!=null&&SelectedSet()!=null&&SelectedCatalog()!=null;stopRun.Enabled=IsBusy;toggleResults.Enabled=LastRun!=null&&!IsBusy;exports.Enabled=free&&LastRun!=null;foreach(Control c in runButtons.Controls)if(c!=stopRun&&c!=startRun&&c!=toggleResults)c.Enabled=free;}
        public void SetExternalBusy(bool value){externalBusy=value;RefreshRunControls();}
        public void Cancel(){if(researchCancellation!=null){stopRun.Enabled=false;status.Text="調査を中止しています…";researchCancellation.Cancel();}}
        ResearchRunSettings CaptureSettings(){uint value;if(!uint.TryParse(seed.Text,out value))throw new ArgumentException("シードは0〜4,294,967,295の整数で指定してください。");return new ResearchRunSettings{AhiOnly=ahi.Checked,AllowMissingDefinitions=missing.Checked,AllowFrequencyOnly=frequencyOnly.Checked,Trials=(int)trials.Value,Seed=value,GroupBy=new[]{"suit_pair","suit","pair","side_ranks"}[grouping.SelectedIndex]};}
        async Task ExecuteSelectedAsync(){try{if(SelectedSet()==null||SelectedCatalog()==null)throw new ArgumentException("カタログと条件セットを選択してください。");await ExecuteResearchAsync(SelectedCatalog(),SelectedSet(),CaptureSettings());}catch(Exception ex){status.Text=ex.Message;}}
        public async Task ExecuteResearchAsync(ResearchCatalog catalog,ResearchConditionSet set,ResearchRunSettings settings){
            if(IsBusy||externalBusy)throw new InvalidOperationException("他の計算が終わってから開始してください。");
            catalog=ResearchJson.Clone(catalog);set=ResearchJson.Clone(set);settings=ResearchJson.Clone(settings);ResearchRun.ValidateRequest(catalog,set,settings);
            LastRun=null;LastSavedPath=null;results.SetData(null);showResults=false;ResizeResults();researchCancellation=new CancellationTokenSource();var source=researchCancellation;var token=source.Token;RefreshRunControls();if(BusyChanged!=null)BusyChanged(true);var timer=Stopwatch.StartNew();
            var progress=new Progress<ResearchProgress>(p=>{if(!IsDisposed&&ReferenceEquals(source,researchCancellation)&&!source.IsCancellationRequested)status.Text=p.Completed+" / "+p.Total+" ボード · "+p.Message;});
            try{
                var completed=await Task.Run(()=>ResearchRun.Execute(catalog,set,settings,token,p=>{if(timer.ElapsedMilliseconds>=100||p.Completed==p.Total){timer.Restart();((IProgress<ResearchProgress>)progress).Report(p);}}));if(IsDisposed)return;
                LastRun=completed;results.SetData(completed);showResults=true;ResizeResults();stopRun.Enabled=false;
                string saveMessage;try{LastSavedPath=ResearchFiles.SaveHistory(completed,Path.Combine(Path.GetDirectoryName(store.FilePath),"Runs"));saveMessage="結果を自動保存しました。";}catch(Exception ex){saveMessage="結果の自動保存に失敗："+ex.Message+" 手動保存で再試行できます。";}
                status.Text=completed.Catalog.Name+" / "+completed.ConditionSet.Name+" · "+completed.Status+" · "+completed.CompletedSpots+" / "+completed.TargetSpots+"ボード · "+saveMessage+" "+completed.Error;
            }catch(OperationCanceledException){if(!IsDisposed)status.Text="調査を中止しました。";}
            catch(Exception ex){if(!IsDisposed)status.Text="一括調査に失敗："+ex.Message;}
            finally{if(ReferenceEquals(researchCancellation,source))researchCancellation=null;source.Dispose();if(!IsDisposed){RefreshRunControls();if(BusyChanged!=null)BusyChanged(false);}}
        }
        void LoadResult(){using(var d=new OpenFileDialog{Filter="一括調査の結果JSON|*.json",InitialDirectory=Path.Combine(Path.GetDirectoryName(store.FilePath),"Runs")})if(d.ShowDialog(this)==DialogResult.OK){var loaded=ResearchFiles.Read(d.FileName);LastRun=loaded;LastSavedPath=d.FileName;results.SetData(loaded);showResults=true;ResizeResults();RefreshRunControls();status.Text="保存結果："+loaded.Catalog.Name+" / "+loaded.ConditionSet.Name+" · "+loaded.Status+" · "+loaded.CompletedSpots+" / "+loaded.TargetSpots+"ボード（保存時の入力・分類設定）";}}
        void ExportRun(string extension){if(LastRun==null||IsBusy||externalBusy)throw new InvalidOperationException("保存できる調査結果がありません。");using(var d=new SaveFileDialog{Filter="調査結果|*"+extension,FileName="research-"+LastRun.Id+extension,OverwritePrompt=true})if(d.ShowDialog(this)==DialogResult.OK){ResearchFiles.Export(LastRun,d.FileName,extension,results.Grouped,store.FilePath);status.Text="保存しました："+d.FileName;}}
        protected override void Dispose(bool disposing){if(disposing&&researchCancellation!=null)researchCancellation.Cancel();base.Dispose(disposing);}
    }
}
