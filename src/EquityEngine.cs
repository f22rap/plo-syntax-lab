using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PloSyntax;

namespace FlopCommands.Equity {
    // Card IDs deliberately match SyntaxMatcher: A..2, then s/h/d/c.
    public static class EquityCards {
        public static int[] Parse(string text, int min, int max) {
            string s = Regex.Replace(text ?? "", @"[\s,\-]+", "").Replace("10", "T")
                .Replace("♠", "s").Replace("♥", "h").Replace("♦", "d").Replace("♣", "c");
            if (s.Length % 2 != 0 || s.Length / 2 < min || s.Length / 2 > max)
                throw new ArgumentException("カード枚数が正しくありません。");
            int[] cards = new int[s.Length / 2]; long mask = 0;
            for (int i = 0; i < cards.Length; i++) {
                int r = "AKQJT98765432".IndexOf(char.ToUpperInvariant(s[2*i]));
                int suit = "shdc".IndexOf(char.ToLowerInvariant(s[2*i+1]));
                if (r < 0 || suit < 0) throw new ArgumentException("カード表記が正しくありません。");
                int c = r * 4 + suit; long bit = 1L << c;
                if ((mask & bit) != 0) throw new ArgumentException("同じカードが重複しています。");
                mask |= bit; cards[i] = c;
            }
            return cards;
        }
        public static long Mask(int[] cards) {
            if (cards == null) throw new ArgumentNullException("cards");
            long mask = 0;
            foreach (int c in cards) {
                if (c < 0 || c >= 52 || (mask & (1L << c)) != 0)
                    throw new ArgumentException("不正または重複したカードです。");
                mask |= 1L << c;
            }
            return mask;
        }
        public static string Text(int[] cards) {
            return string.Concat(cards.Select(c => "AKQJT98765432"[c/4].ToString() + "shdc"[c%4]));
        }
    }

    public sealed class EquityCombo {
        internal readonly int[] Cards;
        public long Mask { get; private set; }
        public double Weight { get; private set; }
        public string Hand { get { return EquityCards.Text(Cards); } }
        public EquityCombo(string hand, double weight) : this(EquityCards.Parse(hand, 4, 4), weight) {}
        public EquityCombo(int[] cards, double weight) {
            if (cards == null || cards.Length != 4) throw new ArgumentException("手札は4枚必要です。");
            if (double.IsNaN(weight) || double.IsInfinity(weight) || weight <= 0)
                throw new ArgumentException("重みは有限の正数で指定してください。");
            Cards = (int[])cards.Clone(); Array.Sort(Cards); Mask = EquityCards.Mask(Cards); Weight = weight;
        }
    }

