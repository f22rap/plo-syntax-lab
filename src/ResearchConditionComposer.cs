using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlopCommands.Research;
namespace FlopCommands {
 public sealed class ResearchConditionComposer : Form {
  sealed class Item {public string Key,Label;public override string ToString(){return Label;}}
  readonly List<CheckedListBox> axes=new List<CheckedListBox>();
  readonly TextBox summary=ResearchUi.TextBox(true),board=ResearchUi.TextBox();
  readonly CheckBox separate=new CheckBox{Text="組み合わせ別に登録（未選択なら各軸の選択肢をORで統合）",AutoSize=true};
  readonly Button add,preview;int previewVersion;
  readonly ResearchDefaultPreset[] presets=ResearchPresets.All();
  public List<ResearchCondition> Conditions {get;private set;}
  public ResearchConditionComposer(string initial=null){
   ResearchUi.Form(this,"条件を掛け合わせる",1100,720);Name="researchConditionComposer";
   var root=ResearchUi.Rows(46,-1,38,44,160,44);Controls.Add(root);
   root.Controls.Add(new Label{Text="項目間はAND（×）、同じ項目内の複数選択はOR。追加項目が未選択なら指定なしです。\nBDFDの構成枚数と本数は別項目です。基本役を少なくとも1つ選択してください。",Dock=DockStyle.Fill},0,0);
   var scroll=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,WrapContents=true};root.Controls.Add(scroll,0,1);
   var basic=presets.Where(p=>p.Supported&&(p.Id==initial||!p.Id.StartsWith("axis.")&&p.Label.EndsWith(" all")&&p.Label.Count(c=>c==' ')==1)).ToArray();
   Axis(scroll,"基本役",basic.Select(p=>new Item{Key=p.Id+"@"+p.Revision,Label=p.Label}).ToArray());
   AddAxis(scroll,"FD","fd",new[]{"all:あり","none:なし","nut:ナッツ","second:セカンド","third:サード","other:それ以下"});
   AddAxis(scroll,"pp","pp",new[]{"none:なし","any:あり"}.Concat("23456789TJQKA".Select(c=>char.ToLowerInvariant(c)+":"+c+c)).ToArray());
   AddAxis(scroll,"BDFD構成枚数","bdfd",new[]{"pair:同スートちょうど2枚","triple:同スート3枚以上"});
   AddAxis(scroll,"BDFD強さ","bdfd",new[]{"nut:ナッツ","second:セカンド","low:それ以下"});
   AddAxis(scroll,"BDFD本数","bdfd",new[]{"none:なし","one:1本","two:2本"});
   AddAxis(scroll,"キッカー","kicker",new[]{"top:トップ","second:セカンド","other:それ以下"});
   AddAxis(scroll,"SD・BDSD","sd",new[]{"all:通常SDあり","none:通常SDなし","wrap:ラップ","nut_open:ナッツOESD","nut_gut:ナッツガット","open:非ナッツOESD","gut:非ナッツガット","bdsd:BDSD","bdsd4:BDSD4","bdsd8:BDSD8","bdsd9:BDSD9+","bdsdother:BDSDその他"});
   AddAxis(scroll,"SDブロッカー","sd",new[]{"blocker:あり","nutblocker:ナッツ","no_blocker:なし（自身SD除外）"});
   AddAxis(scroll,"FDブロッカー","fd",new[]{"blocker:あり","blockernut:ナッツ","blockersecond:セカンド","blockerthird:サード","blockerlow:それ以下","blockernone:なし（自身FD除外）"});
   var mode=ResearchUi.Bar();mode.Controls.Add(separate);var advanced=new CheckBox{Text="＋追加条件を表示",AutoSize=true};mode.Controls.Add(advanced);root.Controls.Add(mode,0,2);foreach(var a in axes.Skip(4))a.Parent.Visible=false;advanced.CheckedChanged+=(s,e)=>{foreach(var a in axes.Skip(4))a.Parent.Visible=advanced.Checked;};separate.CheckedChanged+=(s,e)=>RefreshPreview();
   var tools=ResearchUi.Bar();tools.Controls.Add(new Label{Text="確認用ボード",AutoSize=true});board.Width=150;board.Dock=DockStyle.None;board.Text="As7s2d";tools.Controls.Add(board);
   preview=ResearchUi.Button("このボードでハンド数・syntaxを確認",()=>{});preview.Click+=async(s,e)=>await Preview();tools.Controls.Add(preview);root.Controls.Add(tools,0,3);
   summary.ReadOnly=true;summary.ScrollBars=ScrollBars.Both;summary.WordWrap=false;root.Controls.Add(summary,0,4);
   var bar=ResearchUi.Bar();add=ResearchUi.Button("条件セットへ追加",()=>{Conditions=Build();DialogResult=DialogResult.OK;Close();});bar.Controls.Add(add);bar.Controls.Add(ResearchUi.Button("キャンセル",()=>Close()));root.Controls.Add(bar,0,5);
   if(initial!=null)for(int i=0;i<axes[0].Items.Count;i++)if(((Item)axes[0].Items[i]).Key.StartsWith(initial+"@"))axes[0].SetItemChecked(i,true);
   if(axes[0].CheckedItems.Count==0){int at=Array.FindIndex(basic,p=>p.Label=="Thit all");if(at>=0)axes[0].SetItemChecked(at,true);}
   if(initial!=null){var original=presets.FirstOrDefault(p=>p.Id==initial);if(original!=null){int space=original.Label.IndexOf(' ');var basePreset=presets.FirstOrDefault(p=>p.Label==original.Label.Substring(0,space)+" all");var atom=presets.FirstOrDefault(p=>p.Id.StartsWith("axis.")&&p.Label=="Any "+original.Label.Substring(space+1));if(basePreset!=null&&atom!=null){for(int i=0;i<axes[0].Items.Count;i++)axes[0].SetItemChecked(i,((Item)axes[0].Items[i]).Key==basePreset.Id+"@"+basePreset.Revision);foreach(var axis in axes.Skip(1))for(int i=0;i<axis.Items.Count;i++)if(((Item)axis.Items[i]).Key==atom.Id+"@"+atom.Revision){axis.SetItemChecked(i,true);if(axes.IndexOf(axis)>=4)advanced.Checked=true;}}}}
   RefreshPreview();
  }
  void AddAxis(FlowLayoutPanel panel,string title,string group,string[] options){Axis(panel,title,options.Select(o=>{var a=o.Split(':');var p=presets.Single(x=>x.Id=="axis."+group+"."+a[0]);return new Item{Key=p.Id+"@"+p.Revision,Label=title+" "+a[1]};}).ToArray());}
  void Axis(FlowLayoutPanel panel,string title,Item[] options){var box=new GroupBox{Text=title,Width=200,Height=150};var list=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true,Name="axis"+axes.Count,Tag=title};list.Items.AddRange(options);axes.Add(list);box.Controls.Add(list);panel.Controls.Add(box);list.ItemCheck+=(s,e)=>{if(IsHandleCreated)BeginInvoke((Action)RefreshPreview);};}
  public List<ResearchCondition> Build(){
   if(axes[0].CheckedItems.Count==0)throw new ArgumentException("基本役を選んでください。");
   var selected=axes.Where(a=>a.CheckedItems.Count>0).Select(a=>a.CheckedItems.Cast<Item>().ToArray()).ToArray();
   var combinations=new List<Item[][]>{new Item[0][]};
   foreach(var group in selected){if(separate.Checked){if((long)combinations.Count*group.Length>1000)throw new ArgumentException("生成条件が1,000件を超えます。選択肢を減らしてください。");combinations=combinations.SelectMany(c=>group.Select(i=>c.Concat(new[]{new[]{i}}).ToArray())).ToList();}else combinations=combinations.Select(c=>c.Concat(new[]{group}).ToArray()).ToList();}
   return combinations.Select(groups=>new ResearchCondition{Label=string.Join(" × ",groups.Select(g=>"("+string.Join(" または ",g.Select(i=>i.Label))+ ")")),Kind="generated",Syntax="",Generator=new ResearchGenerator{Source="combination",Groups=groups.Select(g=>g.Select(i=>i.Key).ToArray()).ToArray()}}).ToList();
  }
  void RefreshPreview(){previewVersion++;if(add==null)return;try{var rows=Build();summary.Text="登録予定："+rows.Count+"条件\r\n"+string.Join("\r\n",rows.Select(c=>c.Label));add.Enabled=true;}catch(Exception e){summary.Text=e.Message;add.Enabled=false;}}
  async Task Preview(){
   preview.Enabled=false;int version=previewVersion;try{
    var set=new ResearchConditionSet{Name="掛け合わせプレビュー",Conditions=Build()};string b=ResearchValidation.Board(board.Text);
    string home=AppDomain.CurrentDomain.BaseDirectory,script=null;for(int n=0;n<3&&home!=null;n++,home=Path.GetDirectoryName(home.TrimEnd(Path.DirectorySeparatorChar))){var candidate=Path.Combine(home,"mcp","preview-combination.cjs");if(File.Exists(candidate)){script=candidate;break;}}
    if(script==null)throw new IOException("プレビュー用ファイルが見つかりません。アプリ一式を更新してください。");
    string input=Path.GetTempFileName();try{File.WriteAllBytes(input,ResearchJson.Encode(set));summary.Text="ボード "+b+" を解析中…";string output=await Task.Run(()=>{
     var start=new ProcessStartInfo("node","\""+script+"\" \""+input+"\" "+b){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
     using(var process=Process.Start(start)){var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(180000)){process.Kill();throw new TimeoutException("プレビューが時間内に完了しませんでした。");}Task.WaitAll(stdout,stderr);if(process.ExitCode!=0)throw new Exception(stderr.Result);return stdout.Result;}
    });if(!IsDisposed&&version==previewVersion)summary.Text=output;}finally{File.Delete(input);}
   }catch(Exception ex){if(!IsDisposed)summary.Text="プレビュー失敗："+ex.Message+"\r\n条件の保存は可能です。";}finally{if(!IsDisposed)preview.Enabled=true;}
  }
 }
}
