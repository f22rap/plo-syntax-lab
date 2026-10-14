using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FlopCommands.Research;
using PloSyntax;

namespace EquityTests {
    static class ResearchTests {
        static int checks;
        static void Check(bool value,string why){checks++;if(!value)throw new Exception("Research: "+why);}
        static void Reject(Action action,string why){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Research accepted: "+why);}
        static ResearchConditionSet Conditions(){return new ResearchConditionSet{Name="ユーザー条件",Conditions=new List<ResearchCondition>{
            new ResearchCondition{Label="AAを含む",Syntax=" AA "},
            new ResearchCondition{Label="A＋第2ランク",Kind="per_board",Syntax="",ByBoard=new Dictionary<string,string>{{"AsKd7c","AK"},{"Ah8h3c","A8"}}}
        }};}
        static void Definitions(){
            var set=Conditions();var a=ResearchValidation.Resolve(set,"7cAsKd");Check(a.Length==2&&a[0].Syntax==" AA "&&a[1].Syntax=="AK","user bytes preserved and flop order resolved");
            Check(ResearchValidation.Resolve(set,"Ah8h3c")[1].Syntax=="A8","per board distinct syntax");Check(ResearchValidation.Resolve(set,"AhKd7c")[1].Status=="missing_definition","suits not silently remapped");
            Check(new SyntaxMatcher(a[0].Syntax).Match("AcAd4c5d"),"resolved user syntax matches real hand");Check(!new SyntaxMatcher(a[0].Syntax).Match("AcQd4c5d"),"resolved user syntax excludes nonmatch");
            var copy=ResearchJson.Clone(set);copy.Conditions[0].Syntax="KK";Check(set.Conditions[0].Syntax==" AA ","independent snapshot");
            foreach(var c in copy.Conditions)c.Enabled=false;Check(ResearchValidation.Resolve(copy,"AsKd7c").Length==0,"disabled set does not become all hands");
            var empty=ResearchJson.Clone(set);empty.Conditions[0].Syntax="";Reject(()=>ResearchValidation.Set(empty),"empty syntax");
            empty.Conditions[0].Syntax="AA:()";Reject(()=>ResearchValidation.Set(empty),"empty group not rewritten");
            empty.Conditions[0].Syntax="System.IO.File.Delete(x)";Reject(()=>ResearchValidation.Set(empty),"executable text");
            var bad=ResearchJson.Clone(set);bad.Conditions[1].ByBoard.Add("7cKdAs","QQ");Reject(()=>ResearchValidation.Set(bad),"equivalent duplicate board");
            bad=ResearchJson.Clone(set);bad.Conditions[1].Kind="generated";Reject(()=>ResearchValidation.Set(bad),"unimplemented generated type");
            bad=ResearchJson.Clone(set);bad.Conditions[1].Syntax="*";Reject(()=>ResearchValidation.Set(bad),"ambiguous literal and board override");
            bad=ResearchJson.Clone(set);bad.Conditions[1].ByBoard.Clear();Reject(()=>ResearchValidation.Set(bad),"empty mapping");
            bad=ResearchJson.Clone(set);bad.Conditions[1].Id=bad.Conditions[0].Id;Reject(()=>ResearchValidation.Set(bad),"duplicate stable id");
            bad=ResearchJson.Clone(set);bad.Conditions[1].Label=bad.Conditions[0].Label;ResearchValidation.Set(bad);Check(bad.Conditions[0].Id!=bad.Conditions[1].Id,"same labels keep distinct ids");
            Reject(()=>ResearchValidation.Board("AsAs7c"),"repeated physical card");Reject(()=>ResearchValidation.Board("AsKd7c2h"),"turn not accepted as flop");
        }
        static ResearchSpot Draft(string dir){return new ResearchSpot{Board="7cAsKd",Node="BB first action",AllActionsCommonScale=true,OpponentCommonScale=true,
            Actions=new List<ResearchAction>{new ResearchAction{Label="Check",Kind="check",Source=new ResearchSource{Path=Path.Combine(dir,"check.csv")}},new ResearchAction{Label="Bet 33%",Kind="bet",PotFraction="0.33",Source=new ResearchSource{Path=Path.Combine(dir,"bet.csv")}}},
            Opponents=new List<ResearchSource>{new ResearchSource{Path=Path.Combine(dir,"相手 part1.csv")},new ResearchSource{Path=Path.Combine(dir,"相手 part2.csv")}}
        };}
        static ResearchCatalog Catalog(ResearchSpot spot){return new ResearchCatalog{Name="Ahiの調査",Scenario=new ResearchScenario{HeroPosition="BB",VillainPosition="BTN",StackBb="100",PotType="SRP",ActionHistory="BTN open, BB call",SolutionContext="synthetic test",EquityReference="双方のフロップアクション前"},Spots=new List<ResearchSpot>{spot}};}
        static void FilesAndPersistence(string dir){
            Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"check.csv"),"hand,frequency,percent,weight,ev,ev_diff,variations\nAcAd4c5d,1,100,0.25,0,0,1\n");File.WriteAllText(Path.Combine(dir,"bet.csv"),"hand,weight\nAcAd4c5d,0.75\n");
            File.WriteAllText(Path.Combine(dir,"相手 part1.csv"),"hand,frequency,weight\nKcKh6h9d,1,0.25\n");File.WriteAllText(Path.Combine(dir,"相手 part2.csv"),"hand,frequency,weight\n9d6hKhKc,1,0.75\nJdTh6c9s,1,0.5\n");
            var draft=Draft(dir);var spot=ResearchValidation.ValidateFiles(draft,CancellationToken.None);Check(draft.ValidatedUtc==""&&draft.Actions[0].Source.Sha256=="","validation atomic draft unchanged");Check(spot.HeroHands==1&&spot.OpponentHands==2,"legal merged physical counts");Check(spot.Opponents.All(s=>s.Sha256.Length==64)&&spot.Actions.All(s=>s.Source.Sha256.Length==64),"all source hashes captured");Check(spot.Opponents.All(s=>Path.IsPathRooted(s.Path)),"opponent full paths retained");
            var catalog=Catalog(spot);var second=ResearchJson.Clone(spot);second.Id=Guid.NewGuid().ToString("N");second.Board="Ah8h3c";second=ResearchValidation.ValidateFiles(second,CancellationToken.None);catalog.Spots.Add(second);ResearchValidation.Catalog(catalog);Check(ResearchValidation.CheckFiles(catalog,CancellationToken.None).Length==0,"two board catalog validates");
            var dup=ResearchJson.Clone(catalog);var twice=ResearchJson.Clone(spot);twice.Id=Guid.NewGuid().ToString("N");twice.Board="As7cKd";dup.Spots.Add(twice);Reject(()=>ResearchValidation.Catalog(dup),"same board node duplicate");
            dup=ResearchJson.Clone(catalog);dup.Scenario.StackBb="";Reject(()=>ResearchValidation.Catalog(dup),"missing scenario metadata");dup.Scenario.StackBb="100";dup.Scenario.Players=3;Reject(()=>ResearchValidation.Catalog(dup),"unimplemented multiway equity");
            var broken=Draft(dir);broken.Actions[1].Source.Path=broken.Actions[0].Source.Path;Reject(()=>ResearchValidation.ValidateFiles(broken,CancellationToken.None),"duplicate CSV");
            broken=Draft(dir);broken.AllActionsCommonScale=false;Reject(()=>ResearchValidation.ValidateFiles(broken,CancellationToken.None),"unconfirmed hero scale");
            broken=Draft(dir);broken.OpponentCommonScale=false;Reject(()=>ResearchValidation.ValidateFiles(broken,CancellationToken.None),"unconfirmed opponent scale");
            broken=Draft(dir);broken.Opponents[0].Path=broken.Actions[0].Source.Path;Reject(()=>ResearchValidation.ValidateFiles(broken,CancellationToken.None),"hero opponent source reused");
            broken=Draft(dir);broken.Actions[1].PotFraction="-1";Reject(()=>ResearchValidation.ValidateFiles(broken,CancellationToken.None),"negative size");
            broken=Draft(dir);broken.Actions.Add(new ResearchAction{Label="same bet",Kind="bet",PotFraction="0.330",Source=new ResearchSource{Path=Path.Combine(dir,"other.csv")}});Reject(()=>ResearchValidation.Spot(broken,false),"equivalent numeric sizes duplicate");
            broken=Draft(dir);broken.Opponents.Clear();broken.OpponentCommonScale=false;var frequencyOnly=ResearchValidation.ValidateFiles(broken,CancellationToken.None);Check(frequencyOnly.OpponentHands==0,"frequency only registration explicit");
            using(var cts=new CancellationTokenSource()){cts.Cancel();bool cancelled=false;try{ResearchValidation.ValidateFiles(draft,cts.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled&&draft.ValidatedUtc=="","cancel preserves original");}
            string check=Path.Combine(dir,"check.csv"),original=File.ReadAllText(check);File.WriteAllText(check,"hand,weight\nAsAd4c5d,1\n");Reject(()=>ResearchValidation.ValidateFiles(draft,CancellationToken.None),"board collision in action CSV");Check(ResearchValidation.CheckFiles(catalog,CancellationToken.None).Length==1,"changed CSV reported once across boards");File.WriteAllText(check,original);
            var store=new ResearchStore(Path.Combine(dir,"settings"));var w=store.Load();Check(w.Revision==0&&w.Sets.Count==0,"first start empty");w.Sets.Add(Conditions());w.Catalogs.Add(catalog);w=store.Save(w,0);Check(w.Revision==1&&w.Sets[0].Revision==1&&w.Catalogs[0].Revision==1,"initial revisions");var loaded=store.Load();Check(loaded.Sets[0].Conditions[0].Syntax==" AA "&&loaded.Catalogs[0].Spots.Count==2,"JSON restart roundtrip");Check(loaded.Catalogs[0].Spots[0].Opponents[1].Path.EndsWith("相手 part2.csv"),"unicode full path roundtrip");
            var stale=ResearchJson.Clone(loaded);loaded.Sets[0].Name="編集したセット";loaded=store.Save(loaded,1);Check(loaded.Sets[0].Revision==2&&loaded.Catalogs[0].Revision==1,"only changed entity revision increments");byte[] before=File.ReadAllBytes(store.FilePath);bool conflict=false;try{store.Save(stale,1);}catch(InvalidOperationException){conflict=true;}Check(conflict&&before.SequenceEqual(File.ReadAllBytes(store.FilePath)),"stale writer cannot overwrite");
            loaded=store.Save(loaded,2);Check(loaded.Sets[0].Revision==2,"no-op set save keeps entity version");
            string export=Path.Combine(dir,"export.json");store.Export(export,loaded);var imported=ResearchStore.ImportAsCopies(loaded,export);Check(imported.Sets.Count==2&&imported.Catalogs.Count==2,"import appends copies");Check(imported.Sets[0].Id!=imported.Sets[1].Id&&imported.Catalogs[0].Id!=imported.Catalogs[1].Id,"copy identities independent");Check(imported.Sets[1].Conditions[0].Syntax==loaded.Sets[0].Conditions[0].Syntax,"import syntax unchanged");
            Reject(()=>store.Export(store.FilePath,loaded),"export cannot clobber active store");Reject(()=>store.Export(check,loaded),"export cannot clobber input CSV");
            var invalid=ResearchJson.Clone(loaded);invalid.SchemaVersion=99;Reject(()=>store.Save(invalid,loaded.Revision),"unknown schema");Check(File.ReadAllBytes(store.FilePath).SequenceEqual(ResearchJson.Encode(loaded)),"failed validation preserves bytes");
            File.Delete(Path.Combine(dir,"相手 part2.csv"));Check(store.Load().Catalogs.Count==1,"offline metadata survives missing CSV");Check(ResearchValidation.CheckFiles(catalog,CancellationToken.None).Length==1,"missing CSV reported");
            using(var gate=new FileStream(store.FilePath+".lock",FileMode.Open,FileAccess.ReadWrite,FileShare.None)){bool blocked=false;try{store.Save(loaded,loaded.Revision);}catch(IOException){blocked=true;}Check(blocked,"concurrent store writer locked");}
            File.WriteAllText(store.FilePath,"invalid JSON");bool unreadable=false;try{store.Save(loaded,loaded.Revision);}catch(Exception){unreadable=true;}Check(unreadable&&File.ReadAllText(store.FilePath)=="invalid JSON","corrupt existing store never silently replaced");
        }
        public static void Run(){Definitions();string dir=Path.Combine(Path.GetTempPath(),"plo-research-tests-"+Guid.NewGuid().ToString("N"));try{FilesAndPersistence(dir);}finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}Console.WriteLine("PASS: "+checks+" research setup checks");GeneratedConditionTests.Run();}
    }
}
