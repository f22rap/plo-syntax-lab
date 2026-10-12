using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using System.Globalization;
using FlopCommands.Equity;
namespace EquityTests {
    public static class InputTests {
        static int checks;static CancellationToken None=CancellationToken.None;
        static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        static void Fails(Action run,string part){try{run();}catch(ArgumentException e){Check(e.Message.Contains(part),"Error should contain "+part+": "+e.Message);return;}throw new Exception("Expected rejection: "+part);}
        static EquityCsvImport Csv(string s,bool percent=false){return EquityInput.ImportRange(Encoding.UTF8.GetBytes(s),percent,None);}
        static EquityJobRequest Request(){return new EquityJobRequest{Board="2c3d7h9sTc",HeroMode=EquityHeroMode.InputRange,HeroText="AcAd4c5d",VillainText="KcKd4h5h@25%,Jc8hKhQh@75%",Conditions=new[]{new EquityCondition{Label="AA",Syntax="AA"}},Settings=new EquitySettings{Seed=123}};}
        static void CsvCases(){
            var a=Csv("\ufeffweight,notes,combo\r\n25%,\"escaped \"\"quote\"\"\",AcAd4c5d\r\n0.75,ok,AA**");Check(a.Rows==2&&a.EncodingName=="UTF-8","BOM, reordered columns, escaped quotes");
            Check(EquityRange.Parse(a.Text,None).GetCombos().Single(combo=>combo.Mask==EquityCards.Mask(EquityCards.Parse("AcAd4c5d",4,4))).Weight==.75,"range overlaps use max");
            var b=Csv("レンジ,頻度\rAA**,50\rKK**,0\rAKQJ:ds,25%",true);Check(b.Rows==2&&b.ZeroRows==1,"percent units and zero rows");
            Check(b.Text.Contains("@0.5")&&b.Text.Contains("@0.25"),"percent conversion");
            Check(Csv("AcAd4c5d\nAsAh4h5h").Rows==2,"headerless one column");Check(Csv("AcAd4c5d,\nAA**,1e-3").Text.Contains("@1"),"blank weight default");
            Check(Csv("hand\nAA**").Rows==1,"header-only hand column");
            Check(Csv("hand,weight\nAA**,1e-100").Text.Contains("1E-100"),"tiny weights retained");
            Fails(()=>Csv("hand,weight\nBad,0"),"2行目");Fails(()=>Csv("hand,weight\nAA**"),"2行目");Fails(()=>Csv("hand,amount\nAA**,1"),"見出し");
            Fails(()=>Csv("hand,weight\nAA**,NaN"),"数値");Fails(()=>Csv("hand,weight\nAA**,2"),"範囲外");Fails(()=>Csv("hand,weight\nAA**,-1"),"範囲外");
            Fails(()=>Csv("hand,weight\n!AA**,1"),"2行目");Fails(()=>Csv("hand,weight\n\"AsAh\nKdQc\",1"),"2行目");Fails(()=>Csv("hand,weight\n\"AA**,KK**\",1"),"2行目");
            Fails(()=>Csv("hand,weight\nAA**@0.5,1"),"2行目");Fails(()=>Csv("hand,weight\n\"AA**,1"),"引用符");Fails(()=>Csv("hand,weight\n\"AA**\"bad,1"),"引用符");
            Fails(()=>Csv("AA**\nKK**,1"),"最初");Fails(()=>Csv("AA**,1,other"),"最大2列");Fails(()=>Csv("hand,combo,weight\nAA**,KK**,1"),"重複");Fails(()=>Csv("hand,weight\nAA**,0"),"正の重み");
            Fails(()=>EquityInput.ImportRange(new byte[EquityInput.MaxCsvBytes+1],false,None),"20MiB");
            // .NET Framework has code pages by default; .NET 8 tests register the built-in provider via reflection.
            var provider=Type.GetType("System.Text.CodePagesEncodingProvider, System.Text.Encoding.CodePages");
            if(provider!=null){var instance=provider.GetProperty("Instance").GetValue(null,null);var register=typeof(Encoding).GetMethod("RegisterProvider");if(register!=null)register.Invoke(null,new[]{instance});}
            byte[] sjis=Encoding.GetEncoding(932).GetBytes("ハンド,重み\nAcAd4c5d,1");var sjisResult=EquityInput.ImportRange(sjis,false,None);Check(sjisResult.EncodingName=="Shift_JIS"&&sjisResult.Rows==1,"Shift_JIS fallback");
            var large=new StringBuilder("hand,weight\n");foreach(var combo in EquityRange.Parse("random",None).GetCombos().Take(20000))large.AppendLine(combo.Hand+",0.005");
            var imported=Csv(large.ToString());Check(imported.Rows==20000,"20k CSV rows");Check(EquityRange.Parse(imported.Text,None).Count==20000,"20k expansion map");
        }
        static void Jobs(string directory){
            var request=Request();var result=EquityJob.Run(request,None,null);Check(result.Rows.Length==1&&Math.Abs(result.Rows[0].Result.Equity-25)<1e-10,"input to exact EQ");
            var copy=request.Snapshot();request.Conditions[0].Syntax="KK";request.ActionPaths=new[]{"changed"};request.Settings.Trials=9;Check(copy.Conditions[0].Syntax=="AA"&&copy.Settings.Trials==50000&&copy.ActionPaths.Length==0,"snapshot isolation");
            request=Request();request.Conditions=new[]{new EquityCondition{Label="first",Syntax="AA"},new EquityCondition{Label="duplicate",Syntax="AA"},new EquityCondition{Label="empty",Syntax="KK"},new EquityCondition{Label="invalid",Syntax="("}};
            result=EquityJob.Run(request,None,null);Check(result.Rows[0].Result.Equity==result.Rows[1].Result.Equity,"same syntax shared EQ");Check(result.Rows[2].Result==null&&result.Rows[2].Status.Contains("該当なし"),"empty row no EQ");Check(result.Rows[3].Result==null,"per-condition error");
            request=Request();request.Settings.MaxExactComparisons=1;result=EquityJob.Run(request,None,null);Check(result.Rows[0].Result==null&&result.Rows[0].Status.Contains("上限"),"no implicit MC fallback");
            request=Request();request.Settings.ExactRiver=false;request.Settings.Trials=10000;var r1=EquityJob.Run(request,None,null);var r2=EquityJob.Run(request,None,null);Check(r1.Rows[0].Result.Equity==r2.Rows[0].Result.Equity,"batch seed reproducibility");
            request.Settings.Seed=null;Check(EquityJob.Run(request,None,null).Rows[0].Result!=null,"generated seed");
            request=Request();request.HeroMode=EquityHeroMode.Uniform;request.Conditions[0].Syntax="AcAd4c5d";Check(EquityJob.Run(request,None,null).Rows[0].Result.Equity==25,"uniform and fixed syntax");
            string check=Path.Combine(directory,"check.csv"),bet=Path.Combine(directory,"bet.csv");
            File.WriteAllText(check,"hand,weight\nAcAd4c5d,0.2\nAsAh4h5h,0.3",new UTF8Encoding(true));File.WriteAllText(bet,"combo,weight\n5d4cAdAc,0.8\n5h4hAhAs,0.7",new UTF8Encoding(true));
            request=Request();request.HeroMode=EquityHeroMode.AllActions;request.ActionPaths=new[]{check,bet};request.ActionLabels=new[]{"check","bet"};
            Fails(()=>EquityJob.Run(request,None,null),"共通");request.CommonScaleConfirmed=true;var fromActions=EquityJob.Run(request,None,null);
            var explicitInput=Request();explicitInput.HeroText="AcAd4c5d,AsAh4h5h";Check(fromActions.Rows[0].Result.Equity==EquityJob.Run(explicitInput,None,null).Rows[0].Result.Equity,"action sum equals pre-action input");
            File.WriteAllText(check,"hand,weight\nAcAd4c5d,0.7\nAsAh4h5h,0.9");File.WriteAllText(bet,"hand,weight\nAcAd4c5d,0.3\nAsAh4h5h,0.1");
            Check(fromActions.Rows[0].Result.Equity==EquityJob.Run(request,None,null).Rows[0].Result.Equity,"redistributing actions while retaining totals cannot change EQ");
            request.ActionPaths=new[]{check,check};Fails(()=>EquityJob.Run(request,None,null),"同じCSV");
            File.WriteAllText(check,"hand,weight\nAcAd4c5d,0.2\n5d4cAdAc,0.8");Fails(()=>EquityInput.ReadAction(check,"check",request.Board,None),"重複");
            File.WriteAllText(check,"hand,weight\n2cAd4c5d,0");Fails(()=>EquityInput.ReadAction(check,"check",request.Board,None),"ボード");
            File.WriteAllText(check,"hand,frequency\nAcAd4c5d,1");Fails(()=>EquityInput.ReadAction(check,"check",request.Board,None),"weight列");
            File.WriteAllText(check,"hand,weight\nAcAd4c5d,1.1");Fails(()=>EquityInput.ReadAction(check,"check",request.Board,None),"0〜1");
            string mismatch=Path.Combine(directory,"check_AsKd7s.csv");File.WriteAllText(mismatch,"hand,weight\nAcAd4c5d,1");Fails(()=>EquityInput.ReadAction(mismatch,"check",request.Board,None),"ファイル名");
            var cts=new CancellationTokenSource();request=Request();request.Settings.ExactRiver=false;request.Settings.Trials=2000000;
            bool canceled=false;try{EquityJob.Run(request,cts.Token,p=>{if(p.Calculation!=null)cts.Cancel();});}catch(OperationCanceledException){canceled=true;}Check(canceled,"cancel batch yields no complete result");
            Console.WriteLine("CSV + EQ jobs: "+checks+" checks passed.");
        }
        public static void Run(){string dir=Path.Combine(Path.GetTempPath(),"plo-equity-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);try{CsvCases();Jobs(dir);}finally{Directory.Delete(dir,true);}}
    }
}
