using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeskBox.UI.Views
{
    public sealed class TaskMonthCalendar:Control
    {
        private readonly Store _store;
        public Palette Palette;
        public Func<TaskItem, bool> TaskScope;
        public DateTime Month{get;set;}
        public DateTime SelectedDate{get;private set;}
        public event Action<DateTime> DateSelected;
        public TaskMonthCalendar(Store store,Palette palette){_store=store;Palette=palette;Month=SelectedDate=DateTime.Today;DoubleBuffered=true;Cursor=Cursors.Hand;Font=new Font("Microsoft YaHei UI",9);}
        private DateTime First{get{var first=new DateTime(Month.Year,Month.Month,1);return first.AddDays(-((int)first.DayOfWeek+6)%7);}}
        protected override void OnPaint(PaintEventArgs e)
        {
            var p=Palette;var g=e.Graphics;g.Clear(p.CardBg);int width=Math.Max(1,ClientSize.Width/7),height=Math.Max(20,(Height-80)/6);
            TextRenderer.DrawText(g,"‹",Font,new Rectangle(8,4,40,34),p.Accent,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);TextRenderer.DrawText(g,Month.ToString("yyyy 年 MM 月"),Font,new Rectangle(50,4,Width-100,34),p.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);TextRenderer.DrawText(g,"›",Font,new Rectangle(Width-48,4,40,34),p.Accent,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            for(int i=0;i<7;i++)TextRenderer.DrawText(g,new[]{"周一","周二","周三","周四","周五","周六","周日"}[i],Font,new Rectangle(i*width,40,width,26),p.TextDim,TextFormatFlags.HorizontalCenter);
            var dueTasks = _store.Tasks.Where(task => !string.IsNullOrEmpty(task.Due) && (TaskScope == null || TaskScope(task))).ToLookup(task => task.Due);
            for(int i=0;i<42;i++)
            {
                var date=First.AddDays(i);var box=new Rectangle(i%7*width,i/7*height+72,width-2,height-2);bool selected=date.Date==SelectedDate.Date;
                using(var fill=new SolidBrush(selected?p.Hover:p.CardBg))g.FillRectangle(fill,box);using(var border=new Pen(selected?p.Accent:p.Line))g.DrawRectangle(border,box);
                var tasks=dueTasks[date.ToString("yyyy-MM-dd")].ToList();var color=date.Month==Month.Month?p.Text:p.TextFaint;
                TextRenderer.DrawText(g,date.Day.ToString(),Font,new Rectangle(box.X+8,box.Y+3,30,Math.Min(22,height-4)),date.Date==DateTime.Today?p.Accent:color);
                if(tasks.Count>0){TextRenderer.DrawText(g,tasks.Count+" 项",Font,new Rectangle(box.Right-54,box.Y+3,50,Math.Min(22,height-4)),tasks.Any(t=>!t.Done&&t.Priority=="high")?Color.FromArgb(251,113,133):p.Accent);if(height>48)TextRenderer.DrawText(g,tasks[0].Title,Font,new Rectangle(box.X+8,box.Y+29,Math.Max(1,width-16),22),p.TextDim,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);}
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);if(e.Y<40){if(e.X<50)Month=Month.AddMonths(-1);else if(e.X>Width-50)Month=Month.AddMonths(1);Invalidate();return;}
            if(e.Y<72)return;int height=Math.Max(20,(Height-80)/6),col=e.X/Math.Max(1,Width/7),row=(e.Y-72)/height;if(col>6||row>5)return;SelectedDate=First.AddDays(row*7+col);Invalidate();if(DateSelected!=null)DateSelected(SelectedDate);
        }
    }
}
