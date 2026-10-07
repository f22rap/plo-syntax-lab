using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FlopCommands {
    public sealed class RatioChart : ScrollableControl {
        readonly Color[] colors={Color.FromArgb(37,99,235),Color.FromArgb(0,132,112),Color.FromArgb(180,83,9)};
        readonly ToolTip tip=new ToolTip{AutoPopDelay=20000};
        readonly List<Tuple<RectangleF,string>> hits=new List<Tuple<RectangleF,string>>();
        RunResult data;
        string lastTip="";
        int legendBottom=42;
        public int RowCount {get{return data==null?0:data.Rows.Count;}}
        public string[] LegendLabels {get{return data==null?new string[0]:data.Rows[0].Sources.Select(s=>s.Label).ToArray();}}
        public RatioChart(){DoubleBuffered=true;AutoScroll=true;BackColor=Color.White;Font=new Font("Yu Gothic UI",10);Dock=DockStyle.Fill;AccessibleName="CSVラベル別の比率グラフ";}
        public void SetData(RunResult result){data=result;AutoScrollPosition=Point.Empty;lastTip="";tip.SetToolTip(this,"");Reflow();Invalidate();}
        int ItemWidth(string label){return Math.Min(260,Math.Max(110,TextRenderer.MeasureText(label,Font).Width+35));}
        void Reflow(){
            int x=16,y=12,w=Math.Max(320,ClientSize.Width-24);
            if(data!=null)foreach(var item in data.Rows[0].Sources){int size=ItemWidth(item.Label);if(x>16&&x+size>w){x=16;y+=27;}x+=size;}
            legendBottom=y+38;AutoScrollMinSize=new Size(0,data==null?0:legendBottom+30+data.Rows.Count*42+12);
        }
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Reflow();Invalidate();}
        static void DrawText(Graphics g,string text,Font font,Color color,RectangleF area,StringAlignment alignment=StringAlignment.Near){
            using(var brush=new SolidBrush(color))using(var format=new StringFormat{Alignment=alignment,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text,font,brush,area,format);
        }
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e);hits.Clear();var g=e.Graphics;g.TranslateTransform(AutoScrollPosition.X,AutoScrollPosition.Y);
            var ink=Color.FromArgb(22,35,48);var muted=Color.FromArgb(94,108,120);
            if(data==null){DrawText(g,"実行すると、条件ごとの比率を表示します。",Font,muted,new RectangleF(16,20,Width-32,40));return;}
            int width=Math.Max(320,ClientSize.Width-24),x=16,y=12;
            for(int i=0;i<data.Rows[0].Sources.Count;i++){
                var source=data.Rows[0].Sources[i];int size=ItemWidth(source.Label);if(x>16&&x+size>width){x=16;y+=27;}
                using(var brush=new SolidBrush(colors[i]))g.FillRectangle(brush,x,y+6,13,13);
                DrawText(g,source.Label,Font,ink,new RectangleF(x+21,y,size-27,25));hits.Add(Tuple.Create(new RectangleF(x,y,size,25),source.Label+" (CSV"+(i+1)+")\r\n"+source.Path));x+=size;
            }
            int left=Math.Min(162,width/3),plotWidth=Math.Max(100,width-left-12),top=legendBottom+26;
            using(var pen=new Pen(Color.FromArgb(225,232,238))){
                for(int tick=0;tick<=100;tick+=25){float tx=left+plotWidth*tick/100f;g.DrawLine(pen,tx,top-4,tx,top+data.Rows.Count*42-10);DrawText(g,tick+"%",Font,muted,new RectangleF(tick==100?tx-60:tick==0?tx:tx-30,legendBottom-3,60,24),tick==100?StringAlignment.Far:tick==0?StringAlignment.Near:StringAlignment.Center);}
            }
            for(int r=0;r<data.Rows.Count;r++){
                var row=data.Rows[r];float rowY=top+r*42;
                DrawText(g,row.Label,Font,ink,new RectangleF(12,rowY,left-22,34));
                using(var brush=new SolidBrush(Color.FromArgb(237,241,245)))g.FillRectangle(brush,left,rowY,plotWidth,34);
                if(row.Total==0||row.Sources.Any(s=>!s.Percent.HasValue))DrawText(g,"比率なし（全CSVの合計0）",Font,muted,new RectangleF(left+10,rowY,plotWidth-20,34));
                else{
                    decimal cumulative=0;
                    for(int i=0;i<row.Sources.Count;i++){
                        decimal percent=row.Sources[i].Percent.Value;float start=left+(float)(cumulative/100m)*plotWidth;cumulative+=percent;float end=left+(float)(cumulative/100m)*plotWidth;
                        if(percent<=0)continue;
                        using(var brush=new SolidBrush(colors[i]))g.FillRectangle(brush,start,rowY,Math.Max(0,end-start),34);
                        if(end-start>=55)DrawText(g,percent.ToString("0.0")+"%",Font,Color.White,new RectangleF(start,rowY,end-start,34),StringAlignment.Center);
                    }
                }
                string details=row.Label+"\r\n"+string.Join("\r\n",row.Sources.Select((s,i)=>s.Label+" (CSV"+(i+1)+"): "+(s.Percent.HasValue?s.Percent.Value.ToString("0.00")+"%":"—")+" / weight "+s.Sum.ToString("0.####")));
                hits.Add(Tuple.Create(new RectangleF(12,rowY,width-12,34),details));
            }
        }
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);var point=new PointF(e.X-AutoScrollPosition.X,e.Y-AutoScrollPosition.Y);var hit=hits.FirstOrDefault(h=>h.Item1.Contains(point));string text=hit==null?"":hit.Item2;if(text!=lastTip){lastTip=text;tip.SetToolTip(this,text);}}
        protected override void Dispose(bool disposing){if(disposing)tip.Dispose();base.Dispose(disposing);}
    }
}
