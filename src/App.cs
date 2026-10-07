using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Threading.Tasks;
using PloSyntax;

namespace FlopCommands {
    public sealed class Choice {
        public Filter Filter;
        public string Expression;
        public string Problem;
        public bool Available { get { return Problem == null; } }
    }
    public static class Commands {
        public static string NormalizeSyntax(string syntax){
            if(syntax==null)return null;
            string previous;
            do{previous=syntax;syntax=Regex.Replace(syntax,@":\s*!?\s*\(\s*\)","");}while(syntax!=previous);
            return syntax;
        }
        public static string NormalizeBoard(string input) {
            string s=Regex.Replace(input??"",@"[\s,;|/・]+","").Replace("10","T");
            s=s.Replace("♠","s").Replace("♥","h").Replace("♦","d").Replace("♣","c");
            if(!Regex.IsMatch(s,@"^(?:[AKQJT2-9][shdc]){3,5}$",RegexOptions.IgnoreCase))throw new Exception("ボードを3〜5枚入力してください。例：AsKd7s");
            var cards=new List<string>();for(int i=0;i<s.Length;i+=2)cards.Add(char.ToUpperInvariant(s[i]).ToString()+char.ToLowerInvariant(s[i+1]));
            if(cards.Distinct().Count()!=cards.Count)throw new Exception("同じカードが重複しています。");return string.Concat(cards.ToArray());
        }
        public static string[] ReadColumns(string path) {
            using(var reader=new StreamReader(path,Encoding.UTF8,true)) {
                string line=reader.ReadLine();
                if(string.IsNullOrWhiteSpace(line))throw new Exception("CSVのヘッダーが空です。");
                return line.Split(',').Select(x=>x.Trim().Trim('"').Trim()).ToArray();
            }
        }
        public static void CheckCsvBoard(string path,string board) {
            var match=Regex.Match(Path.GetFileNameWithoutExtension(path),@"(?:^|_)((?:[AKQJT2-9][shdc]){3,5})(?=_|$)",RegexOptions.IgnoreCase);
            if(match.Success&&!SameBoard(NormalizeBoard(match.Groups[1].Value),board))
                throw new Exception("CSVのファイル名のボードと入力したフロップが異なります。CSVかフロップを変更してください。");
        }
        public static bool SameBoard(string a,string b){return a==b||(a.Length>=6&&a.Length==b.Length&&a.Substring(6)==b.Substring(6)&&Enumerable.Range(0,3).Select(i=>a.Substring(i*2,2)).OrderBy(c=>c).SequenceEqual(Enumerable.Range(0,3).Select(i=>b.Substring(i*2,2)).OrderBy(c=>c)));}
        public static string Quote(string text) {return "'"+(text??"").Replace("'","''")+"'";}
    }

