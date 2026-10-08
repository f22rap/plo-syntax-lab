using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Threading;
using System.Web.Script.Serialization;
using FlopCommands;

namespace PloMcp {
    public sealed class Request {
        public string action,board,historyRoot,id,json,csvBase64,command;
        public string[] paths,labels;
        public LabRange[] ranges;
        public bool includeAll;
        public object provenance;
    }
    static class NativeHost {
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=64*1024*1024};
        static string Number(decimal value){return value.ToString(CultureInfo.InvariantCulture);}
        static object Rows(RunResult result){return result.Rows.Select(r=>new {
            board=r.Board,label=r.Label,syntax=r.Syntax,status=r.Status,note=r.Note,total_weight=Number(r.Total),
            sources=r.Sources.Select(s=>new {path=s.Path,label=s.Label,matched_hands=s.Count,weight_sum=Number(s.Sum),percent=s.Percent.HasValue?Number(s.Percent.Value):null}).ToArray()
        }).ToArray();}
        static object Execute(Request q){
            if(q==null)throw new Exception("Request is required.");
            if(q.action=="compare"){
                string board=Commands.NormalizeBoard(q.board);
                if(q.paths==null||q.paths.Length<2||q.paths.Length>3)throw new Exception("Two or three CSVs are required.");
                if(q.ranges==null||q.ranges.Length>185)throw new Exception("Invalid conditions.");
                var choices=q.ranges.Select((r,i)=>{
                    if(r==null||r.count<=0||string.IsNullOrWhiteSpace(r.label))throw new Exception("An empty condition cannot be compared.");
                    string syntax=Commands.NormalizeSyntax(r.syntax);new PloSyntax.SyntaxMatcher(syntax);
                    return new Choice{Filter=new Filter{Cell=string.IsNullOrEmpty(r.cell)?"MCP_"+i:r.cell,Label=r.label,Syntax=syntax}};
                }).ToArray();
                if(choices.Any(c=>c.Filter.Cell=="ALL"))throw new Exception("ALL is reserved.");
                string command=CompareCommands.Build(board,q.paths,choices,q.includeAll,q.labels);
                var result=CommandRunner.RunAsync(command,CancellationToken.None).GetAwaiter().GetResult();
                return new {rows=Rows(result),json=result.Json,csvBase64=Convert.ToBase64String(result.Csv),command=command};
            }
            if(string.IsNullOrWhiteSpace(q.historyRoot))throw new Exception("History root is required.");
            var store=new HistoryStore(q.historyRoot);
            if(q.action=="save"){
                var result=ResultCodec.Decode(q.json,Convert.FromBase64String(q.csvBase64));
                var entry=store.Save(result,q.command,"",q.provenance);
                return new {id=entry.Id,saved_utc=entry.SavedUtc};
            }
            if(q.action=="list"){
                int skipped;var entries=store.List(out skipped);
                return new {unreadable=skipped,entries=entries.Select(e=>new {id=e.Id,saved_utc=e.SavedUtc,board=e.Board,labels=e.Labels,conditions=e.Conditions}).ToArray()};
            }
            if(q.action=="get"){
                var e=store.Load(q.id);
                return new {id=e.Id,saved_utc=e.SavedUtc,board=e.Board,rows=Rows(e.Restore()),command=e.Command,csvBase64=e.CsvBase64,provenance=e.Provenance};
            }
            if(q.action=="delete"){store.Load(q.id);store.Delete(q.id);return new {deleted=q.id};}
            throw new Exception("Unknown action.");
        }
        static int Main(){
            Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
            try{Console.Write(Json.Serialize(Execute(Json.Deserialize<Request>(Console.In.ReadToEnd()))));return 0;}
            catch(Exception ex){Console.Error.Write(ex.Message);return 1;}
        }
    }
}
