using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FlopCommands.Equity;

// Separate executable: compare the same seed, trial count and inputs before/after.
// CSV output includes results and pair counts, not just wall-clock timing.
namespace EquityTests {
    public static class Benchmark {
        static readonly CancellationToken None=CancellationToken.None;
        static string F(double x){return x.ToString("R",CultureInfo.InvariantCulture);}
        static void Emit(string name,int repeat,double prepare,double calculate,EquityResult r){
            Console.WriteLine(name+","+repeat+","+F(prepare)+","+F(calculate)+","+F(r.Equity)+","+F(r.Win)+","+F(r.Tie)+","+F(r.Loss)+","+r.Attempts+","+r.LegalPairs+","+r.HeroCount+","+r.VillainCount);
        }
        public static void Main(string[] args){
            int repeats=args.Length>0?int.Parse(args[0]):3;
            var all=EquityRange.Parse("random",None);var aces=all.SelectSyntax("AA",None);
            var settings=new EquitySettings{Trials=50000,Seed=7123};
            var warm=EquityEngine.Prepare("9s8d2d",aces,all,None,null);
            EquityEngine.Calculate(warm,new EquitySettings{Trials=2000,Seed=7123},None,null);
            Console.WriteLine("case,repeat,prepare_ms,calculate_ms,eq,win,tie,loss,attempts,legal_pairs,hero_count,villain_count");
            for(int rep=0;rep<repeats;rep++){
                foreach(string board in new[]{"9s8d2d","9s8d2d7h"})foreach(bool wide in new[]{true,false}){
                    var sw=Stopwatch.StartNew();var plan=EquityEngine.Prepare(board,wide?all:aces,all,None,null);double prepare=sw.Elapsed.TotalMilliseconds;
                    sw.Restart();var result=EquityEngine.Calculate(plan,settings,None,null);Emit((board.Length==6?"flop_":"turn_")+(wide?"random":"AA"),rep,prepare,sw.Elapsed.TotalMilliseconds,result);
                }
            }
            string dir=Path.Combine(Path.GetTempPath(),"plo-equity-benchmark-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{
                var paths=new[]{Path.Combine(dir,"check.csv"),Path.Combine(dir,"bet.csv")};
                long boardMask=EquityCards.Mask(EquityCards.Parse("9s8d2d",3,3));
                var check=new StringBuilder("hand,frequency,percent,weight,ev,ev_diff,variations\n");var bet=new StringBuilder(check.ToString());
                foreach(var combo in all.GetCombos())if((combo.Mask&boardMask)==0){check.AppendLine(combo.Hand+",0.4,40,0.4,0,0,1");bet.AppendLine(combo.Hand+",0.6,60,0.6,0,0,1");}
                File.WriteAllText(paths[0],check.ToString());File.WriteAllText(paths[1],bet.ToString());
                var request=new EquityJobRequest{Board="9s8d2d",HeroMode=EquityHeroMode.AllActions,ActionPaths=paths,ActionLabels=new[]{"check","bet"},CommonScaleConfirmed=true,BetActions=new[]{false,true},VillainText="random",Settings=settings,
                    Conditions=new[]{new EquityCondition{Label="all",Syntax="*"},new EquityCondition{Label="top",Syntax="99"},new EquityCondition{Label="middle",Syntax="88!(99)"},new EquityCondition{Label="bottom",Syntax="22!((88,99))"}}};
                for(int rep=0;rep<repeats;rep++){
                    var sw=Stopwatch.StartNew();var job=EquityJob.Run(request,None,null);double elapsed=sw.Elapsed.TotalMilliseconds;
                    foreach(var row in job.Rows){if(row.Result==null)throw new Exception(row.Status);Emit("job_"+row.Label,rep,0,elapsed,row.Result);if(Math.Abs(row.Frequency.Percent[1]-60)>1e-7)throw new Exception("Frequency changed");}
                }
            }finally{Directory.Delete(dir,true);}
        }
    }
}