    public sealed class EquityRange {
        internal readonly EquityCombo[] Items;
        public int Count { get { return Items.Length; } }
        public double TotalWeight { get; private set; }
        public EquityCombo[] GetCombos() { return (EquityCombo[])Items.Clone(); }
        // Repeated range declarations use maximum weight, never addition.
        public EquityRange(IEnumerable<EquityCombo> combos) {
            if (combos == null) throw new ArgumentNullException("combos");
            var map = new Dictionary<long, EquityCombo>();
            foreach (var c in combos) {
                if (c == null) throw new ArgumentException("空のコンボです。");
                EquityCombo previous;
                if (!map.TryGetValue(c.Mask, out previous) || previous.Weight < c.Weight) map[c.Mask] = c;
            }
            Items = map.Values.OrderBy(c => c.Mask).ToArray();
            TotalWeight = Items.Sum(c => c.Weight);
            if (double.IsInfinity(TotalWeight)) throw new ArgumentException("重み総和が大きすぎます。");
        }
        public EquityRange SelectSyntax(string syntax, CancellationToken cancellation) {
            var matcher = new SyntaxMatcher(syntax); var selected = new List<EquityCombo>();
            for (int i = 0; i < Items.Length; i++) {
                if ((i & 1023) == 0) cancellation.ThrowIfCancellationRequested();
                if (matcher.Matches(Items[i].Mask)) selected.Add(Items[i]);
            }
            return new EquityRange(selected);
        }
        // Only use datasets exported with a shared weight scale and all actions.
        // This operation does NOT normalize each action independently.
        public static EquityRange FromActions(IEnumerable<EquityRange> actions, bool commonScaleConfirmed) {
            if (!commonScaleConfirmed) throw new ArgumentException("全アクションが共通のweight基準であることを確認してください。別々に正規化されている場合はアクション前レンジを入力してください。");
            if (actions == null) throw new ArgumentNullException("actions");
            var sum = new Dictionary<long, double>(); var cards = new Dictionary<long, EquityCombo>();
            foreach (var action in actions) {
                if (action == null) throw new ArgumentException("空のアクションレンジです。");
                foreach (var c in action.Items) {
                    double old; sum.TryGetValue(c.Mask, out old); sum[c.Mask] = old + c.Weight; cards[c.Mask] = c;
                }
            }
            return new EquityRange(sum.Select(p => new EquityCombo(cards[p.Key].Cards, p.Value)));
        }
        sealed class Rule {
            public int[] Ranks; public string Suit; public bool Exclude;
            public double Weight; public long Exact;
            public bool Matches(int a, int b, int c, int d) {
                int[] cs = { a, b, c, d };
                if (Exact != 0) return Exact == ((1L<<a)|(1L<<b)|(1L<<c)|(1L<<d));
                if (Ranks != null) {
                    int used = 0;
                    foreach (int r in Ranks) {
                        int i; for (i = 0; i < 4; i++) if ((used & (1<<i)) == 0 && cs[i]/4 == r) { used |= 1<<i; break; }
                        if (i == 4) return false;
                    }
                }
                if (Suit == null) return true;
                int[] counts = new int[4]; foreach (int card in cs) counts[card%4]++; Array.Sort(counts);
                return Suit == "ds" ? counts[3] == 2 && counts[2] == 2 :
                    Suit == "ss" ? counts[3] == 2 && counts[2] == 1 :
                    Suit == "r" ? counts[3] == 1 : Suit == "ts" ? counts[3] == 3 : counts[3] == 4;
            }
        }
        // Equity Lab's simple range language is separate from the hero syntax language.
        public static EquityRange Parse(string text, CancellationToken cancellation) {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("レンジを入力してください。");
            var rules = new List<Rule>(); var exact = new Dictionary<long, double>(); var excluded = new HashSet<long>();
            bool hasInclude = false;
            foreach (string source in Regex.Split(text, @"[,;\r\n]+")) {
                cancellation.ThrowIfCancellationRequested();
                string token = source.Trim(); if (token.Length == 0) continue;
                var rule = new Rule { Weight = 1, Exclude = token.StartsWith("!") };
                if (rule.Exclude) token = token.Substring(1).Trim(); else hasInclude = true;
                string[] weighted = token.Split('@');
                if (weighted.Length > 2 || (weighted.Length == 2 && rule.Exclude)) throw new ArgumentException("重み指定が正しくありません。");
                if (weighted.Length == 2) {
                    string w = weighted[1].Trim(); bool percent = w.EndsWith("%");
                    if (percent) w = w.Substring(0, w.Length-1);
                    double value;
                    if (!double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) throw new ArgumentException("重みを数値で指定してください。");
                    rule.Weight = percent ? value / 100 : value;
                    if (double.IsNaN(rule.Weight) || rule.Weight <= 0 || rule.Weight > 1) throw new ArgumentException("手入力の重みは0より大きく1以下で指定してください。");
                }
                string pattern = Regex.Replace(weighted[0], @"\s+", "").Replace("10", "T");
                if (Regex.IsMatch(pattern, @"^(?:[2-9TJQKA][shdc]){4}$", RegexOptions.IgnoreCase)) {
                    rule.Exact = EquityCards.Mask(EquityCards.Parse(pattern, 4, 4));
                    if (rule.Exclude) excluded.Add(rule.Exact);
                    else { double old; exact.TryGetValue(rule.Exact, out old); exact[rule.Exact] = Math.Max(old, rule.Weight); }
                    continue;
                }
                string[] qualified = pattern.Split(':');
                if (qualified.Length > 2) throw new ArgumentException("未対応のレンジ構文です。");
                if (qualified.Length == 2) {
                    rule.Suit = qualified[1].ToLowerInvariant();
                    if (!new[] {"ds","ss","r","ts","mono"}.Contains(rule.Suit)) throw new ArgumentException("スート条件が正しくありません。");
                }
                string p = qualified[0].ToUpperInvariant();
                if (p != "RANDOM" && p != "ANY" && p != "*") {
                    if (!Regex.IsMatch(p, @"^[2-9TJQKA*X]{4}$")) throw new ArgumentException("4文字のランクパターンか具体的な4枚を指定してください。");
                    rule.Ranks = p.Where(ch => ch != '*' && ch != 'X').Select(ch => "AKQJT98765432".IndexOf(ch)).ToArray();
                }
                rules.Add(rule);
            }
            if (!hasInclude) throw new ArgumentException("含めるレンジを1つ以上指定してください。");
            var result = new List<EquityCombo>(); int n = 0;
            for (int a=0;a<49;a++) for (int b=a+1;b<50;b++) for (int c=b+1;c<51;c++) for (int d=c+1;d<52;d++) {
                if ((n++ & 1023) == 0) cancellation.ThrowIfCancellationRequested();
                long mask=(1L<<a)|(1L<<b)|(1L<<c)|(1L<<d); if (excluded.Contains(mask)) continue;
                double weight; exact.TryGetValue(mask, out weight); bool omit = false;
                foreach (var rule in rules) if (rule.Matches(a,b,c,d)) {
                    if (rule.Exclude) { omit = true; break; } weight = Math.Max(weight, rule.Weight);
                }
                if (!omit && weight > 0) result.Add(new EquityCombo(new[] {a,b,c,d}, weight));
            }
            return new EquityRange(result);
        }
    }

