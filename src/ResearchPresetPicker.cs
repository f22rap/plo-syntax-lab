using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FlopCommands.Research;

namespace FlopCommands {
    public sealed class ResearchPresetPicker : Form {
        readonly ResearchDefaultPreset[] presets=ResearchPresets.All();
        readonly HashSet<string> chosen=new HashSet<string>(StringComparer.Ordinal),existing;
        readonly TextBox search=ResearchUi.TextBox();
        readonly DataGridView grid=ResearchUi.Grid();
        readonly Label count=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
        readonly Button add;
        readonly CheckBox splitPocket=new CheckBox{Text="pp条件をランク別に出力",AutoSize=true};
        public string ComposePresetId {get;private set;}
        public bool SplitPocket {get{return splitPocket.Checked;}}
        bool loading;
        public string[] SelectedPresetIds {get{return presets.Where(p=>chosen.Contains(p.Id)).Select(p=>p.Id).ToArray();}}
        public ResearchPresetPicker(ResearchConditionSet set){
            existing=new HashSet<string>(set.Conditions.Select(ResearchPresets.ExistingKey).Where(k=>k!=null),StringComparer.Ordinal);
            ResearchUi.Form(this,"デフォルト条件から選択",1040,690);Name="researchPresetPicker";
            var root=ResearchUi.Rows(56,38,38,-1,36,42);Controls.Add(root);
            root.Controls.Add(new Label{Text="Syntax Labと同じ名称・順序です。選んだ条件をボードに応じて自動生成します。\n現在のボードで該当しない条件も選べます。選択済みの条件は重複追加しません。",Dock=DockStyle.Fill},0,0);
            var fields=ResearchUi.Fields(1);search.Name="researchPresetSearch";ResearchUi.Field(fields,0,"条件名で検索",search);root.Controls.Add(fields,0,1);
            var tools=ResearchUi.Bar();splitPocket.Name="researchSplitPocket";tools.Controls.Add(splitPocket);tools.Controls.Add(ResearchUi.Button("表示中の選択可能条件を選択",()=>SelectVisible(true)));tools.Controls.Add(ResearchUi.Button("表示中の選択を解除",()=>SelectVisible(false)));root.Controls.Add(tools,0,2);
            grid.Name="researchPresetGrid";grid.EditMode=DataGridViewEditMode.EditOnEnter;grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="selected",HeaderText="選択",FillWeight=9});
            grid.Columns.Add(new DataGridViewTextBoxColumn{Name="label",HeaderText="デフォルト条件名",FillWeight=32,ReadOnly=true});
            grid.Columns.Add(new DataGridViewTextBoxColumn{Name="state",HeaderText="状態",FillWeight=13,ReadOnly=true});
            grid.Columns.Add(new DataGridViewTextBoxColumn{Name="reason",HeaderText="説明",FillWeight=65,ReadOnly=true});
            foreach(DataGridViewColumn column in grid.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;
            grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            grid.CellValueChanged+=(s,e)=>{if(loading||e.RowIndex<0||e.ColumnIndex!=0)return;var row=grid.Rows[e.RowIndex];var p=(ResearchDefaultPreset)row.Tag;if(!CanChoose(p))return;if(Convert.ToBoolean(row.Cells[0].Value))chosen.Add(p.Id);else chosen.Remove(p.Id);RefreshCount();};
            root.Controls.Add(grid,0,3);root.Controls.Add(count,0,4);
            var bottom=ResearchUi.Bar();add=ResearchUi.Button("選択した条件を追加",()=>{if(chosen.Count==0)return;DialogResult=DialogResult.OK;Close();});add.Name="researchPresetAdd";bottom.Controls.Add(add);bottom.Controls.Add(ResearchUi.Button("この条件に掛け合わせる",()=>{if(grid.CurrentRow==null)return;var p=(ResearchDefaultPreset)grid.CurrentRow.Tag;if(!p.Supported)throw new ArgumentException(p.Reason);ComposePresetId=p.Id;DialogResult=DialogResult.OK;Close();}));
            var cancel=ResearchUi.Button("キャンセル",()=>{DialogResult=DialogResult.Cancel;Close();});bottom.Controls.Add(cancel);CancelButton=cancel;root.Controls.Add(bottom,0,5);
            search.TextChanged+=(s,e)=>Reload();Reload();
        }
        bool CanChoose(ResearchDefaultPreset p){return p.Supported&&!existing.Contains(p.Id+"@"+p.Revision);}
        static string SearchKey(string value){return string.Concat(value.Where(c=>!char.IsWhiteSpace(c))).ToLowerInvariant();}
        void Reload(){
            loading=true;try{grid.Rows.Clear();string query=SearchKey(search.Text);
                foreach(var p in presets.Where(p=>SearchKey(p.Label+" "+p.Id).Contains(query))){
                    bool present=existing.Contains(p.Id+"@"+p.Revision);int i=grid.Rows.Add(present||chosen.Contains(p.Id),p.Label,!p.Supported?"未対応":present?"選択済み":"選択可能",!p.Supported?p.Reason:present?"条件セットに登録されています。":"対象ボードごとに生成");
                    var row=grid.Rows[i];row.Tag=p;row.Cells[0].ReadOnly=!CanChoose(p);row.Cells[3].ToolTipText=Convert.ToString(row.Cells[3].Value);
                    if(!CanChoose(p)){row.DefaultCellStyle.ForeColor=Color.DimGray;row.DefaultCellStyle.BackColor=Color.FromArgb(245,245,245);}
                }
            }finally{loading=false;}RefreshCount();
        }
        void SelectVisible(bool value){foreach(DataGridViewRow row in grid.Rows){var p=(ResearchDefaultPreset)row.Tag;if(CanChoose(p))row.Cells[0].Value=value;}RefreshCount();}
        void RefreshCount(){count.Text="新規選択 "+chosen.Count+"件 ／ 対応 "+presets.Count(p=>p.Supported)+"件・未対応 "+presets.Count(p=>!p.Supported)+"件 ／ 表示 "+grid.Rows.Count+"件";add.Enabled=chosen.Count>0;}
    }
}
