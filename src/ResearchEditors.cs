using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlopCommands.Research;

namespace FlopCommands {
    internal static class ResearchUi {
        public static Button Button(string text,Action action){var b=new Button{Text=text,AutoSize=true,Height=32,Margin=new Padding(3)};b.Click+=(s,e)=>{try{action();}catch(Exception ex){MessageBox.Show(b.FindForm(),ex.Message,"調査設定",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};return b;}
        public static TableLayoutPanel Rows(params int[] heights){var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=heights.Length,Padding=new Padding(8)};foreach(var h in heights)p.RowStyles.Add(h<0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,h));return p;}
        public static FlowLayoutPanel Bar(){return new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true,Margin=Padding.Empty};}
        public static TextBox TextBox(bool multi=false){return new TextBox{Dock=DockStyle.Fill,Multiline=multi,ScrollBars=multi?ScrollBars.Vertical:ScrollBars.None,MaxLength=multi?2000000:4000};}
        public static DataGridView Grid(){return new DataGridView{Dock=DockStyle.Fill,RowHeadersVisible=false,AutoGenerateColumns=false,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BackgroundColor=Color.White};}
        public static void Form(Form form,string title,int width,int height){form.Text=title;form.ClientSize=new Size(width,height);form.MinimumSize=new Size(width,height);form.StartPosition=FormStartPosition.CenterParent;form.Font=new Font("Yu Gothic UI",10);form.AutoScaleMode=AutoScaleMode.Dpi;form.ShowInTaskbar=false;}
        public static void Field(TableLayoutPanel grid,int row,string label,Control input){grid.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);grid.Controls.Add(input,1,row);}
        public static TableLayoutPanel Fields(int rows){var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=rows};p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,200));p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));for(int i=0;i<rows;i++)p.RowStyles.Add(new RowStyle(SizeType.Percent,100f/rows));return p;}
    }
    public sealed class ResearchConditionEditor : Form {
        public ResearchConditionSet Value {get;private set;}
        readonly TextBox setName=ResearchUi.TextBox(),label=ResearchUi.TextBox(),syntax=ResearchUi.TextBox(true);
        readonly CheckBox enabled=new CheckBox{Text="この条件を使用",Dock=DockStyle.Fill};
        readonly ComboBox kind=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
        readonly ListBox list=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};
        readonly DataGridView mapping=ResearchUi.Grid();
        readonly Panel body=new Panel{Dock=DockStyle.Fill};
        readonly TextBox extra=ResearchUi.TextBox(true);
        readonly Label generatedInfo=new Label{Dock=DockStyle.Fill};
        readonly TableLayoutPanel generated=ResearchUi.Rows(120,36,-1);
        int current=-1;bool loading;
        public ResearchConditionEditor(ResearchConditionSet value){
            Value=ResearchJson.Clone(value);ResearchUi.Form(this,"調査条件セットを編集",1000,690);Name="researchConditionEditor";var root=ResearchUi.Rows(38,42,48,-1,42);Controls.Add(root);
            var name=ResearchUi.Fields(1);ResearchUi.Field(name,0,"条件セット名",setName);setName.Text=Value.Name;root.Controls.Add(name,0,0);
            setName.Name="researchSetName";setName.MaxLength=200;label.Name="researchConditionLabel";label.MaxLength=1000;enabled.Name="researchConditionEnabled";list.Name="researchConditionList";
            var presets=ResearchUi.Bar();var choose=ResearchUi.Button("デフォルト条件から追加",ChooseDefaults);choose.Name="researchChooseDefaults";presets.Controls.Add(choose);presets.Controls.Add(ResearchUi.Button("条件を掛け合わせる",()=>Compose(null)));presets.Controls.Add(ResearchUi.Button("選択条件をppランク別に分割",()=>{CommitCurrent();if(current<0)return;int index=current;ResearchPresets.SplitPocketRanks(Value,Value.Conditions[current].Id);Reload(index);}));presets.Controls.Add(new Label{Text="複数選択できます。表示名を変えても元の条件は保持されます。",AutoSize=true,Padding=new Padding(8,6,0,0)});root.Controls.Add(presets,0,1);
            root.Controls.Add(new Label{Text="デフォルト条件はボードごとに自動生成します。手入力の共通syntax・ボード別syntaxも同じセットに保存できます。",Dock=DockStyle.Fill},0,2);
            var split=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,250));split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.Controls.Add(split,0,3);
            var left=ResearchUi.Rows(-1,42);left.Controls.Add(list,0,0);var bar=ResearchUi.Bar();bar.Controls.Add(ResearchUi.Button("追加",()=>{CommitCurrent();Value.Conditions.Add(new ResearchCondition());Reload(Value.Conditions.Count-1);}));bar.Controls.Add(ResearchUi.Button("複製",()=>{CommitCurrent();if(current<0)return;var c=ResearchJson.Clone(Value.Conditions[current]);c.Id=Guid.NewGuid().ToString("N");c.Label+=" コピー";Value.Conditions.Add(c);Reload(Value.Conditions.Count-1);}));bar.Controls.Add(ResearchUi.Button("削除",()=>{if(current<0)return;int index=current;current=-1;Value.Conditions.RemoveAt(index);Reload(Math.Min(index,Value.Conditions.Count-1));}));left.Controls.Add(bar,0,1);split.Controls.Add(left,0,0);
            var right=ResearchUi.Rows(104,38,-1,42);var fields=ResearchUi.Fields(3);ResearchUi.Field(fields,0,"条件名",label);ResearchUi.Field(fields,1,"適用方法",kind);ResearchUi.Field(fields,2,"",enabled);right.Controls.Add(fields,0,0);right.Controls.Add(new Label{Text="自動生成の定義は保持されます。不要な条件は左側の一覧から削除できます。",Dock=DockStyle.Fill},0,1);
            syntax.Font=new Font("Consolas",11);mapping.Columns.Add("board","ボード（例 AsKd7c）");mapping.Columns.Add("syntax","syntax");mapping.Columns[0].FillWeight=28;mapping.Columns[1].FillWeight=72;mapping.AllowUserToAddRows=true;mapping.AllowUserToDeleteRows=true;body.Controls.Add(syntax);body.Controls.Add(mapping);right.Controls.Add(body,0,2);
            extra.Name="researchAdditionalSyntax";extra.Font=new Font("Consolas",11);extra.MaxLength=1000000;generated.Padding=Padding.Empty;generated.Controls.Add(generatedInfo,0,0);generated.Controls.Add(new Label{Text="追加syntax（AND）。指定なしは *。ここで指定したランク・スートは固定です。",Dock=DockStyle.Fill},0,1);generated.Controls.Add(extra,0,2);body.Controls.Add(generated);
            right.Controls.Add(new Label{Text="全ハンドを含める場合も「*」を明示的に登録してください。空欄は全ハンドになりません。",Dock=DockStyle.Fill},0,3);split.Controls.Add(right,1,0);
            var bottom=ResearchUi.Bar();var save=ResearchUi.Button("検証して保存",()=>{CommitCurrent();Value.Name=setName.Text;ResearchValidation.Set(Value);DialogResult=DialogResult.OK;Close();});save.Name="researchConditionSave";bottom.Controls.Add(save);var cancel=ResearchUi.Button("キャンセル",()=>{DialogResult=DialogResult.Cancel;Close();});bottom.Controls.Add(cancel);CancelButton=cancel;root.Controls.Add(bottom,0,4);
            kind.SelectedIndexChanged+=(s,e)=>{syntax.Visible=kind.SelectedIndex==0;mapping.Visible=kind.SelectedIndex==1;generated.Visible=kind.SelectedIndex==2;if(generated.Visible)generated.BringToFront();else if(mapping.Visible)mapping.BringToFront();else syntax.BringToFront();};
            list.SelectedIndexChanged+=(s,e)=>{if(loading)return;int next=list.SelectedIndex;try{CommitCurrent();LoadCondition(next);}catch(Exception ex){loading=true;list.SelectedIndex=current;loading=false;MessageBox.Show(this,ex.Message,"条件を確認してください");}};Reload(Value.Conditions.Count>0?0:-1);
        }
        public int AddDefaults(IEnumerable<string> ids){return AddDefaults(ids,false);}
        public int AddDefaults(IEnumerable<string> ids,bool splitPocket){CommitCurrent();
            var before=new HashSet<string>(Value.Conditions.Select(c=>c.Id));
            int added=ResearchPresets.Add(Value,ids);
            if(added>0){
                // Only discard untouched empty placeholders; never erase user input.
                Value.Conditions.RemoveAll(c=>c.Kind=="literal"&&c.Label=="新しい条件"&&string.IsNullOrWhiteSpace(c.Syntax)&&c.ByBoard.Count==0&&c.Generator==null);
                if(splitPocket)foreach(var c in Value.Conditions.Where(c=>!before.Contains(c.Id)&&c.Generator!=null&&c.Generator.PresetId.Contains(".pocket.")).ToArray())ResearchPresets.SplitPocketRanks(Value,c.Id);
            }
            current=-1;Reload(Value.Conditions.FindIndex(c=>!before.Contains(c.Id)));return added;}

        void Compose(string initial){CommitCurrent();using(var d=new ResearchConditionComposer(initial))if(d.ShowDialog(this)==DialogResult.OK){if(Value.Conditions.Count+d.Conditions.Count>1000)throw new ArgumentException("条件は1,000件までです。");Value.Conditions.RemoveAll(c=>c.Kind=="literal"&&c.Label=="新しい条件"&&string.IsNullOrWhiteSpace(c.Syntax)&&c.ByBoard.Count==0&&c.Generator==null);Value.Conditions.AddRange(d.Conditions);current=-1;Reload(Value.Conditions.Count-d.Conditions.Count);}}
        void ChooseDefaults(){CommitCurrent();using(var dialog=new ResearchPresetPicker(Value))if(dialog.ShowDialog(this)==DialogResult.OK){if(dialog.ComposePresetId!=null)Compose(dialog.ComposePresetId);else AddDefaults(dialog.SelectedPresetIds,dialog.SplitPocket);}}
        void CommitCurrent(){if(current<0)return;mapping.EndEdit();var c=Value.Conditions[current];var next=new Dictionary<string,string>();if(kind.SelectedIndex==1)foreach(DataGridViewRow row in mapping.Rows){if(row.IsNewRow)continue;string board=Convert.ToString(row.Cells[0].Value),text=Convert.ToString(row.Cells[1].Value);if(string.IsNullOrWhiteSpace(board)&&string.IsNullOrWhiteSpace(text))continue;string key=ResearchValidation.Board(board);if(next.ContainsKey(key))throw new ArgumentException("同じボードの定義が重複しています。");next.Add(key,text);}c.Label=label.Text;c.Enabled=enabled.Checked;c.Kind=kind.SelectedIndex==2?"generated":kind.SelectedIndex==1?"per_board":"literal";c.Syntax=c.Kind=="literal"?syntax.Text:"";c.ByBoard=next;if(c.Kind=="generated")c.Generator.AdditionalSyntax=extra.Text;else c.Generator=null;loading=true;list.Items[current]=c;loading=false;}
        void Reload(int selected){loading=true;list.Items.Clear();foreach(var c in Value.Conditions)list.Items.Add(c);list.SelectedIndex=selected;loading=false;LoadCondition(selected);}
        void LoadCondition(int index){current=index;label.Enabled=kind.Enabled=enabled.Enabled=body.Enabled=index>=0;mapping.Rows.Clear();kind.Items.Clear();kind.Items.AddRange(new object[]{"全ボード共通","ボード別に指定"});if(index<0){label.Clear();syntax.Clear();extra.Clear();generatedInfo.Text="";return;}var c=Value.Conditions[index];label.Text=c.Label;enabled.Checked=c.Enabled;syntax.Text=c.Syntax;
            if(c.Kind=="generated"){
                kind.Items.Add("ボードごとに自動生成");kind.Enabled=false;kind.SelectedIndex=2;extra.Text=c.Generator.AdditionalSyntax;
                var p=c.Generator.Source=="preset"?ResearchPresets.All().FirstOrDefault(x=>x.Id==c.Generator.PresetId):null;
                generatedInfo.Text=c.Generator.Source=="preset"?"元のデフォルト条件："+(p==null?c.Generator.PresetId:p.Label)+"\n保存した定義：版 "+c.Generator.PresetRevision+"\n"+(p==null?"この条件は現在の一覧にありません。定義は保持されています。":!p.Supported?p.Reason:p.Revision!=c.Generator.PresetRevision?"保存された版は現在の一覧と異なります。定義は保持されています。":"対象ボードに応じてランク・ドローを判定します。"):
                    c.Generator.Source=="combination"?"掛け合わせ条件（各軸はOR、軸間はAND）\n"+c.Label+"\n対象ボードごとに生成します。":
                    "保存済みの生成フィルター\n完成役・ドロー・固定／相対ランクの指定を保持しています。";
            }else kind.SelectedIndex=c.Kind=="per_board"?1:0;
            foreach(var p in c.ByBoard)mapping.Rows.Add(p.Key,p.Value);
        }
    }
    public sealed class ResearchSpotEditor : Form {
        public ResearchSpot Value {get;private set;}
        readonly TextBox board=ResearchUi.TextBox(),node=ResearchUi.TextBox(),opponents=ResearchUi.TextBox(true);
        readonly DataGridView actions=ResearchUi.Grid();
        readonly CheckBox all=new CheckBox{Text="自分の全アクションCSVであり、共通weight基準である",Dock=DockStyle.Fill};
        readonly CheckBox opponentScale=new CheckBox{Text="相手CSVは同じアクション前のレンジで、共通weight基準である",Dock=DockStyle.Fill};
        readonly ComboBox unit=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=180};
        readonly Label status=new Label{Dock=DockStyle.Fill};
        readonly Control editor;readonly Button save,cancel;
        CancellationTokenSource cancellation;
        public ResearchSpotEditor(ResearchSpot value){
            Value=ResearchJson.Clone(value);ResearchUi.Form(this,"ボードとCSVを登録",1080,740);var root=ResearchUi.Rows(-1,54,42);Controls.Add(root);var input=ResearchUi.Rows(70,34,150,42,32,34,-1,42,32);editor=input;root.Controls.Add(input,0,0);
            var fields=ResearchUi.Fields(2);ResearchUi.Field(fields,0,"フロップ（スート付き）",board);ResearchUi.Field(fields,1,"アクション位置・ノード",node);board.Text=Value.Board;node.Text=Value.Node;input.Controls.Add(fields,0,0);
            input.Controls.Add(new Label{Text="自分：2〜3アクション。サイズはポット比率（33%なら0.33）。raiseもポット比率で明示してください。",Dock=DockStyle.Fill},0,1);
            actions.Columns.Add("label","表示名");actions.Columns.Add(new DataGridViewComboBoxColumn{Name="kind",HeaderText="種類",DataSource=new[]{"check","bet","fold","call","raise"}});actions.Columns.Add("size","ポット比 / allin");actions.Columns.Add("path","CSVのフルパス");actions.Columns[3].FillWeight=280;actions.Columns[3].ReadOnly=true;foreach(var a in Value.Actions){int i=actions.Rows.Add(a.Label,a.Kind,a.PotFraction,a.Source.Path);actions.Rows[i].Tag=a.Id;}input.Controls.Add(actions,0,2);
            var actionBar=ResearchUi.Bar();actionBar.Controls.Add(ResearchUi.Button("アクション追加",()=>{if(actions.Rows.Count>=3)throw new ArgumentException("初期版は3アクションまでです。");int i=actions.Rows.Add("","check","","");actions.Rows[i].Tag=Guid.NewGuid().ToString("N");actions.CurrentCell=actions.Rows[i].Cells[0];}));actionBar.Controls.Add(ResearchUi.Button("選択行のCSVを指定",ChooseAction));actionBar.Controls.Add(ResearchUi.Button("選択行を削除",()=>{if(actions.CurrentRow!=null)actions.Rows.Remove(actions.CurrentRow);}));input.Controls.Add(actionBar,0,3);all.Checked=Value.AllActionsCommonScale;input.Controls.Add(all,0,4);
            input.Controls.Add(new Label{Text="相手：複数CSVを統合（未指定なら頻度調査用として登録）。登録するすべてのフルパスを表示します。",Dock=DockStyle.Fill},0,5);opponents.ReadOnly=true;opponents.BackColor=Color.White;opponents.Text=string.Join(Environment.NewLine,Value.Opponents.Select(s=>s.Path));input.Controls.Add(opponents,0,6);
            var oppBar=ResearchUi.Bar();oppBar.Controls.Add(ResearchUi.Button("相手CSVを追加（複数可）",ChooseOpponents));oppBar.Controls.Add(ResearchUi.Button("相手CSVを解除",()=>opponents.Clear()));unit.Items.AddRange(new object[]{"相手weight：0〜1","相手weight：0〜100"});unit.SelectedIndex=Value.OpponentPercentUnit?1:0;oppBar.Controls.Add(unit);input.Controls.Add(oppBar,0,7);opponentScale.Checked=Value.OpponentCommonScale;input.Controls.Add(opponentScale,0,8);
            root.Controls.Add(status,0,1);var bottom=ResearchUi.Bar();save=new Button{Text="CSVを検証して登録",AutoSize=true,Height=32};save.Click+=async(s,e)=>await ValidateAsync();cancel=ResearchUi.Button("キャンセル",()=>{if(cancellation!=null){cancellation.Cancel();return;}DialogResult=DialogResult.Cancel;Close();});bottom.Controls.Add(save);bottom.Controls.Add(cancel);root.Controls.Add(bottom,0,2);CancelButton=cancel;
            FormClosing+=(s,e)=>{if(cancellation!=null){e.Cancel=true;cancellation.Cancel();}};
        }
        void ChooseAction(){if(actions.CurrentRow==null)throw new ArgumentException("アクションの行を選択してください。");using(var d=new OpenFileDialog{Filter="CSV|*.csv"})if(d.ShowDialog(this)==DialogResult.OK)actions.CurrentRow.Cells[3].Value=d.FileName;}
        void ChooseOpponents(){using(var d=new OpenFileDialog{Filter="CSV|*.csv",Multiselect=true})if(d.ShowDialog(this)==DialogResult.OK)opponents.Text=string.Join(Environment.NewLine,opponents.Lines.Where(x=>!string.IsNullOrWhiteSpace(x)).Concat(d.FileNames).Distinct(StringComparer.OrdinalIgnoreCase));}
        ResearchSpot Draft(){actions.EndEdit();var next=ResearchJson.Clone(Value);next.Board=board.Text;next.Node=node.Text;next.AllActionsCommonScale=all.Checked;next.OpponentCommonScale=opponentScale.Checked;next.OpponentPercentUnit=unit.SelectedIndex==1;next.Actions=actions.Rows.Cast<DataGridViewRow>().Select(r=>new ResearchAction{Id=(string)r.Tag,Label=Convert.ToString(r.Cells[0].Value),Kind=Convert.ToString(r.Cells[1].Value),PotFraction=Convert.ToString(r.Cells[2].Value),Source=new ResearchSource{Path=Convert.ToString(r.Cells[3].Value)}}).ToList();next.Opponents=opponents.Lines.Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>new ResearchSource{Path=x}).ToList();return next;}
        async Task ValidateAsync(){
            try{var draft=Draft();ResearchValidation.Spot(draft,false);cancellation=new CancellationTokenSource();var token=cancellation.Token;editor.Enabled=save.Enabled=false;cancel.Text="検証を中止";status.Text="CSV内容・ボード・weight・統合ハンド数を検証中…";var checkedSpot=await Task.Run(()=>ResearchValidation.ValidateFiles(draft,token));token.ThrowIfCancellationRequested();Value=checkedSpot;cancellation.Dispose();cancellation=null;DialogResult=DialogResult.OK;Close();}
            catch(OperationCanceledException){status.Text="検証を中止しました。登録内容は変更していません。";}
            catch(Exception ex){status.Text=ex.Message;}
            finally{if(cancellation!=null){cancellation.Dispose();cancellation=null;}if(!IsDisposed){editor.Enabled=save.Enabled=true;cancel.Text="キャンセル";}}
        }
    }
    public sealed class ResearchCatalogEditor : Form {
        public ResearchCatalog Value {get;private set;}
        readonly TextBox name=ResearchUi.TextBox(),hero=ResearchUi.TextBox(),villain=ResearchUi.TextBox(),stack=ResearchUi.TextBox(),pot=ResearchUi.TextBox(),history=ResearchUi.TextBox(),context=ResearchUi.TextBox(),reference=ResearchUi.TextBox();
        readonly ListBox spots=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};
        public ResearchCatalogEditor(ResearchCatalog value){
            Value=ResearchJson.Clone(value);ResearchUi.Form(this,"複数ボードのCSV登録",1050,700);var root=ResearchUi.Rows(48,272,-1,42,42);Controls.Add(root);
            root.Controls.Add(new Label{Text="このカタログ内は同じ局面条件で比較します。ポジション・スタック等が異なる場合は別カタログにしてください。初期版はPLO4・2人・フロップです。",Dock=DockStyle.Fill},0,0);
            var fields=ResearchUi.Fields(8);var labels=new[]{"カタログ名","自分のポジション","相手のポジション","実効スタック（BB）","ポット種別（SRP/3bet等）","基準点より前のアクション","レーキ・ソルバー設定","EQの基準点"};var boxes=new[]{name,hero,villain,stack,pot,history,context,reference};for(int i=0;i<boxes.Length;i++)ResearchUi.Field(fields,i,labels[i],boxes[i]);root.Controls.Add(fields,0,1);
            var s=Value.Scenario;name.Text=Value.Name;hero.Text=s.HeroPosition;villain.Text=s.VillainPosition;stack.Text=s.StackBb;pot.Text=s.PotType;history.Text=s.ActionHistory;context.Text=s.SolutionContext;reference.Text=s.EquityReference;root.Controls.Add(spots,0,2);
            var bar=ResearchUi.Bar();bar.Controls.Add(ResearchUi.Button("ボードとCSVを追加",()=>EditSpot(-1)));bar.Controls.Add(ResearchUi.Button("選択ボードを編集",()=>{if(spots.SelectedIndex>=0)EditSpot(spots.SelectedIndex);}));bar.Controls.Add(ResearchUi.Button("選択ボードを登録から外す",()=>{int i=spots.SelectedIndex;if(i<0)return;Value.Spots.RemoveAt(i);Reload();}));root.Controls.Add(bar,0,3);
            var bottom=ResearchUi.Bar();bottom.Controls.Add(ResearchUi.Button("カタログを保存",()=>{Value.Name=name.Text;Value.Scenario=new ResearchScenario{HeroPosition=hero.Text,VillainPosition=villain.Text,StackBb=stack.Text,PotType=pot.Text,ActionHistory=history.Text,SolutionContext=context.Text,EquityReference=reference.Text};ResearchValidation.Catalog(Value);DialogResult=DialogResult.OK;Close();}));var cancel=ResearchUi.Button("キャンセル",()=>{DialogResult=DialogResult.Cancel;Close();});bottom.Controls.Add(cancel);CancelButton=cancel;root.Controls.Add(bottom,0,4);Reload();
        }
        void Reload(){spots.Items.Clear();foreach(var s in Value.Spots)spots.Items.Add(s);}
        void EditSpot(int index){var value=index<0?new ResearchSpot{Actions=new List<ResearchAction>{new ResearchAction{Label="Check",Kind="check"},new ResearchAction{Label="Bet 33%",Kind="bet",PotFraction="0.33"}}}:Value.Spots[index];using(var dialog=new ResearchSpotEditor(value))if(dialog.ShowDialog(this)==DialogResult.OK){if(index<0)Value.Spots.Add(dialog.Value);else Value.Spots[index]=dialog.Value;Reload();spots.SelectedIndex=index<0?spots.Items.Count-1:index;}}
    }
}