    // One evaluator per job: the rank cache and scratch arrays are not shared across threads.
    public sealed class OmahaEvaluator {
        readonly uint[] rankCache = new uint[371293]; // 13^5, zero means not populated
        readonly int[] count = new int[15];
        static uint Encode(int category, int a, int b, int c, int d, int e) {
            return (uint)(((((category*15+a)*15+b)*15+c)*15+d)*15+e);
        }
        internal uint Five(int a, int b, int c, int d, int e) {
            int r1=14-a/4,r2=14-b/4,r3=14-c/4,r4=14-d/4,r5=14-e/4;
            int key=((((a/4*13+b/4)*13+c/4)*13+d/4)*13+e/4);
            uint score=rankCache[key];
            if (score == 0) {
                Array.Clear(count,0,15); count[r1]++;count[r2]++;count[r3]++;count[r4]++;count[r5]++;
                int mask=(1<<r1)|(1<<r2)|(1<<r3)|(1<<r4)|(1<<r5), straight=0;
                for (int high=14;high>=6;high--) if ((mask & (31<<(high-4))) == (31<<(high-4))) { straight=high;break; }
                if (straight == 0 && (mask & 16444) == 16444) straight=5; // A2345
                int four=0,three=0,p1=0,p2=0,k1=0,k2=0,k3=0,k4=0,k5=0;
                for (int r=14;r>=2;r--) {
                    if(count[r]==4)four=r; else if(count[r]==3)three=r;
                    else if(count[r]==2){if(p1==0)p1=r;else p2=r;}
                    else if(count[r]==1){if(k1==0)k1=r;else if(k2==0)k2=r;else if(k3==0)k3=r;else if(k4==0)k4=r;else k5=r;}
                }
                score=four>0?Encode(7,four,k1,0,0,0):three>0&&p1>0?Encode(6,three,p1,0,0,0):
                    straight>0?Encode(4,straight,0,0,0,0):three>0?Encode(3,three,k1,k2,0,0):
                    p2>0?Encode(2,p1,p2,k1,0,0):p1>0?Encode(1,p1,k1,k2,k3,0):Encode(0,k1,k2,k3,k4,k5);
                rankCache[key]=score+1;
            } else score--;
            if ((a&3)==(b&3)&&(a&3)==(c&3)&&(a&3)==(d&3)&&(a&3)==(e&3)) {
                if (score / 759375 == 4) return score + 4*759375;
                return score + 5*759375; // Distinct same-suit cards cannot pair.
            }
            return score;
        }
        internal uint Score(int[] hand, int[] board) {
            uint best=0;
            for(int a=0;a<3;a++)for(int b=a+1;b<4;b++)
                for(int c=0;c<3;c++)for(int d=c+1;d<4;d++)for(int e=d+1;e<5;e++)
                    best=Math.Max(best,Five(hand[a],hand[b],board[c],board[d],board[e]));
            return best;
        }
        public uint Evaluate(string hand, string board) {
            int[] h=EquityCards.Parse(hand,4,4),b=EquityCards.Parse(board,5,5);
            if((EquityCards.Mask(h)&EquityCards.Mask(b))!=0)throw new ArgumentException("手札とボードが重複しています。");
            return Score(h,b);
        }
    }

