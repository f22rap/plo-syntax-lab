using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;

namespace FlopCommands.Equity {
    public enum EquityHeroMode { Uniform, InputRange, AllActions }
    public sealed class EquityCondition { public string Label, Syntax; }
    public sealed class EquityJobRequest {
        public string Board, HeroText, VillainText;
        public EquityHeroMode HeroMode;
        public bool CommonScaleConfirmed;
        // Legacy archive metadata only; every action is now plotted.
        public bool[] BetActions=new bool[0];
        public EquityCondition[] Conditions;
        public string[] RangeInputPaths=new string[0];
        public string[] ActionPaths=new string[0], ActionLabels=new string[0];
        public EquitySettings Settings=new EquitySettings();
        public EquityJobRequest Snapshot(){return new EquityJobRequest{Board=Board,HeroText=HeroText,VillainText=VillainText,HeroMode=HeroMode,CommonScaleConfirmed=CommonScaleConfirmed,RangeInputPaths=(string[])RangeInputPaths.Clone(),BetActions=(bool[])BetActions.Clone(),
            Conditions=Conditions==null?null:Conditions.Select(c=>new EquityCondition{Label=c.Label,Syntax=c.Syntax}).ToArray(),ActionPaths=(string[])ActionPaths.Clone(),ActionLabels=(string[])ActionLabels.Clone(),
            Settings=new EquitySettings{Trials=Settings.Trials,Seed=Settings.Seed,ExactRiver=Settings.ExactRiver,MaxExactComparisons=Settings.MaxExactComparisons}};}
    }
    public sealed class EquityRow { public string Label,Syntax,Status; public EquityResult Result; public EquityFrequency Frequency; }
    public sealed class EquityJobResult { public EquityJobRequest Input; public uint BaseSeed; public EquityRow[] Rows; public double ElapsedMs; public string PlotStatus; }
    public sealed class EquityJobProgress { public string Message; public int ConditionIndex,ConditionCount; public EquityProgress Calculation; }
    public static class EquityJob {
        public static EquityJobResult Run(EquityJobRequest input,CancellationToken token,Action<EquityJobProgress> progress){
            if(input==null)throw new ArgumentNullException("input");var request=input.Snapshot();var watch=Stopwatch.StartNew();token.ThrowIfCancellationRequested();
            request.Board=EquityCards.Text(EquityCards.Parse(request.Board,3,5));
            if(request.Conditions==null||request.Conditions.Length==0||request.Conditions.Length>1000)throw new ArgumentException("集計タブで1〜1,000条件を選択してください。");
            if(request.Conditions.Any(c=>string.IsNullOrWhiteSpace(c.Label)||string.IsNullOrWhiteSpace(c.Syntax)))throw new ArgumentException("条件名またはsyntaxが空です。");
            if(request.Settings.Trials<1||request.Settings.Trials>2000000)throw new ArgumentException("試行数は1〜2,000,000で指定してください。");
            Report(progress,"アクション前レンジを読み込み中",0,request.Conditions.Length,null);
            bool wantsPlot=request.ActionPaths.Length>0;string plotStatus="散布図：集計に使うアクションCSVを選択してください。";
            EquityActionData[] datasets=null;EquityFrequencyPlan frequencies=null;
            if(request.HeroMode==EquityHeroMode.AllActions||wantsPlot){
                try{
                    if(!request.CommonScaleConfirmed)throw new ArgumentException("全アクション・共通weight基準の確認が必要です。");
                    if(request.ActionPaths.Length<2||request.ActionPaths.Length>3||request.ActionLabels.Length!=request.ActionPaths.Length)throw new ArgumentException("集計タブで全アクションのCSVを2〜3個選択してください。");
                    if(request.ActionPaths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=request.ActionPaths.Length)throw new ArgumentException("同じCSVを複数指定することはできません。");
                    datasets=request.ActionPaths.Select((p,i)=>EquityInput.ReadAction(p,request.ActionLabels[i],request.Board,token)).ToArray();
                }catch(Exception ex){
                    if(ex is OperationCanceledException||request.HeroMode==EquityHeroMode.AllActions)throw;
                    if(!(ex is ArgumentException||ex is IOException||ex is UnauthorizedAccessException))throw;
                    plotStatus="散布図なし："+ex.Message;
                }
            }
            EquityRange hero;
            if(request.HeroMode==EquityHeroMode.Uniform)hero=EquityRange.Parse("random",token);
            else if(request.HeroMode==EquityHeroMode.InputRange)hero=EquityRange.Parse(request.HeroText,token);
            else if(request.HeroMode==EquityHeroMode.AllActions)hero=EquityRange.FromActions(datasets.Select(d=>d.Range),true);
            else throw new ArgumentException("自分のレンジ指定方法が不正です。");
            if(wantsPlot&&datasets!=null){
                frequencies=new EquityFrequencyPlan(datasets,token);plotStatus="全CSVのアクション頻度を色分け表示。同じ条件のEQは共通。横線はEQの95%区間。";
            }
            long boardMask=EquityCards.Mask(EquityCards.Parse(request.Board,3,5));
            var villain=EquityRange.Parse(request.VillainText,token);
            uint seed;if(request.Settings.Seed.HasValue)seed=request.Settings.Seed.Value;else{var bytes=new byte[4];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);seed=BitConverter.ToUInt32(bytes,0);}
            var rows=new List<EquityRow>();var results=new Dictionary<string,EquityResult>();var frequencyResults=new Dictionary<string,EquityFrequency>();
            for(int index=0;index<request.Conditions.Length;index++){
                token.ThrowIfCancellationRequested();var condition=request.Conditions[index];var row=new EquityRow{Label=condition.Label,Syntax=condition.Syntax};
                int current=index;var timer=Stopwatch.StartNew();
                Action<EquityProgress> notify=p=>{if(timer.ElapsedMilliseconds>=100||p.Completed==p.Target){Report(progress,condition.Label,current,request.Conditions.Length,p);timer.Restart();}};
                Report(progress,condition.Label,index,request.Conditions.Length,null);
                try{
                    // Reuse the same syntax-filtered range for both frequency and EQ.
                    EquityRange selected=null;
                    if(frequencies!=null){
                        EquityFrequency frequency;
                        if(!frequencyResults.TryGetValue(condition.Syntax,out frequency)){
                            selected=hero.SelectSyntax(condition.Syntax,token);frequency=frequencies.Calculate(selected,boardMask,token);frequencyResults[condition.Syntax]=frequency;
                        }
                        row.Frequency=frequency;
                    }
                    EquityResult result;
                    if(!results.TryGetValue(condition.Syntax,out result)){
                        if(selected==null)selected=hero.SelectSyntax(condition.Syntax,token);
                        if(selected.Count==0){row.Status="該当なし：アクション前レンジにこのsyntaxのハンドがありません。";rows.Add(row);continue;}
                        var plan=EquityEngine.Prepare(request.Board,selected,villain,token,notify);
                        uint derived=seed;unchecked{foreach(char c in condition.Syntax)derived=(derived^c)*16777619;}
                        var settings=new EquitySettings{Trials=request.Settings.Trials,ExactRiver=request.Settings.ExactRiver,Seed=derived,MaxExactComparisons=request.Settings.MaxExactComparisons};
                        result=EquityEngine.Calculate(plan,settings,token,notify);results[condition.Syntax]=result;
                    }
                    row.Result=result;row.Status="完了";
                }catch(ArgumentException ex){row.Status=ex.Message;}
                rows.Add(row);
            }
            token.ThrowIfCancellationRequested();Report(progress,"完了",request.Conditions.Length,request.Conditions.Length,null);
            return new EquityJobResult{Input=request,Rows=rows.ToArray(),BaseSeed=seed,ElapsedMs=watch.Elapsed.TotalMilliseconds,PlotStatus=plotStatus};
        }
        static void Report(Action<EquityJobProgress> progress,string message,int index,int count,EquityProgress calculation){if(progress!=null)progress(new EquityJobProgress{Message=message,ConditionIndex=index,ConditionCount=count,Calculation=calculation});}
    }
}
