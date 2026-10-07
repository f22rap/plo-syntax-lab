using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FlopCommands {
    public static class CompareCommands {
        public static string Build(string board,string[] paths,IEnumerable<Choice> selected,bool includeAll=false,string[] labels=null) {
            board=Commands.NormalizeBoard(board);
            if(paths.Length>3)throw new Exception("CSV1・CSV2を選択してください。CSV3は任意です。");
            if(paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=paths.Length)throw new Exception("同じCSVを複数指定することはできません。");
            for(int i=0;i<paths.Length;i++){
                Commands.CheckCsvBoard(paths[i],board);
                var columns=Commands.ReadColumns(paths[i]);
                if(!columns.Contains("weight",StringComparer.OrdinalIgnoreCase))throw new Exception("CSV"+(i+1)+"にはweight列が必要です。");
                if(!columns.Contains("hand",StringComparer.OrdinalIgnoreCase)&&!columns.Contains("combo",StringComparer.OrdinalIgnoreCase))throw new Exception("CSV"+(i+1)+"にはhandまたはcombo列が必要です。");
            }
            var filters=selected.ToList();
            if(filters.Count==0&&!includeAll)throw new Exception("全ハンドのチェックを入れるか、条件を1つ以上選択してください。");
            if(filters.Any(c=>!c.Available))throw new Exception("未定義の条件は選択できません。");
            if(labels!=null&&labels.Length!=paths.Length)throw new Exception("CSVの数とラベルの数が異なります。");
            labels=Enumerable.Range(0,paths.Length).Select(i=>labels==null||string.IsNullOrWhiteSpace(labels[i])?"CSV"+(i+1):labels[i].Trim()).ToArray();
            var code=new StringBuilder();
            code.AppendLine("#requires -Version 5.1\r\n[CmdletBinding()]\r\nparam(");
            code.AppendLine("    [string[]]$CsvPaths = @("+string.Join(", ",paths.Select(Commands.Quote))+"),");
            code.AppendLine("    [string[]]$CsvLabels = @("+string.Join(", ",labels.Select(Commands.Quote))+"),");
            code.AppendLine("    [string]$OutputPath, [switch]$Force\r\n)\r\nSet-StrictMode -Version Latest\r\n$ErrorActionPreference = 'Stop'");
            code.AppendLine("$Board = "+Commands.Quote(board));
            code.AppendLine("# Per filter: each CSV weight sum / combined weight sum * 100");
            code.AppendLine("$filters = @(");
            code.AppendLine("# Conditions are symbolic syntax; the matcher below binds distinct cards.");
            if(includeAll)code.AppendLine("    [pscustomobject]@{ Cell = 'ALL'; Label = '全ハンド'; Syntax = '' }");
            foreach(var c in filters){
                code.AppendLine("    [pscustomobject]@{ Cell = "+Commands.Quote(c.Filter.Cell)+"; Label = "+Commands.Quote(c.Filter.Label));
                code.AppendLine("        Syntax = "+Commands.Quote(c.Filter.Syntax)+" }");
            }
            code.AppendLine(")");
            code.AppendLine("if (-not ('PloSyntax.SyntaxMatcher' -as [type])) { Add-Type -TypeDefinition @'");
            using(var matcher=Assembly.GetExecutingAssembly().GetManifestResourceStream("SyntaxMatcher.cs"))using(var reader=new StreamReader(matcher,Encoding.UTF8))code.AppendLine(reader.ReadToEnd());
            code.AppendLine("'@\r\n}");
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("compare-runtime.ps1"))
            using(var reader=new StreamReader(stream,Encoding.UTF8))code.Append(reader.ReadToEnd());
            return code.ToString();
        }
    }
}
