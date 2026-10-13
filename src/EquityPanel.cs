using System;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlopCommands.Equity;

namespace FlopCommands {
    public sealed class EquityPanel : UserControl {
        readonly Label summary=new Label(),status=new Label(),heroInfo=new Label(),villainInfo=new Label();
        readonly ComboBox villainUnit=new ComboBox();
        readonly TextBox villainText=new TextBox(),villainPath=new TextBox(),seed=new TextBox();
        readonly NumericUpDown trials=new NumericUpDown(),exactLimit=new NumericUpDown();
        readonly CheckBox commonScale=new CheckBox(),exact=new CheckBox();
        readonly Button saveCsv=new Button(),saveJson=new Button(),saveSvg=new Button(),history=new Button();
        readonly EquityHistoryStore historyStore;EquityArchive archive;string[] villainSources=new string[0];
        readonly FlowLayoutPanel plotSettings=new FlowLayoutPanel();readonly Label[] actionLabels={new Label(),new Label(),new Label()};
        readonly EquityScatterChart scatter=new EquityScatterChart();
        readonly TableLayoutPanel inputRoot=new TableLayoutPanel(),footer=new TableLayoutPanel();readonly FlowLayoutPanel exports=new FlowLayoutPanel();readonly Button detailToggle=new Button();
        bool externalBusy;
        public Control PlotView {get{return scatter;}}
        public Control TableView {get{return table;}}
        public Control FooterView {get{return footer;}}
        public int InputHeight {get{return (int)Math.Ceiling(inputRoot.RowStyles.Cast<RowStyle>().Sum(row=>row.Height));}}
        public event Action InputHeightChanged;
        public event Action InputChanged;
        readonly DataGridView table=new DataGridView();readonly ProgressBar progress=new ProgressBar();
        readonly TableLayoutPanel inputs=new TableLayoutPanel();readonly FlowLayoutPanel settings=new FlowLayoutPanel();
        EquityJobRequest context;string contextKey="";int version;CancellationTokenSource cancellation;bool initializing=true;
        public event Action<bool> BusyChanged;
        public bool IsBusy {get{return cancellation!=null;}}
        public EquityJobResult LastResult {get;private set;}
        public string StatusText {get{return status.Text;}}
        public int PlotPointCount {get{return scatter.PointCount;}}
        public int ResultRowCount {get{return table.Rows.Count;}}
        static readonly Color Ink=Color.FromArgb(22,35,48),Muted=Color.FromArgb(94,108,120);
        public EquityPanel(EquityHistoryStore store=null){
            historyStore=store??new EquityHistoryStore(Path.Combine(HistoryStore.DefaultRoot,"EQ"));
            Dock=DockStyle.Fill;BackColor=Color.FromArgb(240,244,247);Padding=new Padding(24,0,24,0);Margin=Padding.Empty;Name="equityPanel";
            inputRoot.Dock=DockStyle.Fill;inputRoot.ColumnCount=1;inputRoot.RowCount=5;inputRoot.Margin=Padding.Empty;Controls.Add(inputRoot);
            inputRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,210));inputRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,72));inputRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,40));inputRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,30));inputRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,0));
            inputs.Dock=DockStyle.Fill;inputs.ColumnCount=2;inputs.RowCount=1;inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));inputRoot.Controls.Add(inputs,0,0);
            inputs.Controls.Add(HeroRangeBox(),0,0);
            inputs.Controls.Add(VillainRangeBox(),1,0);
            inputRoot.Controls.Add(VillainSourceRow(),0,1);
            villainText.Name="eqVillainRange";villainText.Text="random";
            plotSettings.Dock=DockStyle.Fill;plotSettings.WrapContents=false;plotSettings.AutoScroll=true;plotSettings.Name="eqActionLegend";plotSettings.Controls.Add(Caption("表示アクション（全CSV）："));
            for(int a=0;a<actionLabels.Length;a++){var label=actionLabels[a];label.Name="eqAction"+a;label.AutoSize=true;label.Visible=false;label.Padding=new Padding(0,6,12,0);label.ForeColor=EquityScatterChart.ActionColor(a);plotSettings.Controls.Add(label);}inputRoot.Controls.Add(plotSettings,0,2);
            detailToggle.Text="詳細設定 ▼";detailToggle.Name="eqDetailsToggle";detailToggle.AutoSize=true;detailToggle.Height=28;
            var info=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};info.Controls.Add(detailToggle);summary.AutoSize=true;summary.Padding=new Padding(8,4,0,0);summary.ForeColor=Muted;info.Controls.Add(summary);inputRoot.Controls.Add(info,0,3);
            settings.Dock=DockStyle.Fill;settings.WrapContents=true;settings.Visible=false;inputRoot.Controls.Add(settings,0,4);
            detailToggle.Click+=(s,e)=>{bool show=inputRoot.RowStyles[4].Height==0;settings.Visible=show;inputRoot.RowStyles[4].Height=show?60:0;detailToggle.Text=show?"詳細設定 ▲":"詳細設定 ▼";if(InputHeightChanged!=null)InputHeightChanged();};
            trials.Minimum=1;trials.Maximum=2000000;trials.Value=50000;trials.Increment=10000;trials.Width=110;trials.ThousandsSeparator=true;trials.Name="eqTrials";
            seed.Width=110;seed.Name="eqSeed";seed.MaxLength=10;
            exact.Text="リバーは全列挙";exact.Checked=true;exact.AutoSize=true;exact.Name="eqExactRiver";
            exactLimit.Minimum=1;exactLimit.Maximum=100000000;exactLimit.Value=5000000;exactLimit.Increment=1000000;exactLimit.Width=120;exactLimit.ThousandsSeparator=true;
            settings.Controls.Add(Caption("試行数／syntax"));settings.Controls.Add(trials);settings.Controls.Add(Caption("シード（空欄＝自動）"));settings.Controls.Add(seed);settings.Controls.Add(exact);settings.Controls.Add(Caption("全列挙の上限組数"));settings.Controls.Add(exactLimit);
            exports.Dock=DockStyle.Fill;exports.WrapContents=false;exports.AutoScroll=true;
            SetupSave(saveCsv,"EQ CSV保存","eqSaveCsv",".csv",exports);SetupSave(saveJson,"EQ JSON保存","eqSaveJson",".json",exports);SetupSave(saveSvg,"EQグラフSVG保存","eqSaveSvg",".svg",exports);
            history.Text="EQ履歴";history.Name="eqHistory";history.AutoSize=true;history.Height=32;history.Click+=(s,e)=>{using(var window=new EquityHistoryForm(historyStore))window.ShowDialog(this);};exports.Controls.Add(history);
            table.Dock=DockStyle.Fill;table.Name="eqResults";table.ReadOnly=true;table.AllowUserToAddRows=false;table.AllowUserToDeleteRows=false;table.RowHeadersVisible=false;table.AutoGenerateColumns=false;table.BackgroundColor=Color.White;table.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;table.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;table.DefaultCellStyle.NullValue="—";
            AddColumn("label","条件",150);AddColumn("syntax","syntax",170);AddColumn("eq","自分のEQ (%)",95);AddColumn("ci","95%区間 (%)",125);AddColumn("frequency","アクション頻度 (%)",220);AddColumn("method","方式",80);AddColumn("hero","自分の有効コンボ",115);AddColumn("villain","相手の有効コンボ",115);AddColumn("total","処理件数",100);AddColumn("seconds","計算秒",80);AddColumn("note","状態／理由",240);
            footer.Dock=DockStyle.Fill;footer.ColumnCount=1;footer.RowCount=3;footer.RowStyles.Add(new RowStyle(SizeType.Absolute,8));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
            progress.Dock=DockStyle.Fill;progress.Visible=false;status.Dock=DockStyle.Fill;status.ForeColor=Muted;footer.Controls.Add(progress,0,0);footer.Controls.Add(status,0,1);footer.Controls.Add(exports,0,2);
            villainText.TextChanged+=(s,e)=>InvalidateResult();seed.TextChanged+=(s,e)=>InvalidateResult();trials.ValueChanged+=(s,e)=>InvalidateResult();exact.CheckedChanged+=(s,e)=>InvalidateResult();exactLimit.ValueChanged+=(s,e)=>InvalidateResult();commonScale.CheckedChanged+=(s,e)=>InvalidateResult();
            initializing=false;UpdateHeroSummary();status.Text="相手レンジを指定し、「集計＋EQ計算」を押してください。";
        }
        static Label Caption(string text){return new Label{Text=text,AutoSize=true,Padding=new Padding(0,6,0,0),Margin=new Padding(0,0,7,0)};}
        Control HeroRangeBox(){
            var group=new GroupBox{Text="自分のアクション前レンジ（CSV合計）",Dock=DockStyle.Fill,Padding=new Padding(8),Margin=new Padding(0,0,8,6)};
            var rows=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};rows.RowStyles.Add(new RowStyle(SizeType.Absolute,74));rows.RowStyles.Add(new RowStyle(SizeType.Percent,100));rows.RowStyles.Add(new RowStyle(SizeType.Absolute,32));group.Controls.Add(rows);
            rows.Controls.Add(new Label{Text="集計に入力した全CSVを合計します。\r\n同じハンドのweightを加算し、各syntaxに該当するハンドのEQを計算します。\r\n全CSVのアクション頻度を色分けして表示します。",Dock=DockStyle.Fill,ForeColor=Ink},0,0);
            heroInfo.Name="eqHeroSummary";heroInfo.Dock=DockStyle.Fill;heroInfo.ForeColor=Muted;heroInfo.AutoEllipsis=true;rows.Controls.Add(heroInfo,0,1);
            commonScale.Text="選択CSVは全アクション・共通weight基準";commonScale.AutoSize=true;commonScale.Name="eqCommonScale";rows.Controls.Add(commonScale,0,2);
            return group;
        }
        Control VillainRangeBox(){
            var group=new GroupBox{Text="相手のアクション前レンジ",Dock=DockStyle.Fill,Padding=new Padding(8),Margin=new Padding(8,0,0,6)};
            var rows=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};rows.RowStyles.Add(new RowStyle(SizeType.Absolute,32));rows.RowStyles.Add(new RowStyle(SizeType.Percent,100));rows.RowStyles.Add(new RowStyle(SizeType.Absolute,36));rows.RowStyles.Add(new RowStyle(SizeType.Absolute,44));group.Controls.Add(rows);
            rows.Controls.Add(new Label{Text="例：AA**, KK**, AKQJ:ds @50%",Dock=DockStyle.Fill,ForeColor=Muted},0,0);
            villainText.Multiline=true;villainText.ScrollBars=ScrollBars.Vertical;villainText.Dock=DockStyle.Fill;villainText.Font=new Font("Consolas",10);rows.Controls.Add(villainText,0,1);
            var line=new FlowLayoutPanel{Dock=DockStyle.Fill};villainUnit.DropDownStyle=ComboBoxStyle.DropDownList;villainUnit.Items.AddRange(new object[]{"CSV重み 0〜1","CSV重み 0〜100"});villainUnit.SelectedIndex=0;villainUnit.Width=145;
            var import=new Button{Text="CSV読込（複数可）",AutoSize=true,Height=30,Name="eqImportVillain"};import.Click+=async(s,e)=>await ImportAsync();line.Controls.Add(import);line.Controls.Add(villainUnit);rows.Controls.Add(line,0,2);
            villainInfo.Name="eqVillainImportStatus";villainInfo.Dock=DockStyle.Fill;villainInfo.ForeColor=Muted;villainInfo.AutoEllipsis=true;villainInfo.Margin=Padding.Empty;villainInfo.Text="CSV未読込。テキスト入力またはレンジCSVを読み込んでください。";rows.Controls.Add(villainInfo,0,3);
            return group;
        }
        Control VillainSourceRow(){
            // Separate full-width row: never compress the path into the range
            // box's short status area. Wrapped text remains selectable/copyable.
            var sourceRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(3,0,3,0),Name="eqVillainSourceRow"};
            sourceRow.RowStyles.Add(new RowStyle(SizeType.Absolute,24));sourceRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            sourceRow.Controls.Add(new Label{Text="相手レンジCSVの保存先（全ファイルのフルパス）",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Ink,Margin=Padding.Empty},0,0);
            villainPath.Name="eqVillainFilePath";villainPath.AccessibleName="相手レンジCSVの保存先（全ファイルのフルパス）";villainPath.ReadOnly=true;villainPath.Multiline=true;villainPath.WordWrap=true;villainPath.ScrollBars=ScrollBars.Vertical;villainPath.Dock=DockStyle.Fill;villainPath.Margin=new Padding(0,0,0,6);villainPath.BackColor=Color.White;villainPath.ForeColor=Ink;villainPath.Text="未読込（「CSV読込（複数可）」から1個以上選択してください）";sourceRow.Controls.Add(villainPath,0,1);
            return sourceRow;
        }
        void AddColumn(string name,string title,int width){table.Columns.Add(new DataGridViewTextBoxColumn{Name=name,HeaderText=title,Width=width,SortMode=DataGridViewColumnSortMode.NotSortable});}
        void UpdateHeroSummary(){heroInfo.Text=context==null||context.ActionPaths.Length==0?"集計に使う全アクションのCSVを選択してください。":"合計対象："+context.ActionPaths.Length+"ファイル\r\n"+string.Join(" / ",context.ActionPaths.Select(Path.GetFileName))+"\r\n各CSVのweightを、そのまま加算します。";}
        public void SetContext(EquityJobRequest value){
            string[] pieces=value==null?new[]{"invalid"}:new[]{value.Board,value.Conditions.Length.ToString(),value.ActionPaths.Length.ToString(),value.ActionLabels.Length.ToString()}.Concat(value.Conditions.SelectMany(c=>new[]{c.Label,c.Syntax})).Concat(value.ActionPaths).Concat(value.ActionLabels).ToArray();
            string key=string.Concat(pieces.Select(p=>(p??"").Length+":"+p));if(key==contextKey)return;
            bool sameActions=context!=null&&value!=null&&context.ActionPaths.SequenceEqual(value.ActionPaths)&&context.ActionLabels.SequenceEqual(value.ActionLabels);
            contextKey=key;context=value==null?null:value.Snapshot();
            for(int a=0;a<actionLabels.Length;a++){
                bool visible=context!=null&&a<context.ActionPaths.Length;
                actionLabels[a].Visible=visible;
                if(visible)actionLabels[a].Text="● "+context.ActionLabels[a]+" (CSV"+(a+1)+")";
            }
            if(!sameActions)commonScale.Checked=false;
            InvalidateResult();table.Rows.Clear();if(context!=null)foreach(var c in context.Conditions)table.Rows.Add(c.Label,c.Syntax,null,null,null,null,null,null,null,null,"未計算");UpdateHeroSummary();
            summary.Text=context==null?"ボードと条件の準備ができていません。":context.Board+" · "+context.Conditions.Length+"条件 · アクションCSV "+context.ActionPaths.Length+"個";

        }
        void InvalidateResult(){if(initializing)return;version++;LastResult=null;archive=null;SaveState();scatter.SetData(null);table.DefaultCellStyle.ForeColor=Muted;if(IsBusy)cancellation.Cancel();if(InputChanged!=null)InputChanged();status.Text="入力・条件が変更されました。EQを再計算してください。";}
        void Busy(bool value){bool locked=value||externalBusy;inputs.Enabled=!locked;settings.Enabled=!locked;plotSettings.Enabled=!locked;detailToggle.Enabled=!locked;progress.Visible=value;history.Enabled=!locked;SaveState();if(BusyChanged!=null)BusyChanged(value);}
        public void SetExternalBusy(bool value){externalBusy=value;Busy(IsBusy);}
        public void NotifyComparisonStopped(string message){InvalidateResult();table.Rows.Clear();status.Text=message;}
        public void ResetForComparison(){InvalidateResult();table.Rows.Clear();status.Text="集計終了後にEQを計算します。";}
        public EquityJobRequest CaptureForComparison(){try{return Request();}catch(Exception ex){status.Text="EQ入力を確認してください："+ex.Message;return null;}}
        public void Cancel(){if(cancellation!=null){cancellation.Cancel();status.Text="中止しています…";}}
        EquityJobRequest Request(){
            if(context==null)throw new ArgumentException("ボードと条件を選択してください。");var r=context.Snapshot();r.HeroMode=EquityHeroMode.AllActions;r.RangeInputPaths=(string[])villainSources.Clone();r.HeroText=null;r.VillainText=villainText.Text;r.CommonScaleConfirmed=commonScale.Checked;r.BetActions=new bool[0];
            uint parsed;r.Settings=new EquitySettings{Trials=(int)trials.Value,ExactRiver=exact.Checked,MaxExactComparisons=(long)exactLimit.Value};
            if(seed.Text.Trim().Length>0){if(!uint.TryParse(seed.Text.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out parsed))throw new ArgumentException("シードは0〜4,294,967,295の整数にしてください。");r.Settings.Seed=parsed;}return r;
        }
        public Task ExecuteAsync(){if(IsBusy||externalBusy||!Enabled)return Task.FromResult(0);var request=CaptureForComparison();return request==null?Task.FromResult(0):ExecutePreparedAsync(request,CancellationToken.None);}
        public async Task ExecutePreparedAsync(EquityJobRequest request,CancellationToken token){
            if(IsBusy||request==null||token.IsCancellationRequested)return;if(InvokeRequired)throw new InvalidOperationException("画面から開始してください。");
            if(!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            int job=++version;var source=CancellationTokenSource.CreateLinkedTokenSource(token);cancellation=source;LastResult=null;archive=null;scatter.SetData(null);table.Rows.Clear();Busy(true);status.Text="アクション前レンジを展開しています…";progress.Style=ProgressBarStyle.Marquee;
            var reporter=new Progress<EquityJobProgress>(p=>{if(IsDisposed||job!=version||source.IsCancellationRequested||!ReferenceEquals(cancellation,source))return;
                string stage=p.Calculation==null?"レンジ準備":p.Calculation.Phase=="prepare"||p.Calculation.Phase=="compatible"?"対戦組を確認":p.Calculation.Phase=="exact"?"全列挙":"試行計算";
                status.Text=Math.Min(p.ConditionIndex+1,p.ConditionCount)+" / "+p.ConditionCount+" · "+p.Message+" · "+stage;
                if(p.Calculation!=null){var c=p.Calculation;progress.Style=ProgressBarStyle.Continuous;progress.Value=c.Target==0?0:(int)Math.Min(100,100.0*c.Completed/c.Target);status.Text+="\r\n"+c.Completed.ToString("N0")+" / "+c.Target.ToString("N0")+(c.Equity.HasValue?" · 途中EQ "+c.Equity.Value.ToString("F2")+"%":"");}
            });
            try{
                var completed=await Task.Run(()=>EquityJob.Run(request,source.Token,p=>((IProgress<EquityJobProgress>)reporter).Report(p)),source.Token);
                if(job!=version||source.IsCancellationRequested||IsDisposed)return;LastResult=completed;table.DefaultCellStyle.ForeColor=Ink;
                foreach(var row in completed.Rows){var r=row.Result;int index=table.Rows.Add(row.Label,row.Syntax,r==null?null:(object)r.Equity.ToString("F2"),r==null?null:r.CiLow.ToString("F2")+"–"+r.CiHigh.ToString("F2"),FrequencyText(completed,row),r==null?null:r.Exact?"全列挙":"試行計算",r==null?null:(object)r.HeroCount,r==null?null:(object)r.VillainCount,r==null?null:(object)r.Total,r==null?null:(object)(r.ElapsedMs/1000).ToString("F2"),(row.Status+(row.Frequency!=null&&row.Frequency.Percent==null?" / "+row.Frequency.Status:"")));
                    string details=r==null?row.Status:"勝ち "+r.Win.ToString("F2")+"% / 引き分け "+r.Tie.ToString("F2")+"% / 負け "+r.Loss.ToString("F2")+"%\r\nシード "+r.Seed+" / 合法対戦組 "+r.LegalPairs.ToString("N0")+"\r\nボード重複除外 自分 "+r.HeroBoardBlocked+" / 相手 "+r.VillainBoardBlocked+"\r\n対戦相手なし 自分 "+r.HeroNoOpponent+" / 相手 "+r.VillainNoOpponent;
                    foreach(DataGridViewCell cell in table.Rows[index].Cells)cell.ToolTipText=details;
                }
                scatter.SetData(completed);
                status.Text="計算終了："+completed.Rows.Count(r=>r.Result!=null)+" / "+completed.Rows.Length+"条件が完了 · 全処理 "+(completed.ElapsedMs/1000).ToString("F2")+"秒 · 基準シード "+completed.BaseSeed+"\r\n"+completed.PlotStatus;
                if(completed.Rows.Any(r=>r.Result!=null)){
                    try{archive=EquityFiles.Create(completed);archive=historyStore.Save(completed);status.Text+=" · EQ履歴に保存済み";}
                    catch(Exception ex){status.Text+=" · 自動保存できません："+ex.Message+(archive!=null?"（結果は手動保存できます）":"");}
                }
            }catch(OperationCanceledException){if(!IsDisposed){LastResult=null;if(job==version){table.Rows.Clear();status.Text="中止しました。確定したEQ結果はありません。";}}}
            catch(Exception ex){if(!IsDisposed&&job==version){LastResult=null;status.Text="計算できません："+ex.Message;}}
            finally{if(ReferenceEquals(cancellation,source)){cancellation=null;if(!IsDisposed)Busy(false);}source.Dispose();}
        }
        void SetupSave(Button button,string text,string name,string extension,Control parent){button.Text=text;button.Name=name;button.AutoSize=true;button.Height=32;button.Enabled=false;button.Click+=(s,e)=>{if(!IsBusy&&LastResult!=null)EquityExportUi.Save(this,archive,extension);};parent.Controls.Add(button);}
        void SaveState(){bool ready=!IsBusy&&!externalBusy&&LastResult!=null&&archive!=null;saveCsv.Enabled=saveJson.Enabled=ready;saveSvg.Enabled=ready&&EquityPlot.Points(LastResult).Length>0;}
        public void ExportTo(string path,string extension){if(IsBusy||externalBusy||LastResult==null||archive==null)throw new InvalidOperationException("保存できる確定EQがありません。");EquityFiles.Export(archive,path,extension,Application.ExecutablePath);}
        static string FrequencyText(EquityJobResult job,EquityRow row){
            if(row.Frequency==null||row.Frequency.Percent==null)return "—";
            return string.Join(" / ",Enumerable.Range(0,row.Frequency.Percent.Length).Select(a=>job.Input.ActionLabels[a]+" (CSV"+(a+1)+"): "+row.Frequency.Percent[a].ToString("F2")+"%"));
        }
        async Task ImportAsync(){
            if(IsBusy||externalBusy)return;string[] paths;using(var dialog=new OpenFileDialog{Filter="CSVファイル|*.csv",Title="相手のアクション前レンジCSV（複数選択可）",Multiselect=true}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;paths=dialog.FileNames;}
            await ImportRangeFilesAsync(paths);
        }
        public Task ImportRangeFileAsync(string path){return ImportRangeFilesAsync(new[]{path});}
        public async Task ImportRangeFilesAsync(string[] paths){
            if(IsBusy||externalBusy)return;
            var source=new CancellationTokenSource();cancellation=source;int job=version;bool percent=villainUnit.SelectedIndex==1;Busy(true);progress.Style=ProgressBarStyle.Marquee;status.Text="相手CSVを読み込み・統合しています…";villainInfo.ForeColor=Muted;villainInfo.Text=status.Text;
            try{var inputPaths=paths==null?null:(string[])paths.Clone();var result=await Task.Run(()=>EquityInput.ImportRangeFiles(inputPaths,percent,source.Token),source.Token);
                if(IsDisposed||job!=version||source.IsCancellationRequested)return;
                // All validation completed. Replace the editable text atomically.
                cancellation=null;villainSources=(string[])result.Paths.Clone();villainText.Text=result.Text;
                villainPath.Text=string.Join(Environment.NewLine,villainSources);villainPath.SelectionStart=0;villainPath.SelectionLength=0;
                villainInfo.ForeColor=Ink;villainInfo.Text=villainSources.Length+"ファイル読込済み · 統合後 "+result.ComboCount.ToString("N0")+"ハンド\r\nweight合計 · "+result.EncodingName+" · weight 0除外 "+result.ZeroRows+(result.WeightScale>1?" · 相対weight表示":"");status.Text=villainSources.Length+"個の相手CSVを統合しました。EQを計算してください。";
            }catch(OperationCanceledException){if(!IsDisposed&&job==version){status.Text="CSV読み込みを中止しました。元の入力・読込元は保持しています。";villainInfo.Text=status.Text;}}
            catch(Exception ex){if(!IsDisposed&&job==version){status.Text="CSVを読み込めません："+ex.Message+"（元の入力・読込元を保持）";villainInfo.ForeColor=Color.Firebrick;villainInfo.Text=status.Text;}}
            finally{if(ReferenceEquals(cancellation,source))cancellation=null;if(!IsDisposed)Busy(false);source.Dispose();}
        }
        protected override void Dispose(bool disposing){if(disposing&&cancellation!=null){version++;cancellation.Cancel();}base.Dispose(disposing);}
    }
}
