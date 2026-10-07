using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FlopCommands {
    public sealed class HistoryPanel : UserControl {
        readonly HistoryStore store;
        readonly DataGridView entries=new DataGridView(),results=new DataGridView();
        readonly TextBox search=new TextBox(),details=new TextBox(),code=new TextBox();
        readonly RatioChart chart=new RatioChart();
        readonly Label status=new Label();
        readonly Button export=new Button{Text="結果をCSV保存",Width=150,Height=35,Enabled=false};
        readonly Button delete=new Button{Text="選択した履歴を削除",Width=175,Height=35,Enabled=false};
        readonly TabControl preview=new TabControl();
        List<HistoryEntry> records=new List<HistoryEntry>();
        HistoryEntry current;RunResult result;
        bool refreshing;
        public RunResult CurrentResult {get{return result;}}
        public string CurrentId {get{return current==null?null:current.Id;}}
        public int EntryCount {get{return records.Count;}}
        public int VisibleEntryCount {get{return entries.Rows.Count;}}
        public bool CanDelete {get{return delete.Enabled;}}
        public void Search(string query){search.Text=query;}
        public string Message {get{return status.Text;}}
        public HistoryPanel(HistoryStore historyStore){
            store=historyStore;Dock=DockStyle.Fill;BackColor=Color.FromArgb(240,244,247);Font=new Font("Yu Gothic UI",10);Padding=new Padding(20);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,37));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,63));Controls.Add(layout);
            var left=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=new Padding(0,0,16,0)};
            left.RowStyles.Add(new RowStyle(SizeType.Absolute,40));left.RowStyles.Add(new RowStyle(SizeType.Absolute,36));left.RowStyles.Add(new RowStyle(SizeType.Percent,100));left.RowStyles.Add(new RowStyle(SizeType.Absolute,60));layout.Controls.Add(left,0,0);
            var tools=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));
            tools.Controls.Add(new Label{Text="保存した結果",Dock=DockStyle.Fill,Font=new Font("Yu Gothic UI",16)},0,0);
            var refresh=new Button{Text="更新",Dock=DockStyle.Fill};refresh.Click+=(s,e)=>RefreshEntries(CurrentId);tools.Controls.Add(refresh,1,0);left.Controls.Add(tools,0,0);
            search.Dock=DockStyle.Fill;search.AccessibleName="履歴を日時・フロップ・ラベル・条件・ファイル名で検索";var tip=new ToolTip();tip.SetToolTip(search,"日時・フロップ・CSVラベル・条件名・ファイル名で検索");search.TextChanged+=(s,e)=>FillEntries(CurrentId);
            var searchRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,50));searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));searchRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));searchRow.Controls.Add(new Label{Text="検索",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);searchRow.Controls.Add(search,1,0);left.Controls.Add(searchRow,0,1);
            SetupGrid(entries);entries.MultiSelect=false;entries.SelectionMode=DataGridViewSelectionMode.FullRowSelect;entries.Columns.Add(new DataGridViewTextBoxColumn{Name="date",HeaderText="日時",Width=185});entries.Columns.Add(new DataGridViewTextBoxColumn{Name="board",HeaderText="ボード",Width=115});entries.Columns.Add(new DataGridViewTextBoxColumn{Name="labels",HeaderText="CSVラベル",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,MinimumWidth=140});entries.Columns.Add(new DataGridViewTextBoxColumn{Name="conditions",HeaderText="件数",Width=55});entries.SelectionChanged+=(s,e)=>{if(!refreshing)ShowSelected();};left.Controls.Add(entries,0,2);
            status.Dock=DockStyle.Fill;status.Padding=new Padding(0,8,0,0);left.Controls.Add(status,0,3);
            var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};right.RowStyles.Add(new RowStyle(SizeType.Absolute,160));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.Controls.Add(right,1,0);
            details.Dock=DockStyle.Fill;details.Multiline=true;details.ReadOnly=true;details.ScrollBars=ScrollBars.Vertical;details.BackColor=Color.White;details.AccessibleName="保存時の日時・フロップ・条件・CSV";right.Controls.Add(details,0,0);
            preview.Dock=DockStyle.Fill;var graphPage=new TabPage("グラフ");graphPage.Controls.Add(chart);preview.TabPages.Add(graphPage);var tablePage=new TabPage("表");SetupGrid(results);tablePage.Controls.Add(results);preview.TabPages.Add(tablePage);var codePage=new TabPage("保存時のコマンド");code.Dock=DockStyle.Fill;code.ReadOnly=true;code.Multiline=true;code.WordWrap=false;code.ScrollBars=ScrollBars.Both;code.Font=new Font("Consolas",10);codePage.Controls.Add(code);preview.TabPages.Add(codePage);right.Controls.Add(preview,0,1);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};export.Click+=(s,e)=>SaveCsv();actions.Controls.Add(export);delete.Click+=(s,e)=>ConfirmDelete();actions.Controls.Add(delete);right.Controls.Add(actions,0,2);
            RefreshEntries();
        }
        static void SetupGrid(DataGridView grid){grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.RowHeadersVisible=false;grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.None;grid.RowTemplate.Height=34;grid.ColumnHeadersHeight=42;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(225,233,239);grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(215,241,234);grid.DefaultCellStyle.SelectionForeColor=Color.FromArgb(22,35,48);}
        public void RefreshEntries(string preferredId=null){
            try{int unreadable;records=store.List(out unreadable);FillEntries(preferredId);status.Text=records.Count+"件保存済み"+(unreadable>0?" · "+unreadable+"件は読み込めませんでした。":"")+"\r\n実行が完了すると自動保存します。";}
            catch(Exception ex){status.Text="履歴を読み込めません："+ex.Message;}
        }
        void FillEntries(string preferredId){
            refreshing=true;entries.Rows.Clear();string query=search.Text.Trim();int selected=-1;
            foreach(var item in records){
                string searchable=item.LocalTime.ToString("yyyy/MM/dd HH:mm:ss")+" "+item.Board+" "+string.Join(" ",item.Labels)+" "+string.Join(" ",item.Conditions)+" "+string.Join(" ",item.Paths);
                if(query.Length>0&&searchable.IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)continue;
                int index=entries.Rows.Add(item.LocalTime.ToString("yyyy/MM/dd HH:mm:ss"),item.Board,string.Join(" / ",item.Labels),item.Conditions.Length);entries.Rows[index].Tag=item.Id;
                foreach(DataGridViewCell cell in entries.Rows[index].Cells)cell.ToolTipText=searchable;
                if(item.Id==preferredId)selected=index;
            }
            if(entries.Rows.Count>0){if(selected<0)selected=0;entries.CurrentCell=entries.Rows[selected].Cells[0];entries.Rows[selected].Selected=true;}
            refreshing=false;ShowSelected();
        }
        void ClearPreview(){current=null;result=null;details.Clear();code.Clear();results.Rows.Clear();results.Columns.Clear();chart.SetData(null);export.Enabled=false;}
        void ShowSelected(){
            ClearPreview();delete.Enabled=entries.CurrentRow!=null;if(entries.CurrentRow==null)return;
            try{
                current=store.Load((string)entries.CurrentRow.Tag);result=current.Restore();chart.SetData(result);code.Text=current.Command;
                details.Text=current.LocalTime.ToString("yyyy/MM/dd HH:mm:ss")+"  ·  "+current.Board+"  ·  "+current.Conditions.Length+"条件\r\n条件："+string.Join(" / ",current.Conditions)+"\r\n"+string.Join("\r\n",current.Paths.Select((p,i)=>current.Labels[i]+" (CSV"+(i+1)+"): "+p))+(string.IsNullOrEmpty(current.WorkbookPath)?"\r\n生成元：PLO Syntax Lab":"\r\n元Excel："+current.WorkbookPath);
                results.Columns.Add(new DataGridViewTextBoxColumn{Name="condition",HeaderText="条件名",Width=145,Frozen=true});
                for(int i=0;i<current.Labels.Length;i++){
                    results.Columns.Add(new DataGridViewTextBoxColumn{Name="sum"+i,HeaderText=current.Labels[i]+"\nweight合計",ToolTipText=current.Labels[i],Width=115,DefaultCellStyle=new DataGridViewCellStyle{Format="0.####",Alignment=DataGridViewContentAlignment.MiddleRight}});
                    results.Columns.Add(new DataGridViewTextBoxColumn{Name="percent"+i,HeaderText=current.Labels[i]+"\n比率 (%)",ToolTipText=current.Labels[i],Width=90,DefaultCellStyle=new DataGridViewCellStyle{Format="0.00",NullValue="—",Alignment=DataGridViewContentAlignment.MiddleRight}});
                }
                results.Columns.Add(new DataGridViewTextBoxColumn{Name="total",HeaderText="全CSV\nweight合計",Width=110,DefaultCellStyle=new DataGridViewCellStyle{Format="0.####"}});
                foreach(var row in result.Rows){var values=new List<object>{row.Label};foreach(var source in row.Sources){values.Add(source.Sum);values.Add(source.Percent);}values.Add(row.Total);int index=results.Rows.Add(values.ToArray());foreach(DataGridViewCell cell in results.Rows[index].Cells)cell.ToolTipText=row.Syntax+"\r\n"+string.Join("\r\n",row.Sources.Select(s=>s.Label+": "+s.Count.ToString("N0")+"ハンド"));}
                export.Enabled=true;
            }catch(Exception ex){ClearPreview();details.Text="この履歴を開けません："+ex.Message;}
        }
        public void SelectEntry(string id){search.Clear();RefreshEntries(id);}
        public void DeleteSelected(){
            var row=entries.CurrentRow;if(row==null)return;
            string id=(string)row.Tag;int index=row.Index;
            string nextId=index+1<entries.Rows.Count?(string)entries.Rows[index+1].Tag:index>0?(string)entries.Rows[index-1].Tag:null;
            store.Delete(id);
            RefreshEntries(nextId);
        }
        void ConfirmDelete(){
            var row=entries.CurrentRow;if(row==null)return;
            string description=Convert.ToString(row.Cells[0].Value)+"  ·  "+Convert.ToString(row.Cells[1].Value)+"\r\n"+Convert.ToString(row.Cells[2].Value);
            if(MessageBox.Show(this,description+"\r\n\r\nこの履歴を削除しますか？\r\n削除した履歴は元に戻せません。元のCSVやExcel、書き出したCSVは削除しません。","履歴の削除",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            try{DeleteSelected();}catch(Exception ex){MessageBox.Show(this,"履歴を削除できません："+ex.Message,"削除できません",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
        public void ExportTo(string path){
            if(current==null||result==null)throw new Exception("履歴を選択してください。");string full=Path.GetFullPath(path);
            if(!string.Equals(Path.GetExtension(full),".csv",StringComparison.OrdinalIgnoreCase))throw new Exception("保存先は .csv にしてください。");
            foreach(string input in current.Paths.Concat(new[]{current.WorkbookPath,Application.ExecutablePath}))if(!string.IsNullOrEmpty(input)&&string.Equals(full,Path.GetFullPath(input),StringComparison.OrdinalIgnoreCase))throw new Exception("入力ファイルには上書きできません。");
            File.WriteAllBytes(full,result.Csv);
        }
        void SaveCsv(){if(current==null)return;using(var dialog=new SaveFileDialog{Filter="CSVファイル|*.csv",FileName=current.Board+"_"+current.LocalTime.ToString("yyyyMMdd_HHmmss")+"_history.csv",OverwritePrompt=true})if(dialog.ShowDialog(this)==DialogResult.OK)try{ExportTo(dialog.FileName);status.Text="履歴の結果をCSVに保存しました。";}catch(Exception ex){MessageBox.Show(this,ex.Message,"保存できません",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
    }
}
