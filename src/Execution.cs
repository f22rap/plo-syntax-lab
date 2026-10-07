using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FlopCommands {
    public sealed class CsvResult {public string Path,Label;public int Count;public decimal Sum;public decimal? Percent;}
    public sealed class ResultRow {
        public string Label,Syntax,Status,Note,Board;
        public decimal Total;
        public readonly List<CsvResult> Sources=new List<CsvResult>();
    }
    public sealed class RunResult {
        public readonly List<ResultRow> Rows=new List<ResultRow>();
        public byte[] Csv;
        public string Json;
    }
    public static class CommandRunner {
        public static async Task<RunResult> RunAsync(string code,CancellationToken cancellation) {
            string root=Path.Combine(Path.GetTempPath(),"FlopCommandApp");
            string directory=Path.Combine(root,Guid.NewGuid().ToString("N"));
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            try {
                string script=Path.Combine(directory,"commands.ps1"), wrapper=Path.Combine(directory,"run.ps1");
                string csv=Path.Combine(directory,"result.csv"), json=Path.Combine(directory,"result.json");
                File.WriteAllText(script,code,new UTF8Encoding(true));
                string launch="$ErrorActionPreference = 'Stop'\r\n"+
                    "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)\r\n"+
                    "try {\r\n"+
                    "    $items = @(& "+Commands.Quote(script)+" -OutputPath "+Commands.Quote(csv)+")\r\n"+
                    "    $json = ConvertTo-Json -InputObject $items -Depth 6\r\n"+
                    "    [IO.File]::WriteAllText("+Commands.Quote(json)+", $json, [Text.UTF8Encoding]::new($false))\r\n"+
                    "    exit 0\r\n"+
                    "} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }\r\n";
                File.WriteAllText(wrapper,launch,new UTF8Encoding(true));
                string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
                if(!File.Exists(powershell))throw new Exception("Windows PowerShellが見つかりません。");
                using(var process=new Process()) {
                    process.StartInfo=new ProcessStartInfo {
                        FileName=powershell,
                        Arguments="-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+wrapper+"\"",
                        WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
                        RedirectStandardOutput=true,RedirectStandardError=true,
                        StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
                    };
                    cancellation.ThrowIfCancellationRequested();
                    if(!process.Start())throw new Exception("PowerShellを起動できませんでした。");
                    using(cancellation.Register(()=>{try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}})) {
                        Task<string> stdout=process.StandardOutput.ReadToEndAsync(), stderr=process.StandardError.ReadToEndAsync();
                        await Task.Run(()=>process.WaitForExit()).ConfigureAwait(false);
                        string output=await stdout.ConfigureAwait(false), error=await stderr.ConfigureAwait(false);
                        cancellation.ThrowIfCancellationRequested();
                        if(process.ExitCode!=0)throw new Exception(string.IsNullOrWhiteSpace(error)?"PowerShellの実行に失敗しました。\r\n"+output:error.Trim());
                    }
                }
                if(!File.Exists(csv)||!File.Exists(json))throw new Exception("PowerShellから集計結果が返されませんでした。");
                return ResultCodec.Decode(File.ReadAllText(json,Encoding.UTF8),File.ReadAllBytes(csv));
            } finally {
                // Only this run's GUID directory is owned by the runner.
                string full=Path.GetFullPath(directory), prefix=Path.GetFullPath(root)+Path.DirectorySeparatorChar;
                if(full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(full)) {
                    try{Directory.Delete(full,true);}catch(IOException){}catch(UnauthorizedAccessException){}
                }
            }
        }
    }
    public static class ResultCodec {
        public static RunResult Decode(string json,byte[] csv){
            var serializer=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};
            var entries=serializer.DeserializeObject(json) as object[];
            if(entries==null)throw new Exception("集計結果の形式が正しくありません。");
            var result=new RunResult{Csv=csv,Json=json};
            foreach(object entry in entries){
                var item=entry as Dictionary<string,object>;
                if(item==null)throw new Exception("集計結果の行を読み込めません。");
                var row=new ResultRow{Label=Text(item,"label"),Syntax=Text(item,"syntax"),Status=Text(item,"status"),Note=Text(item,"note"),Board=Text(item,"board"),Total=Convert.ToDecimal(item["total_weight"],CultureInfo.InvariantCulture)};
                int count=Convert.ToInt32(item["csv_count"],CultureInfo.InvariantCulture);
                if(count<2||count>3)throw new Exception("集計結果のCSV数が正しくありません。");
                for(int i=1;i<=count;i++){
                    string prefix="csv"+i;
                    row.Sources.Add(new CsvResult{Path=Text(item,prefix+"_path"),Label=Text(item,prefix+"_label"),Count=Convert.ToInt32(item[prefix+"_matched_hands"],CultureInfo.InvariantCulture),Sum=Convert.ToDecimal(item[prefix+"_weight_sum"],CultureInfo.InvariantCulture),Percent=item[prefix+"_percent"]==null?(decimal?)null:Convert.ToDecimal(item[prefix+"_percent"],CultureInfo.InvariantCulture)});
                }
                result.Rows.Add(row);
            }
            if(result.Rows.Count==0)throw new Exception("集計結果が0行でした。");
            return result;
        }
        static string Text(Dictionary<string,object> item,string key){return Convert.ToString(item[key],CultureInfo.InvariantCulture);}
    }
}
