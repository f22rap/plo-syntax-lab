using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FlopCommands.Research;

namespace EquityTests {
    static class GeneratedConditionTests {
        static int checks;
        static void Check(bool ok,string reason){checks++;if(!ok)throw new Exception("Generated conditions: "+reason);}
        static void Reject(Action action,string reason){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Accepted invalid definition: "+reason);}
        static ResearchConditionSet Selected(){var set=new ResearchConditionSet{Name="保存したデフォルト選択"};ResearchPresets.Add(set,new[]{"aa.sd.bdsd","set.top.all","set.middle.all"});return set;}
        static byte[] Text(string text){return Encoding.UTF8.GetBytes(text);}
        static ResearchConditionSet Examples(){using(var stream=typeof(GeneratedConditionTests).Assembly.GetManifestResourceStream("GeneratedExamples.json"))using(var bytes=new MemoryStream()){stream.CopyTo(bytes);return new ResearchConditionSet{Name="構造化条件の往復",Conditions=ResearchJson.Decode<List<ResearchCondition>>(bytes.ToArray())};}}
        static void Definitions(){
            var draft=new ResearchConditionSet();draft.Conditions.Add(new ResearchCondition());
            Check(ResearchPresets.Add(draft,new[]{"pair.top.pocket.under"})==1,"empty editor draft does not block preset addition");
            var pp=draft.Conditions.Last();pp.Generator.AdditionalSyntax="ss";string ppId=pp.Id;
            Check(ResearchPresets.SplitPocketRanks(draft,ppId)==13,"pp expands to 13 ranks");
            Check(draft.Conditions.Skip(1).Select(c=>c.Id).Distinct().Count()==13,"pp identities unique");
            Check(draft.Conditions[1].Generator.AdditionalSyntax=="(ss):22"&&draft.Conditions.Last().Generator.AdditionalSyntax=="(ss):AA","pp preserves additional syntax");
            Check(draft.Conditions.Skip(1).All(c=>c.Generator.PresetId=="pair.top.pocket.under"),"pp preserves board-relative recipe");
            var all=ResearchPresets.All();Check(all.Length==185&&all.Count(p=>p.Supported)==159,"185 rows, 159 supported");Check(all[0].Id=="set.top.all"&&all[0].Label=="Tset all"&&all[0].Cell=="L49","canonical first row");
            all[0].Label="変更";Check(ResearchPresets.All()[0].Label=="Tset all","catalog is immutable to callers");
            var set=Selected();Check(set.Conditions.Select(c=>c.Generator.PresetId).SequenceEqual(new[]{"set.top.all","set.middle.all","aa.sd.bdsd"}),"selection follows catalog order");
            var ids=set.Conditions.Select(c=>c.Id).ToArray();set.Conditions[0].Label="自分のトップセット名";set.Conditions[0].Enabled=false;
            Check(ResearchPresets.Add(set,new[]{"set.top.all","set.top.all"})==0,"duplicate identity ignored even after rename/disable");Check(set.Conditions.Select(c=>c.Id).SequenceEqual(ids),"stable identities preserved");
            byte[] before=ResearchJson.Encode(set);Reject(()=>ResearchPresets.Add(set,new[]{"set.bottom.all",all.First(p=>!p.Supported).Id}),"unsupported selection atomic");Check(before.SequenceEqual(ResearchJson.Encode(set)),"failed multi-add leaves entire set unchanged");
            Reject(()=>ResearchPresets.Add(set,new[]{"unknown.id"}),"unknown identity");
            var extra=ResearchJson.Clone(set);extra.Conditions[0].Generator.AdditionalSyntax="AA";Check(ResearchPresets.Add(extra,new[]{"set.top.all"})==1,"fixed additional syntax is a distinct recipe");
            var examples=Examples();ResearchValidation.Set(examples);var cloned=ResearchJson.Clone(examples);Check(ResearchJson.Encode(examples).SequenceEqual(ResearchJson.Encode(cloned)),"12 filter/preset/legacy definitions roundtrip");Check(cloned.Conditions.Single(c=>c.Id=="third_fd").Generator.Filter.Fd=="fdThird","third FD distinct from triple-suit FD");
            var unknown=ResearchJson.Clone(set);unknown.Conditions[0].Generator.PresetRevision=2;ResearchValidation.Set(unknown);Check(ResearchJson.Clone(unknown).Conditions[0].Generator.PresetRevision==2,"future revision retained, never substituted");
            var invalid=ResearchJson.Clone(examples);invalid.Conditions[0].Generator.Filter.Fd="made_up";Reject(()=>ResearchValidation.Set(invalid),"unknown enum");
            invalid=ResearchJson.Clone(examples);invalid.Conditions[0].Generator.Filter.Clean="all";Reject(()=>ResearchValidation.Set(invalid),"incompatible clean and FD");
            invalid=ResearchJson.Clone(examples);invalid.Conditions.Single(c=>c.Id=="bdsd8").Generator.Filter.Sd="sd";Reject(()=>ResearchValidation.Set(invalid),"BDSD plus ordinary SD");
            invalid=ResearchJson.Clone(set);invalid.Conditions[0].Syntax="AA";Reject(()=>ResearchValidation.Set(invalid),"generated syntax cannot be persisted");
            invalid=ResearchJson.Clone(set);invalid.Conditions[0].Generator.AdditionalSyntax="";Reject(()=>ResearchValidation.Set(invalid),"blank additional syntax");
            invalid=ResearchJson.Clone(set);invalid.Conditions[0].Kind="literal";invalid.Conditions[0].Syntax="AA";Reject(()=>ResearchValidation.Set(invalid),"ambiguous literal with generator");
            string raw=Encoding.UTF8.GetString(ResearchJson.Encode(examples));
            foreach(string bad in new[]{raw.Replace("\"fd\":","\"typo\":true,\"fd\":"),raw.Replace("\"recipe_version\":1","\"recipe_version\":1,\"future_rule\":true"),raw.Replace("\"fd\":\"fdThird\"","\"fd\":\"fdThird\",\"fd\":\"fdLow\""),raw.Replace("\"position\":\"top\"","\"position\":\"top\",\"rank\":\"A\""),raw.Replace("\"enabled\":true,","")})Reject(()=>ResearchJson.Decode<ResearchConditionSet>(Text(bad)),"unknown, duplicate or missing member");
            Reject(()=>ResearchValidation.Resolve(Selected(),"AsKd7s"),"unexpanded generated condition cannot enter legacy matcher");
            var guard=new ResearchConditionSet{Name="disabled",Conditions=new List<ResearchCondition>{new ResearchCondition{Syntax="AA"},set.Conditions[0]}};Check(ResearchValidation.Resolve(guard,"AsKd7s").Length==1,"disabled generated condition leaves legacy conditions usable");
        }
        static void Persistence(string root){
            var store=new ResearchStore(Path.Combine(root,"main"));var v1=new ResearchWorkspace{Sets=new List<ResearchConditionSet>{new ResearchConditionSet{Name="旧条件",Conditions=new List<ResearchCondition>{new ResearchCondition{Syntax=" AA "}}}}};
            v1=store.Save(v1,0);byte[] oldBytes=File.ReadAllBytes(store.FilePath);Check(v1.SchemaVersion==1,"legacy-only save remains schema 1");
            var stale=ResearchJson.Clone(v1);var set=Selected();v1.Sets.Add(set);var v2=store.Save(v1,v1.Revision);string backup=store.FilePath+".schema-v1.r1.bak";
            Check(v2.SchemaVersion==2&&oldBytes.SequenceEqual(File.ReadAllBytes(backup)),"schema 2 migration keeps exact old bytes");Check(v1.SchemaVersion==1&&v1.Revision==1,"save does not mutate caller");
            var reopened=new ResearchStore(Path.GetDirectoryName(store.FilePath)).Load();Check(reopened.Sets[1].Conditions.Select(c=>c.Id).SequenceEqual(set.Conditions.Select(c=>c.Id)),"reopen retains condition IDs");Check(reopened.Sets[0].Conditions[0].Syntax==" AA ","legacy syntax bytes retained");
            reopened.Sets[1].Conditions[0].Label="再起動後の条件名";reopened.Sets[1].Conditions[1].Enabled=false;reopened=store.Save(reopened,reopened.Revision);Check(reopened.Sets[0].Revision==1&&reopened.Sets[1].Revision==2,"only edited set advances version");
            Check(Directory.GetFiles(Path.GetDirectoryName(store.FilePath),"*.bak").Length==1,"backup not recreated on subsequent saves");
            byte[] saved=File.ReadAllBytes(store.FilePath);bool conflict=false;try{store.Save(stale,1);}catch(InvalidOperationException){conflict=true;}Check(conflict&&saved.SequenceEqual(File.ReadAllBytes(store.FilePath)),"stale writer cannot overwrite migrated workspace");
            string export=Path.Combine(root,"export.json");store.Export(export,reopened);var imported=ResearchStore.ImportAsCopies(new ResearchWorkspace(),export);Check(imported.SchemaVersion==2&&imported.Sets.Count==2&&imported.Sets[1].Id!=reopened.Sets[1].Id,"schema 2 import copies set identity");Check(imported.Sets[1].Conditions[0].Generator.PresetId=="set.top.all","import retains semantic preset");
            reopened.Sets.RemoveAll(s=>s.Conditions.Any(c=>c.Kind=="generated"));reopened.SchemaVersion=1;reopened=store.Save(reopened,reopened.Revision);Check(reopened.SchemaVersion==2,"schema cannot downgrade after migration");
            var malformed=new ResearchWorkspace{Sets=new List<ResearchConditionSet>{Selected()}};Reject(()=>ResearchValidation.Workspace(malformed),"schema 1 file may not contain generated data");
            string unknown=Encoding.UTF8.GetString(File.ReadAllBytes(store.FilePath)).Replace("\"schema_version\":2","\"schema_version\":2,\"future_workspace\":true");File.WriteAllText(store.FilePath,unknown);Reject(()=>store.Load(),"unknown schema 2 member");Check(File.ReadAllText(store.FilePath)==unknown,"failed load preserves original");
            var broken=new ResearchStore(Path.Combine(root,"backup-failure"));var old=broken.Save(new ResearchWorkspace(),0);byte[] oldContent=File.ReadAllBytes(broken.FilePath);Directory.CreateDirectory(broken.FilePath+".schema-v1.r1.bak");old.Sets.Add(Selected());bool failed=false;try{broken.Save(old,1);}catch(IOException){failed=true;}Check(failed&&oldContent.SequenceEqual(File.ReadAllBytes(broken.FilePath)),"failed backup prevents migration");
            var fixture=new ResearchWorkspace{SchemaVersion=2,Sets=new List<ResearchConditionSet>{Selected(),Examples()}};fixture.Sets[0].Conditions[0].Label="改名したトップセット";fixture.Sets[0].Conditions[1].Enabled=false;
            string withUnknown=Encoding.UTF8.GetString(ResearchJson.Encode(fixture)).Replace("\"conditions\":","\"future_set\":true,\"conditions\":");Reject(()=>ResearchJson.Decode<ResearchWorkspace>(Text(withUnknown)),"unknown nested workspace metadata retained by refusing load");
            string target=Environment.GetEnvironmentVariable("PLO_GENERATED_TEST_OUTPUT");if(!string.IsNullOrEmpty(target)){Directory.CreateDirectory(target);File.WriteAllBytes(Path.Combine(target,"csharp-workspace.json"),ResearchJson.Encode(fixture));}
        }
        public static void Run(){Definitions();string root=Path.Combine(Path.GetTempPath(),"plo-generated-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{Persistence(root);}finally{Directory.Delete(root,true);}Console.WriteLine("PASS: "+checks+" generated-selection and migration checks");}
    }
}
