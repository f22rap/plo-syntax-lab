using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FlopCommands {
    public static class DefaultRunner {
        static readonly System.Collections.Generic.List<Process> active=new System.Collections.Generic.List<Process>();
        static void Stop(Process process){try{if(!process.HasExited){using(var killer=Process.Start(new ProcessStartInfo{FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"taskkill.exe"),Arguments="/PID "+process.Id+" /T /F",UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){killer.WaitForExit(5000);}}}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
        public static void StopAll(){Process[] processes;lock(active)processes=active.ToArray();foreach(var process in processes)Stop(process);}
        static string Browser(){
            string[] roots={Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)};
            foreach(string relative in new[]{@"Microsoft\Edge\Application\msedge.exe",@"Google\Chrome\Application\chrome.exe"})foreach(string root in roots){string path=Path.Combine(root,relative);if(File.Exists(path))return path;}
            throw new Exception("デフォルトsyntaxの生成にはEdgeまたはChromeが必要です。");
        }
        public static async Task<LabSelection> RunAsync(string url,Task<LabSelection> result,CancellationToken cancellation){
            string browser=Browser(),root=Path.Combine(Path.GetTempPath(),"FlopCommandApp","DefaultBrowser"),profile=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(profile);
            using(var process=new Process())try{
                process.StartInfo=new ProcessStartInfo{FileName=browser,Arguments="--headless --disable-gpu --no-first-run --no-default-browser-check --disable-background-networking --disable-extensions --user-data-dir=\""+profile+"\" \""+url+"\"",UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true,RedirectStandardOutput=true};
                cancellation.ThrowIfCancellationRequested();process.Start();lock(active)active.Add(process);File.WriteAllText(Path.Combine(profile,"owner-pid.txt"),process.Id.ToString());
                Task<string> errors=process.StandardError.ReadToEndAsync(),stdout=process.StandardOutput.ReadToEndAsync();
                Task exited=Task.Run(()=>process.WaitForExit());
                var cancel=new TaskCompletionSource<bool>();using(cancellation.Register(()=>cancel.TrySetResult(true))){
                    var done=await Task.WhenAny(result,Task.Delay(TimeSpan.FromMinutes(4)),cancel.Task,exited).ConfigureAwait(false);cancellation.ThrowIfCancellationRequested();
                    if(done==exited)throw new Exception("生成用ブラウザが終了しました（"+process.ExitCode+"）。「再生成」をお試しください。");
                    if(done!=result)throw new Exception("生成が時間内に完了しませんでした。「再生成」をお試しください。");return await result.ConfigureAwait(false);
                }
            }finally{
                Stop(process);lock(active)active.Remove(process);
                string full=Path.GetFullPath(profile),prefix=Path.GetFullPath(root)+Path.DirectorySeparatorChar;
                if(full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(full))try{Directory.Delete(full,true);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
    }
}
