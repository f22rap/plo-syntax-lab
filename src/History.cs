using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace FlopCommands {
    public sealed class HistoryEntry {
        public int Schema {get;set;}
        public string Id {get;set;}
        public string SavedUtc {get;set;}
        public string Board {get;set;}
        public string WorkbookPath {get;set;}
        public string[] Labels {get;set;}
        public string[] Paths {get;set;}
        public string[] Conditions {get;set;}
        public string Command {get;set;}
        public string ResultJson {get;set;}
        public string CsvBase64 {get;set;}
        public object Provenance {get;set;}
        [ScriptIgnore]public DateTime LocalTime {get{return DateTime.Parse(SavedUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToLocalTime();}}
        public RunResult Restore(){return ResultCodec.Decode(ResultJson,Convert.FromBase64String(CsvBase64));}
    }
    public sealed class HistoryStore {
        public static string DefaultRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FlopCommandApp","History");
        public readonly string Root;
        public HistoryStore():this(DefaultRoot){}
        public HistoryStore(string root){Root=Path.GetFullPath(root);}
        JavaScriptSerializer Serializer(){return new JavaScriptSerializer{MaxJsonLength=64*1024*1024};}
        string FilePath(string id){
            if(!Regex.IsMatch(id??"",@"^\d{8}T\d{13}Z_[a-f0-9]{32}$"))throw new Exception("履歴IDが正しくありません。");
            return Path.Combine(Root,id+".json");
        }
        public HistoryEntry Save(RunResult result,string command,string workbookPath,object provenance=null){
            if(result==null||result.Rows.Count==0||string.IsNullOrEmpty(result.Json)||result.Csv==null)throw new Exception("保存する集計結果がありません。");
            DateTime now=DateTime.UtcNow;
            var entry=new HistoryEntry{Schema=1,Id=now.ToString("yyyyMMddTHHmmssfffffffZ",CultureInfo.InvariantCulture)+"_"+Guid.NewGuid().ToString("N"),SavedUtc=now.ToString("o",CultureInfo.InvariantCulture),Board=result.Rows[0].Board,WorkbookPath=workbookPath,Labels=result.Rows[0].Sources.Select(s=>s.Label).ToArray(),Paths=result.Rows[0].Sources.Select(s=>s.Path).ToArray(),Conditions=result.Rows.Select(r=>r.Label).ToArray(),Command=command,ResultJson=result.Json,CsvBase64=Convert.ToBase64String(result.Csv)};
            entry.Provenance=provenance;
            string target=FilePath(entry.Id),temp=target+".tmp";
            Directory.CreateDirectory(Root);
            byte[] bytes=new UTF8Encoding(false).GetBytes(Serializer().Serialize(entry));
            try{
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                File.Move(temp,target);
            }finally{if(File.Exists(temp))try{File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
            return entry;
        }
        public void Delete(string id){File.Delete(FilePath(id));}
        public HistoryEntry Load(string id){
            var entry=Serializer().Deserialize<HistoryEntry>(File.ReadAllText(FilePath(id),Encoding.UTF8));
            DateTime parsed;
            if(entry==null||entry.Schema!=1||entry.Id!=id||!DateTime.TryParse(entry.SavedUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out parsed)||string.IsNullOrEmpty(entry.Board)||entry.Labels==null||entry.Paths==null||entry.Labels.Length!=entry.Paths.Length||entry.Paths.Length<2||entry.Paths.Length>3||entry.Conditions==null||entry.Conditions.Length==0||string.IsNullOrEmpty(entry.ResultJson)||string.IsNullOrEmpty(entry.CsvBase64))throw new Exception("履歴ファイルの形式が正しくありません。");
            return entry;
        }
        public List<HistoryEntry> List(out int unreadable){
            unreadable=0;var entries=new List<HistoryEntry>();
            if(!Directory.Exists(Root))return entries;
            foreach(string path in Directory.EnumerateFiles(Root,"*.json")){
                try{entries.Add(Load(Path.GetFileNameWithoutExtension(path)));}
                catch(Exception){unreadable++;}
            }
            return entries.OrderByDescending(e=>e.SavedUtc,StringComparer.Ordinal).ThenByDescending(e=>e.Id,StringComparer.Ordinal).ToList();
        }
    }
}
