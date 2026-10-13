using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FlopCommands.Equity;
using FlopCommands.Research;
using PloMcp;

namespace PloMcpResearchTests {
    public static class Program {
        static int checks;
        static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        static void Reject(Action action,string message){try{action();}catch(ArgumentException){checks++;return;}throw new Exception(message);}
        public static int Main(){
            string dir=Path.Combine(Path.GetTempPath(),"plo-mcp-research-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{
                string history=Path.Combine(dir,"history"),output=Path.Combine(dir,"output");Directory.CreateDirectory(output);
                var c=new ResearchCatalog{Id="catalog",Name="Synthetic",Scenario=new ResearchScenario{HeroPosition="BB",VillainPosition="BTN",StackBb="100",PotType="SRP",ActionHistory="BTN open, BB call",SolutionContext="synthetic"}};
                for(int i=0;i<2;i++){
                    string a=Path.Combine(dir,i+"check.csv"),b=Path.Combine(dir,i+"bet.csv"),v=Path.Combine(dir,i+"villain.csv");File.WriteAllText(a,"hand,weight\nAcAd4c5d,0.25\n");File.WriteAllText(b,"hand,weight\nAcAd4c5d,0.75\n");File.WriteAllText(v,"hand,weight\nKcKh6h9d,0.25\nJdTh6c9s,0.75\n");
                    c.Spots.Add(ResearchValidation.ValidateFiles(new ResearchSpot{Id="spot"+i,Board=i==0?"AsKd7c":"Ah8h3c",Node="BB first",AllActionsCommonScale=true,OpponentCommonScale=true,Actions=new List<ResearchAction>{new ResearchAction{Id="check",Label="Check",Kind="check",Source=new ResearchSource{Path=a}},new ResearchAction{Id="bet",Label="Bet",Kind="bet",PotFraction="0.33",Source=new ResearchSource{Path=b}}},Opponents=new List<ResearchSource>{new ResearchSource{Path=v}}},CancellationToken.None));
                }
                var set=new ResearchConditionSet{Id="set",Name="User syntax",Conditions=new List<ResearchCondition>{new ResearchCondition{Id="aa",Label="AA",Syntax="AA"}}};
                var store=new ResearchStore(Path.Combine(history,"Research"));var w=store.Save(new ResearchWorkspace{Catalogs=new List<ResearchCatalog>{c},Sets=new List<ResearchConditionSet>{set}},0);c=w.Catalogs[0];set=w.Sets[0];
                var q=new ResearchRequest{action="run",catalogId=c.Id,catalogRevision=c.Revision,setId=set.Id,setRevision=set.Revision,settings=new ResearchRunSettings{Trials=1000,Seed=7123,GroupBy="pair"},historyRoot=history,outputRoot=output,inputRoots=new[]{dir},maxCsvBytes=1000000};
                ResearchBridge.Execute(q,CancellationToken.None,null);var files=Directory.GetFiles(Path.Combine(history,"Research","Runs"));Check(files.Length==1,"Run must save exactly one result");var r=ResearchFiles.Read(files[0]);Check(r.Status=="completed"&&r.CompletedSpots==2,"Two complete boards");
                foreach(var spot in c.Spots){var villain=EquityInput.ImportRangeFiles(spot.Opponents.Select(s=>s.Path).ToArray(),false,CancellationToken.None);var gui=EquityJob.Run(new EquityJobRequest{Board=spot.Board,HeroMode=EquityHeroMode.AllActions,CommonScaleConfirmed=true,ActionPaths=spot.Actions.Select(a=>a.Source.Path).ToArray(),ActionLabels=spot.Actions.Select(a=>a.Label).ToArray(),VillainText=villain.Text,Conditions=new[]{new EquityCondition{Label="AA",Syntax="AA"}},Settings=new EquitySettings{Trials=1000,Seed=7123,ExactRiver=false}},CancellationToken.None,null);var row=r.Rows.Single(x=>x.SpotId==spot.Id);Check(row.Equity.Equity==gui.Rows[0].Result.Equity,"MCP EQ equals GUI seeded EQ");Check(row.Equity.Seed==gui.Rows[0].Result.Seed,"Derived seed equals GUI");Check(row.Actions[0].Frequency==gui.Rows[0].Frequency.Percent[0],"MCP action frequency equals GUI");}
                using(var cancel=new CancellationTokenSource()){ResearchBridge.Execute(q,cancel.Token,p=>{if(p.Completed==1)cancel.Cancel();});}
                var cancelled=Directory.GetFiles(Path.Combine(history,"Research","Runs")).Select(ResearchFiles.Read).Single(x=>x.Status=="cancelled");Check(cancelled.CompletedSpots==1&&cancelled.Rows.Count==1,"Cancellation preserves exactly one complete board");Check(ResearchRun.Summaries(cancelled)[0].Target==2,"Cancellation retains target population");
                q.catalog=ResearchJson.Clone(c);q.conditionSet=ResearchJson.Clone(set);q.conditionSet.Conditions[0].Syntax="KK";Reject(()=>ResearchBridge.Execute(q,CancellationToken.None,null),"Changed snapshot accepted");q.catalog=null;q.conditionSet=null;
                q.catalogRevision=0;Reject(()=>ResearchBridge.Execute(q,CancellationToken.None,null),"Stale catalog accepted");q.catalogRevision=c.Revision;
                var original=File.ReadAllText(c.Spots[0].Actions[0].Source.Path);File.AppendAllText(c.Spots[0].Actions[0].Source.Path,"\n");Reject(()=>ResearchBridge.Execute(q,CancellationToken.None,null),"Changed CSV accepted");File.WriteAllText(c.Spots[0].Actions[0].Source.Path,original);
                q.inputRoots=new[]{output};Reject(()=>ResearchBridge.Execute(q,CancellationToken.None,null),"Outside root accepted");q.inputRoots=new[]{dir};
                q.action="export";q.id=r.Id;q.artifactId=Guid.NewGuid().ToString();q.format="json";ResearchBridge.Execute(q,CancellationToken.None,null);Check(ResearchFiles.Read(Path.Combine(output,q.artifactId+".json")).Id==r.Id,"Export uses same GUI archive");bool conflict=false;try{ResearchBridge.Execute(q,CancellationToken.None,null);}catch(IOException){conflict=true;}Check(conflict,"Export cannot overwrite");
                q.action="result";q.id="../other";Reject(()=>ResearchBridge.Execute(q,CancellationToken.None,null),"Path traversal accepted");
                Console.WriteLine("PASS: "+checks+" research bridge checks");return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{Directory.Delete(dir,true);}
        }
    }
}
