using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FlopCommands.Research;

namespace PloMcp {
    [DataContract] public sealed class ResearchRequest {
        [DataMember] public string action,historyRoot,outputRoot,id,format,artifactId,query,view,conditionId,spotId;
        [DataMember] public string[] inputRoots,spotIds;
        [DataMember] public long maxCsvBytes;
        [DataMember] public int expectedRevision,catalogRevision,setRevision,offset,limit;
        [DataMember] public string catalogId,setId;
        [DataMember] public bool grouped;
        [DataMember] public ResearchConditionSet conditionSet;
        [DataMember] public ResearchCatalog catalog;
        [DataMember] public ResearchRunSettings settings;
    }
    public static class ResearchBridge {
        public static string J<T>(T value){return Encoding.UTF8.GetString(ResearchJson.Encode(value));}
        static string Obj(params string[] pairs){return "{"+string.Join(",",Enumerable.Range(0,pairs.Length/2).Select(i=>J(pairs[i*2])+":"+pairs[i*2+1]))+"}";}
        static void Need(bool ok,string message){if(!ok)throw new ArgumentException(message);}
        // A configured path must remain an ordinary path. Reject junctions/symlinks,
        // including ancestors, instead of following a changed link between checks.
        public static void PlainPath(string file){for(string p=Path.GetFullPath(file);!string.IsNullOrEmpty(p);p=Path.GetDirectoryName(p)){if(File.Exists(p)||Directory.Exists(p))Need((File.GetAttributes(p)&FileAttributes.ReparsePoint)==0,"Linked paths are not allowed for research: "+p);}}
        static bool Within(string root,string file){root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;return file.StartsWith(root,Path.DirectorySeparatorChar=='\\'?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);}
        static void Scope(ResearchRequest q,ResearchCatalog c){
            Need(q.inputRoots!=null&&q.inputRoots.Length>0&&q.maxCsvBytes>0,"Configured input roots are required.");
            foreach(var source in c.Spots.SelectMany(s=>s.Actions.Select(a=>a.Source).Concat(s.Opponents))){string p=Path.GetFullPath(source.Path);Need(Path.IsPathRooted(source.Path)&&q.inputRoots.Any(r=>Within(r,p)),"CSV is outside configured inputRoots.");PlainPath(p);Need(File.Exists(p)&&new FileInfo(p).Length<=q.maxCsvBytes,"CSV missing or too large: "+p);}
        }
        static string Page<T>(IEnumerable<T> input,ResearchRequest q){var all=input.ToArray();Need(q.offset>=0&&q.limit>=1&&q.limit<=200,"Invalid pagination.");return Obj("total",J(all.Length),"offset",J(q.offset),"items",J(all.Skip(q.offset).Take(q.limit).ToArray()),"next_offset",J(q.offset+q.limit<all.Length?(int?)(q.offset+q.limit):null));}
        static ResearchCatalog Catalog(ResearchWorkspace w,string id){var c=w.Catalogs.SingleOrDefault(x=>x.Id==id);Need(c!=null,"Unknown catalog_id.");return c;}
        static ResearchConditionSet Set(ResearchWorkspace w,string id){var s=w.Sets.SingleOrDefault(x=>x.Id==id);Need(s!=null,"Unknown set_id.");return s;}
        static ResearchCatalog Select(ResearchRequest q,ResearchWorkspace w){var c=ResearchJson.Clone(Catalog(w,q.catalogId));Need(c.Revision==q.catalogRevision,"Catalog changed; validate a new plan.");if(q.spotIds!=null&&q.spotIds.Length>0){Need(q.spotIds.Distinct().Count()==q.spotIds.Length&&q.spotIds.All(id=>c.Spots.Any(s=>s.Id==id)),"Unknown or duplicate spot_ids.");c.Spots=c.Spots.Where(s=>q.spotIds.Contains(s.Id)).ToList();}return c;}
        static void CheckSources(ResearchRequest q,ResearchCatalog c,CancellationToken token,bool full){
            Scope(q,c);var errors=ResearchValidation.CheckFiles(c,token);Need(errors.Length==0,string.Join("\n",errors));
            if(full)foreach(var s in c.Spots){token.ThrowIfCancellationRequested();ResearchValidation.ValidateFiles(s,token);}
            Scope(q,c);errors=ResearchValidation.CheckFiles(c,token);Need(errors.Length==0,string.Join("\n",errors));
        }
        static string Meta(ResearchRunResult r){return Obj("result_id",J(r.Id),"status",J(r.Status),"started_utc",J(r.StartedUtc),"finished_utc",J(r.FinishedUtc),"target_spots",J(r.TargetSpots),"completed_spots",J(r.CompletedSpots),"filtered_spots",J(r.FilteredSpots),"row_count",J(r.Rows.Count),"error",J(r.Error),"classification_version",J(r.ClassificationVersion),"engine_version",J(r.EngineVersion),"application_version",J(r.ApplicationVersion),"settings",J(r.Settings),"catalog",J(r.Catalog),"condition_set",J(r.ConditionSet),"status_counts",J(r.Rows.GroupBy(x=>x.Status).ToDictionary(g=>g.Key,g=>g.Count())));}
        public static string Execute(ResearchRequest q,CancellationToken token,Action<ResearchProgress> progress){
            Need(q!=null&&!string.IsNullOrEmpty(q.historyRoot),"History root is required.");string dir=Path.Combine(Path.GetFullPath(q.historyRoot),"Research"),runs=Path.Combine(dir,"Runs");PlainPath(dir);PlainPath(runs);var store=new ResearchStore(dir);PlainPath(store.FilePath);PlainPath(store.FilePath+".lock");
            if(q.action=="workspace")return J(store.Load());
            if(q.action=="save_set"){
                var w=store.Load();Need(w.Revision==q.expectedRevision,"Workspace changed; reload before saving.");ResearchValidation.Set(q.conditionSet);var old=w.Sets.SingleOrDefault(s=>s.Id==q.conditionSet.Id);Need(q.conditionSet.Revision==(old==null?0:old.Revision),"Condition-set revision conflict.");if(old!=null)w.Sets.Remove(old);w.Sets.Add(q.conditionSet);var saved=store.Save(w,q.expectedRevision);return Obj("workspace_revision",J(saved.Revision),"condition_set",J(Set(saved,q.conditionSet.Id)));
            }
            if(q.action=="save_catalog"){
                var w=store.Load();Need(w.Revision==q.expectedRevision,"Workspace changed; reload before saving.");Need(q.catalog!=null&&q.catalog.Spots!=null&&q.catalog.Spots.Count<=200,"Save at most 200 spots at once.");Scope(q,q.catalog);var old=w.Catalogs.SingleOrDefault(c=>c.Id==q.catalog.Id);Need(q.catalog.Revision==(old==null?0:old.Revision),"Catalog revision conflict.");q.catalog.Spots=q.catalog.Spots.Select(s=>ResearchValidation.ValidateFiles(s,token)).ToList();CheckSources(q,q.catalog,token,false);ResearchValidation.Catalog(q.catalog);token.ThrowIfCancellationRequested();if(old!=null)w.Catalogs.Remove(old);w.Catalogs.Add(q.catalog);var saved=store.Save(w,q.expectedRevision);return Obj("workspace_revision",J(saved.Revision),"catalog",J(Catalog(saved,q.catalog.Id)));
            }
            if(q.action=="validate"||q.action=="run"){
                var w=store.Load();var c=Select(q,w);var s=ResearchJson.Clone(Set(w,q.setId));Need(s.Revision==q.setRevision,"Condition set changed; validate a new plan.");if(q.catalog!=null)Need(J(c)==J(q.catalog),"Catalog snapshot changed; validate again.");if(q.conditionSet!=null)Need(J(s)==J(q.conditionSet),"Condition snapshot changed; validate again.");ResearchRun.ValidateRequest(c,s,q.settings);
                var targets=ResearchJson.Clone(c);targets.Spots=targets.Spots.Where(x=>!q.settings.AhiOnly||ResearchFeatures.From(x.Board).Aces>0).ToList();
                CheckSources(q,targets,token,q.action=="validate");
                if(q.action=="validate")return Obj("catalog",J(c),"condition_set",J(s),"settings",J(q.settings),"target_spots",J(targets.Spots.Count),"estimated_trials",J((long)targets.Spots.Count*s.Conditions.Count(x=>x.Enabled)*q.settings.Trials),"resolved",J(targets.Spots.ToDictionary(x=>x.Id,x=>ResearchValidation.Resolve(s,x.Board))),"features",J(targets.Spots.ToDictionary(x=>x.Id,x=>ResearchFeatures.From(x.Board))));
                var r=ResearchRun.Execute(c,s,q.settings,token,progress);Scope(q,targets);PlainPath(runs);ResearchFiles.SaveHistory(r,runs);return Meta(r);
            }
            if(q.action=="history"){
                var values=new List<string>();int unreadable=0;if(Directory.Exists(runs))foreach(var p in Directory.EnumerateFiles(runs,"*.json").OrderByDescending(p=>p).Take(10000)){try{PlainPath(p);var r=ResearchFiles.Read(p);if((r.Catalog.Name+" "+r.ConditionSet.Name+" "+r.Status).IndexOf(q.query??"",StringComparison.OrdinalIgnoreCase)>=0)values.Add(Obj("result_id",J(r.Id),"status",J(r.Status),"finished_utc",J(r.FinishedUtc),"catalog_name",J(r.Catalog.Name),"set_name",J(r.ConditionSet.Name),"completed_spots",J(r.CompletedSpots),"target_spots",J(r.TargetSpots)));}catch{unreadable++;}}
                Need(q.offset>=0&&q.limit>=1&&q.limit<=200,"Invalid pagination.");return Obj("total",J(values.Count),"unreadable",J(unreadable),"items","["+string.Join(",",values.Skip(q.offset).Take(q.limit))+"]","next_offset",J(q.offset+q.limit<values.Count?(int?)(q.offset+q.limit):null));
            }
            Need(Regex.IsMatch(q.id??"","^[a-f0-9]{32}$"),"Invalid research result_id.");string resultPath=Path.Combine(runs,q.id+".json");PlainPath(resultPath);var result=ResearchFiles.Read(resultPath);
            if(q.action=="result"){
                if(q.view=="metadata")return Meta(result);
                if(q.view=="category")return Page(ResearchRun.Summaries(result).Where(x=>string.IsNullOrEmpty(q.conditionId)||x.ConditionId==q.conditionId),q);
                Need(q.view=="board","Unknown result view.");return Page(result.Rows.Where(x=>(string.IsNullOrEmpty(q.conditionId)||x.ConditionId==q.conditionId)&&(string.IsNullOrEmpty(q.spotId)||x.SpotId==q.spotId)),q);
            }
            if(q.action=="export"){
                Need(Regex.IsMatch(q.artifactId??"","^[a-f0-9-]{36}$"),"Invalid artifact ID.");string ext=q.format=="markdown"?"md":q.format;Need(new[]{"csv","json","svg","md"}.Contains(ext),"Unknown format.");PlainPath(q.outputRoot);string p=Path.Combine(Path.GetFullPath(q.outputRoot),q.artifactId+"."+ext);PlainPath(p);byte[] bytes=ext=="csv"?ResearchFiles.Csv(result):ext=="json"?ResearchFiles.Encode(result):ext=="svg"?ResearchFiles.Svg(result,q.grouped):ResearchFiles.Markdown(result);using(var f=new FileStream(p,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(bytes,0,bytes.Length);}return Obj("path",J(p),"bytes",J(bytes.Length));
            }
            throw new ArgumentException("Unknown research action.");
        }
    }
    static class ResearchHost {
        static readonly object Gate=new object();
        static void Send(string kind,string value){lock(Gate){Console.WriteLine("{\"type\":"+ResearchBridge.J(kind)+",\"value\":"+value+"}");Console.Out.Flush();}}
        static int Main(){
            Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
            try{string line=Console.ReadLine();if(line==null||Encoding.UTF8.GetByteCount(line)>ResearchJson.MaxBytes)throw new ArgumentException("Invalid request size.");var q=ResearchJson.Decode<ResearchRequest>(Encoding.UTF8.GetBytes(line));using(var cancel=new CancellationTokenSource()){
                // stdin stays open during the job; a control line cancels cooperatively.
                Task.Run(()=>{try{string control;while((control=Console.ReadLine())!=null){if(control=="cancel")cancel.Cancel();}}catch(ObjectDisposedException){}});
                var watch=System.Diagnostics.Stopwatch.StartNew();string lastMessage=null;var result=ResearchBridge.Execute(q,cancel.Token,p=>{if(p.Message!=lastMessage||watch.ElapsedMilliseconds>=100||p.Completed==p.Total){lastMessage=p.Message;watch.Restart();Send("progress",ResearchBridge.J(p));}});Send("result",result);return 0;
            }}catch(Exception ex){Send("error",ResearchBridge.J(ex.Message));return 1;}
        }
    }
}
