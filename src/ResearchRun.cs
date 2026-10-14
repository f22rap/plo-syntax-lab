using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using FlopCommands.Equity;

namespace FlopCommands.Research {
    [DataContract] public sealed class ResearchRunSettings {
        [DataMember(EmitDefaultValue=false)] public bool? AhiOnly;
        [DataMember(EmitDefaultValue=false)] public string HighestRank;
        [DataMember] public bool AllowMissingDefinitions,AllowFrequencyOnly;
        [DataMember] public int Trials=50000;
        [DataMember] public uint Seed=7123;
        [DataMember] public string GroupBy="suit_pair";
    }
    [DataContract] public sealed class ResearchFeatures {
        public const string Version="flop-texture-v1";
        [DataMember] public string Suit,Pair,OtherRanks;
        [DataMember] public int Aces,High,Middle,Low;
        public static ResearchFeatures From(string board){var cards=EquityCards.Parse(board,3,3);var ranks=cards.Select(c=>c/4).ToArray();int suits=cards.Select(c=>c%4).Distinct().Count(),distinct=ranks.Distinct().Count();return new ResearchFeatures{Suit=suits==1?"モノトーン":suits==2?"ツートーン":"レインボー",Pair=distinct==3?"アンペア":distinct==2?"ペア":"トリップス",Aces=ranks.Count(r=>r==0),High=ranks.Count(r=>r>=1&&r<=4),Middle=ranks.Count(r=>r>=5&&r<=8),Low=ranks.Count(r=>r>=9),OtherRanks=string.Concat(ranks.Where(r=>r>0).OrderBy(r=>r).Select(r=>"AKQJT98765432"[r]))};}
        public string Group(string by){switch(by){case "suit":return Suit;case "pair":return Pair;case "suit_pair":return Suit+" / "+Pair;case "side_ranks":return "A "+Aces+"枚 / T〜K "+High+"枚 / 6〜9 "+Middle+"枚 / 2〜5 "+Low+"枚";default:throw new ArgumentException("未対応の分類です。");}}
    }
    [DataContract] public sealed class ResearchActionResult {
        [DataMember] public string Key,Label,SourcePath;
        [DataMember] public double? Frequency;
    }
    [DataContract] public sealed class ResearchRow {
        [DataMember] public string SpotId,Board,Node,ConditionId,Label,Syntax,Status,Reason;
        [DataMember] public ResearchFeatures Features;
        [DataMember] public int SelectedHands;
        [DataMember] public double? RangePercent;
        [DataMember] public EquityResult Equity;
        [DataMember] public List<ResearchActionResult> Actions=new List<ResearchActionResult>();
    }
    [DataContract] public sealed class ResearchRunResult {
        [DataMember] public int Schema=1;
        [DataMember] public string Id=Guid.NewGuid().ToString("N"),StartedUtc,FinishedUtc,Status="running",ClassificationVersion=ResearchFeatures.Version;
        [DataMember] public string EngineVersion="pre-action-joint-v1",ApplicationVersion="2026.10.14-research-run";
        [DataMember] public ResearchCatalog Catalog;
        [DataMember] public ResearchConditionSet ConditionSet;
        [DataMember] public ResearchRunSettings Settings;
        [DataMember(EmitDefaultValue=false)] public ResearchGenerationSnapshot Generation;
        [DataMember] public int TargetSpots,CompletedSpots,FilteredSpots;
        [DataMember] public double ElapsedMs;
        [DataMember] public List<ResearchRow> Rows=new List<ResearchRow>();
        [DataMember] public string Error="";
    }
    public sealed class ResearchSummary {
        public string Group,ConditionId,Label,ActionKey,Action;
        public int Target,Available,FrequencyCount,EquityCount,PairedCount;
        public double? FrequencyMean,FrequencyMin,FrequencyMax,EquityMean,RangeMean,PairedEquity,PairedFrequency;
        public string[] FrequencySpots,PairedSpots;
    }
    public sealed class ResearchProgress {public string Message;public int Completed,Total;}
    public sealed class ResearchPoint {public string Label,Syntax,ActionKey,Action,Details;public double Equity,Frequency;public double? CiLow,CiHigh;}
    public static class ResearchRun {
        public const string TotalBet="total_bet";
        public static string ActionKey(ResearchAction action){return action.Kind+(action.Kind=="bet"||action.Kind=="raise"?":"+(action.PotFraction=="allin"?"allin":decimal.Parse(action.PotFraction,CultureInfo.InvariantCulture).ToString("G29",CultureInfo.InvariantCulture)):"");}
        public static string ActionLabel(string key){if(key==TotalBet)return "総ベット";var a=key.Split(':');string name=a[0]=="check"?"Check":a[0]=="bet"?"Bet":a[0]=="fold"?"Fold":a[0]=="call"?"Call":"Raise";return a.Length==1?name:a[1]=="allin"?name+" All-in":name+" "+(decimal.Parse(a[1],CultureInfo.InvariantCulture)*100).ToString("0.####",CultureInfo.InvariantCulture)+"% pot";}
        public static void ValidateRequest(ResearchCatalog catalog,ResearchConditionSet set,ResearchRunSettings settings,ResearchGenerationSnapshot generation=null,bool prepareGeneration=false){
            ResearchValidation.Catalog(catalog);ResearchValidation.Set(set);if(settings==null||settings.Trials<1||settings.Trials>2000000)throw new ArgumentException("試行数は1〜2,000,000で指定してください。");ResearchFeatures.From("AsKd7c").Group(settings.GroupBy);
            var generated=prepareGeneration?null:ResearchGeneration.Validate(generation,catalog,set,settings);
            var checking=set;if(prepareGeneration){checking=ResearchJson.Clone(set);checking.Conditions.RemoveAll(c=>c.Kind=="generated");}
            var spots=Targets(catalog,settings).ToArray();int conditions=set.Conditions.Count(c=>c.Enabled);if(spots.Length==0||conditions==0)throw new ArgumentException("対象ボードと使用する条件を1つ以上選択してください。");
            if(spots.Length>200||(long)spots.Length*conditions>10000||(long)spots.Length*conditions*settings.Trials>100000000)throw new ArgumentException("一括調査の上限は200ボード・10,000条件行・合計1億試行です。対象か試行数を減らしてください。");
            foreach(var spot in spots){if(!settings.AllowFrequencyOnly&&spot.Opponents.Count==0)throw new ArgumentException(spot.Board+"：相手レンジが未登録です。頻度のみを許可するか、相手CSVを登録してください。");if(!settings.AllowMissingDefinitions){var missing=ResearchGeneration.Resolve(checking,spot.Board,generated).Where(c=>c.Status=="missing_definition").Select(c=>c.Label).ToArray();if(missing.Length>0)throw new ArgumentException(spot.Board+"：syntaxが未設定です（"+string.Join("、",missing)+"）。");}}
        }
        public static string BoardRank(ResearchCatalog catalog,ResearchRunSettings settings){return ResearchBoardFilter.Resolve(settings.HighestRank,settings.AhiOnly,catalog.HighestRank);}
        public static IEnumerable<ResearchSpot> Targets(ResearchCatalog catalog,ResearchRunSettings settings){string rank=BoardRank(catalog,settings);return catalog.Spots.Where(s=>ResearchBoardFilter.Matches(s.Board,rank));}
        static void Report(Action<ResearchProgress> progress,string message,int completed,int total){if(progress!=null)progress(new ResearchProgress{Message=message,Completed=completed,Total=total});}
        static void CheckSources(ResearchSpot spot,CancellationToken token){foreach(var source in spot.Actions.Select(a=>a.Source).Concat(spot.Opponents)){token.ThrowIfCancellationRequested();if(source.Sha256!=ResearchValidation.Hash(source.Path,token))throw new ArgumentException(spot.Board+"：登録後にCSVが変更されています。編集画面で再登録してください："+source.Path);}}
        static ResearchRow Row(ResearchSpot spot,ResolvedResearchCondition condition){return new ResearchRow{SpotId=spot.Id,Board=ResearchValidation.Board(spot.Board),Node=spot.Node.Trim(),ConditionId=condition.Id,Label=condition.Label,Syntax=condition.Syntax,Features=ResearchFeatures.From(spot.Board),Status=condition.Status=="ready"?"pending":condition.Status,Reason=condition.Status=="ready"?"":condition.Status=="empty_condition"?"このボードでは条件に該当する合法ハンドがありません。":"このボードのsyntaxが未設定です。",Actions=spot.Actions.Select(a=>new ResearchActionResult{Key=ActionKey(a),Label=a.Label,SourcePath=a.Source.Path}).Concat(new[]{new ResearchActionResult{Key=TotalBet,Label="総ベット",SourcePath=""}}).ToList()};}
        static uint Seed(uint seed,string syntax){unchecked{foreach(char c in syntax)seed=(seed^c)*16777619;}return seed;}
        public static ResearchRunResult Execute(ResearchCatalog catalog,ResearchConditionSet set,ResearchRunSettings settings,CancellationToken token,Action<ResearchProgress> progress,ResearchGenerationSnapshot generation=null){
            // Freeze definitions and registered hashes before any asynchronous work.
            catalog=ResearchJson.Clone(catalog);set=ResearchJson.Clone(set);settings=ResearchJson.Clone(settings);generation=generation==null?null:ResearchJson.Clone(generation);ValidateRequest(catalog,set,settings,generation);var generated=ResearchGeneration.Validate(generation,catalog,set,settings);
            settings.HighestRank=BoardRank(catalog,settings);
            var spots=Targets(catalog,settings).ToArray();var result=new ResearchRunResult{Schema=generation==null?1:2,Generation=generation,Catalog=catalog,ConditionSet=set,Settings=settings,StartedUtc=DateTime.UtcNow.ToString("o"),TargetSpots=spots.Length,FilteredSpots=catalog.Spots.Count-spots.Length};var watch=Stopwatch.StartNew();
            try{
                // Fail before calculation if any selected source changed since registration.
                for(int i=0;i<spots.Length;i++){Report(progress,spots[i].Board+"：入力CSVの変更を確認中",0,spots.Length);CheckSources(spots[i],token);}
                for(int i=0;i<spots.Length;i++){
                    token.ThrowIfCancellationRequested();var spot=spots[i];Report(progress,spot.Board+"：レンジを読込中",i,spots.Length);CheckSources(spot,token);
                    var rows=ResearchGeneration.Resolve(set,spot.Board,generated).Select(c=>Row(spot,c)).ToList();
                    if(generated!=null){var reasons=generated.Rows.Where(r=>r.Board==ResearchValidation.Board(spot.Board)).ToDictionary(r=>r.Id,r=>r.Reason);foreach(var row in rows.Where(r=>r.Status=="empty_condition"))row.Reason=reasons[row.ConditionId];}
                    try{
                        var datasets=spot.Actions.Select(a=>EquityInput.ReadAction(a.Source.Path,a.Label,spot.Board,token)).ToArray();var hero=EquityRange.FromActions(datasets.Select(d=>d.Range),true);var frequency=new EquityFrequencyPlan(datasets,token);EquityRange villain=null;
                        if(spot.Opponents.Count>0){var imported=EquityInput.ImportRangeFiles(spot.Opponents.Select(s=>s.Path).ToArray(),spot.OpponentPercentUnit,token);villain=EquityRange.Parse(imported.Text,token);}
                        CheckSources(spot,token);long boardMask=EquityCards.Mask(EquityCards.Parse(spot.Board,3,3));double scale=hero.Items.Select(c=>c.Weight).DefaultIfEmpty(1).Max(),total=hero.Items.Sum(c=>c.Weight/scale);var cache=new Dictionary<string,ResearchRow>();
                        foreach(var row in rows){
                            token.ThrowIfCancellationRequested();if(row.Status=="missing_definition"||row.Status=="empty_condition")continue;Report(progress,spot.Board+" · "+row.Label+"：計算中",i,spots.Length);ResearchRow previous;
                            if(cache.TryGetValue(row.Syntax,out previous)){row.SelectedHands=previous.SelectedHands;row.RangePercent=previous.RangePercent;row.Equity=previous.Equity;row.Status=previous.Status;row.Reason=previous.Reason;for(int a=0;a<row.Actions.Count;a++)row.Actions[a].Frequency=previous.Actions[a].Frequency;continue;}
                            var selected=hero.SelectSyntax(row.Syntax,token);row.SelectedHands=selected.Count;row.RangePercent=total>0?Math.Max(0,Math.Min(100,100*selected.Items.Sum(c=>c.Weight/scale)/total)):(double?)null;
                            if(selected.Count==0){row.Status="no_matching_hands";row.Reason="自分のアクション前レンジに該当ハンドがありません。";cache[row.Syntax]=row;continue;}
                            var f=frequency.Calculate(selected,boardMask,token);if(f.Percent==null)throw new ArgumentException(f.Status);
                            for(int a=0;a<spot.Actions.Count;a++)row.Actions[a].Frequency=f.Percent[a];row.Actions.Last().Frequency=Math.Max(0,Math.Min(100,spot.Actions.Select((a,index)=>a.Kind=="bet"?f.Percent[index]:0).Sum()));
                            if(villain==null){row.Status="frequency_only";row.Reason="相手レンジ未登録。EQは未計算です。";}
                            else try{var plan=EquityEngine.Prepare(spot.Board,selected,villain,token,null);row.Equity=EquityEngine.Calculate(plan,new EquitySettings{Trials=settings.Trials,Seed=Seed(settings.Seed,row.Syntax),ExactRiver=false},token,null);row.Status="completed";row.Reason="";}
                            catch(ArgumentException ex){row.Status="equity_unavailable";row.Reason=ex.Message;}
                            cache[row.Syntax]=row;
                        }
                        // Do not publish numbers based on files modified while calculating.
                        CheckSources(spot,token);
                    }catch(OperationCanceledException){throw;}
                    catch(Exception ex){if(!(ex is ArgumentException||ex is System.IO.IOException||ex is UnauthorizedAccessException))throw;foreach(var row in rows.Where(r=>r.Status!="missing_definition"&&r.Status!="empty_condition")){row.Status="failed";row.Reason=ex.Message;row.Equity=null;row.RangePercent=null;row.SelectedHands=0;foreach(var a in row.Actions)a.Frequency=null;}}
                    result.Rows.AddRange(rows);result.CompletedSpots++;Report(progress,spot.Board+"：完了",result.CompletedSpots,spots.Length);
                }
                token.ThrowIfCancellationRequested();result.Status=result.Rows.Any(r=>r.Status=="failed"||r.Status=="equity_unavailable"||r.Status=="missing_definition")?"completed_with_gaps":"completed";
            }catch(OperationCanceledException){result.Status="cancelled";result.Error="中止されました。確定済みボードだけを保持しています。";}
            catch(Exception ex){if(!(ex is ArgumentException||ex is System.IO.IOException||ex is UnauthorizedAccessException))throw;result.Status="failed";result.Error=ex.Message;}
            result.FinishedUtc=DateTime.UtcNow.ToString("o");result.ElapsedMs=watch.Elapsed.TotalMilliseconds;return result;
        }
        static double? Average(IEnumerable<double> values){var a=values.ToArray();return a.Length==0?(double?)null:a.Average();}
        public static ResearchSummary[] Summaries(ResearchRunResult result){
            var summaries=new List<ResearchSummary>();
            foreach(var target in Targets(result.Catalog,result.Settings).GroupBy(s=>new{Group=ResearchFeatures.From(s.Board).Group(result.Settings.GroupBy),Node=s.Node.Trim()})){
                var planned=target.ToArray();var ids=new HashSet<string>(planned.Select(s=>s.Id));
                foreach(var condition in result.ConditionSet.Conditions.Where(c=>c.Enabled)){
                    var rows=result.Rows.Where(r=>ids.Contains(r.SpotId)&&r.ConditionId==condition.Id).ToArray();
                    foreach(string key in planned.SelectMany(s=>s.Actions.Select(ActionKey)).Concat(new[]{TotalBet}).Distinct().OrderBy(k=>k,StringComparer.Ordinal)){
                        var available=rows.Where(r=>r.Actions.Any(a=>a.Key==key)).ToArray();var withFrequency=available.Where(r=>r.Actions.Single(a=>a.Key==key).Frequency.HasValue).ToArray();var paired=withFrequency.Where(r=>r.Equity!=null).ToArray();var freq=withFrequency.Select(r=>r.Actions.Single(a=>a.Key==key).Frequency.Value).ToArray();
                        summaries.Add(new ResearchSummary{Group=target.Key.Node+" | "+target.Key.Group,ConditionId=condition.Id,Label=condition.Label,ActionKey=key,Action=ActionLabel(key),Target=planned.Length,Available=planned.Count(s=>key==TotalBet||s.Actions.Any(a=>ActionKey(a)==key)),FrequencyCount=freq.Length,EquityCount=available.Count(r=>r.Equity!=null),PairedCount=paired.Length,FrequencyMean=Average(freq),FrequencyMin=freq.Length==0?(double?)null:freq.Min(),FrequencyMax=freq.Length==0?(double?)null:freq.Max(),EquityMean=Average(available.Where(r=>r.Equity!=null).Select(r=>r.Equity.Equity)),RangeMean=Average(available.Where(r=>r.RangePercent.HasValue).Select(r=>r.RangePercent.Value)),PairedEquity=Average(paired.Select(r=>r.Equity.Equity)),PairedFrequency=Average(paired.Select(r=>r.Actions.Single(a=>a.Key==key).Frequency.Value)),FrequencySpots=withFrequency.Select(r=>r.SpotId).ToArray(),PairedSpots=paired.Select(r=>r.SpotId).ToArray()});
                    }
                }
            }return summaries.ToArray();
        }
        public static ResearchPoint[] Points(ResearchRunResult result,bool grouped){
            if(result==null)return new ResearchPoint[0];if(grouped)return Summaries(result).Where(s=>s.ActionKey!=TotalBet&&s.PairedCount>0).Select(s=>new ResearchPoint{Label=s.Group+" · "+s.Label,Syntax="ボードごとの定義はボード別表・JSONを参照",ActionKey=s.ActionKey,Action=s.Action,Equity=s.PairedEquity.Value,Frequency=s.PairedFrequency.Value,Details="同じ "+s.PairedCount+" ボードの等重み平均。カテゴリの信頼区間は未算出。\n対象スポット："+string.Join(", ",s.PairedSpots)}).ToArray();
            return result.Rows.Where(r=>r.Equity!=null).SelectMany(r=>r.Actions.Where(a=>a.Key!=TotalBet&&a.Frequency.HasValue).Select(a=>new ResearchPoint{Label=r.Board+" · "+r.Label,Syntax=r.Syntax,ActionKey=a.Key,Action=ActionLabel(a.Key),Equity=r.Equity.Equity,Frequency=a.Frequency.Value,CiLow=r.Equity.CiLow,CiHigh=r.Equity.CiHigh,Details="ノード："+r.Node+"\nCSV："+a.SourcePath+"\nEQ 95%区間："+r.Equity.CiLow.ToString("F2")+"–"+r.Equity.CiHigh.ToString("F2")+"%"})).ToArray();
        }
    }
}
