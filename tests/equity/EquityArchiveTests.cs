using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Globalization;
using System.Xml;
using FlopCommands.Equity;
namespace EquityTests {
    public static class ArchiveTests {
        static int checks;
        static void Check(bool value,string message){checks++;if(!value)throw new Exception("Archive: "+message);}
        static void Fails(Action work,string message){bool failed=false;try{work();}catch(ArgumentException){failed=true;}catch(IOException){failed=true;}Check(failed,message);}
        public static void Run(){
            string dir=Path.Combine(Path.GetTempPath(),"plo-archive-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            var oldCulture=Thread.CurrentThread.CurrentCulture;
            try{
                string check=Path.Combine(dir,"check.csv"),bet=Path.Combine(dir,"bet.csv");File.WriteAllText(check,"hand,weight\nAcAd4c5d,0.2");File.WriteAllText(bet,"hand,weight\nAcAd4c5d,0.8");
                var input=new EquityJobRequest{Board="2c3d7h9sTc",HeroMode=EquityHeroMode.AllActions,CommonScaleConfirmed=true,ActionPaths=new[]{check,bet},ActionLabels=new[]{"check","=危険,\"quoted\" <&>"},BetActions=new[]{false,true},VillainText="KcKd4h5h@25%,Jc8hKhQh@75%",Conditions=new[]{new EquityCondition{Label="AA,日本語\n<&>",Syntax="AA"},new EquityCondition{Label="empty",Syntax="KK"}},Settings=new EquitySettings{Seed=123}};
                var job=EquityJob.Run(input,CancellationToken.None,null);var a=EquityFiles.Create(job);var bytes=EquityFiles.Encode(a);var restored=EquityFiles.Decode(bytes);
                Check(restored.Job.Rows[0].Result.Equity==25,"roundtrip exact EQ");Check(restored.Job.Rows[1].Result==null,"empty row stays null");Check(restored.Job.Input.VillainText==input.VillainText,"opponent range preserved");Check(restored.Job.BaseSeed==123&&restored.Job.Input.Settings.Seed==123,"settings and seed preserved");Check(restored.Job.Rows[0].Frequency.Percent[1]==80,"frequency preserved");Check(EquityPlot.Points(restored.Job).Length==1,"restored plot");Check(restored.Job.Input.Conditions[0].Label==input.Conditions[0].Label,"Unicode and newline roundtrip");
                var store=new EquityHistoryStore(Path.Combine(dir,"history"));var saved=store.Save(job);Check(store.Load(saved.Id).Job.Rows[0].Result.Equity==25,"disk history roundtrip");File.Delete(check);File.Delete(bet);Check(EquityPlot.Points(store.Load(saved.Id).Job).Single().Frequency==80,"history restores without CSV files");
                File.WriteAllText(Path.Combine(store.Root,"broken.json"),"{");int unreadable;Check(store.List(out unreadable).Count==1&&unreadable==1,"corrupt history isolated");Fails(()=>store.Load("../bad"),"path traversal rejected");
                Thread.CurrentThread.CurrentCulture=new CultureInfo("fr-FR");string csv=Encoding.UTF8.GetString(EquityFiles.Csv(a));Check(csv[0]=='\ufeff',"CSV UTF8 BOM");Check(csv.Contains("\"25\"")&&csv.Contains("\"80\""),"CSV invariant numbers");Check(csv.Contains("\"'=危険,\"\"quoted\"\" <&>\""),"CSV formula text escaped");Check(csv.Contains("\"AA,日本語\n<&>\""),"CSV multiline condition quoting");Check(csv.Contains("\"empty\",\"KK\""),"undefined rows included");
                byte[] svg=EquityFiles.Svg(a);var document=new XmlDocument();document.XmlResolver=null;document.LoadXml(Encoding.UTF8.GetString(svg));Check(document.DocumentElement.NamespaceURI=="http://www.w3.org/2000/svg","valid SVG namespace");Check(document.GetElementsByTagName("script").Count==0,"labels cannot create elements");Check(document.GetElementsByTagName("title").Cast<XmlNode>().Any(n=>n.InnerText.Contains("<&>")),"SVG labels are escaped but recoverable");Check(Encoding.UTF8.GetString(svg).Contains("x=\"317.5\""),"EQ25 maps to correct SVG coordinate");
                string json=Path.Combine(dir,"result.json"),output=Path.Combine(dir,"result.csv");EquityFiles.Export(a,json,".json","app.exe");EquityFiles.Export(a,output,".csv","app.exe");Check(EquityFiles.Decode(File.ReadAllBytes(json)).Id==a.Id,"JSON export roundtrip");EquityFiles.Export(a,output,".csv","app.exe");Check(!Directory.GetFiles(dir,"*.tmp").Any(),"atomic overwrite cleans temp");
                File.WriteAllText(check,"original");Fails(()=>EquityFiles.Export(a,check,".csv","app.exe"),"action CSV overwrite blocked");Check(File.ReadAllText(check)=="original","input file preserved");a.Job.Input.RangeInputPaths=new[]{output};Fails(()=>EquityFiles.Export(a,output,".csv","app.exe"),"imported range CSV overwrite blocked");a.Job.Input.RangeInputPaths=new string[0];Fails(()=>EquityFiles.Export(a,output,".csv",output),"executable overwrite blocked");Fails(()=>EquityFiles.Export(a,json,".csv","app.exe"),"wrong extension rejected");
                Fails(()=>EquityFiles.WriteAtomic(output,new byte[]{1},false),"no-overwrite atomic write");Check(Encoding.UTF8.GetString(File.ReadAllBytes(output)).Contains("saved_utc"),"existing output preserved on refusal");
                restored.Schema=99;Fails(()=>EquityFiles.Encode(restored),"unknown schema rejected");restored=EquityFiles.Decode(bytes);restored.Job.Rows[0].Result.Equity=double.NaN;Fails(()=>EquityFiles.Encode(restored),"NaN rejected");restored=EquityFiles.Decode(bytes);restored.Job.Rows[0].Frequency.Percent=new[]{100.0};Fails(()=>EquityFiles.Encode(restored),"mismatched frequency length rejected");Fails(()=>EquityFiles.Decode(Encoding.UTF8.GetBytes("{}")),"missing properties rejected");Fails(()=>EquityFiles.Create(null),"no incomplete results saved");
                restored=EquityFiles.Decode(bytes);restored.Job.Input.BetActions=new[]{false,false};Fails(()=>EquityFiles.Svg(restored),"no-points SVG rejected");store.Delete(saved.Id);Check(store.List(out unreadable).Count==0,"delete selected history only");Check(File.Exists(json)&&File.Exists(check),"delete leaves exports and inputs");
                // Produce a real export for visual inspection and the validation record.
                var target=Path.Combine("bdsd-work","test-results","equity");if(Directory.Exists(target)){File.WriteAllBytes(Path.Combine(target,"archive-preview.svg"),svg);File.WriteAllBytes(Path.Combine(target,"archive-preview.json"),bytes);}
                Console.WriteLine("EQ archive + export: "+checks+" checks passed.");
            }finally{Thread.CurrentThread.CurrentCulture=oldCulture;Directory.Delete(dir,true);}
        }
    }
}
