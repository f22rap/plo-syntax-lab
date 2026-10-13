using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FlopCommands.Equity;

namespace EquityTests {
    public static class Program {
        static int checks;
        static CancellationToken None = CancellationToken.None;
        static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
        static void Close(double x,double expected,double tolerance,string reason){Check(Math.Abs(x-expected)<=tolerance,reason+": "+x+" != "+expected);}
        static void Throws<T>(Action action,string reason) where T:Exception {try{action();}catch(T){checks++;return;}throw new Exception("Expected "+typeof(T).Name+": "+reason);}
        static EquityRange Range(string text){return EquityRange.Parse(text,None);}
        static EquityRange One(string text){return new EquityRange(new[]{new EquityCombo(text,1)});}
        static EquityResult Run(string board,EquityRange h,EquityRange v,bool exact,int n=50000,uint seed=123){return EquityEngine.Calculate(EquityEngine.Prepare(board,h,v,None,null),new EquitySettings{ExactRiver=exact,Trials=n,Seed=seed},None,null);}
        // Independent slow five-card evaluator: sorted rank groups and lexicographic digits.
        // No engine rank cache, masks, category helpers, or evaluator are used.
        static uint ReferenceFive(int[] cards){
            int[] ranks=cards.Select(c=>14-c/4).OrderByDescending(x=>x).ToArray();
            var groups=ranks.GroupBy(x=>x).OrderByDescending(g=>g.Count()).ThenByDescending(g=>g.Key).ToArray();
            int[] distinct=ranks.Distinct().ToArray();int high=0;
            if(distinct.Length==5){if(distinct[0]-distinct[4]==4)high=distinct[0];else if(distinct.SequenceEqual(new[]{14,5,4,3,2}))high=5;}
            bool flush=cards.All(c=>c%4==cards[0]%4);int category;int[] kickers;
            if(flush&&high>0){category=8;kickers=new[]{high};}
            else if(groups[0].Count()==4){category=7;kickers=groups.Select(g=>g.Key).ToArray();}
            else if(groups[0].Count()==3&&groups[1].Count()==2){category=6;kickers=groups.Select(g=>g.Key).ToArray();}
            else if(flush){category=5;kickers=ranks;}
            else if(high>0){category=4;kickers=new[]{high};}
            else {category=groups[0].Count()==3?3:groups[0].Count()==2?(groups[1].Count()==2?2:1):0;kickers=groups.Select(g=>g.Key).ToArray();}
            uint score=(uint)category;for(int i=0;i<5;i++)score=score*15+(uint)(i<kickers.Length?kickers[i]:0);return score;
        }
        static IEnumerable<int[]> Choose(int[] xs,int n){return ChooseAt(xs,n,0,new int[0]);}
        static IEnumerable<int[]> ChooseAt(int[] xs,int n,int start,int[] prefix){
            if(prefix.Length==n){yield return prefix;yield break;}
            for(int i=start;i<=xs.Length-(n-prefix.Length);i++)foreach(var row in ChooseAt(xs,n,i+1,prefix.Concat(new[]{xs[i]}).ToArray()))yield return row;
        }
        static uint ReferenceOmaha(int[] hand,int[] board){return Choose(hand,2).SelectMany(p=>Choose(board,3).Select(t=>ReferenceFive(p.Concat(t).ToArray()))).Max();}
        static int[] Deal(Random rng,int n){var cards=Enumerable.Range(0,52).ToArray();for(int i=0;i<n;i++){int j=rng.Next(i,52),t=cards[i];cards[i]=cards[j];cards[j]=t;}return cards.Take(n).ToArray();}
        static void Evaluator(){
            var evaluator=new OmahaEvaluator();var rng=new Random(456);
            for(int i=0;i<20000;i++){int[] c=Deal(rng,5);Check(evaluator.Five(c[0],c[1],c[2],c[3],c[4])==ReferenceFive(c),"Five-card oracle "+i);}
            for(int i=0;i<500;i++){int[] c=Deal(rng,9),h=c.Take(4).ToArray(),b=c.Skip(4).ToArray();Check(evaluator.Score(h,b)==ReferenceOmaha(h,b),"Omaha oracle "+i);}
            string[] hands={"Ac2d4h5s","AsKs3d4c","AhAdAcKs","2h3h4h5h"};
            string[] boards={"QsJsTs9s8s","2c2d2s3c3d","4s5s6s7s8s","AcKdQhJsTc"};
            foreach(string h in hands)foreach(string b in boards){int[] hh=EquityCards.Parse(h,4,4),bb=EquityCards.Parse(b,5,5);if(hh.Intersect(bb).Any())continue;Check(evaluator.Evaluate(h,b)==ReferenceOmaha(hh,bb),"Exactly two/three on board-only straight/flush/quads");}
            Check(evaluator.Evaluate("As2sKhQc","3s4s5s9dTc")/759375==8,"Wheel straight flush");
            Check(evaluator.Evaluate("AcKd2h3d","QsJsTs9s8s")/759375!=8,"Board cannot play by itself");
            Throws<ArgumentException>(()=>evaluator.Evaluate("AsKsQhJd","As2d3h4c5s"),"board collision");
            Console.WriteLine("Evaluator: 20,000 independent five-card and 500 Omaha comparisons passed.");
        }
        static void Ranges(){
            Check(Range("random").Count==270725,"All combinations");Check(Range("AA**").Count==6961,"AA combinations");
            Check(Range("AKQJ:ds").Count==36,"double suited");Check(Range("AKQJ:ss").Count==144,"single suited");Check(Range("AKQJ:r").Count==24,"rainbow");
            Check(Range("AKQJ:ts").Count==48,"three suited");Check(Range("AKQJ:mono").Count==4,"monotone");
            var aa=Range("AA**@0.25, AAxx@75%");Close(aa.TotalWeight,6961*.75,1e-9,"duplicate max");
            Check(Range("!AA**,random").Count==270725-6961,"exclusion independent of order");
            Check(Range("as ah 10d qc@5e-3; QcTdAhAs@1%").Count==1,"physical deduplication");
            Close(Range("AsAhTdQc@1e-100").TotalWeight,1e-100,0,"no tiny-weight cutoff");
            var actions=new[]{Range("AsAhKdQc@0.2,2s2h3d4c@0.1"),Range("QcKdAhAs@0.3,2s2h3d4c@0.4")};
            var combined=EquityRange.FromActions(actions,true);Check(combined.Count==2,"action union");Close(combined.TotalWeight,1,1e-15,"action sum");
            Check(combined.GetCombos().All(c=>Math.Abs(c.Weight-.5)<1e-15),"sum by physical combo");
            Throws<ArgumentException>(()=>EquityRange.FromActions(actions,false),"independent normalization refused");
            Check(combined.SelectSyntax("AA:!ss",None).Count==1,"existing syntax");Check(combined.SelectSyntax("!*",None).Count==0,"empty does not become all");
            foreach(string s in new[]{"!AA**","AA**@0","AA**@-1","AA**@NaN","AA**@Infinity","AA**@2","(AA,KK)","AKQJ:bad","AsAsKdQc","AKQJ@0.5@0.2"})Throws<ArgumentException>(()=>Range(s),"invalid range "+s);
            Check(EquityCards.Text(EquityCards.Parse("a♠ - A♥, 10♦ Q♣",4,4))=="AsAhTdQc","card normalization");
            Throws<OperationCanceledException>(()=>EquityRange.Parse("random",new CancellationToken(true)),"cancel range expansion");
            Console.WriteLine("Ranges: counts, syntax, shared-scale action sums, duplicates, tiny weights and invalid input passed.");
        }
        static double Oracle(string board,EquityRange h,EquityRange v,out long count){
            int[] b=EquityCards.Parse(board,5,5);double numerator=0,denominator=0;count=0;
            foreach(var hero in h.GetCombos())foreach(var villain in v.GetCombos()){
                int[] hc=EquityCards.Parse(hero.Hand,4,4),vc=EquityCards.Parse(villain.Hand,4,4);
                if(hc.Intersect(vc).Any()||hc.Intersect(b).Any()||vc.Intersect(b).Any())continue;
                uint hs=ReferenceOmaha(hc,b),vs=ReferenceOmaha(vc,b);double w=hero.Weight*villain.Weight;
                numerator+=w*(hs>vs?1:hs==vs?.5:0);denominator+=w;count++;
            }
            return 100*numerator/denominator;
        }
        static void Calculations(){
            const string board="2c3d7h9sTc";
            var h=One("AcAd4c5d");var v=Range("KcKd4h5h@25%,Jc8hKhQh@75%");
            var exact=Run(board,h,v,true);Close(exact.Equity,25,1e-12,"spec exact 25%");Check(exact.Exact&&exact.Total==2&&exact.Margin==0,"exact metadata");
            Close(exact.CiLow,25,1e-12,"exact CI low");Close(exact.CiHigh,25,1e-12,"exact CI high");
            var mc=Run(board,h,v,false,100000);Close(mc.Equity,25,1,"MC vs exact");
            var again=Run(board,h,v,false,100000);Check(mc.Equity==again.Equity&&mc.Win==again.Win&&mc.Tie==again.Tie&&mc.Attempts==again.Attempts,"seed reproducibility");
            var tie=Run("AsKdQh2c3d",One("JcTc4h5h"),One("JsTs6h7h"),true);Close(tie.Equity,50,0,"all ties EQ");Close(tie.Tie,100,0,"tie rate not halved");
            var win=Run(board,h,One("KcKd4h5h"),true);Close(win.Equity,100,0,"certain win");
            var scaled=Run(board,new EquityRange(new[]{new EquityCombo("AcAd4c5d",1e-100)}),Range("KcKd4h5h@2.5e-101,Jc8hKhQh@7.5e-101"),true);Close(scaled.Equity,25,1e-12,"tiny scale invariance");
            var rng=new Random(88);
            for(int test=0;test<8;test++){
                var hs=new List<EquityCombo>();var vs=new List<EquityCombo>();
                for(int i=0;i<5;i++){hs.Add(new EquityCombo(Deal(rng,4),.1+rng.NextDouble()));vs.Add(new EquityCombo(Deal(rng,4),.1+rng.NextDouble()));}
                var hr=new EquityRange(hs);var vr=new EquityRange(vs);long count;
                double expected=Oracle(board,hr,vr,out count);if(count==0)continue;
                var result=Run(board,hr,vr,true);Close(result.Equity,expected,1e-10,"weighted joint oracle "+test);Check(result.LegalPairs==count&&result.Total==count,"legal pair count");
                var reverse=Run(board,vr,hr,true);Close(result.Equity+reverse.Equity,100,1e-10,"player swap");
            }
            // Different compatible masses: fixed-hero/conditional-villain averaging is WRONG.
            var hero=Range("AcAd4c5d@0.5,KhKd4h5h@0.5");var villain=Range("KcKs6h6d@0.8,Jc8hKhQh@0.2");long pairs;
            double joint=Oracle(board,hero,villain,out pairs);
            var oneByOne=hero.GetCombos().Select(c=>Run(board,new EquityRange(new[]{c}),villain,true).Equity).Average();
            Check(Math.Abs(joint-oneByOne)>1,"fixture detects fixed-hero bias");
            var weighted=Run(board,hero,villain,false,150000);Close(weighted.Equity,joint,1,"joint sampling, not conditional hero average");
            var plan=EquityEngine.Prepare(board,hero,villain,None,null);
            Throws<ArgumentException>(()=>EquityEngine.Calculate(plan,new EquitySettings{MaxExactComparisons=1},None,null),"exact workload limit");
            Throws<ArgumentException>(()=>EquityEngine.Calculate(plan,new EquitySettings{Trials=0},None,null),"invalid trials");
            Throws<ArgumentException>(()=>EquityEngine.Prepare(board,h,h,None,null),"all pairs collide");
            Throws<ArgumentException>(()=>EquityEngine.Prepare(board,One("2cAd4c5d"),villain,None,null),"board-empty range");
            var blocked=EquityEngine.Prepare(board,new EquityRange(h.GetCombos().Concat(new[]{new EquityCombo("2cAd4c5d",1),new EquityCombo("KcKd4h5h",1)})),One("KcKd4h5h"),None,null);
            Check(blocked.HeroBoardBlocked==1&&blocked.HeroNoOpponent==1&&blocked.HeroCount==1,"blocker statistics");
            var cts=new CancellationTokenSource();
            Throws<OperationCanceledException>(()=>EquityEngine.Calculate(plan,new EquitySettings{ExactRiver=false,Trials=1000000},cts.Token,p=>{if(p.Completed>=2048)cts.Cancel();}),"mid-run sampling cancellation");
            cts=new CancellationTokenSource();
            Throws<OperationCanceledException>(()=>EquityEngine.Prepare(board,hero,villain,cts.Token,p=>cts.Cancel()),"preparation cancellation");
            cts=new CancellationTokenSource();
            Throws<OperationCanceledException>(()=>EquityEngine.Calculate(plan,new EquitySettings(),cts.Token,p=>cts.Cancel()),"exact cancellation");
            foreach(string street in new[]{"JsTs2d","JsTs2d7h"}){
                var a=Run(street,One("AsAhKdQc"),Range("AA**,KK**,AKQJ:ds,JT**:ds"),false,10000,123);
                var b=Run(street,One("AsAhKdQc"),Range("AA**,KK**,AKQJ:ds,JT**:ds"),false,10000,123);
                Check(a.Equity==b.Equity&&a.Total==10000&&!a.Exact,"street MC reproducibility");
                if(street=="JsTs2d")Check(a.VillainCount==4595,"spec effective opponent count");
                Close(a.Win+a.Tie+a.Loss,100,1e-10,"W/T/L sums");
                Check(a.CiLow<=a.Equity&&a.Equity<=a.CiHigh,"confidence bounds");
                Console.WriteLine(street+" fixed-hand MC: EQ="+a.Equity.ToString("F2")+", villain count="+a.VillainCount);
            }
            Check(Run(board,h,v,false,1000,0).Equity==Run(board,h,v,false,1000,0).Equity,"seed zero");
            Console.WriteLine("Calculation: exact values, weighted joint oracle, MC, blockers, seed, limits and cancellation passed.");
        }
        static void WideRangesAndRunouts(){
            const string board="2c3d7h9sTc";var rng=new Random(712);int[] b=EquityCards.Parse(board,5,5);
            var hands=new List<EquityCombo>();
            while(hands.Count<520){int[] c=Deal(rng,4);if(!c.Intersect(b).Any())hands.Add(new EquityCombo(c,.01+rng.NextDouble()));}
            var h=new EquityRange(hands.Take(260));var v=new EquityRange(hands.Skip(260));
            var plan=EquityEngine.Prepare(board,h,v,None,null);Check(plan.CandidatePairs>50000,"exercise independent-pair rejection sampler");
            var hs=h.GetCombos();var vs=v.GetCombos();
            var scoreH=hs.Select(x=>ReferenceOmaha(EquityCards.Parse(x.Hand,4,4),b)).ToArray();
            var scoreV=vs.Select(x=>ReferenceOmaha(EquityCards.Parse(x.Hand,4,4),b)).ToArray();
            double wins=0,total=0;long legal=0;
            for(int i=0;i<hs.Length;i++)for(int j=0;j<vs.Length;j++)if((hs[i].Mask&vs[j].Mask)==0){double w=hs[i].Weight*vs[j].Weight;wins+=w*(scoreH[i]>scoreV[j]?1:scoreH[i]==scoreV[j]?.5:0);total+=w;legal++;}
            double expected=100*wins/total;
            var exact=EquityEngine.Calculate(plan,new EquitySettings{Seed=7},None,null);
            Close(exact.Equity,expected,1e-10,"dense weighted exact oracle");Check(plan.LegalPairs==legal,"dense compatible pair count");
            var sampled=EquityEngine.Calculate(plan,new EquitySettings{ExactRiver=false,Trials=150000,Seed=7},None,null);
            Check(sampled.Attempts>sampled.Total,"card collisions rejected");Close(sampled.Equity,expected,1,"dense weighted MC vs oracle");
            foreach(string street in new[]{"JsTs2d","JsTs2d7h"}){
                int[] hero=EquityCards.Parse("AsAhKdQc",4,4),villain=EquityCards.Parse("KhKs9c8d",4,4),flop=EquityCards.Parse(street,3,4);
                int[] available=Enumerable.Range(0,52).Except(hero.Concat(villain).Concat(flop)).ToArray();
                double sum=0;int n=0;
                foreach(var runout in Choose(available,5-flop.Length)){
                    int[] finalBoard=flop.Concat(runout).ToArray();uint sh=ReferenceOmaha(hero,finalBoard),sv=ReferenceOmaha(villain,finalBoard);sum+=sh>sv?1:sh==sv?.5:0;n++;
                }
                var result=Run(street,One("AsAhKdQc"),One("KhKs9c8d"),false,100000,4321);
                Close(result.Equity,100*sum/n,1,"physical runout enumeration "+street);
            }
            var all=Range("random");var timer=Stopwatch.StartNew();var fullPlan=EquityEngine.Prepare("JsTs2d",all,all,None,null);
            Check(fullPlan.HeroCount==211876&&fullPlan.VillainCount==211876,"full flop counts");
            Check(fullPlan.LegalPairs==211876L*148995,"full flop disjoint pair count exceeds Int32");
            var full=EquityEngine.Calculate(fullPlan,new EquitySettings{Trials=10000,Seed=321},None,null);
            Close(full.Equity,50,2,"symmetric random ranges");
            Console.WriteLine("Wide ranges and physical runouts passed. random vs random prepare + 10,000 trials: "+timer.Elapsed.TotalSeconds.ToString("F2")+"s; EQ="+full.Equity.ToString("F2"));
        }
        static void CompatibilityOracle(){
            var rng=new Random(1984);
            for(int test=0;test<30;test++){
                int[] board=Deal(rng,3+test%3);long boardMask=EquityCards.Mask(board);
                var hands=new List<EquityCombo>();
                for(int i=0;i<90;i++){
                    int[] cards=Deal(rng,4);
                    // Shared cards create heavily blocked domains as well as random ones.
                    if(test%3==0&&!cards.Skip(1).Contains(51))cards[0]=51;
                    hands.Add(new EquityCombo(cards,i%2==0?1e-100:1));
                }
                var hero=new EquityRange(hands.Take(45));var villain=new EquityRange(hands.Skip(30));
                var h=hero.GetCombos().Where(c=>(c.Mask&boardMask)==0).ToArray();var v=villain.GetCombos().Where(c=>(c.Mask&boardMask)==0).ToArray();
                long pairs=0;foreach(var a in h)foreach(var b in v)if((a.Mask&b.Mask)==0)pairs++;
                string text=EquityCards.Text(board);
                if(pairs==0){Throws<ArgumentException>(()=>EquityEngine.Prepare(text,hero,villain,None,null),"oracle empty legal domain");continue;}
                var plan=EquityEngine.Prepare(text,hero,villain,None,null);
                int hc=h.Count(a=>v.Any(b=>(a.Mask&b.Mask)==0)),vc=v.Count(b=>h.Any(a=>(a.Mask&b.Mask)==0));
                Check(plan.LegalPairs==pairs,"compatibility count matches direct physical-card intersection");
                Check(plan.HeroCount==hc&&plan.VillainCount==vc,"compatible support matches brute force");
                Check(plan.HeroNoOpponent==h.Length-hc&&plan.VillainNoOpponent==v.Length-vc,"unpaired combo counts match brute force");
                Check(plan.HeroBoardBlocked==hero.Count-h.Length&&plan.VillainBoardBlocked==villain.Count-v.Length,"board-blocked counts match brute force");
            }
            Console.WriteLine("Compatibility: random and heavily blocked ranges match independent pair enumeration.");
        }
        public static int Main(){try{var timer=Stopwatch.StartNew();Evaluator();Ranges();Calculations();WideRangesAndRunouts();CompatibilityOracle();InputTests.Run();PlotTests.Run();ArchiveTests.Run();ResearchTests.Run();ResearchRunTests.Run();Console.WriteLine("PASS: "+checks+" engine checks plus input/job checks, "+timer.Elapsed.TotalSeconds.ToString("F2")+" seconds");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    }
}
