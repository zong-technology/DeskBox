// 布局几何验收：反射检查 WeatherView 的卡片与列表是否重叠、是否被裁切
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using DeskBox.Core;
using DeskBox.UI.Views;

public class LayoutCheck
{
    static int pass = 0, fail = 0;
    static readonly List<string> fails = new List<string>();

    static void Check(string name, bool ok, string detail)
    {
        if (ok) { pass++; Console.WriteLine("PASS  " + name + "  [" + detail + "]"); }
        else { fail++; fails.Add(name + " :: " + detail); Console.WriteLine("FAIL  " + name + "  [" + detail + "]"); }
    }

    static Control Field(object obj, string name)
    {
        var f = obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return f == null ? null : f.GetValue(obj) as Control;
    }

    static Rectangle Abs(Control c)
    {
        var p = c.PointToScreen(Point.Empty);
        return new Rectangle(p, c.Size);
    }

    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lay-" + DateTime.Now.Ticks);
        System.IO.Directory.CreateDirectory(dir);
        DeskBox.Core.AppPaths.DataDir = dir;
        DeskBox.Store.DataDir = dir;
        var store = new DeskBox.Store();
        store.Init();

        var form = new Form { Width = 1440, Height = 900, StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
        var view = new WeatherView(store) { Dock = DockStyle.Fill };
        form.Controls.Add(view);
        form.Show();
        Application.DoEvents();
        // 轮询等待真实天气请求完成（最多 20 秒），避免因网络耗时导致误判
        var hourListProbe = Field(view, "_hourList");
        var probeProp = hourListProbe == null ? null : hourListProbe.GetType().GetProperty("RowCount");
        for (int i = 0; i < 80; i++)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(250);
            if (probeProp != null && (int)probeProp.GetValue(hourListProbe, null) > 0) break;
        }
        Application.DoEvents();

        var nowCard = Field(view, "_nowCard");
        var hourList = Field(view, "_hourList");
        var cityList = Field(view, "_cityList");

        Check("当前天气卡片存在", nowCard != null, nowCard == null ? "null" : nowCard.Size.ToString());
        Check("逐小时列表存在", hourList != null, hourList == null ? "null" : hourList.Size.ToString());
        Check("城市列表存在", cityList != null, cityList == null ? "null" : cityList.Size.ToString());

        if (nowCard != null && hourList != null)
        {
            var a = Abs(nowCard);
            var b = Abs(hourList);
            bool overlap = a.IntersectsWith(b);
            Check("卡片与小时列表不重叠", !overlap, "card=" + a + " hours=" + b);

            Check("卡片高度足够显示 5 天预报", nowCard.Height >= 220, "h=" + nowCard.Height);
            Check("小时列表有可见高度", hourList.Height >= 150, "h=" + hourList.Height);
            Check("小时列表在卡片下方", b.Top >= a.Bottom - 2, "cardBottom=" + a.Bottom + " hoursTop=" + b.Top);
        }

        if (cityList != null && hourList != null)
        {
            var c = Abs(cityList);
            var b = Abs(hourList);
            Check("城市列表在左、小时列表在右", c.Right <= b.Left + 2, "cityRight=" + c.Right + " hoursLeft=" + b.Left);
        }

        // 逐小时数据必须真的有 24 条
        var hourRows = hourList == null ? 0 : (int)hourList.GetType().GetProperty("RowCount").GetValue(hourList, null);
        var nowSnap = Field(view, "_now");
        if (nowSnap != null)
        {
            var t = nowSnap.GetType();
            Console.WriteLine("  [诊断] source=" + t.GetField("Source").GetValue(nowSnap)
                + " city=" + t.GetField("City").GetValue(nowSnap)
                + " temp=" + t.GetField("Temp").GetValue(nowSnap)
                + " hours=" + ((System.Collections.IList)t.GetField("Hours").GetValue(nowSnap)).Count
                + " days=" + ((System.Collections.IList)t.GetField("Days").GetValue(nowSnap)).Count);
        }
        else
        {
            Console.WriteLine("  [诊断] _now 为 null");
            var statusLabel = view.GetType().GetField("_status", BindingFlags.NonPublic | BindingFlags.Instance);
            var sl = statusLabel == null ? null : statusLabel.GetValue(view) as Label;
            Console.WriteLine("  [诊断] status 文本 = " + (sl == null ? "?" : sl.Text));
        }
        Check("逐小时列表有 24 行真实数据", hourRows >= 20, "rows=" + hourRows);

        // 卡片内的绘制不能越界（PaintNow 用 _nowCard.Height - 14）
        if (nowCard != null)
            Check("卡片绘制区域不超出控件", nowCard.Height - 14 > 0, "paintH=" + (nowCard.Height - 14));

        form.Close();
        Console.WriteLine();
        Console.WriteLine("==== 布局验收: PASS " + pass + " / FAIL " + fail + " ====");
        foreach (var f in fails) Console.WriteLine("  ✗ " + f);
        Environment.Exit(fail == 0 ? 0 : 1);
    }
}
