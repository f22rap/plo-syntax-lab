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
        readonly ComboBox heroMode=new ComboBox(),heroUnit=new ComboBox(),villainUnit=new ComboBox();
        readonly TextBox heroText=new TextBox(),villainText=new TextBox(),seed=new TextBox();
        readonly NumericUpDown trials=new NumericUpDown(),exactLimit=new NumericUpDown();
        readonly CheckBox commonScale=new CheckBox(),exact=new CheckBox();
        readonly Button start=new Button(),stop=new Button(),saveCsv=new Button(),saveJson=new Button(),saveSvg=new Button(),history=new Button();
        readonly EquityHistoryStore historyStore;EquityArchive archive;string heroSource,villainSource;
        readonly FlowLayoutPanel plotSettings=new FlowLayoutPanel();readonly CheckBox[] betActions={new CheckBox(),new CheckBox(),new CheckBox()};
        readonly EquityScatterChart scatter=new EquityScatterChart();readonly TabControl resultTabs=new TabControl();
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
        public EquityPanel(Action selectConditions,EquityHistoryStore store=null){
            historyStore=store??new EquityHistoryStore(Path.Combine(HistoryStore.DefaultRoot,"EQ"));
            Dock=DockStyle.Fill;BackColor=Color.FromArgb(240,244,247);Padding=new Padding(18);Name="equityPanel";
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7};Controls.Add(root);
            foreach(float height in new[]{70f,220f,60f,60f,40f})root.RowStyles.Add(new RowStyle(SizeType.Absolute,height));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
            var heading=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2};heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180));
            var intro=new Label{Text="双方がこの局面でアクションを選ぶ前のレンジからEQを計算します。",AutoSize=true,Dock=DockStyle.Fill,ForeColor=Ink};heading.Controls.Add(intro,0,0);heading.SetColumnSpan(intro,2);
            summary.Dock=DockStyle.Fill;summary.AutoEllipsis=true;summary.ForeColor=Ink;heading.Controls.Add(summary,0,1);
            var choose=new Button{Text="集計タブで条件を選ぶ",Dock=DockStyle.Fill};choose.Click+=(s,e)=>selectConditions();heading.Controls.Add(choose,1,1);root.Controls.Add(heading,0,0);
            inputs.Dock=DockStyle.Fill;inputs.ColumnCount=2;inputs.RowCount=1;inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));root.Controls.Add(inputs,0,1);
            heroMode.DropDownStyle=ComboBoxStyle.DropDownList;heroMode.Items.AddRange(new object[]{"全ハンドを均等に扱う","アクション前レンジを入力","集計タブの全アクションCSVを合計"});heroMode.SelectedIndex=0;heroMode.Name="eqHeroMode";
            inputs.Controls.Add(RangeBox("自分のアクション前レンジ",heroText,heroMode,heroUnit,heroInfo,true),0,0);
            inputs.Controls.Add(RangeBox("相手のアクション前レンジ",villainText,null,villainUnit,villainInfo,false),1,0);
            heroText.Name="eqHeroRange";villainText.Name="eqVillainRange";heroText.Text="random";villainText.Text="random";
            settings.Dock=DockStyle.Fill;settings.WrapContents=true;root.Controls.Add(settings,0,2);
            trials.Minimum=1;trials.Maximum=2000000;trials.Value=50000;trials.Increment=10000;trials.Width=110;trials.ThousandsSeparator=true;trials.Name="eqTrials";
            seed.Width=110;seed.Name="eqSeed";seed.MaxLength=10;
            exact.Text="リバーは全列挙";exact.Checked=true;exact.AutoSize=true;exact.Name="eqExactRiver";
            exactLimit.Minimum=1;exactLimit.Maximum=100000000;exactLimit.Value=5000000;exactLimit.Increment=1000000;exactLimit.Width=120;exactLimit.ThousandsSeparator=true;
            settings.Controls.Add(Caption("試行数／syntax"));settings.Controls.Add(trials);settings.Controls.Add(Caption("シード（空欄＝自動）"));settings.Controls.Add(seed);settings.Controls.Add(exact);settings.Controls.Add(Caption("全列挙の上限組数"));settings.Controls.Add(exactLimit);
            var tools=new FlowLayoutPanel{Dock=DockStyle.Fill};start.Text="EQを計算";start.Width=140;start.Height=32;start.Name="eqStart";stop.Text="中止";stop.Width=100;stop.Height=32;stop.Enabled=false;stop.Name="eqStop";tools.Controls.Add(start);tools.Controls.Add(stop);
            SetupSave(saveCsv,"CSV保存","eqSaveCsv",".csv",tools);SetupSave(saveJson,"JSON保存","eqSaveJson",".json",tools);SetupSave(saveSvg,"グラフSVG保存","eqSaveSvg",".svg",tools);
            history.Text="EQ履歴";history.Name="eqHistory";history.AutoSize=true;history.Height=32;history.Click+=(s,e)=>{using(var window=new EquityHistoryForm(historyStore))window.ShowDialog(this);};tools.Controls.Add(history);root.Controls.Add(tools,0,4);
            plotSettings.Dock=DockStyle.Fill;plotSettings.WrapContents=true;plotSettings.AutoScroll=true;plotSettings.Controls.Add(Caption("散布図に表示するベットCSV："));
            for(int a=0;a<betActions.Length;a++){var box=betActions[a];box.Name="eqBetAction"+a;box.AutoSize=true;box.Visible=false;box.CheckedChanged+=(s,e)=>InvalidateResult();plotSettings.Controls.Add(box);}
            plotSettings.Controls.Add(new Label{Text="全アクションのCSVを集計タブで指定し、共通weight基準を確認してください。",AutoSize=true,ForeColor=Muted});root.Controls.Add(plotSettings,0,3);
            table.Dock=DockStyle.Fill;table.Name="eqResults";table.ReadOnly=true;table.AllowUserToAddRows=false;table.AllowUserToDeleteRows=false;table.RowHeadersVisible=false;table.AutoGenerateColumns=false;table.BackgroundColor=Color.White;table.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;table.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;table.DefaultCellStyle.NullValue="—";
            AddColumn("label","条件",150);AddColumn("syntax","syntax",170);AddColumn("eq","自分のEQ (%)",95);AddColumn("ci","95%区間 (%)",125);AddColumn("frequency","ベット頻度 (%)",220);AddColumn("method","方式",80);AddColumn("hero","自分の有効コンボ",115);AddColumn("villain","相手の有効コンボ",115);AddColumn("total","処理件数",100);AddColumn("seconds","計算秒",80);AddColumn("note","状態／理由",240);resultTabs.Dock=DockStyle.Fill;resultTabs.Name="eqResultTabs";var tablePage=new TabPage("結果表");tablePage.Controls.Add(table);var chartPage=new TabPage("EQ × ベット頻度");chartPage.Controls.Add(scatter);resultTabs.TabPages.Add(tablePage);resultTabs.TabPages.Add(chartPage);root.Controls.Add(resultTabs,0,5);
            var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};footer.RowStyles.Add(new RowStyle(SizeType.Absolute,15));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));progress.Dock=DockStyle.Fill;progress.Visible=false;status.Dock=DockStyle.Fill;status.ForeColor=Muted;footer.Controls.Add(progress,0,0);footer.Controls.Add(status,0,1);root.Controls.Add(footer,0,6);
            start.Click+=async(s,e)=>await ExecuteAsync();stop.Click+=(s,e)=>Cancel();
            heroMode.SelectedIndexChanged+=(s,e)=>{ApplyMode();InvalidateResult();};heroText.TextChanged+=(s,e)=>InvalidateResult();villainText.TextChanged+=(s,e)=>InvalidateResult();seed.TextChanged+=(s,e)=>InvalidateResult();trials.ValueChanged+=(s,e)=>InvalidateResult();exact.CheckedChanged+=(s,e)=>InvalidateResult();exactLimit.ValueChanged+=(s,e)=>InvalidateResult();commonScale.CheckedChanged+=(s,e)=>InvalidateResult();
            initializing=false;ApplyMode();status.Text="集計タブでボードと条件を選び、相手のアクション前レンジを指定してください。";start.Enabled=false;
        }
        static Label Caption(string text){return new Label{Text=text,AutoSize=true,Padding=new Padding(0,6,0,0),Margin=new Padding(0,0,7,0)};}
        Control RangeBox(string title,TextBox text,ComboBox mode,ComboBox unit,Label info,bool hero){
            var group=new GroupBox{Text=title,Dock=DockStyle.Fill,Padding=new Padding(10),Margin=new Padding(hero?0:8,0,hero?8:0,6)};
            var rows=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};rows.RowStyles.Add(new RowStyle(SizeType.Absolute,32));rows.RowStyles.Add(new RowStyle(SizeType.Percent,100));rows.RowStyles.Add(new RowStyle(SizeType.Absolute,36));rows.RowStyles.Add(new RowStyle(SizeType.Absolute,58));group.Controls.Add(rows);
            if(mode!=null){mode.Dock=DockStyle.Fill;rows.Controls.Add(mode,0,0);}else rows.Controls.Add(new Label{Text="例：AA**, KK**, AKQJ:ds @50%",Dock=DockStyle.Fill,ForeColor=Muted},0,0);
            text.Multiline=true;text.ScrollBars=ScrollBars.Vertical;text.Dock=DockStyle.Fill;text.Font=new Font("Consolas",10);rows.Controls.Add(text,0,1);
            var line=new FlowLayoutPanel{Dock=DockStyle.Fill};unit.DropDownStyle=ComboBoxStyle.DropDownList;unit.Items.AddRange(new object[]{"CSV重み 0〜1","CSV重み 0〜100"});unit.SelectedIndex=0;unit.Width=145;
            var import=new Button{Text="レンジCSVを読む",AutoSize=true,Height=30,Name=hero?"eqImportHero":"eqImportVillain"};import.Click+=async(s,e)=>await ImportAsync(hero);line.Controls.Add(import);line.Controls.Add(unit);rows.Controls.Add(line,0,2);
            info.Dock=DockStyle.Fill;info.ForeColor=Muted;info.AutoEllipsis=true;
            if(hero){var lower=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};commonScale.Text="選択CSVは全アクション・共通weight基準";commonScale.AutoSize=true;commonScale.Name="eqCommonScale";lower.Controls.Add(commonScale,0,0);lower.Controls.Add(info,0,1);rows.Controls.Add(lower,0,3);}else{info.Text="CSVは全行検証してから置き換えます。重みは相対比率。";rows.Controls.Add(info,0,3);}
            return group;
        }
        void AddColumn(string name,string title,int width){table.Columns.Add(new DataGridViewTextBoxColumn{Name=name,HeaderText=title,Width=width,SortMode=DataGridViewColumnSortMode.NotSortable});}
        void ApplyMode(){heroText.Enabled=heroMode.SelectedIndex==1;heroUnit.Enabled=heroMode.SelectedIndex==1;commonScale.Enabled=true;heroInfo.Text=heroMode.SelectedIndex==2?"個別に正規化されたCSVは合計せず、元レンジを入力してください。":"各syntaxに一致するハンドを、このレンジから抽出します。";}
        public void SetContext(EquityJobRequest value){
            string[] pieces=value==null?new[]{"invalid"}:new[]{value.Board,value.Conditions.Length.ToString(),value.ActionPaths.Length.ToString(),value.ActionLabels.Length.ToString()}.Concat(value.Conditions.SelectMany(c=>new[]{c.Label,c.Syntax})).Concat(value.ActionPaths).Concat(value.ActionLabels).ToArray();
            string key=string.Concat(pieces.Select(p=>(p??"").Length+":"+p));if(key==contextKey)return;contextKey=key;context=value==null?null:value.Snapshot();
            for(int a=0;a<betActions.Length;a++){
                bool visible=context!=null&&a<context.ActionPaths.Length;
                // Changed CSV paths/labels require an explicit action-role selection again.
                betActions[a].Checked=false;betActions[a].Visible=visible;
                if(visible)betActions[a].Text=context.ActionLabels[a]+" (CSV"+(a+1)+")";
            }
            commonScale.Checked=false;
            InvalidateResult();table.Rows.Clear();if(context!=null)foreach(var c in context.Conditions)table.Rows.Add(c.Label,c.Syntax,null,null,null,null,null,null,null,null,"未計算");ApplyMode();
            summary.Text=context==null?"ボードと条件の準備ができていません。":context.Board+" · "+context.Conditions.Length+"条件 · アクションCSV "+context.ActionPaths.Length+"個";
            start.Enabled=!IsBusy&&context!=null&&context.Conditions.Length>0;
        }
        void InvalidateResult(){if(initializing)return;version++;LastResult=null;archive=null;SaveState();scatter.SetData(null);table.DefaultCellStyle.ForeColor=Muted;if(IsBusy)cancellation.Cancel();status.Text="入力・条件が変更されました。EQを再計算してください。";}
        void Busy(bool value){inputs.Enabled=!value;settings.Enabled=!value;plotSettings.Enabled=!value;start.Enabled=!value&&context!=null&&context.Conditions.Length>0;stop.Enabled=value;progress.Visible=value;history.Enabled=!value;SaveState();if(BusyChanged!=null)BusyChanged(value);}
        public void Cancel(){if(cancellation!=null){cancellation.Cancel();stop.Enabled=false;status.Text="中止しています…";}}
        EquityJobRequest Request(){
            if(context==null)throw new ArgumentException("ボードと条件を選択してください。");var r=context.Snapshot();r.HeroMode=(EquityHeroMode)heroMode.SelectedIndex;r.RangeInputPaths=new[]{heroSource,villainSource}.Where(p=>!string.IsNullOrEmpty(p)).ToArray();r.HeroText=heroText.Text;r.VillainText=villainText.Text;r.CommonScaleConfirmed=commonScale.Checked;r.BetActions=betActions.Take(r.ActionPaths.Length).Select(b=>b.Checked).ToArray();
            uint parsed;r.Settings=new EquitySettings{Trials=(int)trials.Value,ExactRiver=exact.Checked,MaxExactComparisons=(long)exactLimit.Value};
            if(seed.Text.Trim().Length>0){if(!uint.TryParse(seed.Text.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out parsed))throw new ArgumentException("シードは0〜4,294,967,295の整数にしてください。");r.Settings.Seed=parsed;}return r;
        }
        public async Task ExecuteAsync(){
            if(IsBusy||!Enabled)return;if(InvokeRequired)throw new InvalidOperationException("画面から開始してください。");
            if(!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            EquityJobRequest request;try{request=Request();}catch(Exception ex){status.Text=ex.Message;return;}
            int job=++version;var source=new CancellationTokenSource();cancellation=source;LastResult=null;archive=null;scatter.SetData(null);table.Rows.Clear();Busy(true);status.Text="アクション前レンジを展開しています…";progress.Style=ProgressBarStyle.Marquee;
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
                scatter.SetData(completed);if(scatter.PointCount>0)resultTabs.SelectedIndex=1;
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
        void SaveState(){bool ready=!IsBusy&&LastResult!=null&&archive!=null;saveCsv.Enabled=saveJson.Enabled=ready;saveSvg.Enabled=ready&&EquityPlot.Points(LastResult).Length>0;}
        public void ExportTo(string path,string extension){if(IsBusy||LastResult==null||archive==null)throw new InvalidOperationException("保存できる確定EQがありません。");EquityFiles.Export(archive,path,extension,Application.ExecutablePath);}
        static string FrequencyText(EquityJobResult job,EquityRow row){
            if(row.Frequency==null||row.Frequency.Percent==null)return "—";
            return string.Join(" / ",Enumerable.Range(0,job.Input.BetActions.Length).Where(a=>job.Input.BetActions[a]).Select(a=>job.Input.ActionLabels[a]+" (CSV"+(a+1)+"): "+row.Frequency.Percent[a].ToString("F2")+"%"));
        }
        async Task ImportAsync(bool hero){
            if(IsBusy)return;string path;using(var dialog=new OpenFileDialog{Filter="CSVファイル|*.csv",Title="アクション前レンジのCSV"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;path=dialog.FileName;}
            var source=new CancellationTokenSource();cancellation=source;int job=version;bool percent=(hero?heroUnit:villainUnit).SelectedIndex==1;Busy(true);progress.Style=ProgressBarStyle.Marquee;status.Text="CSVを読み込み・検証しています…";
            try{var result=await Task.Run(()=>EquityInput.ImportRange(EquityInput.ReadCsv(path,source.Token),percent,source.Token),source.Token);
                if(IsDisposed||job!=version||source.IsCancellationRequested)return;
                // All validation completed. Replace the editable text atomically.
                cancellation=null;if(hero){heroMode.SelectedIndex=1;heroSource=path;}else villainSource=path;(hero?heroText:villainText).Text=result.Text;
                (hero?heroInfo:villainInfo).Text=result.Rows.ToString("N0")+"行を読み込み · "+result.EncodingName+" · weight 0除外 "+result.ZeroRows;status.Text="CSVを読み込みました。EQを計算してください。";
            }catch(OperationCanceledException){if(!IsDisposed&&job==version)status.Text="CSV読み込みを中止しました。元の入力は保持しています。";}
            catch(Exception ex){if(!IsDisposed&&job==version)status.Text="CSVを読み込めません："+ex.Message+"（元の入力を保持）";}
            finally{if(ReferenceEquals(cancellation,source))cancellation=null;if(!IsDisposed)Busy(false);source.Dispose();}
        }
        protected override void Dispose(bool disposing){if(disposing&&cancellation!=null){version++;cancellation.Cancel();}base.Dispose(disposing);}
    }
}
