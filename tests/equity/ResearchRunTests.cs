using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using FlopCommands.Equity;
using FlopCommands.Research;

namespace EquityTests {
    static class ResearchRunTests {
        static int checks;
        static void Check(bool v,string text){checks++;if(!v)throw new Exception("Research run: "+text);}
        static void Near(double? v,double expected,string text){Check(v.HasValue&&Math.Abs(v.Value-expected)<1e-8,text);}
        static void Reject(Action action,string why){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Research run accepted: "+why);}
        static ResearchConditionSet Set(){return new ResearchConditionSet{Name="AA調査",Conditions=new List<ResearchCondition>{new ResearchCondition{Id="aa",Label="AA",Syntax="AA"},new ResearchCondition{Id="empty",Label="KK（該当なし）",Syntax="KK"}}};}
        static ResearchCatalog Catalog(){return new ResearchCatalog{Name="合成 Ahi",Scenario=new ResearchScenario{HeroPosition="BB",VillainPosition="BTN",StackBb="100",PotType="SRP",ActionHistory="BTN open, BB call",SolutionContext="synthetic tests"}};}
        static ResearchSpot Spot(string dir,string prefix,string board,double check,double bet,bool reverse=false){
            var c=Path.Combine(dir,prefix+"_check.csv");var b=Path.Combine(dir,prefix+"_bet.csv");var v=Path.Combine(dir,prefix+"_opponent.csv");File.WriteAllText(c,"hand,weight\nAcAd4c5d,"+check.ToString("R",CultureInfo.InvariantCulture)+"\n");File.WriteAllText(b,"hand,weight\nAcAd4c5d,"+bet.ToString("R",CultureInfo.InvariantCulture)+"\n");File.WriteAllText(v,"hand,weight\nKcKh6h9d,0.25\nJdTh6c9s,0.75\n");
            var actions=new List<ResearchAction>{new ResearchAction{Id="check",Label="Check",Kind="check",Source=new ResearchSource{Path=c}},new ResearchAction{Id="bet",Label="Bet 33%",Kind="bet",PotFraction="0.33",Source=new ResearchSource{Path=b}}};if(reverse)actions.Reverse();
            return ResearchValidation.ValidateFiles(new ResearchSpot{Id=prefix,Board=board,Node="BB first",AllActionsCommonScale=true,OpponentCommonScale=true,Actions=actions,Opponents=new List<ResearchSource>{new ResearchSource{Path=v}}},CancellationToken.None);
        }
        static ResearchRunSettings Settings(){return new ResearchRunSettings{AhiOnly=true,Trials=1000,Seed=7123,GroupBy="pair"};}
        static ResearchRunResult Run(ResearchCatalog c,ResearchConditionSet s=null,ResearchRunSettings settings=null){return ResearchRun.Execute(c,s??Set(),settings??Settings(),CancellationToken.None,null);}
        static ResearchSummary Summary(ResearchRunResult r,string action="check",string id="aa"){return ResearchRun.Summaries(r).Single(s=>s.ActionKey==action&&s.ConditionId==id);}
        static void Features(){
            var f=ResearchFeatures.From("7cAsKd");Check(f.Suit=="レインボー"&&f.Pair=="アンペア"&&f.Aces==1&&f.High==1&&f.Middle==1&&f.Low==0,"rainbow features independent of order");
            f=ResearchFeatures.From("Ah8h3c");Check(f.Suit=="ツートーン"&&f.High==0&&f.Middle==1&&f.Low==1,"two tone counts");
            f=ResearchFeatures.From("AsKsQs");Check(f.Suit=="モノトーン"&&f.OtherRanks=="KQ","monotone ranks");
            f=ResearchFeatures.From("AsAd7c");Check(f.Pair=="ペア"&&f.Aces==2&&f.Middle==1,"paired ace board");
            f=ResearchFeatures.From("AsAdAh");Check(f.Pair=="トリップス"&&f.Aces==3&&f.OtherRanks=="","trip ace has no side ranks");
            Reject(()=>f.Group("wet"),"unversioned subjective classification");
        }
        static void Calculations(string dir){
            var c=Catalog();c.Spots.Add(Spot(dir,"one","AsKd7c",.01,.03));c.Spots.Add(Spot(dir,"two","Ah8h3c",.75,.25,true));c.Spots.Add(Spot(dir,"nonace","KsQd7c",.5,.5));
            RankFilters(c);
            var r=Run(c);ResearchFiles.Validate(r);Check(r.Status=="completed"&&r.CompletedSpots==2&&r.TargetSpots==2&&r.FilteredSpots==1,"Ahi selection excludes non-Ace board");Check(r.Rows.Count==4,"one row per condition per board");
            Near(Summary(r).FrequencyMean,50,"equal board average not pooled CSV weight");Near(Summary(r,"bet:0.33").FrequencyMean,50,"action key unaffected by reversed CSV order");Near(Summary(r,ResearchRun.TotalBet).FrequencyMean,50,"total bet");
            Check(Summary(r).Target==2&&Summary(r).FrequencyCount==2&&Summary(r).PairedCount==2,"summary populations");Near(Summary(r).FrequencyMin,25,"frequency min");Near(Summary(r).FrequencyMax,75,"frequency max");
            Near(Summary(r).RangeMean,100,"condition share");Check(Summary(r,"check","empty").FrequencyMean==null&&Summary(r,"check","empty").PairedCount==0,"no matching hand not zero");
            Check(r.Rows.Where(x=>x.ConditionId=="empty").All(x=>x.Status=="no_matching_hands"&&x.Equity==null&&x.Actions.All(a=>a.Frequency==null)),"empty rows preserved");
            var first=c.Spots[0];var eq=EquityJob.Run(new EquityJobRequest{Board=first.Board,HeroMode=EquityHeroMode.AllActions,CommonScaleConfirmed=true,ActionPaths=first.Actions.Select(a=>a.Source.Path).ToArray(),ActionLabels=first.Actions.Select(a=>a.Label).ToArray(),VillainText=EquityInput.ImportRangeFiles(first.Opponents.Select(s=>s.Path).ToArray(),false,CancellationToken.None).Text,Conditions=new[]{new EquityCondition{Label="AA",Syntax="AA"}},Settings=new EquitySettings{Trials=1000,Seed=7123,ExactRiver=false}},CancellationToken.None,null);
            var actual=r.Rows.Single(x=>x.SpotId==first.Id&&x.ConditionId=="aa");Near(actual.Equity.Equity,eq.Rows[0].Result.Equity,"same seed and EQ as GUI job");Check(actual.Equity.Seed==eq.Rows[0].Result.Seed&&actual.Equity.Attempts==eq.Rows[0].Result.Attempts,"same seeded sampling");Near(actual.Actions[0].Frequency,eq.Rows[0].Frequency.Percent[0],"frequency matches GUI job");
            Check(ResearchRun.Points(r,false).Length==4&&ResearchRun.Points(r,true).Length==2,"all actions in board and summary plots");Check(ResearchRun.Points(r,true).All(p=>!p.CiLow.HasValue),"no invented group confidence interval");
            var all=Settings();all.AhiOnly=false;Check(Run(c,null,all).TargetSpots==3,"all flop option");
            var otherNode=ResearchJson.Clone(c);otherNode.Spots[1].Node="other decision";Check(ResearchRun.Summaries(Run(otherNode)).Where(s=>s.ConditionId=="aa"&&s.ActionKey=="check").Count()==2,"different action positions not mixed");
            var suit=Settings();suit.GroupBy="suit_pair";Check(ResearchRun.Summaries(Run(c,null,suit)).Count(s=>s.ConditionId=="aa"&&s.ActionKey=="check")==2,"suit groups distinct");
            var missing=Set();missing.Conditions.Add(new ResearchCondition{Id="partial",Label="ボード別",Kind="per_board",ByBoard=new Dictionary<string,string>{{"AsKd7c","AA"}}});Reject(()=>Run(c,missing),"strict missing syntax stops before calculation");var partial=Settings();partial.AllowMissingDefinitions=true;var partialResult=Run(c,missing,partial);ResearchFiles.Validate(partialResult);Check(partialResult.Status=="completed_with_gaps"&&partialResult.Rows.Count(x=>x.Status=="missing_definition")==1,"explicit partial definitions retained");Near(Summary(partialResult,"check","partial").FrequencyMean,25,"missing board not zero");Check(Summary(partialResult,"check","partial").FrequencyCount==1&&Summary(partialResult,"check","partial").Target==2,"missing definition denominator shown");
            var frequency=ResearchJson.Clone(c);frequency.Spots[1].Opponents.Clear();frequency.Spots[1].OpponentHands=0;Reject(()=>Run(frequency),"required EQ rejects no opponent");var optional=Settings();optional.AllowFrequencyOnly=true;var o=Run(frequency,null,optional);ResearchFiles.Validate(o);Near(Summary(o).FrequencyMean,50,"frequency includes valid no-opponent board");Near(Summary(o).PairedFrequency,25,"scatter frequency paired to EQ cohort");Check(Summary(o).PairedCount==1&&Summary(o).FrequencyCount==2,"different populations explicit");Check(o.Rows.Any(x=>x.Status=="frequency_only"),"frequency only state");
            var sizes=ResearchJson.Clone(c);sizes.Spots[1].Actions.Single(a=>a.Kind=="bet").PotFraction="0.75";var different=Run(sizes);Check(Summary(different,"bet:0.33").Available==1&&Summary(different,"bet:0.33").FrequencyCount==1,"missing action size not treated as zero");Near(Summary(different,"bet:0.33").FrequencyMean,75,"size mean uses actual availability");
            var duplicate=Set();duplicate.Conditions.Add(new ResearchCondition{Id="alias",Label="別名AA",Syntax="AA"});var dup=Run(c,duplicate);Check(dup.Rows.Single(x=>x.SpotId=="one"&&x.ConditionId=="alias").Equity.Equity==actual.Equity.Equity,"duplicate syntax shares seeded result not labels");
            var blocked=Catalog();var b=Spot(dir,"blocked","AsKd7c",.25,.75);File.WriteAllText(b.Opponents[0].Path,"hand,weight\nAcAd4c5d,1\n");b=ResearchValidation.ValidateFiles(b,CancellationToken.None);blocked.Spots.Add(b);var noPairs=Run(blocked);ResearchFiles.Validate(noPairs);Check(noPairs.Status=="completed_with_gaps"&&noPairs.Rows[0].Status=="equity_unavailable","no legal pair explicit");Near(Summary(noPairs).FrequencyMean,25,"EQ failure preserves frequency");Check(ResearchRun.Points(noPairs,false).Length==0,"no fake EQ plot");
            var snap=Catalog();snap.Spots.Add(c.Spots[0]);var cfg=Settings();var conditions=Set();bool mutated=false;var frozen=ResearchRun.Execute(snap,conditions,cfg,CancellationToken.None,x=>{if(!mutated){snap.Name="after";conditions.Conditions[0].Syntax="KK";cfg.Seed=0;mutated=true;}});Check(frozen.Catalog.Name!="after"&&frozen.ConditionSet.Conditions[0].Syntax=="AA"&&frozen.Settings.Seed==7123,"input snapshot immutable");
            Cancellation(c);Archives(r,dir);ChangedFiles(c,dir);
        }
        static void RankFilters(ResearchCatalog c){
            foreach(char rank in ResearchBoardFilter.Ranks){string board=rank+"s"+rank+"d"+rank+"c";foreach(char other in ResearchBoardFilter.Ranks)Check(ResearchBoardFilter.Matches(board,other.ToString())==(rank==other),"highest rank, including paired/trip boards");Check(ResearchBoardFilter.Matches(board,"all"),"all rank inclusion");}
            Check(!ResearchBoardFilter.Matches("AsKd2c","K")&&!ResearchBoardFilter.Matches("KsQd2c","Q"),"higher rank exclusion");
            var all=new ResearchRunSettings{Trials=1000};Check(Run(c,null,all).TargetSpots==3,"new default includes all boards");
            var selected=ResearchJson.Clone(c);selected.HighestRank="K";var inherited=Run(selected,null,new ResearchRunSettings{Trials=1000});Check(inherited.TargetSpots==1&&inherited.Settings.HighestRank=="K","catalog rank inherited by execution");Check(inherited.Rows.All(row=>row.Board.IndexOf('A')<0),"no aces in Khi results");
            var loaded=ResearchFiles.Decode(ResearchFiles.Encode(inherited));Check(loaded.Catalog.HighestRank=="K"&&loaded.Settings.HighestRank=="K","rank retained in archive");Check(Encoding.UTF8.GetString(ResearchFiles.Markdown(loaded)).Contains("最高ランク条件：K"),"report exposes filter");
            var overridden=new ResearchRunSettings{HighestRank="A",Trials=1000};Check(Run(c,null,overridden).TargetSpots==2,"explicit A filter");
            var old=Run(c);old.Settings.HighestRank=null;Check(ResearchFiles.Decode(ResearchFiles.Encode(old)).TargetSpots==2,"old Ahi-only result remains readable");
            Reject(()=>Run(c,null,new ResearchRunSettings{HighestRank="K",AhiOnly=true,Trials=1000}),"conflicting new and legacy filters");Reject(()=>Run(c,null,new ResearchRunSettings{HighestRank="X",Trials=1000}),"unknown highest rank");
        }
        static void Cancellation(ResearchCatalog c){
            using(var token=new CancellationTokenSource()){token.Cancel();var before=ResearchRun.Execute(c,Set(),Settings(),token.Token,null);ResearchFiles.Validate(before);Check(before.Status=="cancelled"&&before.Rows.Count==0,"cancel before work has no fabricated rows");Check(Summary(before).Target==2&&Summary(before).FrequencyCount==0,"cancel retains planned denominator");}
            using(var token=new CancellationTokenSource()){var after=ResearchRun.Execute(c,Set(),Settings(),token.Token,p=>{if(p.Completed==1)token.Cancel();});ResearchFiles.Validate(after);Check(after.Status=="cancelled"&&after.CompletedSpots==1&&after.Rows.Count==2,"cancel commits whole completed board only");Check(Summary(after).FrequencyCount==1&&Summary(after).Target==2,"cancel summary marks incomplete sample");}
            var tooLarge=Settings();tooLarge.Trials=2000001;Reject(()=>ResearchRun.ValidateRequest(c,Set(),tooLarge),"trial budget");
        }
        static void Archives(ResearchRunResult result,string dir){
            var bytes=ResearchFiles.Encode(result);var loaded=ResearchFiles.Decode(bytes);Check(loaded.Rows.Count==result.Rows.Count&&loaded.Rows[0].Syntax==result.Rows[0].Syntax,"archive roundtrip includes syntax");Check(loaded.Catalog.Spots[0].Actions[0].Source.Sha256==result.Catalog.Spots[0].Actions[0].Source.Sha256,"provenance hash retained");Near(Summary(loaded).FrequencyMean,50,"summary reproducible from saved rows");
            var forged=ResearchJson.Clone(result);forged.Rows[0].Syntax="KK";Reject(()=>ResearchFiles.Encode(forged),"mismatched syntax in archive");forged=ResearchJson.Clone(result);forged.Rows[0].Actions[0].Frequency=0;Reject(()=>ResearchFiles.Encode(forged),"invalid frequency sums");forged=ResearchJson.Clone(result);forged.Rows[0].Features.Suit="other";Reject(()=>ResearchFiles.Encode(forged),"wrong classification");
            string saved=ResearchFiles.SaveHistory(result,Path.Combine(dir,"runs"));Check(File.Exists(saved)&&ResearchFiles.Read(saved).Id==result.Id,"auto history reopen");bool duplicate=false;try{ResearchFiles.SaveHistory(result,Path.Combine(dir,"runs"));}catch(IOException){duplicate=true;}Check(duplicate,"history cannot overwrite same result");
            string csv=Encoding.UTF8.GetString(ResearchFiles.Csv(result));Check(csv.Contains("paired_frequency")&&csv.Contains("category")&&csv.Contains("no_matching_hands"),"CSV has paired cohort and empty rows");var xml=new XmlDocument();xml.LoadXml(Encoding.UTF8.GetString(ResearchFiles.Svg(result,false)));Check(xml.DocumentElement.LocalName=="svg","board SVG valid XML");xml.LoadXml(Encoding.UTF8.GetString(ResearchFiles.Svg(result,true)));Check(xml.InnerText.Contains("same paired observations"),"group SVG identifies averaging");
            string md=Encoding.UTF8.GetString(ResearchFiles.Markdown(result));Check(md.Contains("SHA-256")&&md.Contains("等重み")&&md.Contains("該当"),"report includes provenance and method");
            string settings=Path.Combine(dir,"workspace.json");File.WriteAllText(settings,"keep");Reject(()=>ResearchFiles.Export(result,settings,".json",false,settings),"protected settings");Check(File.ReadAllText(settings)=="keep","export preserves settings");Reject(()=>ResearchFiles.Export(result,result.Catalog.Spots[0].Actions[0].Source.Path,".csv",false,settings),"protected input CSV");
            ResearchFiles.Export(result,Path.Combine(dir,"result.csv"),".csv",false,settings);Check(File.Exists(Path.Combine(dir,"result.csv")),"CSV export");
            // Save synthetic render evidence for the current local verification only.
            string evidence=Environment.GetEnvironmentVariable("PLO_RESEARCH_TEST_ARTIFACTS");if(!string.IsNullOrEmpty(evidence)){Directory.CreateDirectory(evidence);File.WriteAllBytes(Path.Combine(evidence,"research-board.svg"),ResearchFiles.Svg(result,false));File.WriteAllBytes(Path.Combine(evidence,"research-category.svg"),ResearchFiles.Svg(result,true));}
        }
        static void ChangedFiles(ResearchCatalog c,string dir){
            string path=c.Spots[0].Actions[0].Source.Path,old=File.ReadAllText(path);File.AppendAllText(path,"\n");var failed=Run(c);ResearchFiles.Validate(failed);Check(failed.Status=="failed"&&failed.Rows.Count==0&&failed.Error.Contains("変更"),"preflight rejects changed registered source");File.WriteAllText(path,old);
            bool changed=false;var during=ResearchRun.Execute(c,Set(),Settings(),CancellationToken.None,p=>{if(!changed&&p.Message.Contains("計算中")){File.AppendAllText(path,"\n");changed=true;}});ResearchFiles.Validate(during);Check(during.Rows.Where(r=>r.SpotId=="one").All(r=>r.Status=="failed"&&r.Equity==null&&r.Actions.All(a=>a.Frequency==null)),"changed during computation discards affected numbers");File.WriteAllText(path,old);
        }
        public static void Run(){Features();string dir=Path.Combine(Path.GetTempPath(),"plo-research-run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);try{Calculations(dir);}finally{Directory.Delete(dir,true);}Console.WriteLine("PASS: "+checks+" research execution checks");}
    }
}
