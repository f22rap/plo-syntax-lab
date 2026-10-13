using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FlopCommands.Equity {
    public sealed class EquityFrequency {
        public double[] Percent;
        public int MissingHands;
        public string Status;
    }
    public sealed class EquityPlotPoint {
        public int RowIndex,ActionIndex;
        public string Label,Syntax,Action;
        public double Equity,Frequency,CiLow,CiHigh;
        public bool Exact;
    }
    // Each action CSV uses the same scale. Normalize across actions per hand,
    // then average using the chosen pre-action hero weights (never villain weights).
    public sealed class EquityFrequencyPlan {
        readonly Dictionary<long,double[]> strategy=new Dictionary<long,double[]>();
        readonly int count;
        public EquityFrequencyPlan(EquityActionData[] actions,CancellationToken token){
            count=actions.Length;
            for(int a=0;a<count;a++)foreach(var combo in actions[a].Range.Items){
                token.ThrowIfCancellationRequested();double[] weights;
                if(!strategy.TryGetValue(combo.Mask,out weights)){weights=new double[count];strategy.Add(combo.Mask,weights);}
                weights[a]=combo.Weight;
            }
            foreach(var weights in strategy.Values){token.ThrowIfCancellationRequested();double sum=weights.Sum();for(int a=0;a<count;a++)weights[a]/=sum;}
        }
        public EquityFrequency Calculate(EquityRange selected,long boardMask,CancellationToken token){
            var sums=new double[count];int missing=0,hands=0;
            // Scaling avoids underflow for valid, very small input range weights.
            double scale=selected.Items.Where(c=>(c.Mask&boardMask)==0).Select(c=>c.Weight).DefaultIfEmpty(0).Max(),total=0;
            foreach(var combo in selected.Items){
                token.ThrowIfCancellationRequested();if((combo.Mask&boardMask)!=0)continue;hands++;
                double weight=combo.Weight/scale;total+=weight;double[] values;
                if(!strategy.TryGetValue(combo.Mask,out values)){missing++;continue;}
                for(int a=0;a<count;a++)sums[a]+=weight*values[a];
            }
            if(hands==0)return new EquityFrequency{Status="頻度なし：ボードに対する合法ハンドがありません。"};
            if(missing>0)return new EquityFrequency{MissingHands=missing,Status="頻度なし：自分のレンジの "+missing.ToString("N0")+"ハンドにアクションCSVの情報がありません。"};
            return new EquityFrequency{Percent=sums.Select(s=>Math.Max(0,Math.Min(100,100*s/total))).ToArray(),Status="完了"};
        }
    }
    public static class EquityPlot {
        public static EquityPlotPoint[] Points(EquityJobResult job){
            if(job==null)return new EquityPlotPoint[0];var points=new List<EquityPlotPoint>();
            for(int i=0;i<job.Rows.Length;i++){
                var row=job.Rows[i];if(row.Result==null||row.Frequency==null||row.Frequency.Percent==null)continue;
                for(int a=0;a<row.Frequency.Percent.Length;a++)points.Add(new EquityPlotPoint{
                    RowIndex=i,ActionIndex=a,Label=row.Label,Syntax=row.Syntax,Action=job.Input.ActionLabels[a],
                    Equity=row.Result.Equity,Frequency=row.Frequency.Percent[a],CiLow=row.Result.CiLow,CiHigh=row.Result.CiHigh,Exact=row.Result.Exact});
            }
            return points.ToArray();
        }
    }
}
