using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FlopCommands.Equity;

namespace FlopCommands {
    static class EquityExportUi {
        public static void Save(IWin32Window owner,EquityArchive archive,string extension){
            if(archive==null)return;
            using(var dialog=new SaveFileDialog{Filter=extension.Substring(1).ToUpperInvariant()+"ファイル|*"+extension,FileName=archive.Job.Input.Board+"_EQ_"+DateTime.Parse(archive.CreatedUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToLocalTime().ToString("yyyyMMdd_HHmmss")+extension,OverwritePrompt=true}){
                if(dialog.ShowDialog(owner)!=DialogResult.OK)return;
                try{EquityFiles.Export(archive,dialog.FileName,extension,Application.ExecutablePath);}
                catch(Exception ex){MessageBox.Show(owner,ex.Message,"保存できません",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            }
        }
    }
    public sealed class EquityHistoryForm : Form {
        readonly EquityHistoryStore store;readonly ListBox entries=new ListBox();readonly Label status=new Label();
        readonly DataGridView table=new DataGridView();readonly TextBox details=new TextBox();readonly EquityScatterChart chart=new EquityScatterChart();
        readonly Button csv=new Button(),json=new Button(),svg=new Button(),delete=new Button();EquityArchive current;
        sealed class Item {public EquityArchive Value;public override string ToString(){return DateTime.Parse(Value.CreatedUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")+" · "+Value.Job.Input.Board+" · "+Value.Job.Rows.Length+"条件";}}
        public EquityArchive Current {get{return current;}}
        public EquityHistoryForm(EquityHistoryStore history){
            store=history;Text="EQ履歴 — 保存した結果";ClientSize=new Size(1240,760);MinimumSize=new Size(950,600);StartPosition=FormStartPosition.CenterParent;Font=new Font("Yu Gothic UI",10);
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Padding=new Padding(12)};root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,340));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,44));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,64));Controls.Add(root);
            var tools=new FlowLayoutPanel{Dock=DockStyle.Fill};var refresh=new Button{Text="履歴を更新",AutoSize=true};refresh.Click+=(s,e)=>Reload();tools.Controls.Add(refresh);var open=new Button{Text="JSONを開く",AutoSize=true};open.Click+=(s,e)=>OpenJson();tools.Controls.Add(open);root.Controls.Add(tools,0,0);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};SetupButton(csv,"CSV保存",()=>EquityExportUi.Save(this,current,".csv"),actions);SetupButton(json,"JSON保存",()=>EquityExportUi.Save(this,current,".json"),actions);SetupButton(svg,"グラフSVG保存",()=>EquityExportUi.Save(this,current,".svg"),actions);SetupButton(delete,"履歴を削除",DeleteCurrent,actions);root.Controls.Add(actions,1,0);
            entries.Dock=DockStyle.Fill;entries.HorizontalScrollbar=true;entries.SelectedIndexChanged+=(s,e)=>{var item=entries.SelectedItem as Item;ShowRecord(item==null?null:item.Value);delete.Enabled=item!=null;};root.Controls.Add(entries,0,1);
            var tabs=new TabControl{Dock=DockStyle.Fill};var graph=new TabPage("散布図");graph.Controls.Add(chart);tabs.TabPages.Add(graph);
            table.Dock=DockStyle.Fill;table.ReadOnly=true;table.AllowUserToAddRows=false;table.AllowUserToDeleteRows=false;table.RowHeadersVisible=false;table.BackgroundColor=Color.White;
            foreach(var col in new[]{"条件","syntax","EQ (%)","95%区間","ベット頻度 (%)","状態／理由"})table.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=col,Width=col=="状態／理由"?280:160,SortMode=DataGridViewColumnSortMode.NotSortable});
            var rows=new TabPage("結果表");rows.Controls.Add(table);tabs.TabPages.Add(rows);details.Multiline=true;details.ReadOnly=true;details.ScrollBars=ScrollBars.Both;details.Dock=DockStyle.Fill;var inputs=new TabPage("保存時の入力");inputs.Controls.Add(details);tabs.TabPages.Add(inputs);root.Controls.Add(tabs,1,1);
            status.Dock=DockStyle.Fill;root.Controls.Add(status,0,2);root.SetColumnSpan(status,2);Reload();
        }
        static void SetupButton(Button button,string text,Action action,Control parent){button.Text=text;button.AutoSize=true;button.Enabled=false;button.Click+=(s,e)=>action();parent.Controls.Add(button);}
        public void Reload(){try{int unreadable;var all=store.List(out unreadable);entries.Items.Clear();foreach(var a in all)entries.Items.Add(new Item{Value=a});if(entries.Items.Count>0)entries.SelectedIndex=0;else ShowRecord(null);status.Text=all.Count+"件保存済み"+(unreadable>0?" · 読み込めない履歴 "+unreadable+"件":"")+"\r\n入力CSVを再読込せず保存時の結果を表示します。自動保存先："+store.Root;}catch(Exception ex){status.Text="履歴を読めません："+ex.Message;}}
        public void ShowRecord(EquityArchive record){
            if(record!=null)EquityFiles.Validate(record);current=record;table.Rows.Clear();chart.SetData(record==null?null:record.Job);details.Clear();csv.Enabled=json.Enabled=record!=null;svg.Enabled=record!=null&&chart.PointCount>0;delete.Enabled=false;if(record==null)return;
            var j=record.Job;foreach(var row in j.Rows){var r=row.Result;var f=row.Frequency;string frequency=f==null||f.Percent==null?"—":string.Join(" / ",Enumerable.Range(0,j.Input.BetActions.Length).Where(a=>j.Input.BetActions[a]).Select(a=>j.Input.ActionLabels[a]+" (CSV"+(a+1)+"): "+f.Percent[a].ToString("F2")+"%"));table.Rows.Add(row.Label,row.Syntax,r==null?"—":r.Equity.ToString("F2"),r==null?"—":r.CiLow.ToString("F2")+"–"+r.CiHigh.ToString("F2"),frequency,row.Status+(f!=null&&f.Percent==null?" / "+f.Status:""));}
            details.Text="保存日時（UTC）："+record.CreatedUtc+"\r\nボード："+j.Input.Board+"\r\n自分のレンジ指定："+j.Input.HeroMode+"\r\n自分の入力レンジ：\r\n"+j.Input.HeroText+"\r\n相手のアクション前レンジ：\r\n"+j.Input.VillainText+"\r\n基準シード："+j.BaseSeed+"\r\n試行数／syntax："+j.Input.Settings.Trials+"\r\n"+string.Join("\r\n",j.Input.ActionPaths.Select((p,i)=>j.Input.ActionLabels[i]+": "+p))+"\r\n\r\n"+j.PlotStatus+"\r\n\r\n元のアクションCSV本体は保存していません。再計算する場合は元のCSVが必要です。";
        }
        void OpenJson(){using(var dialog=new OpenFileDialog{Filter="EQ結果JSON|*.json"})if(dialog.ShowDialog(this)==DialogResult.OK)try{if(new FileInfo(dialog.FileName).Length>EquityFiles.MaxBytes)throw new ArgumentException("64MiB以下にしてください。");var a=EquityFiles.Decode(File.ReadAllBytes(dialog.FileName));entries.ClearSelected();ShowRecord(a);status.Text="JSONの保存結果を表示中："+dialog.FileName;}catch(Exception ex){MessageBox.Show(this,ex.Message,"開けません",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
        void DeleteCurrent(){var item=entries.SelectedItem as Item;if(item==null)return;if(MessageBox.Show(this,"選択したEQ履歴を削除しますか？\r\n元のCSVや書き出した結果は削除しません。","EQ履歴",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;try{store.Delete(item.Value.Id);Reload();}catch(Exception ex){status.Text=ex.Message;}}
    }
}