    public sealed class EquitySettings {
        public int Trials = 50000;
        public uint? Seed;
        public bool ExactRiver = true;
        // Bound candidate inspections, not only legal pairs: a heavily blocked domain can be costly too.
        public long MaxExactComparisons = 5000000;
    }
    public sealed class EquityProgress {
        public string Phase;
        public long Completed, Target, Attempts;
        public double? Equity;
    }
    public sealed class EquityResult {
        public string CalculationVersion = "pre-action-joint-v1";
        public double Equity, VillainEquity, Win, Tie, Loss, CiLow, CiHigh, Margin, ElapsedMs;
        public bool Exact;
        public uint Seed;
        public long Total, Attempts, LegalPairs, CandidatePairs;
        public int HeroRawCount, VillainRawCount, HeroBoardBlocked, VillainBoardBlocked, HeroNoOpponent, VillainNoOpponent;
        public int HeroCount, VillainCount;
    }
    public sealed class EquityPlan {
        internal readonly EquityCombo[] Hero, Villain;
        internal readonly int[] Board;
        internal readonly long BoardMask;
        public int HeroRawCount {get;private set;}
        public int VillainRawCount {get;private set;}
        public int HeroBoardBlocked {get;private set;}
        public int VillainBoardBlocked {get;private set;}
        public int HeroNoOpponent {get;private set;}
        public int VillainNoOpponent {get;private set;}
        public int HeroCount {get{return Hero.Length;}}
        public int VillainCount {get{return Villain.Length;}}
        public long LegalPairs {get;private set;}
        public long CandidatePairs {get{return (long)Hero.Length*Villain.Length;}}
        internal EquityPlan(string board, EquityRange hero, EquityRange villain, CancellationToken token, Action<EquityProgress> progress) {
            if(hero==null||villain==null)throw new ArgumentNullException("range");
            Board=EquityCards.Parse(board,3,5);BoardMask=EquityCards.Mask(Board);
            HeroRawCount=hero.Count;VillainRawCount=villain.Count;
            var h=hero.Items.Where(c=>(c.Mask&BoardMask)==0).ToArray();
            Villain=villain.Items.Where(c=>(c.Mask&BoardMask)==0).ToArray();
            HeroBoardBlocked=hero.Count-h.Length;VillainBoardBlocked=villain.Count-Villain.Length;
            if(h.Length==0||Villain.Length==0)throw new ArgumentException("ボード除外後のレンジが空です。");
            // Inclusion-exclusion with integer counts gives exact existence/pair counts;
            // do NOT subtract floating-point weight sums (tiny valid weights could vanish).
            var subsets=new Dictionary<long,int>();
            for(int i=0;i<Villain.Length;i++) {
                if((i&1023)==0){token.ThrowIfCancellationRequested();Report(progress,"prepare",i,Villain.Length+h.Length);}
                var c=Villain[i];for(int bits=1;bits<16;bits++) {long key=0;for(int j=0;j<4;j++)if((bits&(1<<j))!=0)key|=1L<<c.Cards[j];int count;subsets.TryGetValue(key,out count);subsets[key]=count+1;}
            }
            var valid=new List<EquityCombo>();
            for(int i=0;i<h.Length;i++) {
                if((i&1023)==0){token.ThrowIfCancellationRequested();Report(progress,"prepare",Villain.Length+i,Villain.Length+h.Length);}
                var c=h[i];int compatible=Villain.Length;
                for(int bits=1;bits<16;bits++){long key=0;int parity=0;for(int j=0;j<4;j++)if((bits&(1<<j))!=0){key|=1L<<c.Cards[j];parity++;}int count;subsets.TryGetValue(key,out count);compatible+=(parity%2==0?count:-count);}
                if(compatible>0){valid.Add(c);LegalPairs+=compatible;}
            }
            Hero=valid.ToArray();HeroNoOpponent=h.Length-Hero.Length;
            if(LegalPairs==0)throw new ArgumentException("カードが衝突しない対戦組がありません。");
            token.ThrowIfCancellationRequested();Report(progress,"prepare",Villain.Length+h.Length,Villain.Length+h.Length);
            // Drop villain combos that have no legal hero partner as well. For a
            // fixed hero this yields exactly the specification's effective range count.
            subsets.Clear();
            for(int i=0;i<Hero.Length;i++) {
                if((i&1023)==0){token.ThrowIfCancellationRequested();Report(progress,"compatible",i,Hero.Length+Villain.Length);}
                var c=Hero[i];for(int bits=1;bits<16;bits++){long key=0;for(int j=0;j<4;j++)if((bits&(1<<j))!=0)key|=1L<<c.Cards[j];int count;subsets.TryGetValue(key,out count);subsets[key]=count+1;}
            }
            valid.Clear();
            for(int i=0;i<Villain.Length;i++) {
                if((i&1023)==0){token.ThrowIfCancellationRequested();Report(progress,"compatible",Hero.Length+i,Hero.Length+Villain.Length);}
                var c=Villain[i];int compatible=Hero.Length;
                for(int bits=1;bits<16;bits++){long key=0;int parity=0;for(int j=0;j<4;j++)if((bits&(1<<j))!=0){key|=1L<<c.Cards[j];parity++;}int count;subsets.TryGetValue(key,out count);compatible+=(parity%2==0?count:-count);}
                if(compatible>0)valid.Add(c);
            }
            token.ThrowIfCancellationRequested();Report(progress,"compatible",Hero.Length+Villain.Length,Hero.Length+Villain.Length);
            VillainNoOpponent=Villain.Length-valid.Count;Villain=valid.ToArray();
        }
        static void Report(Action<EquityProgress> callback,string phase,long done,long total) {
            if(callback!=null)callback(new EquityProgress{Phase=phase,Completed=done,Target=total});
        }
    }
    public static class EquityEngine {
        sealed class Rng {
            uint state;
            public Rng(uint seed){state=seed==0?0x9e3779b9:seed;}
            uint Next(){uint x=state;x^=x<<13;x^=x>>17;x^=x<<5;state=x;return x;}
            public double Unit(){return Next()/4294967296.0;}
            // Rejection avoids modulo bias for drawing physical cards.
            public int Index(int count){uint n=(uint)count,limit=uint.MaxValue-(uint.MaxValue%n),x;do{x=Next();}while(x>limit);return (int)((x-1)%n);}
        }
        sealed class Distribution {
            readonly double[] cumulative; readonly double total;
            public Distribution(double[] weights) {
                cumulative=new double[weights.Length];double max=weights.Max(),sum=0;
                for(int i=0;i<weights.Length;i++){
                    double w=weights[i]/max;
                    if(w==0)throw new ArgumentException("重みの比が数値精度の範囲を超えています。");
                    sum+=w;cumulative[i]=sum;
                }total=sum;
            }
            public int Sample(Rng rng) {double value=rng.Unit()*total;int lo=0,hi=cumulative.Length-1;while(lo<hi){int mid=lo+(hi-lo)/2;if(value<cumulative[mid])hi=mid;else lo=mid+1;}return lo;}
        }
        public static EquityPlan Prepare(string board,EquityRange hero,EquityRange villain,CancellationToken token,Action<EquityProgress> progress) {
            token.ThrowIfCancellationRequested();return new EquityPlan(board,hero,villain,token,progress);
        }
        public static Task<EquityResult> CalculateAsync(EquityPlan plan,EquitySettings settings,CancellationToken token,Action<EquityProgress> progress) {
            if(settings==null)throw new ArgumentNullException("settings");
            var copy=new EquitySettings{Trials=settings.Trials,Seed=settings.Seed,ExactRiver=settings.ExactRiver,MaxExactComparisons=settings.MaxExactComparisons};
            return Task.Run(()=>Calculate(plan,copy,token,progress),token);
        }
        public static EquityResult Calculate(EquityPlan plan,EquitySettings settings,CancellationToken token,Action<EquityProgress> progress) {
            if(plan==null||settings==null)throw new ArgumentNullException("plan/settings");
            if(settings.Trials<1||settings.Trials>2000000)throw new ArgumentException("試行数は1〜2,000,000で指定してください。");
            if(settings.MaxExactComparisons<1)throw new ArgumentException("全列挙上限が正しくありません。");
            token.ThrowIfCancellationRequested();var clock=Stopwatch.StartNew();uint seed;
            if(settings.Seed.HasValue)seed=settings.Seed.Value;else{var bytes=new byte[4];using(var random=RandomNumberGenerator.Create())random.GetBytes(bytes);seed=BitConverter.ToUInt32(bytes,0);}
            bool exact=plan.Board.Length==5&&settings.ExactRiver;
            if(exact&&plan.CandidatePairs>settings.MaxExactComparisons)throw new ArgumentException("全列挙対象は"+plan.CandidatePairs+"組です。上限を超えています。モンテカルロを選択してください。");
            var evaluator=new OmahaEvaluator();var rng=new Rng(seed);
            double win=0,tie=0,loss=0;long total=0,attempts=0;
            if(exact) {
                var heroScores=new uint[plan.Hero.Length];var villainScores=new uint[plan.Villain.Length];
                for(int i=0;i<heroScores.Length;i++){if((i&255)==0)token.ThrowIfCancellationRequested();heroScores[i]=evaluator.Score(plan.Hero[i].Cards,plan.Board);}
                for(int i=0;i<villainScores.Length;i++){if((i&255)==0)token.ThrowIfCancellationRequested();villainScores[i]=evaluator.Score(plan.Villain[i].Cards,plan.Board);}
                double heroMax=plan.Hero.Max(c=>c.Weight),villainMax=plan.Villain.Max(c=>c.Weight);
                for(int i=0;i<plan.Hero.Length;i++)for(int j=0;j<plan.Villain.Length;j++) {
                    if((attempts++&4095)==0){token.ThrowIfCancellationRequested();Report(progress,"exact",total,plan.LegalPairs,attempts-1,win,tie,loss);}
                    if((plan.Hero[i].Mask&plan.Villain[j].Mask)!=0)continue;
                    double weight=(plan.Hero[i].Weight/heroMax)*(plan.Villain[j].Weight/villainMax);
                    if(weight==0)throw new ArgumentException("対戦重みが数値精度の範囲を超えています。");
                    Add(heroScores[i],villainScores[j],weight,ref win,ref tie,ref loss);total++;
                }
            } else {
                var hd=new Distribution(plan.Hero.Select(c=>c.Weight).ToArray());
                var vd=new Distribution(plan.Villain.Select(c=>c.Weight).ToArray());
                // Small/sparse ranges: enumerate legal pairs and draw their product weights directly.
                var pairs=new List<Tuple<int,int>>();Distribution pairDist=null;
                if(plan.CandidatePairs<=50000) {
                    var weights=new List<double>();double hm=plan.Hero.Max(c=>c.Weight),vm=plan.Villain.Max(c=>c.Weight);
                    for(int i=0;i<plan.Hero.Length;i++)for(int j=0;j<plan.Villain.Length;j++) {
                        if((j&1023)==0)token.ThrowIfCancellationRequested();
                        if((plan.Hero[i].Mask&plan.Villain[j].Mask)!=0)continue;
                        double w=(plan.Hero[i].Weight/hm)*(plan.Villain[j].Weight/vm);
                        if(w==0)throw new ArgumentException("対戦重みが数値精度の範囲を超えています。");
                        pairs.Add(Tuple.Create(i,j));weights.Add(w);
                    }pairDist=new Distribution(weights.ToArray());
                }
                int[] board=new int[5],available=new int[52];Array.Copy(plan.Board,board,plan.Board.Length);
                while(total<settings.Trials) {
                    if((attempts&2047)==0){token.ThrowIfCancellationRequested();Report(progress,"sampling",total,settings.Trials,attempts,win,tie,loss);}
                    attempts++;int hi,vi;
                    if(pairDist!=null){var pair=pairs[pairDist.Sample(rng)];hi=pair.Item1;vi=pair.Item2;}
                    else{hi=hd.Sample(rng);vi=vd.Sample(rng);}
                    var h=plan.Hero[hi];var v=plan.Villain[vi];
                    // Redraw BOTH players on collision; holding hero fixed would change joint weights.
                    if((h.Mask&v.Mask)!=0)continue;
                    long blocked=plan.BoardMask|h.Mask|v.Mask;int count=0;
                    for(int c=0;c<52;c++)if((blocked&(1L<<c))==0)available[count++]=c;
                    for(int b=plan.Board.Length;b<5;b++){int k=rng.Index(count);board[b]=available[k];available[k]=available[--count];}
                    Add(evaluator.Score(h.Cards,board),evaluator.Score(v.Cards,board),1,ref win,ref tie,ref loss);total++;
                }
            }
            token.ThrowIfCancellationRequested();double sum=win+tie+loss,mean=(win+tie/2)/sum;
            double variance=Math.Max(0,(win+tie/4)/sum-mean*mean);
            double margin=exact?0:1.96*Math.Sqrt(variance/Math.Max(1,total-1));
            Report(progress,exact?"exact":"sampling",total,exact?plan.LegalPairs:settings.Trials,attempts,win,tie,loss);
            token.ThrowIfCancellationRequested();
            return new EquityResult{Equity=100*mean,VillainEquity=100*(1-mean),Win=100*win/sum,Tie=100*tie/sum,Loss=100*loss/sum,
                CiLow=100*Math.Max(0,mean-margin),CiHigh=100*Math.Min(1,mean+margin),Margin=100*margin,Exact=exact,Seed=seed,
                Total=total,Attempts=attempts,LegalPairs=plan.LegalPairs,CandidatePairs=plan.CandidatePairs,ElapsedMs=clock.Elapsed.TotalMilliseconds,
                HeroRawCount=plan.HeroRawCount,VillainRawCount=plan.VillainRawCount,HeroBoardBlocked=plan.HeroBoardBlocked,VillainBoardBlocked=plan.VillainBoardBlocked,
                HeroNoOpponent=plan.HeroNoOpponent,VillainNoOpponent=plan.VillainNoOpponent,HeroCount=plan.HeroCount,VillainCount=plan.VillainCount};
        }
        static void Add(uint h,uint v,double weight,ref double win,ref double tie,ref double loss){if(h>v)win+=weight;else if(h==v)tie+=weight;else loss+=weight;}
        static void Report(Action<EquityProgress> callback,string phase,long completed,long target,long attempts,double win,double tie,double loss) {
            double sum=win+tie+loss;
            if(callback!=null)callback(new EquityProgress{Phase=phase,Completed=completed,Target=target,Attempts=attempts,Equity=sum>0?(double?)(100*(win+tie/2)/sum):null});
        }
    }
}
