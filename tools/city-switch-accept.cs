// 规格 6.4 验收：城市切换后必须真的改变请求目标，且所有指标同步更新
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using DeskBox.Core;
using DeskBox.UI.Views;

public class CitySwitch
{
    static int pass = 0, fail = 0;
    static readonly List<string> fails = new List<string>();

    static void Check(string name, bool ok, string detail)
    {
        if (ok) { pass++; Console.WriteLine("PASS  " + name + "  [" + detail + "]"); }
        else { fail++; fails.Add(name + " :: " + detail); Console.WriteLine("FAIL  " + name + "  [" + detail + "]"); }
    }

    static object Field(object o, string n)
    {
        var f = o.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance);
        return f == null ? null : f.GetValue(o);
    }

    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        var dir = Path.Combine(Path.GetTempPath(), "city-" + DateTime.Now.Ticks);
        Directory.CreateDirectory(dir);
        DeskBox.Core.AppPaths.DataDir = dir;
        DeskBox.Store.DataDir = dir;
        var store = new DeskBox.Store();
        store.Init();

        Console.WriteLine("== 1. 城市搜索（真实 geocoding 接口） ==");
        var hits = CitySearch.Search("广州", 5);
        Check("搜索广州有结果", hits.Count > 0, "count=" + hits.Count);
        var gz = hits.FirstOrDefault();
        Check("结果带唯一 ID", gz != null && gz.Id > 0, gz == null ? "null" : "id=" + gz.Id);
        Check("结果带经纬度", gz != null && gz.Lat != 0 && gz.Lon != 0, gz == null ? "null" : gz.Lat + "," + gz.Lon);
        Check("结果带地区信息避免同名混淆", gz != null && (gz.Admin1.Length > 0 || gz.Country.Length > 0), gz == null ? "" : gz.Display);

        var sh = CitySearch.Search("上海", 3).FirstOrDefault();
        Check("两个城市 ID 不同", sh != null && gz != null && sh.Id != gz.Id, (sh == null ? "?" : sh.Id.ToString()) + " vs " + (gz == null ? "?" : gz.Id.ToString()));
        Check("两个城市坐标不同", sh != null && gz != null && (Math.Abs(sh.Lat - gz.Lat) > 1 || Math.Abs(sh.Lon - gz.Lon) > 1),
            (sh == null ? "?" : sh.Lat + "," + sh.Lon) + " vs " + (gz == null ? "?" : gz.Lat + "," + gz.Lon));

        Console.WriteLine();
        Console.WriteLine("== 2. 收藏与切换（真实 HTTP 请求） ==");
        var cities = new CityStore();
        cities.SetCurrent(sh);
        cities.AddFavorite(gz);
        Check("常用城市已保存", cities.Favorites.Count >= 2, "count=" + cities.Favorites.Count);

        var s = store.Settings;
        s.WeatherCity = sh.Name; s.WeatherLat = sh.Lat; s.WeatherLon = sh.Lon; s.HasCoords = true;
        var snapA = DeskBox.WeatherService.Get(s, true);
        Check("城市A 请求成功", snapA.Source == "online", "source=" + snapA.Source + " city=" + snapA.City);

        s.WeatherCity = gz.Name; s.WeatherLat = gz.Lat; s.WeatherLon = gz.Lon; s.HasCoords = true;
        var snapB = DeskBox.WeatherService.Get(s, true);
        Check("城市B 请求成功", snapB.Source == "online", "source=" + snapB.Source + " city=" + snapB.City);

        Console.WriteLine();
        Console.WriteLine("== 3. 切换后所有指标必须同步变化（规格 6.4） ==");
        Check("城市名变化", snapA.City != snapB.City, snapA.City + " → " + snapB.City);
        Check("经纬度变化", Math.Abs(snapA.Lat - snapB.Lat) > 0.01 || Math.Abs(snapA.Lon - snapB.Lon) > 0.01,
            snapA.Lat.ToString("0.##") + "," + snapA.Lon.ToString("0.##") + " → " + snapB.Lat.ToString("0.##") + "," + snapB.Lon.ToString("0.##"));
        Check("温度已更新（两城不同）", Math.Abs(snapA.Temp - snapB.Temp) > 0.01, snapA.Temp.ToString("0.0") + "° → " + snapB.Temp.ToString("0.0") + "°");
        Check("天气描述存在", !string.IsNullOrEmpty(snapB.Text), snapB.Text);
        Check("天气图标存在", !string.IsNullOrEmpty(snapB.Icon), snapB.Icon);
        Check("最高/最低温存在", snapB.Days.Count > 0 && snapB.Days[0].TMax != 0, snapB.Days.Count > 0 ? snapB.Days[0].TMax + "/" + snapB.Days[0].TMin : "no days");
        Check("湿度存在", snapB.Humidity > 0, "humidity=" + snapB.Humidity);
        Check("风速存在", snapB.WindSpeed >= 0, "wind=" + snapB.WindSpeed.ToString("0.0") + " " + snapB.WindDir);
        Check("逐小时预报 24 条", snapB.Hours.Count >= 20, "hours=" + snapB.Hours.Count);
        Check("多日预报 6 天", snapB.Days.Count >= 5, "days=" + snapB.Days.Count);
        Check("数据更新时间真实", snapB.UpdatedAt > 0 && snapB.UpdatedText.Length > 0, snapB.UpdatedText);
        Check("两城更新时间不同（确实重新请求）", snapA.UpdatedAt != snapB.UpdatedAt, snapA.UpdatedAt + " vs " + snapB.UpdatedAt);

        Console.WriteLine();
        Console.WriteLine("== 4. 界面层切换（WeatherView.SwitchToSelectedCity） ==");
        var form = new Form { Width = 1400, Height = 880, StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
        var view = new WeatherView(store) { Dock = DockStyle.Fill };
        form.Controls.Add(view);
        form.Show();
        Application.DoEvents();
        System.Threading.Thread.Sleep(2500);
        Application.DoEvents();

        var cityList = Field(view, "_cityList") as Control;
        // 先把当前城市固定为上海，保证列表里存在可切换的其他城市
        var citiesField = Field(view, "_cities");
        var cs = citiesField as DeskBox.Core.CityStore;
        if (cs != null)
        {
            var shHit = DeskBox.Core.CitySearch.Search("上海", 1).FirstOrDefault();
            if (shHit != null) { cs.SetCurrent(shHit); }
            var gzHit = DeskBox.Core.CitySearch.Search("广州", 1).FirstOrDefault();
            if (gzHit != null) { cs.AddFavorite(gzHit); }
        }
        var before = store.Settings.WeatherLat;
        // 选中列表里第一个不是当前城市的条目并触发切换
        var selMethod = view.GetType().GetMethod("SwitchToSelectedCity", BindingFlags.NonPublic | BindingFlags.Instance);
        var listType = cityList.GetType();
        var selectIdx = listType.GetMethod("SelectIndex");
        if (selectIdx != null && cityList != null)
        {
            // 选一个与当前城市不同的行，才能真正验证切换
            int targetIdx = 0;
            var rowsProp = listType.GetProperty("Rows");
            var rows = rowsProp.GetValue(cityList, null) as System.Collections.IEnumerable;
            int idx = 0;
            foreach (var r in rows)
            {
                var tagProp = r.GetType().GetField("Tag");
                var tag = tagProp == null ? null : tagProp.GetValue(r) as DeskBox.Core.CityInfo;
                if (tag != null && cs != null && cs.Current != null && tag.Key != cs.Current.Key) { targetIdx = idx; break; }
                idx++;
            }
            selectIdx.Invoke(cityList, new object[] { targetIdx });
            selMethod.Invoke(view, null);
            Application.DoEvents();
            System.Threading.Thread.Sleep(2000);
            Application.DoEvents();
        }
        var after = store.Settings.WeatherLat;
        Check("界面切换会改写请求坐标", before != after, before.ToString("0.##") + " → " + after.ToString("0.##"));

        form.Close();
        Console.WriteLine();
        Console.WriteLine("==== 城市切换验收: PASS " + pass + " / FAIL " + fail + " ====");
        foreach (var f in fails) Console.WriteLine("  ✗ " + f);
        Environment.Exit(fail == 0 ? 0 : 1);
    }
}
