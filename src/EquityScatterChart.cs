using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using FlopCommands.Equity;

namespace FlopCommands {
    public sealed class EquityScatterChart : ScrollableControl {
        readonly Color[] colors={Color.FromArgb(37,99,235),Color.FromArgb(0,132,112),Color.FromArgb(180,83,9)};
        readonly ToolTip tip=new ToolTip{AutoPopDelay=30000};
        readonly List<Tuple<PointF,EquityPlotPoint>> hits=new List<Tuple<PointF,EquityPlotPoint>>();
        EquityPlotPoint[] points=new EquityPlotPoint[0];
        string message="EQ計算後に散布図を表示します。",lastTip="";
        public int PointCount {get{return points.Length;}}
        public EquityScatterChart(){Dock=DockStyle.Fill;DoubleBuffered=true;AutoScroll=true;BackColor=Color.White;Name="eqScatter";AccessibleName="アクション前EQとベット頻度の散布図";}
        public void SetData(EquityJobResult result){
            points=EquityPlot.Points(result);message=result==null?"入力を確認し、EQを計算してください。":result.PlotStatus;
            if(result!=null){int missing=result.Rows.Count(r=>r.Result==null||r.Frequency==null||r.Frequency.Percent==null);
                if(missing>0)message+=" 表示できない条件："+missing+"件（結果表に理由）。";
                if(points.Length==0)message="描画できる点がありません。 "+message;
            }
            AccessibleDescription=message;AutoScrollPosition=Point.Empty;hits.Clear();lastTip="";tip.SetToolTip(this,"");Reflow();Invalidate();
        }
        void Reflow(){AutoScrollMinSize=points.Length==0?Size.Empty:new Size(650,300);}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Reflow();Invalidate();}
        static void DrawText(Graphics g,string text,Font font,Color color,RectangleF area,StringAlignment alignment){
            using(var brush=new SolidBrush(color))using(var format=new StringFormat{Alignment=alignment,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter})g.DrawString(text??"",font,brush,area,format);
        }
        static void Marker(Graphics g,Color color,int index,float x,float y){
            using(var brush=new SolidBrush(color))using(var outline=new Pen(Color.White,1.2f)){
                if(index==0){g.FillEllipse(brush,x-5,y-5,10,10);g.DrawEllipse(outline,x-5,y-5,10,10);}
                else if(index==1){g.FillRectangle(brush,x-5,y-5,10,10);g.DrawRectangle(outline,x-5,y-5,10,10);}
                else{var triangle=new[]{new PointF(x,y-6),new PointF(x+6,y+5),new PointF(x-6,y+5)};g.FillPolygon(brush,triangle);g.DrawPolygon(outline,triangle);}
            }
        }
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e);hits.Clear();var g=e.Graphics;g.TranslateTransform(AutoScrollPosition.X,AutoScrollPosition.Y);g.SmoothingMode=SmoothingMode.AntiAlias;
            Color ink=Color.FromArgb(22,35,48),muted=Color.FromArgb(94,108,120);
            int w=Math.Max(650,ClientSize.Width),h=Math.Max(300,ClientSize.Height);float legendX=18;
            if(points.Length==0){DrawText(g,message,Font,muted,new RectangleF(18,12,Math.Max(100,ClientSize.Width-36),100),StringAlignment.Near);return;}
            foreach(var series in points.GroupBy(p=>p.ActionIndex)){
                var point=series.First();float size=(w-36)/3f;Marker(g,colors[point.ActionIndex],point.ActionIndex,legendX+6,18);
                DrawText(g,point.Action+" (CSV"+(point.ActionIndex+1)+")",Font,ink,new RectangleF(legendX+18,3,size-22,30),StringAlignment.Near);legendX+=size;
            }
            DrawText(g,message,Font,muted,new RectangleF(18,34,w-36,42),StringAlignment.Near);
            var plot=new RectangleF(80,96,w-114,h-153);
            using(var grid=new Pen(Color.FromArgb(225,232,238)))using(var border=new Pen(Color.FromArgb(160,174,185))){
                for(int value=0;value<=100;value+=20){float x=plot.Left+plot.Width*value/100f,y=plot.Bottom-plot.Height*value/100f;
                    g.DrawLine(grid,x,plot.Top,x,plot.Bottom);g.DrawLine(grid,plot.Left,y,plot.Right,y);
                    DrawText(g,value.ToString(),Font,muted,new RectangleF(x-22,plot.Bottom+3,44,24),StringAlignment.Center);
                    DrawText(g,value.ToString(),Font,muted,new RectangleF(30,y-12,43,24),StringAlignment.Far);
                }
                g.DrawRectangle(border,plot.X,plot.Y,plot.Width,plot.Height);
            }
            DrawText(g,"自分のアクション前EQ (%)",Font,ink,new RectangleF(plot.Left,plot.Bottom+29,plot.Width,26),StringAlignment.Center);
            DrawText(g,"ベット頻度 (%)",Font,ink,new RectangleF(18,72,200,24),StringAlignment.Near);
            // Confidence intervals and points stay on their real coordinates: no jitter or connecting lines.
            foreach(var p in points){float x=plot.Left+plot.Width*(float)p.Equity/100f,y=plot.Bottom-plot.Height*(float)p.Frequency/100f;
                if(!p.Exact){float low=plot.Left+plot.Width*(float)Math.Max(0,p.CiLow)/100f,high=plot.Left+plot.Width*(float)Math.Min(100,p.CiHigh)/100f;
                    using(var pen=new Pen(Color.FromArgb(110,colors[p.ActionIndex]),2)){g.DrawLine(pen,low,y,high,y);g.DrawLine(pen,low,y-3,low,y+3);g.DrawLine(pen,high,y-3,high,y+3);}}
                Marker(g,colors[p.ActionIndex],p.ActionIndex,x,y);hits.Add(Tuple.Create(new PointF(x,y),p));
            }
            // At coincident locations the tooltip lists every condition and action.
        }
        protected override void OnMouseMove(MouseEventArgs e){
            base.OnMouseMove(e);float x=e.X-AutoScrollPosition.X,y=e.Y-AutoScrollPosition.Y;
            var nearby=hits.Where(h=>Math.Abs(h.Item1.X-x)<=8&&Math.Abs(h.Item1.Y-y)<=8).Select(h=>h.Item2);
            string value=string.Join("\r\n\r\n",nearby.Select(p=>p.Label+" · "+p.Syntax+"\r\n"+p.Action+" (CSV"+(p.ActionIndex+1)+") · EQ "+p.Equity.ToString("F2")+"% · ベット頻度 "+p.Frequency.ToString("F2")+"%\r\n"+(p.Exact?"EQ：全列挙":"EQ 95%区間："+p.CiLow.ToString("F2")+"–"+p.CiHigh.ToString("F2")+"%")));
            if(value!=lastTip){lastTip=value;tip.SetToolTip(this,value);}
        }
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);lastTip="";tip.SetToolTip(this,"");}
        protected override void Dispose(bool disposing){if(disposing)tip.Dispose();base.Dispose(disposing);}
    }
}
