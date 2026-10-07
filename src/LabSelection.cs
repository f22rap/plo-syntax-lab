using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using PloSyntax;

namespace FlopCommands {
    public sealed class Filter { public string Cell,Label,Syntax; }
    public sealed class LabRange { public string label {get;set;} public string syntax {get;set;} public int count {get;set;} public string cell {get;set;} public string problem {get;set;} }
    public sealed class LabSelection {
        public string app {get;set;}public string game {get;set;}public string[] board {get;set;}public LabRange[] ranges {get;set;}
        public string mode {get;set;} public string job {get;set;} public string error {get;set;}
        public string NormalizedBoard {get{return Commands.NormalizeBoard(string.Concat(board));}}
        public static LabSelection Read(string json){
            var data=new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Deserialize<LabSelection>(json);
            if(data==null||data.app!="PLO Syntax Lab"||data.game!="PLO4"||data.board==null||data.ranges==null||(data.ranges.Length==0&&!(data.mode=="defaults"&&!string.IsNullOrEmpty(data.error)))||data.ranges.Length>1000)throw new Exception("PLO Syntax Labの条件データを選択してください。");
            string normalized=data.NormalizedBoard;
            foreach(var row in data.ranges){if(row==null||string.IsNullOrWhiteSpace(row.label)||row.label.Length>1000||row.count<0)throw new Exception("条件名または件数が正しくありません。");
                if(row.count==0){if(!string.IsNullOrEmpty(row.syntax))throw new Exception("空集合にsyntaxが指定されています。");}
                else new SyntaxMatcher(row.syntax);
            }return data;
        }
        public List<Choice> Choices(){return ranges.Where(r=>r.count>0).Select(r=>new Choice{Filter=new Filter{Cell="LAB_"+Guid.NewGuid().ToString("N"),Label=r.label,Syntax=Commands.NormalizeSyntax(r.syntax)}}).ToList();}
    }
}