    public sealed class MainForm : Form {
        static readonly Color Ink=Color.FromArgb(22,35,48), Muted=Color.FromArgb(94,108,120), Mint=Color.FromArgb(0,122,104), Canvas=Color.FromArgb(240,244,247);
        readonly TextBox boardInput=new TextBox(), search=new TextBox();
        readonly RadioButton defaultMode=new RadioButton{Text="デフォルト",AutoSize=true,Checked=true,Margin=new Padding(0,2,24,0)};
        readonly RadioButton generatedMode=new RadioButton{Text="syntax生成",AutoSize=true,Margin=new Padding(0,2,0,0)};
        readonly Panel syntaxActions=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};
        readonly FlowLayoutPanel generatedActions=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty};
        readonly Label defaultsInfo=new Label{Text="ボードからデフォルト条件を生成します。",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};
        readonly Panel defaultActions=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};
        readonly Button regenerate=new Button{Text="再生成",Dock=DockStyle.Right,Width=86};
        readonly List<Choice> defaultChoices=new List<Choice>(),generatedChoices=new List<Choice>();
        readonly HashSet<string> defaultSelected=new HashSet<string>(),generatedSelected=new HashSet<string>();
        readonly Dictionary<string,List<Choice>> defaultCache=new Dictionary<string,List<Choice>>();
        System.Threading.CancellationTokenSource defaultCancellation;
        TaskCompletionSource<LabSelection> defaultCompletion;
        string defaultJob,defaultBoard;
        bool defaultBusy;
        public bool DefaultsBusy {get{return defaultBusy;}}
        public string DefaultsMessage {get{return defaultsInfo.Text;}}
        LabBridge bridge;
        readonly TextBox[] csvPaths={new TextBox(),new TextBox(),new TextBox()};
        readonly TextBox[] csvLabels={new TextBox(),new TextBox(),new TextBox()};
        readonly TabControl mainTabs=new TabControl();
        readonly HistoryStore historyStore;
        readonly HistoryPanel historyPanel;
        readonly CheckBox includeAll=new CheckBox{Text="全ハンド（syntaxなし）を集計",Checked=true,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,4),AccessibleName="全ハンドのweight合計・比率も集計"};
        readonly DataGridView grid=new DataGridView();
        readonly TextBox command=new TextBox();
        readonly Label status=new Label(), selectionSummary=new Label(), codeSummary=new Label();
        readonly Label[] cards={new Label(),new Label(),new Label(),new Label(),new Label()};
        readonly Button copy=Button("コピー",false), save=Button(".ps1を保存",false), run=Button("実行",true), cancel=Button("中止",false), export=Button("結果をCSV保存",false);
        readonly TabControl outputTabs=new TabControl();
        readonly TabPage resultsPage=new TabPage("結果");
        readonly DataGridView resultsGrid=new DataGridView();
        readonly TabControl resultViews=new TabControl();
        readonly RatioChart chart=new RatioChart();
        readonly Label errorOutput=new Label();
        readonly Label resultStatus=new Label(), comparison=new Label();
        readonly ProgressBar progress=new ProgressBar();
        readonly Timer executionTimer=new Timer();
        readonly Stopwatch elapsed=new Stopwatch();
        Control settingsArea,filtersArea;
        System.Threading.CancellationTokenSource cancellation;
        RunResult lastResult;
        string[] lastSources=new string[0];
        bool busy,closeWhenDone;
        readonly Timer debounce=new Timer();
        HashSet<string> selected=new HashSet<string>();
        List<Choice> choices=new List<Choice>();
        string board=""; bool refreshing, starting=true;
        public string CurrentCode {get{return command.Text;}}
        public int ChoiceCount {get{return choices.Count;}}
        public int AvailableCount {get{return choices.Count(c=>c.Available);}}
        public RunResult LastResult {get{return lastResult;}}
        public string ResultMessage {get{return resultStatus.Text+"\n"+errorOutput.Text;}}
        public bool IsBusy {get{return busy;}}
        public bool UseDefaults {get{return defaultMode.Checked;}set{if(busy)throw new Exception("集計中は切り替えできません。");if(value)defaultMode.Checked=true;else generatedMode.Checked=true;}}
        public bool IncludeAll {get{return includeAll.Checked;}set{includeAll.Checked=value;}}
        public string[] GraphLabels {get{return chart.LegendLabels;}}
        public int GraphRowCount {get{return chart.RowCount;}}
        public void ShowGraph(bool show){resultViews.SelectedIndex=show?1:0;}
        public HistoryPanel History {get{return historyPanel;}}
        public void ShowHistory(){historyPanel.RefreshEntries();mainTabs.SelectedIndex=1;}
        public MainForm():this(new HistoryStore()){}
        public MainForm(HistoryStore store) {
            historyStore=store;
            Text="PLO Syntax Lab → PowerShell · CSV比較";
            ClientSize=new Size(1480,930);MinimumSize=new Size(1240,840);
            StartPosition=FormStartPosition.CenterScreen;BackColor=Canvas;
            Font=new Font("Yu Gothic UI",10F);ForeColor=Ink;
            AutoScaleMode=AutoScaleMode.Dpi;
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Margin=Padding.Empty};
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,88));root.RowStyles.Add(new RowStyle(SizeType.Absolute,245));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,32));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,30));
            mainTabs.Dock=DockStyle.Fill;Controls.Add(mainTabs);var computePage=new TabPage("集計");computePage.Controls.Add(root);mainTabs.TabPages.Add(computePage);
            historyPanel=new HistoryPanel(historyStore);var historyPage=new TabPage("履歴");historyPage.Controls.Add(historyPanel);mainTabs.TabPages.Add(historyPage);
            mainTabs.SelectedIndexChanged+=(s,e)=>{if(mainTabs.SelectedIndex==1)historyPanel.RefreshEntries(historyPanel.CurrentId);};
            var header=new Panel{Dock=DockStyle.Fill,BackColor=Ink,Margin=Padding.Empty,Padding=new Padding(24,12,24,8)};
            var title=Label("PLO Syntax Lab → PowerShell",20,Color.White);title.Dock=DockStyle.Top;title.Height=43;
            var subtitle=Label("CSV・Excelなしでsyntax生成 → 2〜3つのCSVをweight合計・比率で比較",10,Color.FromArgb(166,185,199));subtitle.Dock=DockStyle.Top;subtitle.Height=22;
            header.Controls.Add(subtitle);header.Controls.Add(title);root.Controls.Add(header,0,0);
            var inputs=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(24,15,24,5),Margin=Padding.Empty};
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,315));inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.Controls.Add(inputs,0,1);
            settingsArea=inputs;
            var flop=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=new Padding(0,0,18,0)};
            flop.RowStyles.Add(new RowStyle(SizeType.Absolute,26));flop.RowStyles.Add(new RowStyle(SizeType.Absolute,39));flop.RowStyles.Add(new RowStyle(SizeType.Absolute,86));flop.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            flop.Controls.Add(Label("1  ボード（3〜5枚）",11,Ink),0,0);
            boardInput.Font=new Font("Consolas",18);boardInput.Dock=DockStyle.Fill;boardInput.AccessibleName="ボード";boardInput.Margin=new Padding(0,0,0,3);flop.Controls.Add(boardInput,0,1);
            var cardsPanel=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(0,7,0,0)};
            foreach(var card in cards){card.Size=new Size(49,66);card.BackColor=Color.White;card.Font=new Font("Segoe UI Symbol",16,FontStyle.Bold);card.TextAlign=ContentAlignment.MiddleCenter;card.Margin=new Padding(0,0,7,0);card.BorderStyle=BorderStyle.FixedSingle;cardsPanel.Controls.Add(card);}
            flop.Controls.Add(cardsPanel,0,2);flop.Controls.Add(Label("例：AsKd7s / AsKd7s5h / AsKd7s5h9c",9,Muted),0,3);inputs.Controls.Add(flop,0,0);
            var files=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Margin=Padding.Empty};
            files.RowStyles.Add(new RowStyle(SizeType.Absolute,25));files.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
            for(int i=0;i<3;i++)files.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
            files.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var modes=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty};modes.Controls.Add(defaultMode);modes.Controls.Add(generatedMode);files.Controls.Add(modes,0,0);
            var openLab=new Button{Text="syntax生成を開く",Width=180,Height=32};openLab.Click+=(s,e)=>OpenGenerator();generatedActions.Controls.Add(openLab);
            var import=new Button{Text="条件JSONを読み込む",Width=180,Height=32};import.Click+=(s,e)=>ImportSelection();generatedActions.Controls.Add(import);
            defaultActions.Controls.Add(defaultsInfo);defaultActions.Controls.Add(regenerate);regenerate.Click+=(s,e)=>QueueDefaults(true);
            syntaxActions.Controls.Add(defaultActions);syntaxActions.Controls.Add(generatedActions);files.Controls.Add(syntaxActions,0,1);
            defaultMode.CheckedChanged+=(s,e)=>{if(defaultMode.Checked&&!starting)SwitchSyntaxMode();};generatedMode.CheckedChanged+=(s,e)=>{if(generatedMode.Checked&&!starting)SwitchSyntaxMode();};
            for(int i=0;i<3;i++){
                int index=i;var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=6,RowCount=1,Margin=Padding.Empty};row.RowStyles.Add(new RowStyle(SizeType.Percent,100));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,111));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,60));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,64));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,165));
                row.Controls.Add(Label("CSV"+(i+1)+(i==2?" 任意":" 集計時"),10,Ink),0,0);
                csvPaths[i].ReadOnly=true;csvPaths[i].BackColor=Color.White;csvPaths[i].Dock=DockStyle.Fill;csvPaths[i].Margin=new Padding(0,0,4,4);row.Controls.Add(csvPaths[i],1,0);
                var choose=Button("CSVを選択",false);choose.Click+=(sender,args)=>SelectCsv(index);row.Controls.Add(choose,2,0);
                var remove=Button("解除",false);remove.Click+=(sender,args)=>{csvPaths[index].Clear();RefreshCommand();};row.Controls.Add(remove,3,0);files.Controls.Add(row,0,i+2);
                row.Controls.Add(Label(" ラベル",10,Ink),4,0);csvLabels[i].Dock=DockStyle.Fill;csvLabels[i].Margin=new Padding(0,0,0,4);csvLabels[i].MaxLength=60;csvLabels[i].Text="CSV"+(i+1);csvLabels[i].AccessibleName="CSV"+(i+1)+"のラベル";row.Controls.Add(csvLabels[i],5,0);csvLabels[i].TextChanged+=(sender,args)=>{if(!starting)RefreshCommand();};
            }
            files.Controls.Add(Label("比率 = 各CSVのweight合計 ÷ 選択した全CSVのweight合計 × 100",9,Muted),0,5);inputs.Controls.Add(files,1,0);
            status.Dock=DockStyle.Fill;status.Margin=new Padding(24,0,24,0);status.TextAlign=ContentAlignment.MiddleLeft;root.Controls.Add(status,0,2);
            var split=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(24,5,24,0),Margin=Padding.Empty};
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,39));split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,61));root.Controls.Add(split,0,3);
            var left=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Margin=new Padding(0,0,12,0)};
            filtersArea=left;
            left.RowStyles.Add(new RowStyle(SizeType.Absolute,39));left.RowStyles.Add(new RowStyle(SizeType.Absolute,33));left.RowStyles.Add(new RowStyle(SizeType.Percent,100));left.RowStyles.Add(new RowStyle(SizeType.Absolute,35));
            var tools=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Margin=Padding.Empty};tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,113));tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,92));
            tools.Controls.Add(Label("2  条件を選択",11,Ink),0,0);search.Dock=DockStyle.Fill;search.AccessibleName="条件名またはsyntaxを検索";search.Margin=new Padding(0,0,8,4);tools.Controls.Add(search,1,0);
            var clear=Button("選択解除",false);clear.Click+=(s,e)=>{selected.Clear();RefreshGrid();RefreshCommand();};tools.Controls.Add(clear,2,0);left.Controls.Add(tools,0,0);
            left.Controls.Add(includeAll,0,1);includeAll.CheckedChanged+=(s,e)=>RefreshCommand();
            ConfigureGrid();left.Controls.Add(grid,0,2);
            selectionSummary.Dock=DockStyle.Fill;selectionSummary.TextAlign=ContentAlignment.MiddleLeft;selectionSummary.ForeColor=Muted;left.Controls.Add(selectionSummary,0,3);split.Controls.Add(left,0,0);
            var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=new Padding(8,0,0,0)};
            right.RowStyles.Add(new RowStyle(SizeType.Absolute,39));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
            var codeTitle=Label("3  コマンドを実行・結果を確認",11,Ink);codeTitle.Dock=DockStyle.Fill;right.Controls.Add(codeTitle,0,0);
            command.Dock=DockStyle.Fill;command.ReadOnly=true;command.WordWrap=false;command.Multiline=true;command.ScrollBars=ScrollBars.Both;command.MaxLength=int.MaxValue;command.Font=new Font("Consolas",10);command.BackColor=Color.FromArgb(19,30,43);command.ForeColor=Color.FromArgb(216,232,238);command.BorderStyle=BorderStyle.None;command.AccessibleName="生成したPowerShellコマンド";command.Margin=Padding.Empty;
            outputTabs.Dock=DockStyle.Fill;outputTabs.Margin=Padding.Empty;
            var codePage=new TabPage("コマンド");codePage.Controls.Add(command);outputTabs.TabPages.Add(codePage);ConfigureResults();outputTabs.TabPages.Add(resultsPage);right.Controls.Add(outputTabs,0,1);
            var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=5,Padding=new Padding(0,8,0,0),Margin=Padding.Empty};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,74));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,107));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,87));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,72));
            codeSummary.Dock=DockStyle.Fill;codeSummary.TextAlign=ContentAlignment.MiddleLeft;codeSummary.Font=new Font("Yu Gothic UI",9);actions.Controls.Add(codeSummary,0,0);actions.Controls.Add(copy,1,0);actions.Controls.Add(save,2,0);actions.Controls.Add(run,3,0);actions.Controls.Add(cancel,4,0);cancel.Enabled=false;right.Controls.Add(actions,0,2);split.Controls.Add(right,1,0);
            var footer=Label("PLO4 · 手札2枚＋ボード3枚 · フロップ / ターン / リバー · Monker実機の受理・抽出は未確認",9,Muted);footer.Dock=DockStyle.Fill;footer.Margin=new Padding(24,0,0,0);footer.TextAlign=ContentAlignment.MiddleLeft;root.Controls.Add(footer,0,4);
            debounce.Interval=350;debounce.Tick+=(s,e)=>{debounce.Stop();ApplyBoard();};boardInput.TextChanged+=(s,e)=>{if(!starting){CancelDefaults();InvalidateCommand();debounce.Stop();debounce.Start();}};
            search.TextChanged+=(s,e)=>RefreshGrid();
            copy.Click+=(s,e)=>CopyCommand();save.Click+=(s,e)=>SaveCommand();
            run.Click+=async(s,e)=>await ExecuteAsync();cancel.Click+=(s,e)=>{if(cancellation!=null){cancel.Enabled=false;resultStatus.Text="中止しています…";cancellation.Cancel();}};export.Click+=(s,e)=>SaveResults();
            executionTimer.Interval=500;executionTimer.Tick+=(s,e)=>{if(busy&&cancellation!=null&&!cancellation.IsCancellationRequested)resultStatus.Text=board+" · weight合計・比率"+"\r\n実行中（"+(int)elapsed.Elapsed.TotalSeconds+"秒）…";};
            FormClosing+=(s,e)=>{if(busy){e.Cancel=true;closeWhenDone=true;if(cancellation!=null)cancellation.Cancel();}};
            var tip=new ToolTip();tip.SetToolTip(search,"条件名またはsyntaxで絞り込みます。非表示になった選択も保持します。");tip.SetToolTip(boardInput,"ランクとスートを3〜5枚。スペース・10・スート記号にも対応します。");
            boardInput.Text="AsKd7s";
            Shown+=(s,e)=>QueueDefaults();FormClosed+=(s,e)=>{CancelDefaults();DefaultRunner.StopAll();if(bridge!=null)bridge.Dispose();};
            starting=false;SwitchSyntaxMode();ApplyBoard();
        }
        static Label Label(string text,float size,Color color){return new Label{Text=text,AutoSize=false,Dock=DockStyle.Top,Height=27,Font=new Font("Yu Gothic UI",size,FontStyle.Regular),ForeColor=color,Margin=Padding.Empty};}
        static Button Button(string text,bool primary){return new Button{Text=text,Dock=DockStyle.Fill,FlatStyle=FlatStyle.Flat,BackColor=primary?Mint:Color.White,ForeColor=primary?Color.White:Ink,Margin=new Padding(4,0,0,2),Cursor=Cursors.Hand,UseVisualStyleBackColor=false};}
        Control PathPicker(TextBox text,string button,EventHandler action){
            var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,111));
            text.ReadOnly=true;text.BackColor=Color.White;text.Dock=DockStyle.Fill;text.Margin=new Padding(0,0,4,4);text.AccessibleName=button+"のパス";p.Controls.Add(text,0,0);
            var b=Button(button,false);b.Click+=action;p.Controls.Add(b,1,0);return p;
        }
        void ConfigureGrid(){
            grid.Dock=DockStyle.Fill;grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.None;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.AllowUserToResizeRows=false;
            grid.RowHeadersVisible=false;grid.AutoGenerateColumns=false;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.EditMode=DataGridViewEditMode.EditOnEnter;
            grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersHeight=35;grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(225,233,239);grid.ColumnHeadersDefaultCellStyle.ForeColor=Ink;
            grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(215,241,234);grid.DefaultCellStyle.SelectionForeColor=Ink;grid.RowTemplate.Height=31;grid.GridColor=Color.FromArgb(234,239,242);
            grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="pick",HeaderText="",Width=36,SortMode=DataGridViewColumnSortMode.NotSortable});
            grid.Columns.Add(new DataGridViewTextBoxColumn{Name="label",HeaderText="条件名",Width=155,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
            grid.Columns.Add(new DataGridViewTextBoxColumn{Name="syntax",HeaderText="syntax",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,MinimumWidth=100,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
            grid.Columns.Add(new DataGridViewTextBoxColumn{Name="state",HeaderText="状態",Width=73,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
            grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            grid.CellValueChanged+=(s,e)=>{
                if(refreshing||e.RowIndex<0||e.ColumnIndex!=0)return;var c=grid.Rows[e.RowIndex].Tag as Choice;if(c==null||!c.Available)return;
                if(Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[0].Value))selected.Add(c.Filter.Cell);else selected.Remove(c.Filter.Cell);RefreshCommand();UpdateSelectionSummary();
            };
            grid.CellClick+=(s,e)=>{if(e.RowIndex>=0){var c=grid.Rows[e.RowIndex].Tag as Choice;if(c!=null&&!c.Available)SetStatus(c.Filter.Label+"："+c.Problem,true);}};
        }
        void ConfigureResults(){
            resultsPage.BackColor=Color.White;resultsPage.Padding=new Padding(10);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,8));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
            resultStatus.Dock=DockStyle.Fill;resultStatus.Text="「実行」を押すと、ここに集計結果を表示します。";resultStatus.ForeColor=Muted;layout.Controls.Add(resultStatus,0,0);
            progress.Dock=DockStyle.Fill;progress.Visible=false;progress.MarqueeAnimationSpeed=30;layout.Controls.Add(progress,0,1);
            var body=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};
            resultsGrid.Dock=DockStyle.Fill;resultsGrid.ReadOnly=true;resultsGrid.AllowUserToAddRows=false;resultsGrid.AllowUserToDeleteRows=false;resultsGrid.AllowUserToResizeRows=false;resultsGrid.RowHeadersVisible=false;resultsGrid.BackgroundColor=Color.White;resultsGrid.BorderStyle=BorderStyle.None;
            resultsGrid.AutoGenerateColumns=false;resultsGrid.ColumnHeadersHeight=36;resultsGrid.EnableHeadersVisualStyles=false;resultsGrid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(225,233,239);resultsGrid.ColumnHeadersDefaultCellStyle.Font=new Font("Yu Gothic UI",9);resultsGrid.RowTemplate.Height=36;resultsGrid.GridColor=Canvas;
            resultsGrid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(215,241,234);resultsGrid.DefaultCellStyle.SelectionForeColor=Ink;resultsGrid.DefaultCellStyle.NullValue="—";
            ConfigureResultColumns(2);
            resultViews.Dock=DockStyle.Fill;var tablePage=new TabPage("表");tablePage.Controls.Add(resultsGrid);var graphPage=new TabPage("グラフ");graphPage.Controls.Add(chart);resultViews.TabPages.Add(tablePage);resultViews.TabPages.Add(graphPage);body.Controls.Add(resultViews);
            errorOutput.Dock=DockStyle.Fill;errorOutput.Padding=new Padding(12);errorOutput.BackColor=Color.FromArgb(255,245,240);errorOutput.ForeColor=Color.FromArgb(171,57,37);errorOutput.Visible=false;body.Controls.Add(errorOutput);layout.Controls.Add(body,0,2);
            var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty,Padding=new Padding(0,8,0,0)};bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,144));
            comparison.Dock=DockStyle.Fill;comparison.Font=new Font("Yu Gothic UI",9);comparison.TextAlign=ContentAlignment.MiddleLeft;bottom.Controls.Add(comparison,0,0);export.Enabled=false;bottom.Controls.Add(export,1,0);layout.Controls.Add(bottom,0,3);resultsPage.Controls.Add(layout);
        }
        void ClearResults(){
            lastResult=null;lastSources=new string[0];resultsGrid.Rows.Clear();chart.SetData(null);errorOutput.Text="";errorOutput.Visible=false;resultsGrid.Visible=true;resultViews.Visible=true;export.Enabled=false;comparison.Text="";
            resultStatus.Text="「実行」を押すと、ここに集計結果を表示します。";
        }
        public void CancelExecution(){if(cancellation!=null){cancel.Enabled=false;resultStatus.Text="中止しています…";cancellation.Cancel();}}
        public async Task ExecuteAsync(){
            if(busy||ActivePaths().Length<2||string.IsNullOrWhiteSpace(command.Text))return;
            mainTabs.SelectedIndex=0;
            // Self-hosted invocations also need the form's UI context for awaited updates.
            if(InvokeRequired)throw new InvalidOperationException("実行はアプリの画面から開始してください。");
            if(!(System.Threading.SynchronizationContext.Current is WindowsFormsSynchronizationContext))System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            string code=command.Text, context=board+" · weight合計・比率"; string[] sources=ActivePaths();
            ClearResults();busy=true;settingsArea.Enabled=false;filtersArea.Enabled=false;run.Enabled=false;cancel.Enabled=true;
            outputTabs.SelectedTab=resultsPage;resultStatus.Text=context+"\r\n実行中…";progress.Style=ProgressBarStyle.Marquee;progress.Visible=true;
            cancellation=new System.Threading.CancellationTokenSource();elapsed.Restart();executionTimer.Start();
            RunResult completed=null;string failure=null;bool canceled=false;
            try{
                completed=await CommandRunner.RunAsync(code,cancellation.Token);
            }catch(OperationCanceledException){canceled=true;}
            catch(Exception ex){failure=string.IsNullOrWhiteSpace(ex.Message)?ex.ToString():ex.Message;}
            finally{
                elapsed.Stop();executionTimer.Stop();progress.Visible=false;busy=false;settingsArea.Enabled=true;filtersArea.Enabled=true;cancel.Enabled=false;run.Enabled=command.Text.Length>0&&ActivePaths().Length>=2;
                cancellation.Dispose();cancellation=null;
            }
            ClearResults();
            if(completed!=null){
                lastResult=completed;lastSources=sources;ConfigureResultColumns(sources.Length,completed.Rows[0].Sources.Select(s=>s.Label).ToArray());
                foreach(var row in completed.Rows){
                    var values=new List<object>{row.Label};
                    foreach(var csv in row.Sources){values.Add(csv.Sum);values.Add(csv.Percent);}
                    values.Add(row.Total);values.Add(row.Status=="OK"?"完了":"合計0");
                    int index=resultsGrid.Rows.Add(values.ToArray());
                    string detail=row.Syntax+"\r\n"+string.Join("\r\n",row.Sources.Select((csv,i)=>csv.Label+" (CSV"+(i+1)+"): "+csv.Count.ToString("N0")+"ハンド · "+csv.Path))+"\r\n"+row.Note;
                    foreach(DataGridViewCell cell in resultsGrid.Rows[index].Cells)cell.ToolTipText=detail;
                }
                resultStatus.Text=context+" · 完了（"+elapsed.Elapsed.TotalSeconds.ToString("0.0")+"秒）\r\n"+completed.Rows.Count+"条件 × "+sources.Length+"ファイル";export.Enabled=true;
                comparison.Text="各条件の比率合計は100%（丸め誤差あり）\r\n全CSVのweight合計が0の場合は —";
                chart.SetData(completed);resultViews.SelectedIndex=1;
                try{
                    var entry=historyStore.Save(completed,code,"");historyPanel.RefreshEntries(entry.Id);resultStatus.Text+=" · 履歴保存済み";
                }catch(Exception ex){resultStatus.Text=context+" · 完了（履歴保存失敗）\r\n結果はCSV保存できます。";SetStatus("履歴を保存できません："+ex.Message,true);}
            }else if(canceled)resultStatus.Text=context+"\r\n実行を中止しました。";
            else{resultStatus.Text=context+"\r\n実行に失敗しました。内容を確認して再実行してください。";resultViews.Visible=false;errorOutput.Visible=true;errorOutput.BringToFront();errorOutput.Text=failure;}
            if(closeWhenDone)BeginInvoke(new Action(Close));
        }
        public void ExportResultsTo(string path){
            if(lastResult==null||busy)throw new Exception("保存できる実行結果がありません。");
            string full=Path.GetFullPath(path);
            if(!string.Equals(Path.GetExtension(full),".csv",StringComparison.OrdinalIgnoreCase))throw new Exception("保存先の拡張子は .csv にしてください。");
            foreach(string input in lastSources.Concat(new[]{Application.ExecutablePath}))if(!string.IsNullOrEmpty(input)&&string.Equals(full,Path.GetFullPath(input),StringComparison.OrdinalIgnoreCase))throw new Exception("入力ファイルには上書きできません。別の保存先を選んでください。");
            File.WriteAllBytes(full,lastResult.Csv);
        }
        void SaveResults(){
            if(lastResult==null||busy)return;
            using(var dialog=new SaveFileDialog{Filter="CSVファイル|*.csv",FileName=board+"_weight_comparison.csv",OverwritePrompt=true})
            if(dialog.ShowDialog(this)==DialogResult.OK)try{ExportResultsTo(dialog.FileName);comparison.Text="結果をCSVに保存しました。";}catch(Exception ex){MessageBox.Show(this,ex.Message,"保存できません",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
        void SelectCsv(int index){using(var dialog=new OpenFileDialog{Filter="CSVファイル|*.csv",Title="CSV"+(index+1)+"を選択"})if(dialog.ShowDialog(this)==DialogResult.OK){csvPaths[index].Text=dialog.FileName;RefreshCommand();}}
        public void LoadCsvs(params string[] paths){if(paths.Length>3)throw new Exception("CSVは3つまでです。");for(int i=0;i<3;i++)csvPaths[i].Text=i<paths.Length?paths[i]:"";RefreshCommand();}
        public void SetCsvLabels(params string[] labels){for(int i=0;i<3;i++)csvLabels[i].Text=i<labels.Length?labels[i]:"";}
        string[] ActivePaths(){if(string.IsNullOrWhiteSpace(csvPaths[0].Text)||string.IsNullOrWhiteSpace(csvPaths[1].Text))return new string[0];return csvPaths.Select(t=>t.Text).Where(t=>!string.IsNullOrWhiteSpace(t)).ToArray();}
        void ConfigureResultColumns(int count,string[] labels=null){
            resultsGrid.Columns.Clear();resultsGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="label",HeaderText="条件名",Width=110,Frozen=true});
            for(int i=1;i<=count;i++){
                string title=labels==null?"CSV"+i:labels[i-1];
                int labelWidth=Math.Min(170,Math.Max(100,TextRenderer.MeasureText(title,resultsGrid.ColumnHeadersDefaultCellStyle.Font).Width+12));
                resultsGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="sum"+i,HeaderText=title+"\nweight合計",ToolTipText=title+" (CSV"+i+")",Width=labelWidth,ValueType=typeof(decimal),DefaultCellStyle=new DataGridViewCellStyle{Format="0.####",Alignment=DataGridViewContentAlignment.MiddleRight}});
                resultsGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="ratio"+i,HeaderText=title+"\n比率 (%)",ToolTipText=title+" (CSV"+i+")",Width=Math.Max(75,labelWidth-25),ValueType=typeof(decimal),DefaultCellStyle=new DataGridViewCellStyle{Format="0.00",NullValue="—",Alignment=DataGridViewContentAlignment.MiddleRight}});
            }
            resultsGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="total",HeaderText="全CSV\nweight合計",Width=100,DefaultCellStyle=new DataGridViewCellStyle{Format="0.####",Alignment=DataGridViewContentAlignment.MiddleRight}});
            resultsGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="state",HeaderText="状態",Width=72});resultsGrid.ColumnHeadersHeight=48;
        }
        public string GeneratorUrl {get{EnsureBridge();return bridge.Url+"?board="+Uri.EscapeDataString(board);}}
        void SwitchSyntaxMode(){
            choices=defaultMode.Checked?defaultChoices:generatedChoices;selected=defaultMode.Checked?defaultSelected:generatedSelected;
            generatedActions.Visible=!defaultMode.Checked;defaultActions.Visible=defaultMode.Checked;
            if(!defaultMode.Checked)CancelDefaults();search.Clear();RefreshGrid();RefreshCommand();if(Visible)QueueDefaults();
        }
        static string ResourceText(string name){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))using(var reader=new StreamReader(stream,Encoding.UTF8))return reader.ReadToEnd();}
        void EnsureBridge(){if(bridge!=null)return;bridge=new LabBridge(ResourceText("Lab.html"),json=>(string)Invoke(new Func<string>(()=>ReceiveSelection(json))),ResourceText("Defaults.html"));}
        string ReceiveSelection(string json){var data=LabSelection.Read(json);if(data.mode!="defaults")return AcceptSelection(json);if(data.job!=defaultJob||defaultCompletion==null)throw new Exception("この生成結果は更新済みです。");defaultCompletion.TrySetResult(data);return "デフォルト条件を受け取りました。";}
        void CancelDefaults(){defaultJob=null;var previous=defaultCancellation;defaultCancellation=null;defaultCompletion=null;if(previous!=null)previous.Cancel();defaultBusy=false;}
        public async void QueueDefaults(bool force=false){
            if(starting||!Visible||!UseDefaults||string.IsNullOrEmpty(board)||busy)return;
            if(!(System.Threading.SynchronizationContext.Current is WindowsFormsSynchronizationContext))System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            if(!force&&defaultBoard==board&&(defaultBusy||defaultChoices.Count>0))return;
            CancelDefaults();string target=board;defaultBoard=target;defaultChoices.Clear();defaultSelected.Clear();
            List<Choice> cached;if(!force&&defaultCache.TryGetValue(target,out cached)){ApplyDefaults(cached);return;}
            var cancellation=new System.Threading.CancellationTokenSource();defaultCancellation=cancellation;defaultCompletion=new TaskCompletionSource<LabSelection>(TaskCreationOptions.RunContinuationsAsynchronously);string job=Guid.NewGuid().ToString("N");defaultJob=job;defaultBusy=true;
            defaultsInfo.Text="185条件をボードに合わせて生成中…";RefreshGrid();RefreshCommand();
            try{
                EnsureBridge();var data=await DefaultRunner.RunAsync(bridge.Url+"defaults?board="+Uri.EscapeDataString(target)+"&job="+job,defaultCompletion.Task,cancellation.Token);
                if(defaultJob!=job||board!=target)return;if(!string.IsNullOrEmpty(data.error))throw new Exception(data.error);
                if(data.ranges.Length!=185||data.ranges.Select(r=>r.cell).Distinct().Count()!=185)throw new Exception("デフォルトの条件一覧が不完全です。");
                var result=data.ranges.Select(r=>new Choice{Filter=new Filter{Cell=r.cell,Label=r.label,Syntax=Commands.NormalizeSyntax(r.syntax)},Problem=r.count>0?null:(r.problem??"このボードでは該当なし")}).ToList();
                if(defaultCache.Count>=6)defaultCache.Clear();defaultCache[target]=result;defaultBusy=false;ApplyDefaults(result);
            }catch(OperationCanceledException){}
            catch(Exception ex){if(defaultJob==job&&!IsDisposed){defaultBoard=null;defaultsInfo.Text="生成できません："+ex.Message;SetStatus(defaultsInfo.Text,true);}}
            finally{if(defaultJob==job){defaultBusy=false;defaultCancellation=null;defaultCompletion=null;if(!IsDisposed)RefreshCommand();}cancellation.Dispose();}
        }
        void ApplyDefaults(List<Choice> items){defaultChoices.Clear();defaultChoices.AddRange(items);foreach(string id in new[]{"L49","L61"})if(defaultChoices.Any(c=>c.Filter.Cell==id&&c.Available))defaultSelected.Add(id);defaultsInfo.Text=board+" · デフォルト185条件（選択可 "+defaultChoices.Count(c=>c.Available)+"件）";if(UseDefaults){RefreshGrid();RefreshCommand();}}
        void OpenGenerator(){try{ApplyBoard();if(board.Length==0)return;Process.Start(new ProcessStartInfo(GeneratorUrl){UseShellExecute=true});}catch(Exception ex){SetStatus("生成画面を開けません："+ex.Message,true);}}
        void ImportSelection(){using(var dialog=new OpenFileDialog{Filter="PLO Syntax Labの条件|*.json"})if(dialog.ShowDialog(this)==DialogResult.OK)try{SetStatus(AcceptSelection(File.ReadAllText(dialog.FileName,Encoding.UTF8)),false);}catch(Exception ex){SetStatus(ex.Message,true);}}
        public string AcceptSelection(string json){
            if(busy)throw new Exception("集計中です。完了後に条件を追加してください。");var data=LabSelection.Read(json);var incoming=data.Choices();if(incoming.Count==0)throw new Exception("該当ハンドのある条件がありません。");
            string next=data.NormalizedBoard;if(!Commands.SameBoard(board,next)){generatedChoices.Clear();generatedSelected.Clear();defaultChoices.Clear();defaultSelected.Clear();defaultBoard=null;}board=next;
            starting=true;boardInput.Text=board;starting=false;debounce.Stop();
            foreach(var c in incoming){var existing=generatedChoices.FirstOrDefault(x=>x.Filter.Syntax==c.Filter.Syntax&&x.Filter.Label==c.Filter.Label);if(existing==null){generatedChoices.Add(c);existing=c;}generatedSelected.Add(existing.Filter.Cell);}
            UseDefaults=false;
            ApplyBoard();mainTabs.SelectedIndex=0;return incoming.Count+"条件を比較画面に追加しました。";
        }
        public void ApplyInput(string input){boardInput.Text=input;debounce.Stop();ApplyBoard();}
        void ApplyBoard(){
            try{
                string next=Commands.NormalizeBoard(boardInput.Text);if(!Commands.SameBoard(board,next)){CancelDefaults();generatedChoices.Clear();generatedSelected.Clear();defaultChoices.Clear();defaultSelected.Clear();defaultBoard=null;}board=next;
                for(int i=0;i<5;i++){if(i*2>=board.Length){cards[i].Text="—";continue;}char suit=board[i*2+1];cards[i].Text=board[i*2]+"\n"+new Dictionary<char,string>{{'s',"♠"},{'h',"♥"},{'d',"♦"},{'c',"♣"}}[suit];cards[i].ForeColor=suit=='d'?Color.FromArgb(28,100,184):suit=='h'?Color.FromArgb(190,49,66):suit=='c'?Mint:Ink;}
                RefreshGrid();RefreshCommand();if(Visible)QueueDefaults();
            }catch(Exception ex){CancelDefaults();board="";defaultBoard=null;defaultChoices.Clear();defaultSelected.Clear();generatedChoices.Clear();generatedSelected.Clear();foreach(var c in cards)c.Text="—";RefreshGrid();InvalidateCommand();SetStatus(ex.Message,true);}
        }
        void RefreshGrid(){
            refreshing=true;grid.SuspendLayout();grid.Rows.Clear();
            string q=search.Text.Trim();
            foreach(var c in choices){
                if(q.Length>0&&(c.Filter.Label+" "+c.Filter.Syntax).IndexOf(q,StringComparison.OrdinalIgnoreCase)<0)continue;
                int index=grid.Rows.Add(selected.Contains(c.Filter.Cell),c.Filter.Label,c.Filter.Syntax,c.Available?"選択可":c.Problem.Contains("該当なし")?"該当なし":"対象外");
                var row=grid.Rows[index];row.Tag=c;row.Cells[0].ReadOnly=!c.Available;
                if(!c.Available){row.DefaultCellStyle.ForeColor=Color.FromArgb(133,141,148);row.DefaultCellStyle.BackColor=Color.FromArgb(247,248,249);}
                foreach(DataGridViewCell cell in row.Cells)cell.ToolTipText=c.Available?c.Filter.Cell+"  "+c.Filter.Syntax:c.Problem;
            }
            grid.ResumeLayout();refreshing=false;UpdateSelectionSummary();
        }
        void UpdateSelectionSummary(){selectionSummary.Text=choices.Count(c=>c.Available&&selected.Contains(c.Filter.Cell))+"件選択"+(includeAll.Checked?" ＋ 全ハンド":"")+"  ·  "+grid.Rows.Count+" / "+choices.Count+"件表示";}
        void SetStatus(string text,bool error){status.Text=text;status.ForeColor=error?Color.FromArgb(171,57,37):Muted;}
        void InvalidateCommand(){command.Clear();copy.Enabled=false;save.Enabled=false;run.Enabled=false;codeSummary.Text="";if(!busy)ClearResults();}
        void RefreshCommand(){
            InvalidateCommand();UpdateSelectionSummary();
            if(starting||board=="")return;
            if(UseDefaults&&defaultBusy){SetStatus("デフォルトsyntaxを生成中です。CSVは先に選択できます。",false);return;}
            try {
                var selection=choices.Where(c=>selected.Contains(c.Filter.Cell)).ToList();
                var paths=ActivePaths();command.Text=CompareCommands.Build(board,paths,selection,includeAll.Checked,csvLabels.Take(paths.Length).Select(t=>t.Text).ToArray());
                copy.Enabled=true;save.Enabled=true;run.Enabled=!busy&&paths.Length>=2;codeSummary.Text=(selection.Count+(includeAll.Checked?1:0))+"集計 · "+ActivePaths().Length+" CSV";
                SetStatus(paths.Length<2?"syntax生成・コマンド保存はCSV不要です。集計時にCSV1・CSV2を選択してください。":board+" · "+choices.Count+"条件 · 集計できます",false);
            }catch(Exception ex){SetStatus(ex.Message,true);}
        }
        public void SelectCells(params string[] ids){selected.Clear();foreach(string id in ids)selected.Add(id);RefreshGrid();RefreshCommand();}
        void CopyCommand(){try{Clipboard.SetText(command.Text);codeSummary.Text="コピーしました";}catch(Exception ex){SetStatus("コピーできません："+ex.Message,true);}}
        void SaveCommand(){
            using(var dialog=new SaveFileDialog{Filter="PowerShellスクリプト|*.ps1",FileName=board+"_weight_comparison.ps1",OverwritePrompt=true})
            if(dialog.ShowDialog(this)==DialogResult.OK){try{if(!string.Equals(Path.GetExtension(dialog.FileName),".ps1",StringComparison.OrdinalIgnoreCase))throw new Exception("保存先の拡張子は .ps1 にしてください。");File.WriteAllText(dialog.FileName,command.Text,new UTF8Encoding(true));codeSummary.Text="保存しました";}catch(Exception ex){SetStatus("保存できません："+ex.Message,true);}}
        }
        public void SaveSnapshot(string path){Refresh();Application.DoEvents();using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
    }
}
