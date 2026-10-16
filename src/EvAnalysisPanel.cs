using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FlopCommands.Equity;
namespace FlopCommands {
 public sealed class EvAnalysisPanel : UserControl {
  readonly CheckBox confirm=new CheckBox{Text="EV集計：全アクション・共通weight／ev_diff＝自分のEV−他の最善EV（bb）",Dock=DockStyle.Top,Height=28};
  readonly ComboBox fold=new ComboBox{Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList};
  readonly Label status=new Label{Text="各アクションを常に選ぶ場合の損失。結果は保存されません。Node.jsが必要です。",Dock=DockStyle.Top,Height=36};
  readonly DataGridView grid=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.DisplayedCells};
  public EvAnalysisPanel(){Dock=DockStyle.Fill;Controls.Add(grid);Controls.Add(status);Controls.Add(fold);Controls.Add(confirm);fold.Items.AddRange(new object[]{"通常のEV比較","続行2択の一本化：CSV1がフォールド（EV=0）","続行2択の一本化：CSV2がフォールド（EV=0）","続行2択の一本化：CSV3がフォールド（EV=0）"});fold.SelectedIndex=0;fold.SelectedIndexChanged+=(s,e)=>Clear();foreach(var name in new[]{"条件","アクション","頻度 %","損失 bb（計算可能範囲）","対象率 %","損失 bb（全アクション共通）","共通対象率 %","続行一本化損失 bb","続行一本化損失の下限 bb","続行損失の確定率 %"})grid.Columns.Add(name,name);grid.DefaultCellStyle.NullValue="—";confirm.CheckedChanged+=(s,e)=>Clear();}
  public void Clear(){grid.Rows.Clear();status.Text="EVは未計算。全アクション共通の対象で損失を比較します。";}
  static object Value(Dictionary<string,object> d,string k){return d.ContainsKey(k)?d[k]:null;}
  static object Display(object v){return v==null?null:(object)Convert.ToDouble(v).ToString("0.#####");}
  public async Task RunAsync(EquityJobRequest request,CancellationToken token){
   if(!confirm.Checked){status.Text="EV集計を行うには定義・全アクション・共通weightの確認欄にチェックしてください。";return;}
   if(request==null){status.Text="EV集計：ボード・条件・CSVを確認してください。";return;}
   if(fold.SelectedIndex>0&&request.ActionPaths.Length!=3){status.Text="続行2択の比較はフォールドを含む3つのCSVを指定してください。";return;}
   int foldIndex=fold.SelectedIndex-1;fold.Enabled=false;confirm.Enabled=false;grid.Rows.Clear();status.Text="条件別EVを計算中…";
   try{
    var json=new JavaScriptSerializer{MaxJsonLength=32*1024*1024};
    var input=new {continuation_comparison=foldIndex<0?null:(object)new{fold_action_id="action"+foldIndex,fold_ev_bb=0,action_ids=Enumerable.Range(0,3).Where(i=>i!=foldIndex).Select(i=>"action"+i).ToArray()},board=request.Board,actions=request.ActionPaths.Select((p,i)=>new{action_id="action"+i,path=Path.GetFullPath(p)}).ToArray(),conditions=request.Conditions.Select((c,i)=>new{condition_id="condition"+i,label=c.Label,syntax=c.Syntax}).ToArray(),ev_definition="action_minus_best_other_bb",complete_action_menu=true,common_weight_scale=true};
    string payload=json.Serialize(input);
    string output=await Task.Run(()=>{
     string root=AppDomain.CurrentDomain.BaseDirectory,script=Path.Combine(root,"mcp","action-ev-cli.mjs");if(!File.Exists(script))script=Path.GetFullPath(Path.Combine(root,"..","mcp","action-ev-cli.mjs"));
     if(!File.Exists(script))throw new Exception("EV計算ファイルがありません。Windows MCPパッケージ全体を更新してください。");
     using(var p=new Process{StartInfo=new ProcessStartInfo("node","\""+script+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8}}){
      p.Start();using(token.Register(()=>{try{if(!p.HasExited)p.Kill();}catch{}})){
       var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();p.StandardInput.Write(payload);p.StandardInput.Close();
       if(!p.WaitForExit(300000)){try{p.Kill();}catch{}throw new Exception("EV計算がタイムアウトしました。");}
       token.ThrowIfCancellationRequested();string result=stdout.GetAwaiter().GetResult(),error=stderr.GetAwaiter().GetResult();if(p.ExitCode!=0)throw new Exception(error);return result;
      }
     }
    },token);
    token.ThrowIfCancellationRequested();var data=json.Deserialize<Dictionary<string,object>>(output);
    foreach(Dictionary<string,object> row in (IEnumerable)data["conditions"]){int i=0;foreach(Dictionary<string,object> action in (IEnumerable)row["actions"]){Dictionary<string,object> pairAction=null;var pair=Value(row,"continuation") as Dictionary<string,object>;if(pair!=null)foreach(Dictionary<string,object> pa in (IEnumerable)pair["actions"])if((string)pa["action_id"]==(string)action["action_id"])pairAction=pa;
     grid.Rows.Add(row["label"],request.ActionLabels[i++],Display(Value(action,"frequency_percent")),Display(Value(action,"mean_loss_bb_on_covered_range")),Display(Value(action,"coverage_percent")),Display(Value(action,"mean_loss_bb_on_common_range")),Display(Value(row,"common_coverage_percent")),pairAction==null?null:Display(Value(pairAction,"mean_loss_bb")),pairAction==null?null:Display(Value(pairAction,"mean_loss_lower_bound_bb")),pairAction==null?null:Display(Value(pairAction,"exact_coverage_percent")));}}
    status.Text="EV集計完了（bb）。頻度は全該当ハンド、共通損失は全アクションを評価できるハンド。矛盾は損失から除外。";
   }catch(OperationCanceledException){status.Text="EV計算を中止しました。";throw;}catch(Exception e){status.Text="EV集計できません："+e.Message;}finally{confirm.Enabled=true;fold.Enabled=true;}
  }
 }
}
