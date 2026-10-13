using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace FlopCommands.Equity {
    public sealed class EquityCsvImport { public string Text, EncodingName; public int Rows, ZeroRows; }
    public sealed class EquityCsvBatchImport {
        public string Text, EncodingName;
        public string[] Paths;
        public int Rows, ZeroRows, ComboCount;
        public double WeightScale;
    }
    public sealed class EquityActionData { public string Path, Label; public EquityRange Range; }
    public static class EquityInput {
        public const int MaxCsvBytes = 20 * 1024 * 1024;
        sealed class Record { public string[] Cells; public int Line; }
        static Exception Error(int line,string text){return new ArgumentException("CSV "+line+"行目："+text);}
        static string Decode(byte[] bytes,bool allowShiftJis,out string encoding){
            if(bytes==null||bytes.Length>MaxCsvBytes)throw new ArgumentException("CSVは20MiB以下にしてください。");
            string text;encoding="UTF-8";
            try{text=new UTF8Encoding(false,true).GetString(bytes);}
            catch(DecoderFallbackException){if(!allowShiftJis)throw new ArgumentException("アクションCSVはUTF-8で保存してください。");text=System.Text.Encoding.GetEncoding(932,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback).GetString(bytes);encoding="Shift_JIS";}
            return text.TrimStart('\ufeff');
        }
        static List<Record> Records(string text,CancellationToken token){
            var records=new List<Record>();var fields=new List<string>();var cell=new StringBuilder();
            bool quoted=false,closed=false;int line=1,start=1;
            for(int i=0;i<=text.Length;i++){
                if((i&4095)==0)token.ThrowIfCancellationRequested();
                bool end=i==text.Length;char ch=end?'\n':text[i];
                if(quoted){
                    if(end)throw Error(start,"引用符が閉じていません。");
                    if(ch=='"'){if(i+1<text.Length&&text[i+1]=='"'){cell.Append('"');i++;}else{quoted=false;closed=true;}}
                    else{if(ch=='\r'){line++;if(i+1<text.Length&&text[i+1]=='\n'){cell.Append(ch);ch=text[++i];}}else if(ch=='\n')line++;cell.Append(ch);}continue;
                }
                if(ch==','||ch=='\r'||ch=='\n'){
                    fields.Add(cell.ToString().Trim());cell.Clear();closed=false;
                    if(ch!=','){
                        if(fields.Any(x=>x.Length>0)||fields.Count>1)records.Add(new Record{Cells=fields.ToArray(),Line=start});
                        fields.Clear();if(ch=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;line++;start=line;
                    }continue;
                }
                if(closed){if(!char.IsWhiteSpace(ch))throw Error(line,"閉じ引用符の後に不正な文字があります。");continue;}
                if(ch=='"'){if(cell.ToString().Trim().Length>0)throw Error(line,"引用符の位置が正しくありません。");cell.Clear();quoted=true;}else cell.Append(ch);
            }
            return records;
        }
        static string Header(string s){return Regex.Replace(s.Trim().ToLowerInvariant(),@"[\s_\-]","");}
        static readonly string[] HandNames={"hand","combo","range","ハンド","コンボ","レンジ"};
        static readonly string[] WeightNames={"weight","重み"};
        static readonly string[] FrequencyNames={"frequency","freq","頻度"};
        static int Column(string[] cells,string[] names){int[] indexes=cells.Select((s,i)=>new{Key=Header(s),Index=i}).Where(x=>names.Contains(x.Key)).Select(x=>x.Index).ToArray();if(indexes.Length>1)throw new ArgumentException("CSVの列見出しが重複しています。");return indexes.Length==0?-1:indexes[0];}
        static void ValidateItem(string item,int line){
            if(item.IndexOfAny(new[]{'@',',',';','\r','\n'})>=0)throw Error(line,"ハンドセルに@・区切り文字・改行は使えません。");
            string s=Regex.Replace(item,@"\s+","").Replace("10","T");
            if(Regex.IsMatch(s,@"^(?:[2-9TJQKA][shdc]){4}$",RegexOptions.IgnoreCase)){try{EquityCards.Parse(s,4,4);}catch(ArgumentException e){throw Error(line,e.Message);}return;}
            if(!Regex.IsMatch(s,@"^(?:random|any|\*|[2-9TJQKA*x]{4})(?::(?:ds|ss|r|ts|mono))?$",RegexOptions.IgnoreCase))throw Error(line,"具体的な4枚または簡易レンジを1つ指定してください。除外行・@・複数条件は使えません。");
        }
        public static EquityCsvImport ImportRange(byte[] bytes,bool percentMode,CancellationToken token){
            return ImportRangeCore(bytes,percentMode,token,false);
        }
        static EquityCsvImport ImportRangeCore(byte[] bytes,bool percentMode,CancellationToken token,bool allowEmpty){
            token.ThrowIfCancellationRequested();string encoding;var rows=Records(Decode(bytes,true,out encoding),token);
            if(rows.Count==0)throw new ArgumentException("CSVにデータがありません。");
            int hand=Column(rows[0].Cells,HandNames),weight=-1,first=0;bool header=hand>=0;
            // Solver exports contain both weight and action frequency. Use weight
            // for the pre-action range; frequency is only a legacy fallback when
            // no weight column exists, never a fallback for an invalid weight cell.
            if(header){weight=Column(rows[0].Cells,WeightNames);if(weight<0)weight=Column(rows[0].Cells,FrequencyNames);first=1;if(rows[0].Cells.Length>1&&weight<0)throw Error(rows[0].Line,"重み列の見出しが必要です。");}
            else{hand=0;weight=rows[0].Cells.Length>1?1:-1;}
            var output=new StringBuilder();var result=new EquityCsvImport{EncodingName=encoding};
            for(int i=first;i<rows.Count;i++){
                token.ThrowIfCancellationRequested();var row=rows[i];string[] cells=row.Cells;
                if(!header&&cells.Length>2)throw Error(row.Line,"見出しなしCSVは最大2列です。");
                if(!header&&weight<0&&cells.Length>1)throw Error(row.Line,"重み列の有無が最初のデータ行と異なります。");
                if(hand>=cells.Length||(weight>=0&&weight>=cells.Length))throw Error(row.Line,"必要なセルがありません。");
                string item=cells[hand];ValidateItem(item,row.Line);double value=1;
                if(weight>=0&&cells[weight].Length>0){string s=cells[weight];bool percent=s.EndsWith("%");if(percent)s=s.Substring(0,s.Length-1);
                    if(!double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||double.IsNaN(value)||double.IsInfinity(value))throw Error(row.Line,"重みが数値ではありません。");
                    if(percent||percentMode)value/=100;if(value<0||value>1)throw Error(row.Line,"重みが指定単位の範囲外です。");
                }
                if(value==0){result.ZeroRows++;continue;}
                if(output.Length>0)output.AppendLine();output.Append(item).Append(" @").Append(value.ToString("R",CultureInfo.InvariantCulture));result.Rows++;
            }
            if(result.Rows==0&&!allowEmpty)throw new ArgumentException("CSVに正の重みを持つレンジがありません。");
            result.Text=output.ToString();return result;
        }
        public static EquityCsvBatchImport ImportRangeFiles(string[] paths,bool percentMode,CancellationToken token){
            token.ThrowIfCancellationRequested();
            if(paths==null||paths.Length==0)throw new ArgumentException("相手レンジのCSVを1個以上選択してください。");
            var fullPaths=paths.Select(Path.GetFullPath).ToArray();
            if(fullPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=fullPaths.Length)throw new ArgumentException("同じ相手CSVを複数指定することはできません。");
            var sums=new Dictionary<long,double>();var hands=new Dictionary<long,EquityCombo>();var encodings=new HashSet<string>();
            var result=new EquityCsvBatchImport{Paths=fullPaths};
            foreach(string path in fullPaths){
                token.ThrowIfCancellationRequested();
                try{
                    var file=ImportRangeCore(ReadCsv(path,token),percentMode,token,true);result.Rows+=file.Rows;result.ZeroRows+=file.ZeroRows;encodings.Add(file.EncodingName);
                    // Preserve the existing within-file overlap/max rule. Across
                    // files, add weights of the same physical four-card hand.
                    if(file.Rows==0)continue;
                    var range=EquityRange.Parse(file.Text,token);int index=0;
                    foreach(var combo in range.Items){
                        if((index++&1023)==0)token.ThrowIfCancellationRequested();double previous;sums.TryGetValue(combo.Mask,out previous);
                        double weight=previous+combo.Weight;if(double.IsInfinity(weight))throw new ArgumentException("統合後のweightが大きすぎます。");
                        sums[combo.Mask]=weight;hands[combo.Mask]=combo;
                    }
                }catch(ArgumentException ex){throw new ArgumentException(Path.GetFileName(path)+"："+ex.Message,ex);}
            }
            if(sums.Count==0)throw new ArgumentException("相手CSV全体に正の重みを持つレンジがありません。");
            // Text input accepts weights <=1. If necessary scale the ENTIRE
            // merged range once, preserving every ratio (never per file).
            result.WeightScale=Math.Max(1,sums.Values.Max());result.ComboCount=sums.Count;
            var output=new StringBuilder();int count=0;
            foreach(var pair in sums.OrderBy(p=>p.Key)){
                if((count++&1023)==0)token.ThrowIfCancellationRequested();double weight=pair.Value/result.WeightScale;
                if(weight==0)throw new ArgumentException("統合したweightの比が数値精度の範囲を超えています。");
                if(output.Length>0)output.AppendLine();output.Append(hands[pair.Key].Hand).Append(" @").Append(weight.ToString("R",CultureInfo.InvariantCulture));
            }
            token.ThrowIfCancellationRequested();result.Text=output.ToString();result.EncodingName=string.Join(" / ",encodings.OrderBy(e=>e,StringComparer.Ordinal));return result;
        }
        public static byte[] ReadCsv(string path,CancellationToken token){
            token.ThrowIfCancellationRequested();using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
                if(file.Length>MaxCsvBytes)throw new ArgumentException("CSVは20MiB以下にしてください。");
                using(var buffer=new MemoryStream()){byte[] chunk=new byte[65536];int n;while((n=file.Read(chunk,0,chunk.Length))>0){token.ThrowIfCancellationRequested();buffer.Write(chunk,0,n);if(buffer.Length>MaxCsvBytes)throw new ArgumentException("CSVは20MiB以下にしてください。");}return buffer.ToArray();}
            }
        }
        public static EquityActionData ReadAction(string path,string label,string board,CancellationToken token){
            var named=Regex.Match(System.IO.Path.GetFileNameWithoutExtension(path),@"(?:^|_)((?:[AKQJT2-9][shdc]){3,5})(?=_|$)",RegexOptions.IgnoreCase);
            if(named.Success){int[] fileBoard=EquityCards.Parse(named.Groups[1].Value,3,5),inputBoard=EquityCards.Parse(board,3,5);if(fileBoard.Length!=inputBoard.Length||!fileBoard.Take(3).OrderBy(x=>x).SequenceEqual(inputBoard.Take(3).OrderBy(x=>x))||!fileBoard.Skip(3).SequenceEqual(inputBoard.Skip(3)))throw new ArgumentException("CSVファイル名のボードが入力と異なります。");}
            string encoding;var rows=Records(Decode(ReadCsv(path,token),false,out encoding),token);
            if(rows.Count<2)throw new ArgumentException("アクションCSVにデータがありません。");
            int hand=Column(rows[0].Cells,new[]{"hand","combo"}),weight=Column(rows[0].Cells,new[]{"weight"});
            if(hand<0||weight<0)throw new ArgumentException("アクションCSVにはhand/comboとweight列が必要です。frequencyでは代用できません。");
            var seen=new HashSet<long>();var combos=new List<EquityCombo>();long blocked=EquityCards.Mask(EquityCards.Parse(board,3,5));
            for(int i=1;i<rows.Count;i++){
                token.ThrowIfCancellationRequested();var row=rows[i];if(hand>=row.Cells.Length||weight>=row.Cells.Length)throw Error(row.Line,"必要なセルがありません。");
                string s=row.Cells[hand];if(!Regex.IsMatch(s,@"^(?:[AKQJT2-9][shdc]){4}$"))throw Error(row.Line,"アクションCSVの手札表記が正しくありません。");
                int[] cards;try{cards=EquityCards.Parse(s,4,4);}catch(ArgumentException e){throw Error(row.Line,e.Message);}long mask=EquityCards.Mask(cards);
                if((mask&blocked)!=0)throw Error(row.Line,"ボードと手札が重複しています。");if(!seen.Add(mask))throw Error(row.Line,"同じ手札が重複しています。");
                decimal value;if(!decimal.TryParse(row.Cells[weight],NumberStyles.Float,CultureInfo.InvariantCulture,out value)||value<0||value>1)throw Error(row.Line,"weightは0〜1で指定してください。");
                if(value>0)combos.Add(new EquityCombo(cards,(double)value));
            }
            return new EquityActionData{Path=System.IO.Path.GetFullPath(path),Label=label,Range=new EquityRange(combos)};
        }
    }
}
