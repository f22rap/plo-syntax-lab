using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FlopCommands.Equity;
using PloSyntax;

namespace FlopCommands.Research {
    // Internal engine output, never a tool input. Keep the exact UTF-8 payload so
    // Node and .NET can verify the same digest without JSON reserialization.
    [DataContract] public sealed class ResearchGenerationSnapshot {
        [DataMember(Name="schema_version")] public int Version;
        [DataMember(Name="payload_json")] public string Payload;
        [DataMember(Name="sha256")] public string Sha256;
    }
    [DataContract] public sealed class ResearchGenerationVersions {
        [DataMember(Name="resolver_version")] public string Resolver;
        [DataMember(Name="ruleset")] public string Ruleset;
        [DataMember(Name="engine_hash")] public string EngineHash;
        [DataMember(Name="preset_registry_hash")] public string PresetHash;
        [DataMember(Name="files")] public Dictionary<string,string> Files;
    }
    [DataContract] public sealed class ResearchGeneratedRow {
        [DataMember(Name="condition_id")] public string Id;
        [DataMember(Name="label")] public string Label;
        [DataMember(Name="board")] public string Board;
        [DataMember(Name="kind")] public string Kind;
        [DataMember(Name="recipe_hash")] public string RecipeHash;
        [DataMember(Name="status")] public string Status;
        [DataMember(Name="reason_code")] public string ReasonCode;
        [DataMember(Name="reason")] public string Reason;
        [DataMember(Name="syntax")] public string Syntax;
        [DataMember(Name="legal_hand_count")] public int? LegalHands;
        [DataMember(Name="verified")] public bool Verified;
    }
    [DataContract] public sealed class ResearchGenerationData {
        [DataMember(Name="schema_version")] public int Version;
        [DataMember(Name="condition_set")] public ResearchConditionSet Set;
        [DataMember(Name="boards")] public string[] Boards;
        [DataMember(Name="versions")] public ResearchGenerationVersions Versions;
        [DataMember(Name="rows")] public List<ResearchGeneratedRow> Rows;
    }
    public static class ResearchGeneration {
        static void Need(bool ok,string message){if(!ok)throw new ArgumentException("生成スナップショット："+message);}
        static bool Digest(string value){return Regex.IsMatch(value??"","^[a-f0-9]{64}$");}
        public static string Hash(string text){using(var sha=SHA256.Create())return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b=>b.ToString("x2")));}
        public static ResearchGenerationData Read(ResearchGenerationSnapshot snapshot){
            Need(snapshot!=null&&snapshot.Version==1&&!string.IsNullOrEmpty(snapshot.Payload),"未対応の形式、または未生成です。Node.js版MCPで検証してください。");
            Need(Encoding.UTF8.GetByteCount(snapshot.Payload)<=ResearchJson.MaxBytes&&Digest(snapshot.Sha256)&&Hash(snapshot.Payload)==snapshot.Sha256,"内容のハッシュが一致しません。再検証してください。");
            byte[] bytes=Encoding.UTF8.GetBytes(snapshot.Payload);ResearchRecipeJson.CheckKnownDocument(bytes,typeof(ResearchGenerationData));
            return ResearchJson.Decode<ResearchGenerationData>(bytes);
        }
        public static ResearchGenerationData Validate(ResearchGenerationSnapshot snapshot,ResearchCatalog catalog,ResearchConditionSet set,ResearchRunSettings settings){
            bool generated=set.Conditions.Any(c=>c.Enabled&&c.Kind=="generated");
            if(!generated){Need(snapshot==null,"自動生成条件のない計画に生成結果は指定できません。");return null;}
            var d=Read(snapshot);Need(d!=null&&d.Version==1,"未対応の内容形式です。");ResearchValidation.Set(d.Set);
            Need(ResearchJson.Encode(set).SequenceEqual(ResearchJson.Encode(d.Set)),"保存条件が変更されています。再検証してください。");
            var boards=ResearchRun.Targets(catalog,settings).Select(s=>ResearchValidation.Board(s.Board)).Distinct().ToArray();
            Need(d.Boards!=null&&d.Boards.SequenceEqual(boards),"対象ボードが一致しません。");
            var v=d.Versions;Need(v!=null&&v.Resolver=="generated-conditions-v1"&&v.Ruleset=="plo4-flop-v1"&&Digest(v.EngineHash)&&Digest(v.PresetHash),"未対応の生成版です。");
            Need(v.Files!=null&&v.Files.Count>0&&v.Files.Count<=100&&v.Files.All(p=>Digest(p.Value)&&Regex.IsMatch(p.Key,"^[a-zA-Z0-9_./-]+$"))&&v.Files.ContainsKey("src/default-presets.json")&&v.Files["src/default-presets.json"]==v.PresetHash,"生成元の記録が不正です。");
            string files="{"+string.Join(",",v.Files.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>"\""+p.Key+"\":\""+p.Value+"\""))+"}";Need(Hash(files)==v.EngineHash,"生成元のハッシュが一致しません。");
            var enabled=set.Conditions.Where(c=>c.Enabled).ToArray();Need(d.Rows!=null&&d.Rows.Count==boards.Length*enabled.Length&&d.Rows.All(r=>r!=null),"条件行数が一致しません。");
            Need(d.Rows.Select(r=>r.Board+":"+r.Id).Distinct().Count()==d.Rows.Count,"条件行が重複しています。");
            foreach(var r in d.Rows){
                var c=enabled.SingleOrDefault(x=>x.Id==r.Id);Need(c!=null&&boards.Contains(r.Board)&&r.Label==c.Label&&r.Kind==c.Kind&&Digest(r.RecipeHash),"条件・ボードとの対応が不正です。");
                Need(new[]{"ready","empty_condition","missing_definition"}.Contains(r.Status),r.Label+"："+r.Status+" / "+r.Reason);
                if(r.Status=="ready"){Need(r.Verified&&r.LegalHands>0&&r.LegalHands<=211876&&!string.IsNullOrWhiteSpace(r.Syntax)&&r.Syntax.Length<=1000000,"未検証のsyntaxです。");new SyntaxMatcher(r.Syntax);}
                else if(r.Status=="empty_condition")Need(r.Verified&&r.LegalHands==0&&r.Syntax==""&&!string.IsNullOrEmpty(r.Reason),"空集合の記録が不正です。");
                else Need(settings.AllowMissingDefinitions&&c.Kind=="per_board"&&!c.ByBoard.Keys.Any(b=>ResearchValidation.Board(b)==r.Board)&&!r.Verified&&!r.LegalHands.HasValue&&r.Syntax=="","未設定を許可できません。");
                if(c.Kind!="generated"&&r.Status=="ready"){string exact=c.Kind=="literal"?c.Syntax:c.ByBoard.Single(p=>ResearchValidation.Board(p.Key)==r.Board).Value;Need(r.Syntax==exact,"固定syntaxが変更されています。");}
                if(c.Kind=="generated")Need(c.Generator.RecipeVersion==1&&c.Generator.Ruleset==v.Ruleset,"未対応の条件版です。");
            }
            return d;
        }
        public static ResolvedResearchCondition[] Resolve(ResearchConditionSet set,string board,ResearchGenerationData generated){
            if(generated==null)return ResearchValidation.Resolve(set,board);
            string key=ResearchValidation.Board(board);
            var rows=generated.Rows.Where(x=>x.Board==key).ToDictionary(x=>x.Id);
            return set.Conditions.Where(c=>c.Enabled).Select(c=>{var r=rows[c.Id];return new ResolvedResearchCondition{Id=c.Id,Label=c.Label,Syntax=r.Status=="missing_definition"?null:r.Syntax,Status=r.Status};}).ToArray();
        }
    }
}
