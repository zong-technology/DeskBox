using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeskBox.UI
{
    internal class ReferenceSlider : Control
    {
        private int _value;
        private readonly Palette _palette;
        public int Minimum=0,Maximum=1000;
        public int Value{get{return _value;}set{_value=Math.Max(Minimum,Math.Min(Maximum,value));Invalidate();}}
        public event EventHandler Scroll;
        public ReferenceSlider(Palette palette){_palette=palette;Height=38;TabStop=true;Cursor=Cursors.Hand;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
        private void SelectAt(int x){Value=Minimum+(int)(Math.Max(0,Math.Min(1,(x-10d)/Math.Max(1,Width-20)))*(Maximum-Minimum));if(Scroll!=null)Scroll(this,EventArgs.Empty);}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();if(e.Button==MouseButtons.Left)SelectAt(e.X);}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(e.Button==MouseButtons.Left)SelectAt(e.X);}
        protected override bool IsInputKey(Keys key){return key==Keys.Left||key==Keys.Right||base.IsInputKey(key);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Left||e.KeyCode==Keys.Right){Value+=e.KeyCode==Keys.Right?10:-10;if(Scroll!=null)Scroll(this,EventArgs.Empty);e.Handled=true;}base.OnKeyDown(e);}
        protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var rect=new Rectangle(9,Height/2-3,Math.Max(1,Width-18),6);Palette.FillRounded(e.Graphics,_palette.Line,rect,3);int x=9+(int)((Width-18)*(_value-Minimum)/(double)Math.Max(1,Maximum-Minimum));Palette.FillRounded(e.Graphics,_palette.Accent,new Rectangle(9,rect.Y,Math.Max(1,x-9),6),3);using(var brush=new SolidBrush(_palette.Accent))e.Graphics.FillEllipse(brush,x-7,Height/2-7,14,14);if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle,_palette.Text,_palette.Bg);}
    }
}
