using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FlopCommands {
    public sealed class Choice {
        public Filter Filter;
        public string Expression;
        public string Problem;
        public bool Available { get { return Problem == null; } }
    }
    public static class Commands {
        public static string NormalizeSyntax(string syntax){
            if(syntax==null)return null;
            string previous;
            do{previous=syntax;syntax=Regex.Replace(syntax,@":\s*!?\s*\(\s*\)","");}while(syntax!=previous);
            return syntax;
        }
        public static string NormalizeBoard(string input) {
            string s=Regex.Replace(input??"",@"[\s,;|/・]+","").Replace("10","T");
            s=s.Replace("♠","s").Replace("♥","h").Replace("♦","d").Replace("♣","c");
            if(!Regex.IsMatch(s,@"^(?:[AKQJT2-9][shdc]){3,5}$",RegexOptions.IgnoreCase))throw new Exception("ボードを3〜5枚入力してください。例：AsKd7s");
            var cards=new List<string>();for(int i=0;i<s.Length;i+=2)cards.Add(char.ToUpperInvariant(s[i]).ToString()+char.ToLowerInvariant(s[i+1]));
            if(cards.Distinct().Count()!=cards.Count)throw new Exception("同じカードが重複しています。");return string.Concat(cards.ToArray());
        }
        public static string[] ReadColumns(string path) {
            using(var reader=new StreamReader(path,Encoding.UTF8,true)) {
                string line=reader.ReadLine();
                if(string.IsNullOrWhiteSpace(line))throw new Exception("CSVのヘッダーが空です。");
                return line.Split(',').Select(x=>x.Trim().Trim('"').Trim()).ToArray();
            }
        }
        public static void CheckCsvBoard(string path,string board) {
            var match=Regex.Match(Path.GetFileNameWithoutExtension(path),@"(?:^|_)((?:[AKQJT2-9][shdc]){3,5})(?=_|$)",RegexOptions.IgnoreCase);
            if(match.Success&&!SameBoard(NormalizeBoard(match.Groups[1].Value),board))
                throw new Exception("CSVのファイル名のボードと入力したフロップが異なります。CSVかフロップを変更してください。");
        }
        public static bool SameBoard(string a,string b){return a==b||(a.Length>=6&&a.Length==b.Length&&a.Substring(6)==b.Substring(6)&&Enumerable.Range(0,3).Select(i=>a.Substring(i*2,2)).OrderBy(c=>c).SequenceEqual(Enumerable.Range(0,3).Select(i=>b.Substring(i*2,2)).OrderBy(c=>c)));}
        public static string Quote(string text) {return "'"+(text??"").Replace("'","''")+"'";}
    }

}
