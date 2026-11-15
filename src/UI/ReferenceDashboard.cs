using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Engineering;

namespace DeskBox.UI
{
    public partial class ReferenceWorkspace
    {
        private Control BuildDesktopPage()
        {
            var page = Page("智能桌面", "连接今日工作、工程进度与系统状态");
            SetSampleHeading(page, "COMMAND CENTER", "打开工程工作区", () => Navigate("home"));
            var view = _legacyFactory("desktop");
            if (view != null) AddMain(page, view);
            return page;
        }
        private void SetSampleHeading(Panel page, string eyebrowText, string actionText, Action action, DarkButton secondary = null)
        {
            page.Padding = new Padding(32, 12, 24, 32);
            var heading = page.Controls[0];
            var title = (Label)heading.Controls[0];
            var subtitle = (Label)heading.Controls[1];
            var eyebrow = TextLabel(eyebrowText, 9, true, _p.Accent);
            var open = Button(actionText, action, false, 170);
            heading.Controls.Add(eyebrow);
            heading.Controls.Add(open);
            if (secondary != null) heading.Controls.Add(secondary);
            Action resize = () =>
            {
                bool wide = page.ClientSize.Width >= 1450;
                heading.Height = wide ? 135 : 100;
                eyebrow.SetBounds(0, 1, 220, 22);
                title.Font = FontOf(wide ? 25 : 20, true);
                int titleHeight = title.GetPreferredSize(Size.Empty).Height;
                title.SetBounds(0, 24, Math.Max(150, heading.Width - 190), titleHeight);
                subtitle.SetBounds(0, title.Bottom + 2, Math.Max(150, heading.Width - 190), 23);
                open.SetBounds(Math.Max(0, heading.Width - 170), 32, 170, 36);
                if (secondary != null)
                {
                    secondary.SetBounds(Math.Max(0, open.Left - 126), 32, 116, 36);
                    title.Width = subtitle.Width = Math.Max(120, secondary.Left - 10);
                }
            };
            heading.Resize += delegate { resize(); };
            resize();
        }
        private Control BuildHistoryPage()
        {
            return BuildBusinessPage("history","工作记录","按时间、操作类型和级别检索真实记录 · 查看详情并导出 CSV");
        }
        private Control BuildToolWorkspace()
        {
            var page=Page("工程工具工作区","仪表换算、S7地址、Modbus、联锁检查与验证模板");var toolbar=Toolbar();toolbar.Controls.Add(Button("返回工具目录",()=>Navigate("engineering"),false,145));var view=_legacyFactory("engineering") as Views.ViewBase;if(view!=null){view.ApplyPalette(_p);view.UseReferenceLayout();AddMain(page,view,toolbar);_reload["tool-workspace"]=()=>view.Activate();}return page;
        }
    }
}
