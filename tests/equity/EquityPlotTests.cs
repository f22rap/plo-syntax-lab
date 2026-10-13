using System;
using System.IO;
using System.Linq;
using System.Threading;
using FlopCommands.Equity;
namespace EquityTests {
    public static class PlotTests {
        static int checks;static readonly CancellationToken None=CancellationToken.None;
        const string H1="AcAd4c5d",H2="AsAh4h5h",Board="2c3d7h9sTc";
        static void Check(bool value,string message){checks++;if(!value)throw new Exception("Plot: "+message);}
        static void Near(double value,double expected,string message){Check(Math.Abs(value-expected)<1e-9,message+" ("+value+" vs "+expected+")");}
        static EquityRange Range(string s){return EquityRange.Parse(s,None);}
        static void Write(string path,double first,double second){File.WriteAllText(path,"hand,weight\n"+H1+","+first.ToString(System.Globalization.CultureInfo.InvariantCulture)+"\n"+H2+","+second.ToString(System.Globalization.CultureInfo.InvariantCulture));}
        static EquityJobRequest Request(string[] paths){return new EquityJobRequest{Board=Board,HeroMode=EquityHeroMode.AllActions,CommonScaleConfirmed=true,
            ActionPaths=(string[])paths.Clone(),ActionLabels=new[]{"check","bet 33%","bet 75%"},BetActions=new[]{false,true,true},
            VillainText="KcKd4h5h@25%,Jc8hKhQh@75%",Settings=new EquitySettings{Seed=123},
            Conditions=new[]{new EquityCondition{Label="AA",Syntax="AA"},new EquityCondition{Label="first",Syntax=H1},new EquityCondition{Label="second",Syntax=H2},new EquityCondition{Label="empty",Syntax="KK"}}};}
        public static void Run(){
            string dir=Path.Combine(Path.GetTempPath(),"plo-plot-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{
                var paths=new[]{Path.Combine(dir,"check.csv"),Path.Combine(dir,"small.csv"),Path.Combine(dir,"big.csv")};
                Write(paths[0],.2,.4);Write(paths[1],.3,.1);Write(paths[2],.5,0);
                var request=Request(paths);var copy=request.Snapshot();request.BetActions[1]=false;Check(copy.BetActions[1],"action selection snapshot isolation");request=copy;
                var job=EquityJob.Run(request,None,null);var points=EquityPlot.Points(job);
                Check(points.Length==9,"three valid syntax groups times all three actions; empty omitted");
                Near(job.Rows[0].Frequency.Percent.Sum(),100,"all action percentages sum to 100");
                Near(job.Rows[0].Frequency.Percent[1],100*.4/1.5,"all-action weights reproduce CSV aggregate small bet");
                Near(job.Rows[0].Frequency.Percent[2],100*.5/1.5,"all-action weights reproduce CSV aggregate big bet");
                Near(points.Single(p=>p.RowIndex==2&&p.ActionIndex==2).Frequency,0,"zero bet remains a plotted point");
                Check(points.Count(p=>p.ActionIndex==0)==3,"check is plotted for every valid group");
                Check(points.Where(p=>p.RowIndex==0).Select(p=>p.Equity).Distinct().Count()==1,"same EQ across all actions");
                Check(points.GroupBy(p=>p.RowIndex).All(g=>Math.Abs(g.Sum(p=>p.Frequency)-100)<1e-9),"plotted frequencies cover 100 percent for each condition");
                request.ActionLabels=new[]{"check","bet","fold"};var named=EquityPlot.Points(EquityJob.Run(request,None,null));Check(named.Any(p=>p.Action=="fold")&&named.Any(p=>p.Action=="check"),"arbitrary check/bet/fold labels appear without role selection");request=copy.Snapshot();request.ActionLabels=new[]{"check","bet 33%","bet 75%"};
                Check(points.All(p=>p.Exact&&p.CiLow==job.Rows[p.RowIndex].Result.CiLow&&p.CiHigh==job.Rows[p.RowIndex].Result.CiHigh),"CI and method preserved");
                Check(job.Rows[3].Frequency.Percent==null&&job.Rows[3].Result==null,"empty syntax no fabricated zero frequency");
                // Unequal file totals detect accidental per-file normalization. The check
                // file must contribute even though it is not selected as a bet series.
                var merged=EquityRange.FromActions(paths.Select((p,i)=>EquityInput.ReadAction(p,"action",Board,None).Range),true);
                Check(merged.Count==2,"same physical hand in three CSVs is a single merged combo");
                Near(merged.SelectSyntax(H1,None).TotalWeight,1,"first hand sums 0.2 + 0.3 + 0.5");
                Near(merged.SelectSyntax(H2,None).TotalWeight,.5,"second hand sums 0.4 + 0.1 + 0 without file normalization");
                var expectedPlan=EquityEngine.Prepare(Board,Range(H1+"@1,"+H2+"@0.5"),Range(request.VillainText),None,null);
                var expected=EquityEngine.Calculate(expectedPlan,new EquitySettings{Seed=123},None,null);
                Near(job.Rows[0].Result.Equity,expected.Equity,"EQ matches manually summed 2:1 pre-action range");
                var roles=request.Snapshot();roles.BetActions=new[]{true,false,false};
                Near(EquityJob.Run(roles,None,null).Rows[0].Result.Equity,expected.Equity,"changing bet selection cannot change hero range");
                roles.BetActions=new[]{false,false,false};
                Near(EquityJob.Run(roles,None,null).Rows[0].Result.Equity,expected.Equity,"all CSVs contribute even with no bet selection");
                File.WriteAllText(paths[2],"hand,weight\n5d4cAdAc,0.5\n"+H2+",0");
                Near(EquityJob.Run(request,None,null).Rows[0].Result.Equity,expected.Equity,"card order does not change cross-file hand identity");
                Write(paths[2],.5,0);
                var oldEQ=job.Rows[0].Result.Equity;
                Write(paths[0],.8,.1);Write(paths[1],.1,.2);Write(paths[2],.1,.2);
                var moved=EquityJob.Run(request,None,null);Near(moved.Rows[0].Result.Equity,oldEQ,"redistribution retaining each hand total cannot change EQ");
                Near(moved.Rows[0].Frequency.Percent[1],20,"redistribution changes small bet frequency");Near(moved.Rows[0].Frequency.Percent[2],20,"redistribution changes large bet frequency");
                Write(paths[0],.2,.4);Write(paths[1],.3,.1);Write(paths[2],.5,0);
                request.HeroMode=EquityHeroMode.InputRange;request.HeroText=H1+"@0.25,"+H2+"@0.75";
                var manual=EquityJob.Run(request,None,null);Near(manual.Rows[0].Frequency.Percent[1],22.5,"manual pre-action weights average per-hand strategy");Near(manual.Rows[0].Frequency.Percent[2],12.5,"manual pre-action weights big bet");
                request.VillainText="6c6d6h6s";var other=EquityJob.Run(request,None,null);Near(other.Rows[0].Frequency.Percent[1],22.5,"opponent range never changes own action frequency");
                request.HeroText=H1+"@1e-200,"+H2+"@3e-200";Near(EquityJob.Run(request,None,null).Rows[0].Frequency.Percent[1],22.5,"very small hero weights stable");
                request.HeroText=H1;request.CommonScaleConfirmed=false;job=EquityJob.Run(request,None,null);Check(job.Rows[0].Result!=null&&EquityPlot.Points(job).Length==0&&job.PlotStatus.Contains("共通"),"unconfirmed CSV scale preserves manual EQ but blocks plot");
                request.CommonScaleConfirmed=true;request.BetActions=new[]{false,false,false};job=EquityJob.Run(request,None,null);Check(job.Rows[0].Result!=null&&EquityPlot.Points(job).Length==6,"all actions plotted even when legacy bet selections are false");
                request.BetActions=new bool[0];job=EquityJob.Run(request,None,null);Check(EquityPlot.Points(job).Length==6,"current request needs no bet selection metadata");
                request=Request(paths);request.HeroMode=EquityHeroMode.InputRange;request.HeroText=H1+",AhAd4d5s";job=EquityJob.Run(request,None,null);
                Check(job.Rows[0].Result!=null&&job.Rows[0].Frequency.Percent==null&&job.Rows[0].Frequency.MissingHands==1,"missing action support retains EQ but blocks frequency");
                Check(!EquityPlot.Points(job).Any(p=>p.RowIndex==0)&&EquityPlot.Points(job).Any(p=>p.RowIndex==1),"unavailable group omitted while valid group survives");
                request=Request(paths);request.HeroMode=EquityHeroMode.Uniform;job=EquityJob.Run(request,None,null);Near(job.Rows[1].Frequency.Percent[1],30,"uniform fixed syntax strategy");Check(job.Rows[0].Frequency.Percent==null,"uniform AA cannot silently discard missing CSV hands");
                request=Request(paths);request.HeroMode=EquityHeroMode.InputRange;request.HeroText=H1;request.ActionPaths[1]=Path.Combine(dir,"missing.csv");job=EquityJob.Run(request,None,null);Check(job.Rows[0].Result!=null&&job.PlotStatus.Contains("散布図なし"),"missing plot CSV cannot destroy independently specified EQ");
                request=Request(paths);request.HeroMode=EquityHeroMode.InputRange;request.HeroText=H1;request.Settings.ExactRiver=false;request.Settings.Trials=1000;job=EquityJob.Run(request,None,null);
                Check(EquityPlot.Points(job).All(p=>!p.Exact&&p.CiLow<=p.Equity&&p.CiHigh>=p.Equity),"MC uncertainty reaches plotted data");
                var data=paths.Select((p,i)=>EquityInput.ReadAction(p,"same",Board,None)).ToArray();var plan=new EquityFrequencyPlan(data,None);
                var f=plan.Calculate(Range(H1+",2cAd4c5d"),EquityCards.Mask(EquityCards.Parse(Board,3,5)),None);Near(f.Percent[1],30,"board-blocked hero ignored for strategy");
                f=plan.Calculate(Range("2cAd4c5d"),EquityCards.Mask(EquityCards.Parse(Board,3,5)),None);Check(f.Percent==null,"all board-blocked is undefined rather than zero");
                Write(paths[0],0,0);Write(paths[1],1,1);Write(paths[2],0,0);request=Request(paths);job=EquityJob.Run(request,None,null);Near(job.Rows[0].Frequency.Percent[1],100,"100 percent boundary");Near(job.Rows[0].Frequency.Percent[2],0,"all-zero action CSV allowed");
                request.ActionLabels=new[]{"same","same","same"};job=EquityJob.Run(request,None,null);Check(EquityPlot.Points(job).Select(p=>p.ActionIndex).Distinct().Count()==3,"duplicate action labels remain three separate CSV series");
                var cts=new CancellationTokenSource();cts.Cancel();bool canceled=false;try{plan.Calculate(Range(H1),0,cts.Token);}catch(OperationCanceledException){canceled=true;}Check(canceled,"frequency cancellation");
                Check(EquityPlot.Points(null).Length==0,"cleared graph has no points");
                Console.WriteLine("EQ frequency + plot: "+checks+" checks passed.");
            }finally{Directory.Delete(dir,true);}
        }
    }
}
