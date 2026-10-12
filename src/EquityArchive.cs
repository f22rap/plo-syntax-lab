using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace FlopCommands.Equity {
    public sealed class EquityArchive {
        public string App="PLO Syntax Lab EQ",Id,CreatedUtc;
        public int Schema=1;
        public EquityJobResult Job;
    }
    public static class EquityFiles {
        public const int MaxBytes=64*1024*1024;
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        public static EquityArchive Create(EquityJobResult job){
            if(job==null||job.Rows==null||!job.Rows.Any(r=>r.Result!=null))throw new ArgumentException("保存できる確定EQがありません。");
            var record=new EquityArchive{Id=Guid.NewGuid().ToString("N"),CreatedUtc=DateTime.UtcNow.ToString("o",Inv),Job=job};Validate(record);return record;
        }
        public static byte[] Encode(EquityArchive record){Validate(record);using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(EquityArchive)).WriteObject(stream,record);if(stream.Length>MaxBytes)throw new ArgumentException("保存データは64MiB以下にしてください。");return stream.ToArray();}}
        public static EquityArchive Decode(byte[] bytes){
            if(bytes==null||bytes.Length>MaxBytes)throw new ArgumentException("履歴は64MiB以下にしてください。");
            try{using(var stream=new MemoryStream(bytes)){var record=(EquityArchive)new DataContractJsonSerializer(typeof(EquityArchive)).ReadObject(stream);Validate(record);return record;}}
            catch(Exception e){if(e is OutOfMemoryException)throw;throw new ArgumentException("EQ履歴の形式が正しくありません。",e);}
        }
        static bool Finite(double n){return !double.IsNaN(n)&&!double.IsInfinity(n);}
        static bool Percent(double n){return Finite(n)&&n>=0&&n<=100;}
        public static void Validate(EquityArchive a){
            DateTime time;
            if(a==null||a.Schema!=1||a.App!="PLO Syntax Lab EQ"||!Regex.IsMatch(a.Id??"","^[a-f0-9]{32}$")||!DateTime.TryParse(a.CreatedUtc,Inv,DateTimeStyles.RoundtripKind,out time))throw new ArgumentException("EQ履歴の版・日時・IDが不正です。");
            var j=a.Job;if(j==null||j.Input==null||j.Rows==null||j.Rows.Length==0||j.Rows.Length>1000||!Finite(j.ElapsedMs)||j.ElapsedMs<0)throw new ArgumentException("EQ結果が不正です。");
            var input=j.Input;EquityCards.Parse(input.Board,3,5);
            if(input.Conditions==null||input.Conditions.Length!=j.Rows.Length||input.Settings==null||input.ActionPaths==null||input.ActionLabels==null||input.BetActions==null||input.ActionPaths.Length!=input.ActionLabels.Length||input.ActionPaths.Length>3||input.BetActions.Length>3||!Enum.IsDefined(typeof(EquityHeroMode),input.HeroMode))throw new ArgumentException("保存時の入力条件が不正です。");
            if(input.RangeInputPaths==null)input.RangeInputPaths=new string[0];
            for(int i=0;i<j.Rows.Length;i++){
                var row=j.Rows[i];var c=input.Conditions[i];if(row==null||c==null||string.IsNullOrWhiteSpace(row.Label)||string.IsNullOrWhiteSpace(row.Syntax)||row.Label!=c.Label||row.Syntax!=c.Syntax)throw new ArgumentException("保存したsyntaxが一致しません。");
                var r=row.Result;if(r!=null){
                    if(r.CalculationVersion!="pre-action-joint-v1"||!new[]{r.Equity,r.VillainEquity,r.Win,r.Tie,r.Loss,r.CiLow,r.CiHigh}.All(Percent)||r.CiLow>r.Equity||r.CiHigh<r.Equity||Math.Abs(r.Equity+r.VillainEquity-100)>1e-6||r.HeroCount<1||r.VillainCount<1||r.Total<1||r.LegalPairs<1||!Finite(r.ElapsedMs)||r.ElapsedMs<0)throw new ArgumentException("保存したEQ数値が不正です。");
                }
                var f=row.Frequency;if(f!=null&&f.Percent!=null&&(f.Percent.Length!=input.ActionPaths.Length||input.BetActions.Length!=f.Percent.Length||!f.Percent.All(Percent)||Math.Abs(f.Percent.Sum()-100)>1e-6))throw new ArgumentException("保存した頻度が不正です。");
            }
            if(!j.Rows.Any(r=>r.Result!=null))throw new ArgumentException("確定EQがありません。");
        }
        static string Cell(string value){value=value??"";string start=value.TrimStart();if(start.Length>0&&"=+-@".IndexOf(start[0])>=0)value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
        static string Number(double v){return v.ToString("R",Inv);}
        public static byte[] Csv(EquityArchive record){
            Validate(record);var j=record.Job;var b=new StringBuilder();b.AppendLine("saved_utc,board,condition,syntax,action_csv,action_label,is_bet,eq_percent,ci_low,ci_high,frequency_percent,method,trials,seed,hero_combos,villain_combos,legal_pairs,status,frequency_status");
            foreach(var row in j.Rows){var r=row.Result;int count=Math.Max(1,j.Input.ActionPaths.Length);
                for(int a=0;a<count;a++){
                    bool action=a<j.Input.ActionPaths.Length;var f=row.Frequency;
                    string[] values={record.CreatedUtc,j.Input.Board,row.Label,row.Syntax,action?(a+1).ToString(Inv):"",action?j.Input.ActionLabels[a]:"",action&&a<j.Input.BetActions.Length?j.Input.BetActions[a]?"true":"false":"",r==null?"":Number(r.Equity),r==null?"":Number(r.CiLow),r==null?"":Number(r.CiHigh),f==null||f.Percent==null||!action?"":Number(f.Percent[a]),r==null?"":r.Exact?"exact":"monte_carlo",r==null?"":r.Total.ToString(Inv),r==null?"":r.Seed.ToString(Inv),r==null?"":r.HeroCount.ToString(Inv),r==null?"":r.VillainCount.ToString(Inv),r==null?"":r.LegalPairs.ToString(Inv),row.Status,f==null?j.PlotStatus:f.Status};
                    b.Append(string.Join(",",values.Select(Cell))).Append("\r\n");
                }
            }
            return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(b.ToString())).ToArray();
        }
        static string N(double n){return n.ToString("0.###",Inv);}
        static void Element(XmlWriter x,string tag,params string[] pairs){x.WriteStartElement(tag);for(int i=0;i<pairs.Length;i+=2)x.WriteAttributeString(pairs[i],pairs[i+1]);x.WriteEndElement();}
        static void Text(XmlWriter x,double px,double py,string text,string fill,string anchor){x.WriteStartElement("text");x.WriteAttributeString("x",N(px));x.WriteAttributeString("y",N(py));x.WriteAttributeString("fill",fill);x.WriteAttributeString("text-anchor",anchor);x.WriteString(text??"");x.WriteEndElement();}
        public static byte[] Svg(EquityArchive record){
            Validate(record);var points=EquityPlot.Points(record.Job);if(points.Length==0)throw new ArgumentException("保存できる散布図がありません。");
            using(var stream=new MemoryStream()){
                using(var x=XmlWriter.Create(stream,new XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=true,CloseOutput=false})){
                    x.WriteStartElement("svg","http://www.w3.org/2000/svg");x.WriteAttributeString("viewBox","0 0 1100 760");x.WriteAttributeString("width","1100");x.WriteAttributeString("height","760");x.WriteAttributeString("font-family","sans-serif");x.WriteAttributeString("font-size","15");
                    x.WriteElementString("title","PLO Syntax Lab — EQ vs Bet Frequency");x.WriteElementString("desc","Pre-action equity vs bet frequency. Horizontal bars: Monte Carlo 95% confidence interval. Hover points for syntax details.");
                    Element(x,"rect","width","1100","height","760","fill","white");Text(x,80,32,record.Job.Input.Board+" · EQ vs Bet Frequency","#162330","start");Text(x,80,55,record.CreatedUtc+" · Base seed "+record.Job.BaseSeed,"#5e6c78","start");
                    string[] colors={"#2563eb","#008470","#b45309"};int legend=0;
                    foreach(var group in points.GroupBy(p=>p.ActionIndex)){var p=group.First();Element(x,"rect","x",N(80+legend*325),"y","72","width","12","height","12","fill",colors[p.ActionIndex]);Text(x,100+legend*325,84,p.Action+" (CSV"+(p.ActionIndex+1)+")",colors[p.ActionIndex],"start");legend++;}
                    const double left=90,top=125,width=930,height=510;
                    for(int v=0;v<=100;v+=20){double px=left+width*v/100,py=top+height-height*v/100;Element(x,"line","x1",N(px),"x2",N(px),"y1",N(top),"y2",N(top+height),"stroke","#e1e8ee");Element(x,"line","x1",N(left),"x2",N(left+width),"y1",N(py),"y2",N(py),"stroke","#e1e8ee");Text(x,px,top+height+27,v.ToString(Inv),"#5e6c78","middle");Text(x,left-15,py+5,v.ToString(Inv),"#5e6c78","end");}
                    Element(x,"rect","x",N(left),"y",N(top),"width",N(width),"height",N(height),"fill","none","stroke","#a0aeb9");Text(x,90,112,"Bet frequency (%)","#162330","start");Text(x,555,695,"Pre-action hero equity (%)","#162330","middle");
                    foreach(var p in points){double px=left+width*p.Equity/100,py=top+height-height*p.Frequency/100;string color=colors[p.ActionIndex];x.WriteStartElement("g");x.WriteElementString("title",p.Label+" | "+p.Syntax+" | "+p.Action+" (CSV"+(p.ActionIndex+1)+") | EQ "+N(p.Equity)+"% | Bet "+N(p.Frequency)+"% | "+(p.Exact?"Exact":"95% CI "+N(p.CiLow)+"–"+N(p.CiHigh)));
                        if(!p.Exact)Element(x,"line","x1",N(left+width*p.CiLow/100),"x2",N(left+width*p.CiHigh/100),"y1",N(py),"y2",N(py),"stroke",color,"stroke-width","2","opacity","0.45");
                        if(p.ActionIndex==0)Element(x,"circle","cx",N(px),"cy",N(py),"r","5","fill",color,"stroke","white");
                        else if(p.ActionIndex==1)Element(x,"rect","x",N(px-5),"y",N(py-5),"width","10","height","10","fill",color,"stroke","white");
                        else Element(x,"polygon","points",N(px)+","+N(py-6)+" "+N(px+6)+","+N(py+5)+" "+N(px-6)+","+N(py+5),"fill",color,"stroke","white");
                        x.WriteEndElement();
                    }
                    Text(x,80,730,"Points: "+points.Length+" · Horizontal bars: EQ 95% CI · See companion CSV / JSON for every condition.","#5e6c78","start");x.WriteEndElement();
                }return stream.ToArray();
            }
        }
        public static void WriteAtomic(string path,byte[] bytes,bool overwrite){
            string full=Path.GetFullPath(path),temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(bytes,0,bytes.Length);f.Flush(true);}if(File.Exists(full)){if(!overwrite)throw new IOException("同名のファイルが存在します。");File.Replace(temp,full,null);}else File.Move(temp,full);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        public static void Export(EquityArchive record,string path,string extension,string executable){
            Validate(record);string full=Path.GetFullPath(path);if(!string.Equals(Path.GetExtension(full),extension,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("保存先は "+extension+" にしてください。");
            foreach(var input in record.Job.Input.ActionPaths.Concat(record.Job.Input.RangeInputPaths).Concat(new[]{executable}))if(!string.IsNullOrWhiteSpace(input)&&string.Equals(full,Path.GetFullPath(input),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("入力ファイルやEXEには上書きできません。");
            byte[] bytes=extension==".csv"?Csv(record):extension==".json"?Encode(record):extension==".svg"?Svg(record):null;if(bytes==null)throw new ArgumentException("未対応の保存形式です。");WriteAtomic(full,bytes,true);
        }
    }
    public sealed class EquityHistoryStore {
        public readonly string Root;
        public EquityHistoryStore(string root){Root=Path.GetFullPath(root);}
        string FilePath(string id){if(!Regex.IsMatch(id??"","^[a-f0-9]{32}$"))throw new ArgumentException("EQ履歴IDが不正です。");return Path.Combine(Root,id+".json");}
        public EquityArchive Save(EquityJobResult result){var a=EquityFiles.Create(result);Directory.CreateDirectory(Root);EquityFiles.WriteAtomic(FilePath(a.Id),EquityFiles.Encode(a),false);return a;}
        public EquityArchive Load(string id){string path=FilePath(id);if(new FileInfo(path).Length>EquityFiles.MaxBytes)throw new ArgumentException("履歴が大きすぎます。");var a=EquityFiles.Decode(File.ReadAllBytes(path));if(a.Id!=id)throw new ArgumentException("履歴IDが一致しません。");return a;}
        public List<EquityArchive> List(out int unreadable){var result=new List<EquityArchive>();unreadable=0;if(Directory.Exists(Root))foreach(string path in Directory.EnumerateFiles(Root,"*.json")){try{result.Add(Load(Path.GetFileNameWithoutExtension(path)));}catch(Exception){unreadable++;}}return result.OrderByDescending(a=>a.CreatedUtc,StringComparer.Ordinal).ToList();}
        public void Delete(string id){File.Delete(FilePath(id));}
    }
}
