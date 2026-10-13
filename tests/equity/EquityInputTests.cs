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
            const string exportHeader="hand,frequency,percent,weight,ev,ev_diff,variations\n";
            var solver=Csv(exportHeader+"AsKsQsJs,1,100,1,1.24,0.29,1\nAsKsQsTs,1,100,1,1.24,0.31,1\nAsKsQs8s,1,100,1,1.37,0.29,1\nAsKsQs7s,1,100,1,0.7,1.06,1\nAsKsQs6s,1,100,1,0.65,1.23,1\nAsKsQs5s,1,100,1,0.62,1.24,1\nAsKsQs4s,1,100,1,0.61,1.29,1");
            var solverRange=EquityRange.Parse(solver.Text,None);Check(solver.Rows==7&&solverRange.Count==7&&solverRange.TotalWeight==7,"user seven-column export imports all seven hands");
            Check(Csv(exportHeader+"AsKsQsJs,0.9,90,0.25,1.24,0.29,4").Text.Contains("@0.25"),"weight overrides frequency and ignores percent, EV and variations");
            Check(Csv("percent,WEIGHT,hand,frequency\n90,0.25,AsKsQsJs,0.9").Text.Contains("@0.25"),"weight priority independent of order and case");
            var zero=Csv(exportHeader+"AsKsQsJs,1,100,0,1.24,0.29,1\nAsKsQsTs,0,0,0.5,1.24,0.31,1");Check(zero.Rows==1&&zero.ZeroRows==1&&zero.Text.Contains("AsKsQsTs @0.5"),"zero weight excluded even with nonzero frequency; zero frequency does not exclude positive weight");
            Check(Csv(exportHeader+"AsKsQsJs,0.9,90,25,1.24,0.29,1",true).Text.Contains("@0.25"),"selected percent unit applies to weight column");
            Check(Csv("hand,frequency\nAsKsQsJs,0.3").Text.Contains("@0.3"),"legacy frequency-only import retained");
            Fails(()=>Csv("hand,weight,frequency\nAsKsQsJs,bad,1"),"数値");
            Fails(()=>Csv("hand,weight,WEIGHT,frequency\nAsKsQsJs,0.2,0.3,1"),"重複");
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
        static void MultipleCsvs(string directory){
            const string first="KcKd4h5h",second="Jc8hKhQh";
            string a=Path.Combine(directory,"opponent-a.csv"),b=Path.Combine(directory,"opponent-b.csv"),zero=Path.Combine(directory,"opponent-zero.csv");
            File.WriteAllText(a,"hand,frequency,percent,weight,ev,ev_diff,variations\n"+first+",1,100,0.2,0,0,1\n"+second+",1,100,0.1,0,0,1");
            File.WriteAllText(b,"hand,weight\n5h4hKdKc,0.3\n"+second+",0.4");
            var merged=EquityInput.ImportRangeFiles(new[]{a,b},false,None);var range=EquityRange.Parse(merged.Text,None);
            Check(merged.Paths.SequenceEqual(new[]{Path.GetFullPath(a),Path.GetFullPath(b)})&&merged.ComboCount==2,"all full paths and unique combo count retained");
            Check(range.GetCombos().All(c=>Math.Abs(c.Weight-.5)<1e-12),"same physical hand sums across files, including reversed card order");
            var request=Request();request.VillainText=merged.Text;request.RangeInputPaths=merged.Paths;
            var result=EquityJob.Run(request,None,null);Check(Math.Abs(result.Rows[0].Result.Equity-50)<1e-10,"merged opponent reaches exact EQ50");
            var restored=EquityFiles.Decode(EquityFiles.Encode(EquityFiles.Create(result)));Check(restored.Job.Input.RangeInputPaths.SequenceEqual(merged.Paths)&&restored.Job.Input.VillainText==merged.Text,"all source paths and merged range survive JSON roundtrip");
            bool protectedInput=false;try{EquityFiles.Export(restored,b,".csv","app.exe");}catch(ArgumentException){protectedInput=true;}Check(protectedInput,"second opponent source is protected against export overwrite");
            File.WriteAllText(b,"hand,weight\n"+first+",0.8\n"+second+",0.1");merged=EquityInput.ImportRangeFiles(new[]{a,b},false,None);range=EquityRange.Parse(merged.Text,None);
            Check(Math.Abs(range.SelectSyntax(first,None).TotalWeight-1)<1e-12&&Math.Abs(range.SelectSyntax(second,None).TotalWeight-.2)<1e-12,"unequal CSV totals are never normalized independently");
            File.WriteAllText(a,"hand,weight\n"+first+",0.8\n"+second+",0.2");File.WriteAllText(b,"hand,weight\n"+first+",0.7\n"+second+",0.3");
            merged=EquityInput.ImportRangeFiles(new[]{a,b},false,None);range=EquityRange.Parse(merged.Text,None);
            Check(merged.WeightScale==1.5&&Math.Abs(range.SelectSyntax(first,None).TotalWeight/range.SelectSyntax(second,None).TotalWeight-3)<1e-12,"weights above one keep the merged 3:1 ratio");
            request.VillainText=merged.Text;Check(Math.Abs(EquityJob.Run(request,None,null).Rows[0].Result.Equity-75)<1e-10,"scaled merged range preserves exact EQ75");
            File.WriteAllText(zero,"hand,weight\n"+first+",0");var withZero=EquityInput.ImportRangeFiles(new[]{a,b,zero},false,None);Check(withZero.Text==merged.Text&&withZero.ZeroRows==1&&withZero.Paths.Length==3,"valid zero-weight CSV adds no combos but stays in source list");
            Fails(()=>EquityInput.ImportRangeFiles(new[]{zero},false,None),"正の重み");Fails(()=>EquityInput.ImportRangeFiles(new string[0],false,None),"1個以上");
            Fails(()=>EquityInput.ImportRangeFiles(new[]{a,Path.Combine(directory,".","opponent-a.csv")},false,None),"同じ相手CSV");
            File.WriteAllText(b,"hand,weight\nBAD,1");Fails(()=>EquityInput.ImportRangeFiles(new[]{a,b},false,None),"opponent-b.csv");
            File.WriteAllText(a,"hand,weight\n"+first+",25\n"+second+",50");File.WriteAllText(b,"hand,weight\n"+first+",25%\n"+second+",0");merged=EquityInput.ImportRangeFiles(new[]{a,b},true,None);Check(EquityRange.Parse(merged.Text,None).GetCombos().All(c=>c.Weight==.5),"percent CSV unit and explicit percent work across files");
            File.WriteAllText(a,"hand,weight\n"+first+",1e-100");File.WriteAllText(b,"hand,weight\n"+first+",2e-100\n"+second+",1e-100");range=EquityRange.Parse(EquityInput.ImportRangeFiles(new[]{a,b},false,None).Text,None);Check(Math.Abs(range.SelectSyntax(first,None).TotalWeight/range.SelectSyntax(second,None).TotalWeight-3)<1e-12,"tiny positive weights retained when merging");
            File.WriteAllText(a,"hand,weight\n"+first+",0.2\n"+first+",0.4");File.WriteAllText(b,"hand,weight\n"+first+",0.3");range=EquityRange.Parse(EquityInput.ImportRangeFiles(new[]{a,b},false,None).Text,None);Check(Math.Abs(range.TotalWeight-.7)<1e-12,"within-file overlap keeps max; across-file weights are added");
            File.WriteAllText(a,"hand,weight\nKK**,0.2");merged=EquityInput.ImportRangeFiles(new[]{a,b},false,None);range=EquityRange.Parse(merged.Text,None);Check(Math.Abs(range.SelectSyntax(first,None).TotalWeight-.5)<1e-12&&range.Count>1,"range patterns merge by concrete physical hand");
            bool canceled=false;try{EquityInput.ImportRangeFiles(new[]{a,b},false,new CancellationToken(true));}catch(OperationCanceledException){canceled=true;}Check(canceled,"multiple CSV import honors cancellation");
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
        public static void Run(){string dir=Path.Combine(Path.GetTempPath(),"plo-equity-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);try{CsvCases();MultipleCsvs(dir);Jobs(dir);}finally{Directory.Delete(dir,true);}}
    }
}
