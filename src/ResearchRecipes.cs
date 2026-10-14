using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Xml;
using PloSyntax;

namespace FlopCommands.Research {
    [DataContract] public sealed class ResearchRankRef {
        [DataMember(Name="type")] public string Type;
        [DataMember(Name="rank",EmitDefaultValue=false)] public string Rank;
        [DataMember(Name="position",EmitDefaultValue=false)] public string Position;
    }
    [DataContract] public sealed class ResearchRoleRecipe {
        [DataMember(Name="type")] public string Type="all";
        [DataMember(Name="category",EmitDefaultValue=false)] public string Category;
        [DataMember(Name="rank",EmitDefaultValue=false)] public ResearchRankRef Rank;
        [DataMember(Name="ranks",EmitDefaultValue=false)] public ResearchRankRef[] Ranks;
        [DataMember(Name="trips",EmitDefaultValue=false)] public ResearchRankRef Trips;
        [DataMember(Name="pair",EmitDefaultValue=false)] public ResearchRankRef Pair;
        [DataMember(Name="high",EmitDefaultValue=false)] public ResearchRankRef High;
        [DataMember(Name="tier",EmitDefaultValue=false)] public string Tier;
    }
    [DataContract] public sealed class ResearchPocketRecipe {
        [DataMember(Name="type")] public string Type="all";
        [DataMember(Name="rank",EmitDefaultValue=false)] public ResearchRankRef Rank;
    }
    [DataContract] public sealed class ResearchBlockerRecipe {
        [DataMember(Name="rank")] public ResearchRankRef Rank;
        [DataMember(Name="mode")] public string Mode;
    }
    [DataContract] public sealed class ResearchFilterRecipe {
        [DataMember(Name="role")] public ResearchRoleRecipe Role=new ResearchRoleRecipe();
        [DataMember(Name="fd")] public string Fd="all";
        [DataMember(Name="sd")] public string Sd="all";
        [DataMember(Name="bdfd")] public string Bdfd="all";
        [DataMember(Name="bdsd")] public string Bdsd="all";
        [DataMember(Name="pocket")] public ResearchPocketRecipe Pocket=new ResearchPocketRecipe();
        [DataMember(Name="clean")] public string Clean="";
        [DataMember(Name="blockers")] public ResearchBlockerRecipe[] Blockers=new ResearchBlockerRecipe[0];
    }
    [DataContract] public sealed class ResearchGenerator {
        [DataMember(Name="recipe_version")] public int RecipeVersion=1;
        [DataMember(Name="ruleset")] public string Ruleset="plo4-flop-v1";
        [DataMember(Name="source")] public string Source="preset";
        [DataMember(Name="preset_id",EmitDefaultValue=false)] public string PresetId;
        [DataMember(Name="preset_revision",EmitDefaultValue=false)] public int? PresetRevision;
        [DataMember(Name="filter",EmitDefaultValue=false)] public ResearchFilterRecipe Filter;
        [DataMember(Name="additional_syntax")] public string AdditionalSyntax="*";
    }
    [DataContract] public sealed class ResearchDefaultPreset {
        [DataMember(Name="preset_id")] public string Id;
        [DataMember(Name="preset_revision")] public int Revision;
        [DataMember(Name="cell")] public string Cell;
        [DataMember(Name="label")] public string Label;
        [DataMember(Name="group")] public string Group;
        [DataMember(Name="supported")] public bool Supported;
        [DataMember(Name="reason")] public string Reason;
    }
    public static class ResearchPresets {
        static readonly Lazy<ResearchDefaultPreset[]> catalog=new Lazy<ResearchDefaultPreset[]>(Load);
        static ResearchDefaultPreset[] Load(){
            using(var stream=typeof(ResearchPresets).Assembly.GetManifestResourceStream("PloResearch.DefaultPresets.json")){
                if(stream==null)throw new InvalidOperationException("デフォルト条件一覧がアプリに含まれていません。");
                using(var bytes=new MemoryStream()){stream.CopyTo(bytes);var all=ResearchJson.Decode<ResearchDefaultPreset[]>(bytes.ToArray());
                    if(all.Length!=185||all.Select(p=>p.Id).Distinct().Count()!=185||all.Select(p=>p.Cell).Distinct().Count()!=185||all.Any(p=>p.Revision<1||string.IsNullOrWhiteSpace(p.Label)||(!p.Supported&&string.IsNullOrWhiteSpace(p.Reason))))throw new ArgumentException("デフォルト条件一覧が不正です。");return all;}
            }
        }
        public static ResearchDefaultPreset[] All(){return ResearchJson.Clone(catalog.Value);}
        public static string ExistingKey(ResearchCondition c){var g=c.Generator;return c.Kind=="generated"&&g!=null&&g.Source=="preset"&&g.RecipeVersion==1&&g.Ruleset=="plo4-flop-v1"&&g.AdditionalSyntax=="*"?g.PresetId+"@"+g.PresetRevision:null;}
        public static int Add(ResearchConditionSet set,IEnumerable<string> presetIds){
            if(set==null||set.Conditions==null)throw new ArgumentException("条件セットがありません。");var wanted=new HashSet<string>(presetIds??new string[0],StringComparer.Ordinal);var all=catalog.Value;
            foreach(string id in wanted){var p=all.SingleOrDefault(x=>x.Id==id);if(p==null||!p.Supported)throw new ArgumentException(p==null?"デフォルト条件が見つかりません："+id:p.Reason);}
            var existing=new HashSet<string>(set.Conditions.Select(ExistingKey).Where(k=>k!=null),StringComparer.Ordinal);
            var additions=all.Where(p=>wanted.Contains(p.Id)&&!existing.Contains(p.Id+"@"+p.Revision)).Select(p=>new ResearchCondition{Label=p.Label,Kind="generated",Syntax="",Generator=new ResearchGenerator{PresetId=p.Id,PresetRevision=p.Revision}}).ToArray();
            if(set.Conditions.Count+additions.Length>1000)throw new ArgumentException("条件は1,000件までです。");set.Conditions.AddRange(additions);return additions.Length;
        }
        public static int SplitPocketRanks(ResearchConditionSet set,string conditionId){
            var source=set.Conditions.Single(c=>c.Id==conditionId);
            if(source.Kind!="generated"||source.Generator==null)throw new ArgumentException("自動生成条件を選択してください。");
            ResearchRecipes.Validate(source.Generator);
            if(set.Conditions.Count+12>1000)throw new ArgumentException("条件は1,000件までです。");
            var additions=new List<ResearchCondition>();
            foreach(char rank in "23456789TJQKA"){
                var c=ResearchJson.Clone(source);c.Id=Guid.NewGuid().ToString("N");c.Label=source.Label+" / pp "+rank+rank;
                string extra=c.Generator.AdditionalSyntax;
                c.Generator.AdditionalSyntax=(extra=="*"?"":"("+extra+"):")+rank+rank;
                ResearchRecipes.Validate(c.Generator);additions.Add(c);
            }
            int index=set.Conditions.IndexOf(source);set.Conditions.RemoveAt(index);set.Conditions.InsertRange(index,additions);return additions.Count;
        }

    }
    public static class ResearchRecipes {
        static void Need(bool value,string reason){if(!value)throw new ArgumentException(reason);}
        static void Choice(string value,string allowed,string name){Need(value!=null&&allowed.Split('|').Contains(value),name+"が不正です。");}
        static string RankKey(ResearchRankRef r){Need(r!=null,"ランク指定がありません。");if(r.Type=="fixed"){Choice(r.Rank,"2|3|4|5|6|7|8|9|T|J|Q|K|A","固定ランク");return "fixed:"+r.Rank;}Choice(r.Type,"board","ランク種別");Choice(r.Position,"top|middle|bottom","相対ランク");return "board:"+r.Position;}
        public static void Validate(ResearchGenerator g){
            Need(g!=null,"自動生成条件がありません。");ResearchRecipeJson.CheckGenerator(ResearchJson.Encode(g));
            Need(g.RecipeVersion>=1,"生成形式の版が不正です。");Need(!string.IsNullOrWhiteSpace(g.Ruleset)&&g.Ruleset.Length<=100,"判定規則が不正です。");
            Need(!string.IsNullOrWhiteSpace(g.AdditionalSyntax)&&g.AdditionalSyntax.Length<=1000000,"追加syntaxを指定してください（指定なしは*）。");new SyntaxMatcher(g.AdditionalSyntax);
            if(g.Source=="preset"){Need(g.PresetId!=null&&Regex.IsMatch(g.PresetId,@"^[a-z][a-z0-9_.-]{0,119}$"),"デフォルト条件IDが不正です。");Need(g.PresetRevision.HasValue&&g.PresetRevision>=1,"デフォルト条件の版が不正です。");return;}
            Choice(g.Source,"filter","生成方法");var f=g.Filter;Need(f!=null&&f.Role!=null&&f.Pocket!=null,"生成フィルターが不足しています。");var r=f.Role;
            switch(r.Type){
                case "all":break;
                case "category":Choice(r.Category,"high_card|one_pair|two_pair|three_of_a_kind|straight|flush|full_house|quads|straight_flush","役分類");break;
                case "pair":case "set":case "trips":case "quads":RankKey(r.Rank);break;
                case "two_pair":Need(r.Ranks!=null&&r.Ranks.Length==2,"2ペアのランクは2個です。");Need(RankKey(r.Ranks[0])!=RankKey(r.Ranks[1]),"2ペアのランクが重複しています。");break;
                case "full_house":Need(RankKey(r.Trips)!=RankKey(r.Pair),"フルハウスのランクが重複しています。");break;
                case "straight":case "straight_flush":RankKey(r.High);break;
                case "flush":Choice(r.Tier,"nut|second|third|other","フラッシュ順位");break;
                default:throw new ArgumentException("未対応の役です。");
            }
            Choice(f.Fd,"all|none|fd|fdNut|fdSecond|fdThird|fdLow|fdTriple|dualFd","FD");Choice(f.Sd,"all|none|sd|wrap|nutGut|nutOpen|nonGut|nonOpen","SD");
            Choice(f.Bdfd,"all|none|bdfd|bdNut|bdSecond|bdLow|bdTriple","BDFD");Choice(f.Bdsd,"all|none|bdsd|bdsd4|bdsd8|bdsd9|bdsdOther","BDSD");Choice(f.Clean,"|regular|all","ドローなし指定");
            Choice(f.Pocket.Type,"all|rank","ポケット指定");if(f.Pocket.Type=="rank")RankKey(f.Pocket.Rank);
            Need(f.Blockers!=null&&f.Blockers.Length<=13,"ブロッカーは13個までです。");var seen=new HashSet<string>();foreach(var b in f.Blockers){Need(b!=null,"ブロッカー指定がありません。");Need(seen.Add(RankKey(b.Rank)),"同じランク指定のブロッカーが重複しています。");Choice(b.Mode,"yes|no|one|two","ブロッカー枚数");}
            Need(f.Clean==""||(f.Fd=="all"&&f.Sd=="all"),"ドローなしと通常ドローの個別指定を併用できません。");Need(f.Clean!="all"||(f.Bdfd=="all"&&f.Bdsd=="all"),"全ドローなしとバックドアの個別指定を併用できません。");
            Need(f.Bdsd=="all"||f.Bdsd=="none"||f.Sd=="all"||f.Sd=="none","BDSDと通常SDを同時に要求できません。");
        }
    }
    // DataContractJsonSerializer drops unknown members. Check generated recipes
    // before decoding, so a newer definition is never silently simplified.
    internal static class ResearchRecipeJson {
        static XmlElement Read(byte[] bytes){var doc=new XmlDocument{XmlResolver=null};using(var reader=JsonReaderWriterFactory.CreateJsonReader(bytes,new XmlDictionaryReaderQuotas{MaxDepth=64,MaxStringContentLength=ResearchJson.MaxBytes,MaxArrayLength=200000,MaxBytesPerRead=4096,MaxNameTableCharCount=ResearchJson.MaxBytes}))doc.Load(reader);return doc.DocumentElement;}
        static XmlElement Child(XmlElement e,string key){return e==null?null:e.ChildNodes.OfType<XmlElement>().FirstOrDefault(x=>x.Name==key);}
        static string Value(XmlElement e,string key){var child=Child(e,key);return child==null?null:child.InnerText;}
        static void Shape(XmlElement e,params string[] names){
            if(e==null||e.GetAttribute("type")!="object")throw new ArgumentException("生成条件のオブジェクトがありません。");var actual=e.ChildNodes.OfType<XmlElement>().Select(x=>x.Name).ToArray();
            if(actual.Length!=names.Length||actual.Distinct().Count()!=actual.Length||names.Any(n=>!actual.Contains(n)))throw new ArgumentException("生成条件の項目が不足しているか、未対応・重複の項目があります。");
        }
        static void Rank(XmlElement r){if(Value(r,"type")=="fixed")Shape(r,"type","rank");else if(Value(r,"type")=="board")Shape(r,"type","position");else throw new ArgumentException("ランク指定が不正です。");}
        static void Generator(XmlElement g){
            if(Value(g,"source")=="preset"){Shape(g,"recipe_version","ruleset","source","additional_syntax","preset_id","preset_revision");return;}
            if(Value(g,"source")!="filter")throw new ArgumentException("未対応の生成方法です。");Shape(g,"recipe_version","ruleset","source","additional_syntax","filter");
            var f=Child(g,"filter");Shape(f,"role","fd","sd","bdfd","bdsd","pocket","clean","blockers");var r=Child(f,"role");
            switch(Value(r,"type")){
                case "all":Shape(r,"type");break;
                case "category":Shape(r,"type","category");break;
                case "pair":case "set":case "trips":case "quads":Shape(r,"type","rank");Rank(Child(r,"rank"));break;
                case "two_pair":Shape(r,"type","ranks");var ranks=Child(r,"ranks");if(ranks==null||ranks.GetAttribute("type")!="array")throw new ArgumentException("2ペアのランクが不正です。");foreach(XmlElement rank in ranks.ChildNodes)Rank(rank);break;
                case "full_house":Shape(r,"type","trips","pair");Rank(Child(r,"trips"));Rank(Child(r,"pair"));break;
                case "straight":case "straight_flush":Shape(r,"type","high");Rank(Child(r,"high"));break;
                case "flush":Shape(r,"type","tier");break;
                default:throw new ArgumentException("未対応の役です。");
            }
            var pocket=Child(f,"pocket");if(Value(pocket,"type")=="all")Shape(pocket,"type");else{Shape(pocket,"type","rank");Rank(Child(pocket,"rank"));}
            var blockers=Child(f,"blockers");if(blockers==null||blockers.GetAttribute("type")!="array")throw new ArgumentException("ブロッカー指定が不正です。");foreach(XmlElement b in blockers.ChildNodes){Shape(b,"rank","mode");Rank(Child(b,"rank"));}
        }
        public static void CheckGenerator(byte[] bytes){Generator(Read(bytes));}
        public static void CheckDocument(byte[] bytes){Walk(Read(bytes));}
        public static void CheckKnownDocument(byte[] bytes,Type type){KnownMembers(Read(bytes),type);CheckDocument(bytes);}
        static void KnownMembers(XmlElement e,Type type){
            if(e==null||e.GetAttribute("type")=="null"||type==typeof(string)||type.IsPrimitive||type.IsEnum||type==typeof(decimal)||Nullable.GetUnderlyingType(type)!=null)return;
            if(type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(Dictionary<,>)){foreach(XmlElement c in e.ChildNodes.OfType<XmlElement>())KnownMembers(c,type.GetGenericArguments()[1]);return;}
            if(type.IsArray||(type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(List<>))){Type item=type.IsArray?type.GetElementType():type.GetGenericArguments()[0];foreach(XmlElement c in e.ChildNodes.OfType<XmlElement>())KnownMembers(c,item);return;}
            var members=type.GetFields(BindingFlags.Public|BindingFlags.Instance).Select(f=>new{Field=f,Attribute=(DataMemberAttribute)Attribute.GetCustomAttribute(f,typeof(DataMemberAttribute))}).Where(x=>x.Attribute!=null).ToDictionary(x=>x.Attribute.Name??x.Field.Name,x=>x.Field.FieldType);
            var seen=new HashSet<string>();foreach(XmlElement child in e.ChildNodes.OfType<XmlElement>()){
                Type nested;if(!seen.Add(child.Name)||!members.TryGetValue(child.Name,out nested))throw new ArgumentException("保存形式2に未対応・重複の項目があります："+child.Name);KnownMembers(child,nested);
            }
        }
        static void Walk(XmlElement e){
            if(e.GetAttribute("type")=="object"&&Value(e,"kind")=="generated"&&(Child(e,"enabled")!=null||Child(e,"generator")!=null||Child(e,"by_board")!=null)){
                Shape(e,"condition_id","label","enabled","kind","syntax","by_board","generator");Generator(Child(e,"generator"));return;
            }
            if(e.GetAttribute("type")=="object"&&Child(e,"condition_sets")!=null&&Value(e,"schema_version")=="2"){Shape(e,"schema_version","revision","condition_sets","catalogs");KnownMembers(e,typeof(ResearchWorkspace));}
            foreach(XmlElement c in e.ChildNodes.OfType<XmlElement>())Walk(c);
        }
    }
}
