using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using FlopCommands.Equity;
using PloSyntax;

namespace FlopCommands.Research {
    [DataContract] public sealed class ResearchCondition {
        [DataMember(Name="condition_id")] public string Id=Guid.NewGuid().ToString("N");
        [DataMember(Name="label")] public string Label="新しい条件";
        [DataMember(Name="enabled")] public bool Enabled=true;
        [DataMember(Name="kind")] public string Kind="literal";
        [DataMember(Name="syntax")] public string Syntax="";
        [DataMember(Name="by_board")] public Dictionary<string,string> ByBoard=new Dictionary<string,string>();
        [DataMember(Name="generator",EmitDefaultValue=false)] public ResearchGenerator Generator;
        public override string ToString(){return (Enabled?"☑ ":"☐ ")+Label+(Kind=="generated"?" [自動]":"");}
    }
    [DataContract] public sealed class ResearchConditionSet {
        [DataMember(Name="set_id")] public string Id=Guid.NewGuid().ToString("N");
        [DataMember(Name="revision")] public int Revision;
        [DataMember(Name="name")] public string Name="新しい条件セット";
        [DataMember(Name="conditions")] public List<ResearchCondition> Conditions=new List<ResearchCondition>();
        public override string ToString(){return Name+"（版 "+Revision+"）";}
    }
    [DataContract] public sealed class ResearchScenario {
        [DataMember(Name="game")] public string Game="PLO4";
        [DataMember(Name="players")] public int Players=2;
        [DataMember(Name="hero_position")] public string HeroPosition="";
        [DataMember(Name="villain_position")] public string VillainPosition="";
        [DataMember(Name="effective_stack_bb")] public string StackBb="";
        [DataMember(Name="pot_type")] public string PotType="";
        [DataMember(Name="action_history")] public string ActionHistory="";
        [DataMember(Name="solution_context")] public string SolutionContext="";
        [DataMember(Name="equity_reference")] public string EquityReference="双方の比較対象アクション前";
    }
    [DataContract] public sealed class ResearchSource {
        [DataMember(Name="path")] public string Path="";
        [DataMember(Name="sha256")] public string Sha256="";
    }
    [DataContract] public sealed class ResearchAction {
        [DataMember(Name="action_id")] public string Id=Guid.NewGuid().ToString("N");
        [DataMember(Name="label")] public string Label="";
        [DataMember(Name="kind")] public string Kind="check";
        [DataMember(Name="pot_fraction")] public string PotFraction="";
        [DataMember(Name="source")] public ResearchSource Source=new ResearchSource();
    }
    [DataContract] public sealed class ResearchSpot {
        [DataMember(Name="spot_id")] public string Id=Guid.NewGuid().ToString("N");
        [DataMember(Name="board")] public string Board="";
        [DataMember(Name="node")] public string Node="";
        [DataMember(Name="hero_actions")] public List<ResearchAction> Actions=new List<ResearchAction>();
        [DataMember(Name="opponent_sources")] public List<ResearchSource> Opponents=new List<ResearchSource>();
        [DataMember(Name="opponent_percent_unit")] public bool OpponentPercentUnit;
        [DataMember(Name="all_actions_common_scale")] public bool AllActionsCommonScale;
        [DataMember(Name="opponent_common_scale")] public bool OpponentCommonScale;
        [DataMember(Name="validated_utc")] public string ValidatedUtc="";
        [DataMember(Name="hero_hands")] public int HeroHands;
        [DataMember(Name="opponent_hands")] public int OpponentHands;
        public override string ToString(){return Board+" · "+Node+" · 自分 "+Actions.Count+" CSV / "+HeroHands+"ハンド · 相手 "+Opponents.Count+" CSV / "+OpponentHands+"ハンド";}
    }
    [DataContract] public sealed class ResearchCatalog {
        [DataMember(Name="catalog_id")] public string Id=Guid.NewGuid().ToString("N");
        [DataMember(Name="revision")] public int Revision;
        [DataMember(Name="name")] public string Name="新しい調査カタログ";
        [DataMember(Name="highest_rank",EmitDefaultValue=false)] public string HighestRank;
        [DataMember(Name="scenario")] public ResearchScenario Scenario=new ResearchScenario();
        [DataMember(Name="spots")] public List<ResearchSpot> Spots=new List<ResearchSpot>();
        public override string ToString(){return Name+"（"+Spots.Count+"ボード・版 "+Revision+"）";}
    }
    [DataContract] public sealed class ResearchWorkspace {
        [DataMember(Name="schema_version")] public int SchemaVersion=1;
        [DataMember(Name="revision")] public int Revision;
        [DataMember(Name="condition_sets")] public List<ResearchConditionSet> Sets=new List<ResearchConditionSet>();
        [DataMember(Name="catalogs")] public List<ResearchCatalog> Catalogs=new List<ResearchCatalog>();
    }
    public sealed class ResolvedResearchCondition {
        public string Id,Label,Syntax,Status;
    }
    public static class ResearchBoardFilter {
        public const string Ranks="AKQJT98765432";
        public static string Resolve(string rank,bool? legacyAhi,string inherited){
            if(rank!=null&&rank!="all"&&(rank.Length!=1||Ranks.IndexOf(rank,StringComparison.Ordinal)<0))throw new ArgumentException("最高ランクはallまたはA〜2で指定してください。");
            string legacy=legacyAhi.HasValue?(legacyAhi.Value?"A":"all"):null;
            if(rank!=null&&legacy!=null&&rank!=legacy)throw new ArgumentException("最高ランクと旧Ahi限定設定が矛盾しています。");
            string selected=rank??legacy??inherited??"all";
            if(selected!="all"&&(selected.Length!=1||Ranks.IndexOf(selected,StringComparison.Ordinal)<0))throw new ArgumentException("保存された最高ランク条件が不正です。");
            return selected;
        }
        public static bool Matches(string board,string rank){var cards=EquityCards.Parse(board,3,3);rank=Resolve(rank,null,null);return rank=="all"||Ranks[cards.Min(c=>c/4)].ToString()==rank;}
    }
    public static class ResearchJson {
        public const int MaxBytes=16*1024*1024;
        static DataContractJsonSerializer Serializer(Type type){return new DataContractJsonSerializer(type,new DataContractJsonSerializerSettings{UseSimpleDictionaryFormat=true,MaxItemsInObjectGraph=200000});}
        public static byte[] Encode<T>(T value){using(var stream=new MemoryStream()){Serializer(typeof(T)).WriteObject(stream,value);if(stream.Length>MaxBytes)throw new ArgumentException("調査設定は16MiB以下にしてください。");return stream.ToArray();}}
        public static T Decode<T>(byte[] bytes){if(bytes==null||bytes.Length>MaxBytes)throw new ArgumentException("調査設定は16MiB以下にしてください。");ResearchRecipeJson.CheckDocument(bytes);using(var stream=new MemoryStream(bytes))return (T)Serializer(typeof(T)).ReadObject(stream);}
        public static T Clone<T>(T value){return Decode<T>(Encode(value));}
        public static byte[] Read(string path){using(var file=File.OpenRead(path)){if(file.Length>MaxBytes)throw new ArgumentException("調査設定は16MiB以下にしてください。");using(var stream=new MemoryStream()){file.CopyTo(stream);if(stream.Length>MaxBytes)throw new ArgumentException("調査設定が大きすぎます。");return stream.ToArray();}}}
    }
    public static class ResearchValidation {
        static void Need(bool value,string reason){if(!value)throw new ArgumentException(reason);}
        static void Text(string value,string label,int max){Need(!string.IsNullOrWhiteSpace(value)&&value.Length<=max,label+"を入力してください（最大"+max+"文字）。");}
        static void Id(string value){Need(value!=null&&Regex.IsMatch(value,@"^[a-zA-Z0-9_-]{1,100}$"),"識別IDが不正です。");}
        public static string Board(string value){var cards=EquityCards.Parse(value,3,3);return EquityCards.Text(cards.OrderBy(x=>x).ToArray());}
        public static void Set(ResearchConditionSet set){
            Need(set!=null,"条件セットがありません。");Id(set.Id);Text(set.Name,"セット名",200);Need(set.Revision>=0,"条件セットの版が不正です。");
            Need(set.Conditions!=null&&set.Conditions.Count<=1000,"条件は1,000件までです。");var ids=new HashSet<string>();
            foreach(var c in set.Conditions){Need(c!=null,"空の条件があります。");Id(c.Id);Need(ids.Add(c.Id),"条件IDが重複しています。");Text(c.Label,"条件名",1000);Need(c.ByBoard!=null,"ボード別定義がありません。");
                Need(c.Kind=="generated"||c.Generator==null,"手入力条件に自動生成定義を混在できません。");
                if(c.Kind=="generated"){Need(c.Syntax==""&&c.ByBoard.Count==0,"自動生成条件には生成済みsyntaxを保存できません。");ResearchRecipes.Validate(c.Generator);}
                else if(c.Kind=="literal"){new SyntaxMatcher(c.Syntax);Need(c.ByBoard.Count==0,"共通syntaxとボード別定義は同時に指定できません。");}
                else if(c.Kind=="per_board"){
                    Need(string.IsNullOrEmpty(c.Syntax),"ボード別条件に共通syntaxは指定できません。");Need(c.ByBoard.Count>0&&c.ByBoard.Count<=10000,"ボード別syntaxを1件以上指定してください。");var boards=new HashSet<string>();
                    foreach(var pair in c.ByBoard){Need(boards.Add(Board(pair.Key)),"同じ物理ボードの定義が重複しています。");new SyntaxMatcher(pair.Value);}
                }else throw new ArgumentException("未対応の適用方法です："+c.Kind);
            }
        }
        public static ResolvedResearchCondition[] Resolve(ResearchConditionSet set,string board){
            Set(set);string key=Board(board);return set.Conditions.Where(c=>c.Enabled).Select(c=>{
                if(c.Kind=="generated")throw new ArgumentException("自動生成条件はボード別の生成が必要です。保存済み条件セットを生成プレビューで確認してください。");
                string syntax=c.Kind=="literal"?c.Syntax:c.ByBoard.Where(p=>Board(p.Key)==key).Select(p=>p.Value).FirstOrDefault();
                return new ResolvedResearchCondition{Id=c.Id,Label=c.Label,Syntax=syntax,Status=syntax==null?"missing_definition":"ready"};
            }).ToArray();
        }
        static void Source(ResearchSource s,bool checkedFiles){Need(s!=null,"CSV参照がありません。");Text(s.Path,"CSVのパス",4096);Need(Path.IsPathRooted(s.Path),"CSVは絶対パスで登録してください。");Need(string.Equals(Path.GetExtension(s.Path),".csv",StringComparison.OrdinalIgnoreCase),"CSVファイルを指定してください。");if(checkedFiles)Need(s.Sha256!=null&&Regex.IsMatch(s.Sha256,@"^[a-f0-9]{64}$"),"CSV検証情報がありません。CSVを再検証してください。");}
        public static void Spot(ResearchSpot spot,bool checkedFiles){
            Need(spot!=null,"ボード登録がありません。");Id(spot.Id);Board(spot.Board);Text(spot.Node,"アクション位置",300);Need(spot.AllActionsCommonScale,"自分の全アクションと共通weight基準を確認してください。");
            Need(spot.Actions!=null&&spot.Actions.Count>=1&&spot.Actions.Count<=3,"自分のアクションCSVは1〜3個です。");Need(spot.Opponents!=null&&spot.Opponents.Count<=100,"相手CSVは100個までです。");
            var ids=new HashSet<string>();var keys=new HashSet<string>();var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var a in spot.Actions){Need(a!=null,"空のアクションがあります。");Id(a.Id);Need(ids.Add(a.Id),"アクションIDが重複しています。");Text(a.Label,"アクション名",200);Need(new[]{"check","bet","fold","call","raise"}.Contains(a.Kind),"アクションの種類が不正です。");string size="";
                if((a.Kind=="bet"||a.Kind=="raise")&&a.PotFraction=="allin"){size="allin";}else if(a.Kind=="bet"||a.Kind=="raise"){decimal amount;Need(decimal.TryParse(a.PotFraction,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out amount)&&amount>0,"ベット・レイズのポット比率を正の小数で指定してください。");size=amount.ToString("G29",CultureInfo.InvariantCulture);}else Need(string.IsNullOrEmpty(a.PotFraction),"サイズはbet/raiseだけに指定してください。");
                Need(keys.Add(a.Kind+":"+size),"同じ種類・サイズのアクションが重複しています。");Source(a.Source,checkedFiles);Need(paths.Add(Path.GetFullPath(a.Source.Path)),"同じCSVを重複指定できません。");
            }
            if(spot.Opponents.Count>0)Need(spot.OpponentCommonScale,"相手レンジの共通weight基準を確認してください。");
            foreach(var s in spot.Opponents){Source(s,checkedFiles);Need(paths.Add(Path.GetFullPath(s.Path)),"自分・相手を含め同じCSVを重複指定できません。");}
            if(checkedFiles)Need(!string.IsNullOrEmpty(spot.ValidatedUtc),"登録前にCSVを検証してください。");
        }
        public static void Catalog(ResearchCatalog c){
            if(c!=null)ResearchBoardFilter.Resolve(c.HighestRank,null,null);
            Need(c!=null,"カタログがありません。");Id(c.Id);Text(c.Name,"カタログ名",200);Need(c.Revision>=0,"カタログの版が不正です。");var s=c.Scenario;Need(s!=null&&s.Game=="PLO4"&&s.Players==2,"初期版はPLO4の2人の局面に対応します。");
            Text(s.HeroPosition,"自分のポジション",100);Text(s.VillainPosition,"相手のポジション",100);Need(!string.Equals(s.HeroPosition,s.VillainPosition,StringComparison.OrdinalIgnoreCase),"双方のポジションが同じです。");decimal stack;Need(decimal.TryParse(s.StackBb,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out stack)&&stack>0,"実効スタックを正のBB数で入力してください。");Text(s.PotType,"ポット種別",200);Text(s.ActionHistory,"それ以前のアクション",4000);Text(s.SolutionContext,"レーキ・ソルバー設定",4000);Text(s.EquityReference,"EQの基準点",500);
            Need(c.Spots!=null&&c.Spots.Count<=10000,"ボードは10,000件までです。");var ids=new HashSet<string>();var keys=new HashSet<string>();foreach(var spot in c.Spots){Spot(spot,true);Need(ids.Add(spot.Id),"ボードIDが重複しています。");Need(keys.Add(Board(spot.Board)+"\n"+spot.Node.Trim()),"同じボード・アクション位置の二重登録です。");}
        }
        public static void Workspace(ResearchWorkspace w){Need(w!=null&&(w.SchemaVersion==1||w.SchemaVersion==2)&&w.Revision>=0,"未対応または不正な調査設定形式です。");Need(w.Sets!=null&&w.Catalogs!=null&&w.Sets.Count<=500&&w.Catalogs.Count<=500,"調査設定の件数が不正です。");var ids=new HashSet<string>();foreach(var s in w.Sets){Set(s);Need(w.SchemaVersion==2||!s.Conditions.Any(c=>c.Kind=="generated"),"自動生成条件には調査設定形式2が必要です。");Need(ids.Add(s.Id),"条件セットIDが重複しています。");}ids.Clear();foreach(var c in w.Catalogs){Catalog(c);Need(ids.Add(c.Id),"カタログIDが重複しています。");}}
        public static ResearchSpot ValidateFiles(ResearchSpot draft,CancellationToken token){
            var spot=ResearchJson.Clone(draft);Spot(spot,false);spot.Board=Board(spot.Board);var sources=spot.Actions.Select(a=>a.Source).Concat(spot.Opponents).ToArray();
            foreach(var s in sources){token.ThrowIfCancellationRequested();s.Path=Path.GetFullPath(s.Path);s.Sha256=Hash(s.Path,token);}
            var actions=spot.Actions.Select(a=>EquityInput.ReadAction(a.Source.Path,a.Label,spot.Board,token)).ToArray();var hero=EquityRange.FromActions(actions.Select(a=>a.Range),true);spot.HeroHands=hero.Count;Need(hero.Count>0,"自分の合計レンジが空です。");spot.OpponentHands=0;
            if(spot.Opponents.Count>0){var merged=EquityInput.ImportRangeFiles(spot.Opponents.Select(s=>s.Path).ToArray(),spot.OpponentPercentUnit,token);var opponent=EquityRange.Parse(merged.Text,token);long blocked=EquityCards.Mask(EquityCards.Parse(spot.Board,3,3));spot.OpponentHands=opponent.Items.Count(h=>(h.Mask&blocked)==0);Need(spot.OpponentHands>0,"相手レンジにボードと重ならないハンドがありません。");}
            foreach(var s in sources){token.ThrowIfCancellationRequested();Need(s.Sha256==Hash(s.Path,token),"検証中にCSVが変更されました。再検証してください。");}
            spot.ValidatedUtc=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);return spot;
        }
        public static string Hash(string path,CancellationToken token){token.ThrowIfCancellationRequested();using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(EquityInput.ReadCsv(path,token))).Replace("-","").ToLowerInvariant();}
        public static string[] CheckFiles(ResearchCatalog catalog,CancellationToken token){Catalog(catalog);var problems=new List<string>();foreach(var source in catalog.Spots.SelectMany(s=>s.Actions.Select(a=>a.Source).Concat(s.Opponents))){token.ThrowIfCancellationRequested();try{if(source.Sha256!=Hash(source.Path,token))problems.Add(source.Path+"：登録後に変更されています。");}catch(IOException ex){problems.Add(source.Path+"："+ex.Message);}catch(UnauthorizedAccessException ex){problems.Add(source.Path+"："+ex.Message);}catch(ArgumentException ex){problems.Add(source.Path+"："+ex.Message);}}return problems.Distinct().ToArray();}
    }
    public sealed class ResearchStore {
        public readonly string FilePath;
        public ResearchStore(string root){FilePath=Path.Combine(Path.GetFullPath(root),"workspace.json");}
        public ResearchWorkspace Load(){if(!File.Exists(FilePath))return new ResearchWorkspace();var w=ResearchJson.Decode<ResearchWorkspace>(ResearchJson.Read(FilePath));ResearchValidation.Workspace(w);return w;}
        public ResearchWorkspace Save(ResearchWorkspace draft,int expectedRevision){
            var value=ResearchJson.Clone(draft);if(value.SchemaVersion==1&&value.Sets!=null&&value.Sets.Any(s=>s!=null&&s.Conditions!=null&&s.Conditions.Any(c=>c!=null&&c.Kind=="generated")))value.SchemaVersion=2;ResearchValidation.Workspace(value);Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            using(var gate=new FileStream(FilePath+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                var old=Load();if(old.Revision!=expectedRevision)throw new InvalidOperationException("別の画面・アプリで調査設定が更新されました。再読込してから保存してください。");if(old.SchemaVersion==2)value.SchemaVersion=2;value.Revision=checked(expectedRevision+1);
                foreach(var s in value.Sets){var previous=old.Sets.FirstOrDefault(x=>x.Id==s.Id);s.Revision=previous==null?1:Same(previous,s)?previous.Revision:checked(previous.Revision+1);}
                foreach(var c in value.Catalogs){var previous=old.Catalogs.FirstOrDefault(x=>x.Id==c.Id);c.Revision=previous==null?1:Same(previous,c)?previous.Revision:checked(previous.Revision+1);}
                byte[] encoded=ResearchJson.Encode(value);if(old.SchemaVersion==1&&value.SchemaVersion==2&&File.Exists(FilePath))BackupV1(old.Revision);WriteAtomic(FilePath,encoded);return value;
            }
        }
        static bool Same<T>(T a,T b){return ResearchJson.Encode(a).SequenceEqual(ResearchJson.Encode(b));}
        void BackupV1(int revision){
            string backup=FilePath+".schema-v1.r"+revision+".bak",temp=backup+"."+Guid.NewGuid().ToString("N")+".tmp";byte[] original=ResearchJson.Read(FilePath);
            if(File.Exists(backup)){if(!ResearchJson.Read(backup).SequenceEqual(original))throw new IOException("移行前バックアップが既存の設定と一致しません："+backup);return;}
            try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(original,0,original.Length);file.Flush(true);}File.Move(temp,backup);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        static void WriteAtomic(string path,byte[] bytes){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
        public void Export(string path,ResearchWorkspace workspace){ResearchValidation.Workspace(workspace);string full=Path.GetFullPath(path);if(!string.Equals(Path.GetExtension(full),".json",StringComparison.OrdinalIgnoreCase)||string.Equals(full,FilePath,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("調査設定の保存先とは別の.jsonファイルを選択してください。");if(workspace.Catalogs.SelectMany(c=>c.Spots).SelectMany(s=>s.Actions.Select(a=>a.Source).Concat(s.Opponents)).Any(s=>string.Equals(full,Path.GetFullPath(s.Path),StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("入力CSVには上書きできません。");WriteAtomic(full,ResearchJson.Encode(workspace));}
        public static ResearchWorkspace ImportAsCopies(ResearchWorkspace current,string path){
            var input=ResearchJson.Decode<ResearchWorkspace>(ResearchJson.Read(path));ResearchValidation.Workspace(input);var next=ResearchJson.Clone(current);
            if(input.SchemaVersion==2)next.SchemaVersion=2;foreach(var s in input.Sets){s.Id=Guid.NewGuid().ToString("N");s.Revision=0;next.Sets.Add(s);}foreach(var c in input.Catalogs){c.Id=Guid.NewGuid().ToString("N");c.Revision=0;next.Catalogs.Add(c);}ResearchValidation.Workspace(next);return next;
        }
    }
}
