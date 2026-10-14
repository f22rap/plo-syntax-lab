using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FlopCommands.Research;

namespace FlopCommands {
    public sealed partial class ResearchPanel : UserControl {
        readonly ResearchStore store;
        ResearchWorkspace workspace;
        readonly ComboBox sets=new ComboBox{Width=290,DropDownStyle=ComboBoxStyle.DropDownList,Name="researchConditionSets"};
        readonly ComboBox catalogs=new ComboBox{Width=290,DropDownStyle=ComboBoxStyle.DropDownList,Name="researchCatalogs"};
        readonly Label status=new Label{Dock=DockStyle.Fill,AutoEllipsis=true,Name="researchStatus"};
        readonly FlowLayoutPanel setBar=ResearchUi.Bar(),catalogBar=ResearchUi.Bar();
        readonly Action<ResearchConditionSet> apply;
        readonly Func<ResearchConditionSet> capture;
        public int SetCount {get{return workspace==null?0:workspace.Sets.Count;}}
        public int CatalogCount {get{return workspace==null?0:workspace.Catalogs.Count;}}
        public ResearchPanel(ResearchStore store,Action<ResearchConditionSet> apply,Func<ResearchConditionSet> capture){
            this.store=store;this.apply=apply;this.capture=capture;Name="researchPanel";Dock=DockStyle.Fill;Margin=Padding.Empty;
            var root=ResearchUi.Rows(36,36,76,48,0,0);root.Padding=new Padding(24,0,24,0);Controls.Add(root);
            setBar.Controls.Add(new Label{Text="調査条件セット",Width=116,Height=30,TextAlign=ContentAlignment.MiddleLeft});setBar.Controls.Add(sets);
            setBar.Controls.Add(ResearchUi.Button("新規",()=>EditSet(new ResearchConditionSet(),false)));
            setBar.Controls.Add(ResearchUi.Button("編集",()=>{var s=SelectedSet();if(s!=null)EditSet(s,false);}));
            setBar.Controls.Add(ResearchUi.Button("複製",()=>{var s=SelectedSet();if(s!=null)EditSet(s,true);}));
            var use=ResearchUi.Button("現在ボードに適用",()=>{var s=SelectedSet();if(s==null)throw new ArgumentException("条件セットを選択してください。");apply(ResearchJson.Clone(s));status.Text="条件セットを適用しました。ボードを変更した場合は再適用してください。";});use.Name="applyResearchConditions";setBar.Controls.Add(use);
            setBar.Controls.Add(ResearchUi.Button("今の選択条件を保存",()=>EditSet(capture(),false)));root.Controls.Add(setBar,0,0);
            catalogBar.Controls.Add(new Label{Text="ボードCSV登録",Width=116,Height=30,TextAlign=ContentAlignment.MiddleLeft});catalogBar.Controls.Add(catalogs);
            catalogBar.Controls.Add(ResearchUi.Button("新規",()=>EditCatalog(new ResearchCatalog(),false)));
            catalogBar.Controls.Add(ResearchUi.Button("編集",()=>{var c=SelectedCatalog();if(c!=null)EditCatalog(c,false);}));
            catalogBar.Controls.Add(ResearchUi.Button("複製",()=>{var c=SelectedCatalog();if(c!=null)EditCatalog(c,true);}));
            catalogBar.Controls.Add(ResearchUi.Button("設定JSON読込",Import));catalogBar.Controls.Add(ResearchUi.Button("設定JSON保存",Export));root.Controls.Add(catalogBar,0,1);
            var reload=ResearchUi.Button("再読込",()=>Reload());catalogBar.Controls.Add(reload);root.Controls.Add(status,0,3);InstallRunControls(root);Reload();
        }
        ResearchConditionSet SelectedSet(){return sets.SelectedItem as ResearchConditionSet;}
        ResearchCatalog SelectedCatalog(){return catalogs.SelectedItem as ResearchCatalog;}
        public void Reload(){try{workspace=store.Load();Bind(null,null);setBar.Enabled=true;status.Text="「新規」→「デフォルト条件から追加」で複数選択して保存できます。手入力syntaxとボードCSVも登録できます。";}catch(Exception ex){workspace=null;sets.Items.Clear();catalogs.Items.Clear();setBar.Enabled=false;status.Text="読込失敗（元データは保持）："+ex.Message;}RefreshRunControls();}
        void RequireLoaded(){if(workspace==null)throw new InvalidOperationException("保存済み設定を読み込めません。元ファイルを確認して再読込してください。");}
        void Bind(string setId,string catalogId){sets.Items.Clear();catalogs.Items.Clear();foreach(var s in workspace.Sets)sets.Items.Add(s);foreach(var c in workspace.Catalogs)catalogs.Items.Add(c);sets.SelectedItem=workspace.Sets.FirstOrDefault(s=>s.Id==setId);catalogs.SelectedItem=workspace.Catalogs.FirstOrDefault(c=>c.Id==catalogId);if(sets.SelectedIndex<0&&sets.Items.Count>0)sets.SelectedIndex=0;if(catalogs.SelectedIndex<0&&catalogs.Items.Count>0)catalogs.SelectedIndex=0;}
        public void SaveSet(ResearchConditionSet value){RequireLoaded();ResearchValidation.Set(value);var next=ResearchJson.Clone(workspace);next.Sets.RemoveAll(s=>s.Id==value.Id);next.Sets.Add(ResearchJson.Clone(value));workspace=store.Save(next,workspace.Revision);Bind(value.Id,SelectedCatalog()==null?null:SelectedCatalog().Id);status.Text=value.Conditions.Any(c=>c.Kind=="generated")?"自動生成条件の選択を保存しました。ボード別syntaxは保存済み条件セットの生成プレビューで確認できます。":"条件セットを保存しました。編集内容を使うには「現在ボードに適用」を押してください。";}
        public void SaveCatalog(ResearchCatalog value){RequireLoaded();ResearchValidation.Catalog(value);var next=ResearchJson.Clone(workspace);next.Catalogs.RemoveAll(c=>c.Id==value.Id);next.Catalogs.Add(ResearchJson.Clone(value));workspace=store.Save(next,workspace.Revision);Bind(SelectedSet()==null?null:SelectedSet().Id,value.Id);status.Text="カタログを保存しました："+value.Spots.Count+"ボード。登録元CSVは変更していません。";}
        void EditSet(ResearchConditionSet value,bool copy){RequireLoaded();var draft=ResearchJson.Clone(value);if(copy){draft.Id=Guid.NewGuid().ToString("N");draft.Revision=0;draft.Name+=" コピー";}using(var dialog=new ResearchConditionEditor(draft))if(dialog.ShowDialog(this)==DialogResult.OK)SaveSet(dialog.Value);}
        void EditCatalog(ResearchCatalog value,bool copy){RequireLoaded();var draft=ResearchJson.Clone(value);if(copy){draft.Id=Guid.NewGuid().ToString("N");draft.Revision=0;draft.Name+=" コピー";}using(var dialog=new ResearchCatalogEditor(draft))if(dialog.ShowDialog(this)==DialogResult.OK)SaveCatalog(dialog.Value);}
        void Import(){RequireLoaded();using(var d=new OpenFileDialog{Filter="調査設定JSON|*.json",Title="条件セットとCSV登録情報をコピーとして読込"})if(d.ShowDialog(this)==DialogResult.OK){var next=ResearchStore.ImportAsCopies(workspace,d.FileName);workspace=store.Save(next,workspace.Revision);Bind(null,null);status.Text="コピーとして読み込みました。CSV本体は含みません。別PCではパスを編集してCSVを再検証してください。";}}
        void Export(){RequireLoaded();using(var d=new SaveFileDialog{Filter="調査設定JSON|*.json",FileName="plo-research-settings.json",OverwritePrompt=true,Title="条件セットとCSVのフルパス・登録情報を保存（CSV本体は含みません）"})if(d.ShowDialog(this)==DialogResult.OK){store.Export(d.FileName,workspace);status.Text="調査設定を保存しました："+d.FileName;}}
    }
}
