// 天气设置验收：温度单位真实生效、刷新间隔控制缓存。
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Core;
using DeskBox.UI;
using DeskBox.UI.Views;

public class WeatherSettingsAccept
{
    static int pass, fail;
    static void Check(string n, bool ok, string d) { if (ok) { pass++; Console.WriteLine("PASS  " + n + "  [" + d + "]"); } else { fail++; Console.WriteLine("FAIL  " + n + "  [" + d + "]"); } }

    [STAThread]
    public static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "deskbox-weather-setting-" + DateTime.Now.Ticks);
        Directory.CreateDirectory(dir); Store.DataDir = dir; AppPaths.DataDir = dir;
        var store = new Store(); store.Init(); store.Settings.TempUnit = "F"; store.Settings.WeatherRefreshMinutes = 30;
        var view = new WeatherView(store);
        var m = typeof(WeatherView).GetMethod("Temperature", BindingFlags.NonPublic | BindingFlags.Instance);
        Check("0°C 转换为 32°F", (string)m.Invoke(view, new object[] { 0.0 }) == "32°F", (string)m.Invoke(view, new object[] { 0.0 }));
        Check("100°C 转换为 212°F", (string)m.Invoke(view, new object[] { 100.0 }) == "212°F", (string)m.Invoke(view, new object[] { 100.0 }));
        store.Settings.TempUnit = "C";
        Check("摄氏度显示带单位", (string)m.Invoke(view, new object[] { 20.0 }) == "20°C", (string)m.Invoke(view, new object[] { 20.0 }));

        var cacheField = typeof(WeatherService).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static);
        var atField = typeof(WeatherService).GetField("_cacheAt", BindingFlags.NonPublic | BindingFlags.Static);
        var fake = new WeatherSnapshot { City = "CACHE-30", Source = "online", UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 15 * 60 * 1000L };
        cacheField.SetValue(null, fake); atField.SetValue(null, fake.UpdatedAt);
        store.Settings.WeatherRefreshMinutes = 30;
        var kept = WeatherService.Get(store.Settings, false);
        Check("30 分钟设置保留 15 分钟缓存", object.ReferenceEquals(fake, kept), kept.City);

        store.Settings.WeatherRefreshMinutes = 10;
        var refreshed = WeatherService.Get(store.Settings, false);
        Check("10 分钟设置刷新 15 分钟缓存", !object.ReferenceEquals(fake, refreshed) && refreshed.City != "CACHE-30", refreshed.City + "/" + refreshed.Source);
        view.Dispose();
        Console.WriteLine("\n==== 天气设置验收: PASS " + pass + " / FAIL " + fail + " ====");
        return fail == 0 ? 0 : 1;
    }
}
