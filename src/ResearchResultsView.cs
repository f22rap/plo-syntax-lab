using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using FlopCommands.Research;

namespace FlopCommands {
    public sealed class ResearchResultsView : UserControl {
        readonly ComboBox mode=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=150,Name="researchResultMode"};
        readonly Label note=new Label{AutoSize=true,Padding=new Padding(4,6,0,0)};
        readonly DataGridView table=ResearchUi.Grid();
        readonly ResearchScatterChart chart=new ResearchScatterChart();
        public ResearchRunResult Result {get;private set;}
        public bool Grouped {get{return mode.SelectedIndex==1;}}
        public int PlotPoints {get{return chart.PointCount;}}
        public ResearchResultsView(){Name="researchResults";Dock=DockStyle.Fill;var root=ResearchUi.Rows(38,250,-1);root.Padding=Padding.Empty;Controls.Add(root);var bar=ResearchUi.Bar();mode.Items.AddRange(new object[]{"ボード別","特徴別（等重み）"});bar.Controls.Add(mode);bar.Controls.Add(note);root.Controls.Add(bar,0,0);table.ReadOnly=true;table.Name="researchResultTable";table.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;root.Controls.Add(table,0,1);root.Controls.Add(chart,0,2);mode.SelectedIndexChanged+=(s,e)=>RefreshResult();mode.SelectedIndex=0;}
        public void SetData(ResearchRunResult result){Result=result;RefreshResult();}
        static string P(double? value){return value.HasValue?value.Value.ToString("F2"):"—";}
        static string State(string status){switch(status){case "completed":return "完了";case "frequency_only":return "頻度のみ";case "missing_definition":return "syntax未設定";case "empty_condition":return "条件が空集合";case "no_matching_hands":return "該当なし";case "equity_unavailable":return "EQ計算不能";default:return "失敗";}}
        void Columns(params string[] names){table.Columns.Clear();foreach(var name in names)table.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=name,Width=name=="syntax"?230:name=="理由"?240:130,SortMode=DataGridViewColumnSortMode.Automatic});}
        void RefreshResult(){
            table.Rows.Clear();if(Result==null){note.Text="一括調査の結果をここに表示します。";chart.SetData(null,false);return;}
            if(Grouped){Columns("特徴・アクション位置","条件名","アクション","頻度平均 %","頻度 最小–最大","頻度有効 / 対象","アクション存在数","EQ平均 %","EQ有効数","レンジ内平均 %","図の頻度 %","図のEQ %","図の共通対象数");foreach(var s in ResearchRun.Summaries(Result)){int i=table.Rows.Add(s.Group,s.Label,s.Action,P(s.FrequencyMean),P(s.FrequencyMin)+"–"+P(s.FrequencyMax),s.FrequencyCount+" / "+s.Target,s.Available,P(s.EquityMean),s.EquityCount,P(s.RangeMean),P(s.PairedFrequency),P(s.PairedEquity),s.PairedCount);foreach(DataGridViewCell c in table.Rows[i].Cells)c.ToolTipText="頻度の対象："+string.Join(", ",s.FrequencySpots)+"\n図の共通対象："+string.Join(", ",s.PairedSpots);}note.Text="図はEQと頻度が両方ある同じボードの平均。未計算・欠損を0にしません。";}
            else {Columns("ボード","アクション位置","条件名","syntax","アクション","頻度 %","EQ %","EQ 95%区間","レンジ内 %","ハンド数","状態","理由");foreach(var r in Result.Rows)foreach(var a in r.Actions){int i=table.Rows.Add(r.Board,r.Node,r.Label,r.Syntax,a.Label,P(a.Frequency),P(r.Equity==null?(double?)null:r.Equity.Equity),r.Equity==null?"—":P(r.Equity.CiLow)+"–"+P(r.Equity.CiHigh),P(r.RangePercent),r.SelectedHands,State(r.Status),r.Reason);foreach(DataGridViewCell c in table.Rows[i].Cells)c.ToolTipText=r.Board+" / "+r.Label+"\n"+r.Syntax+"\n"+r.Reason+"\nCSV："+a.SourcePath;}note.Text="総ベットはbetの合計（raiseは別）。図は個々のアクションを色分けします。";}
            chart.SetData(Result,Grouped);
        }
    }
    public sealed class ResearchScatterChart : ScrollableControl {
        ResearchPoint[] points=new ResearchPoint[0];string[] keys=new string[0];string message="EQとアクション頻度の計算結果を表示します。";
        readonly List<Tuple<PointF,ResearchPoint>> hits=new List<Tuple<PointF,ResearchPoint>>();
        readonly ToolTip tip=new ToolTip{ShowAlways=true,AutoPopDelay=30000};string last="";bool disposed;
        public int PointCount {get{return points.Length;}}
        public ResearchScatterChart(){Name="researchScatter";Dock=DockStyle.Fill;DoubleBuffered=true;AutoScroll=true;BackColor=Color.White;}
        public void SetData(ResearchRunResult result,bool grouped){ClearTip();points=ResearchRun.Points(result,grouped);keys=result==null?new string[0]:ResearchFiles.ActionKeys(result);message=result==null?"一括調査を実行してください。":result.Status+" · "+result.CompletedSpots+" / "+result.TargetSpots+"ボード · "+(grouped?"EQと頻度の共通対象の等重み平均":"ボード別EQ（横線は95%区間）");if(points.Length==0)message+="\n描画できる点がありません。表で未設定・該当なし・EQの状態を確認してください。";AutoScrollPosition=Point.Empty;Reflow();Invalidate();}
        void Reflow(){AutoScrollMinSize=points.Length==0?Size.Empty:new Size(650,Math.Max(350,((keys.Length+2)/3)*24+290));}
        Color Ink(string key){return ColorTranslator.FromHtml(ResearchFiles.Color(key,keys));}
        void DrawText(Graphics g,string value,RectangleF rect,Color color){using(var b=new SolidBrush(color))using(var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter})g.DrawString(value,Font,b,rect,format);}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);hits.Clear();var g=e.Graphics;g.TranslateTransform(AutoScrollPosition.X,AutoScrollPosition.Y);g.SmoothingMode=SmoothingMode.AntiAlias;int w=Math.Max(650,ClientSize.Width),h=Math.Max(AutoScrollMinSize.Height,ClientSize.Height),top=64+((keys.Length+2)/3)*24;
            DrawText(g,message,new RectangleF(14,8,w-28,48),Color.DimGray);if(points.Length==0)return;
            for(int i=0;i<keys.Length;i++){float x=14+(i%3)*(w-28)/3f,y=54+(i/3)*24;using(var b=new SolidBrush(Ink(keys[i])))g.FillRectangle(b,x,y+2,10,10);DrawText(g,ResearchRun.ActionLabel(keys[i]),new RectangleF(x+15,y,(w-28)/3f-20,22),Color.Black);}
            var plot=new RectangleF(65,top,w-98,h-top-55);using(var pen=new Pen(Color.FromArgb(225,232,238))){for(int n=0;n<=100;n+=20){float x=plot.Left+plot.Width*n/100,y=plot.Bottom-plot.Height*n/100;g.DrawLine(pen,x,plot.Top,x,plot.Bottom);g.DrawLine(pen,plot.Left,y,plot.Right,y);DrawText(g,n.ToString(),new RectangleF(x-12,plot.Bottom+3,35,24),Color.DimGray);DrawText(g,n.ToString(),new RectangleF(25,y-8,35,24),Color.DimGray);}}
            DrawText(g,"頻度 %",new RectangleF(8,top-22,70,22),Color.Black);DrawText(g,"アクション前EQ (%)",new RectangleF(plot.Left+plot.Width/2-95,plot.Bottom+27,220,24),Color.Black);
            foreach(var p in points){float x=plot.Left+plot.Width*(float)p.Equity/100,y=plot.Bottom-plot.Height*(float)p.Frequency/100;var color=Ink(p.ActionKey);if(p.CiLow.HasValue)using(var pen=new Pen(Color.FromArgb(100,color),2))g.DrawLine(pen,plot.Left+plot.Width*(float)p.CiLow.Value/100,y,plot.Left+plot.Width*(float)p.CiHigh.Value/100,y);hits.Add(Tuple.Create(new PointF(x,y),p));}
            foreach(var group in points.GroupBy(p=>new{p.Equity,p.Frequency})){float x=plot.Left+plot.Width*(float)group.Key.Equity/100,y=plot.Bottom-plot.Height*(float)group.Key.Frequency/100;var colors=group.Select(p=>p.ActionKey).Distinct().ToArray();for(int i=0;i<colors.Length;i++)using(var brush=new SolidBrush(Ink(colors[i])))g.FillPie(brush,x-6,y-6,12,12,i*360f/colors.Length,360f/colors.Length);g.DrawEllipse(Pens.White,x-6,y-6,12,12);}
        }
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);float x=e.X-AutoScrollPosition.X,y=e.Y-AutoScrollPosition.Y;string value=string.Join("\n\n",hits.Where(h=>Math.Abs(h.Item1.X-x)<=10&&Math.Abs(h.Item1.Y-y)<=10).Select(h=>h.Item2).Select(p=>p.Label+"\nsyntax："+p.Syntax+"\n"+p.Action+" / EQ "+p.Equity.ToString("F2")+"% / 頻度 "+p.Frequency.ToString("F2")+"%\n"+p.Details));if(value.Length==0){ClearTip();return;}Cursor=Cursors.Hand;if(value!=last&&IsHandleCreated&&!disposed){last=value;tip.Show(value,this,new Point(Math.Min(e.X+12,Math.Max(0,ClientSize.Width-24)),Math.Min(e.Y+18,Math.Max(0,ClientSize.Height-24))),30000);}}
        void ClearTip(){last="";if(tip!=null&&!disposed&&IsHandleCreated)tip.Hide(this);Cursor=Cursors.Default;}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);ClearTip();}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);ClearTip();if(hits!=null)hits.Clear();if(points!=null)Reflow();Invalidate();}
        protected override void OnScroll(ScrollEventArgs e){base.OnScroll(e);ClearTip();Invalidate();}
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(!Visible)ClearTip();}
        protected override void Dispose(bool disposing){if(disposing&&!disposed){ClearTip();tip.Dispose();disposed=true;}base.Dispose(disposing);}
    }
}
