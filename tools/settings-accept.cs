// 设置中心验收（规格十一）：7 页切换、设置真实持久化、API Key 不泄漏
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.UI.Views;

public class SettingsAccept
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

    static void Invoke(object o, string name, params object[] args)
    {
        var m = o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m != null) m.Invoke(o, args);
    }

    // 递归收集所有 TextBox / ComboBox / CheckBox 的可见文本
    static void CollectTexts(Control c, List<string> outList)
    {
        foreach (Control cc in c.Controls)
        {
            var tb = cc as TextBox;
            if (tb != null) outList.Add(tb.Text ?? "");
            var cb = cc as ComboBox;
            if (cb != null) { outList.Add(cb.Text ?? ""); foreach (var it in cb.Items) outList.Add(Convert.ToString(it)); }
            var l = cc as Label;
            if (l != null) outList.Add(l.Text ?? "");
            CollectTexts(cc, outList);
        }
    }

    [STAThread]
    public static int Main()
    {
        Application.EnableVisualStyles();
        var dir = Path.Combine(Path.GetTempPath(), "setacc-" + DateTime.Now.Ticks);
        Directory.CreateDirectory(dir);
        DeskBox.Core.AppPaths.DataDir = dir;
        DeskBox.Store.DataDir = dir;

        var store = new DeskBox.Store();
        store.Init();

        var providers = new ProviderStore();
        var p1 = new AiProvider
        {
            Name = "测试服务商", Protocol = "openai",
            BaseUrl = "http://127.0.0.1:18080", Model = "mock-chat",
            ApiKey = "sk-test-1234567890abcdef", Enabled = true
        };
        providers.Add(p1);
        providers.Save();

        var form = new Form { Width = 1400, Height = 900, StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
        var view = new SettingsCenterView(store, providers) { Dock = DockStyle.Fill };
        form.Controls.Add(view);
        form.Show();
        Application.DoEvents();

        var nav = (System.Collections.IList)Field(view, "_nav");
        Check("二级导航有 7 页", nav != null && nav.Count == 7, "count=" + (nav == null ? 0 : nav.Count));

        var keys = new[] { "appearance", "ai", "weather", "automation", "privacy", "notify", "system" };
        var titles = new List<string>();
        for (int i = 0; i < keys.Length; i++)
        {
            Invoke(view, "Switch", i);
            Application.DoEvents();
            var host = Field(view, "_contentHost") as Panel;
            Check("第 " + (i + 1) + " 页（" + keys[i] + "）有内容", host != null && host.Controls.Count > 0,
                "controls=" + (host == null ? 0 : host.Controls.Count));
            if (host != null && host.Controls.Count > 0)
            {
                var texts = new List<string>();
                CollectTexts(host, texts);
                titles.Add(keys[i] + ":" + texts.Count);
            }
        }
        Console.WriteLine("  页面控件文本数: " + string.Join(" | ", titles.ToArray()));

        Console.WriteLine();
        Console.WriteLine("== 设置真实持久化 ==");
        store.Settings.Theme = "aurora";
        store.Settings.Accent = "#34D399";
        store.Settings.Animations = false;
        store.Settings.WeatherRefreshMinutes = 30;
        store.Settings.WorkflowFailMode = "skip";
        store.Settings.WorkflowConcurrency = 2;
        store.Settings.AiFileAccess = true;
        store.Settings.NotifyTray = true;
        store.Settings.LogKeepDays = 90;
        store.SaveSettingsOnly();

        var store2 = new DeskBox.Store();
        store2.Init();
        var s2 = store2.Settings;
        Check("主题持久化", s2.Theme == "aurora", s2.Theme);
        Check("强调色持久化", s2.Accent == "#34D399", s2.Accent);
        Check("动画开关持久化", s2.Animations == false, s2.Animations.ToString());
        Check("天气刷新间隔持久化", s2.WeatherRefreshMinutes == 30, s2.WeatherRefreshMinutes.ToString());
        Check("失败策略持久化", s2.WorkflowFailMode == "skip", s2.WorkflowFailMode);
        Check("并发上限持久化", s2.WorkflowConcurrency == 2, s2.WorkflowConcurrency.ToString());
        Check("AI 文件访问开关持久化", s2.AiFileAccess == true, s2.AiFileAccess.ToString());
        Check("托盘通知开关持久化", s2.NotifyTray == true, s2.NotifyTray.ToString());
        Check("日志保留天数持久化", s2.LogKeepDays == 90, s2.LogKeepDays.ToString());

        Console.WriteLine();
        Console.WriteLine("== 旧配置兼容（缺少新字段不崩、不丢旧值） ==");
        var legacyFile = Path.Combine(dir, "state.json");
        var legacyJson = "{\"Settings\":{\"Theme\":\"daylight\",\"Accent\":\"#FBBF24\",\"IconSize\":72},\"Categories\":[],\"Items\":{}}";
        File.WriteAllText(legacyFile, legacyJson);
        var store3 = new DeskBox.Store();
        store3.Init();
        Check("旧配置能读", store3.Settings.Theme == "daylight", store3.Settings.Theme);
        Check("旧配置保留图标尺寸", store3.Settings.IconSize == 72, store3.Settings.IconSize.ToString());
        Check("缺失新字段取默认值", store3.Settings.WeatherRefreshMinutes == 10 && store3.Settings.LogKeepDays == 30,
            store3.Settings.WeatherRefreshMinutes + "/" + store3.Settings.LogKeepDays);

        Console.WriteLine();
        Console.WriteLine("== API Key 不泄漏 ==");
        Invoke(view, "Switch", 1);
        Application.DoEvents();
        var hostAi = Field(view, "_contentHost") as Panel;
        var aiTexts = new List<string>();
        CollectTexts(hostAi, aiTexts);
        bool leaked = aiTexts.Any(t => (t ?? "").Contains("sk-test-1234567890abcdef"));
        Check("界面文本不含完整 Key", !leaked, "checked " + aiTexts.Count + " texts");
        var keyField = Field(view, "_fKey") as Control;
        if (keyField != null)
        {
            var innerProp = keyField.GetType().GetProperty("Inner", BindingFlags.Public | BindingFlags.Instance);
            var tb = innerProp == null ? null : innerProp.GetValue(keyField, null) as TextBox;
            var keyText = (keyField as DeskBox.UI.DarkInput);
            bool masked = keyText != null && (keyText.Text == "" || keyText.Text.Contains("…"));
            Check("Key 输入框只显示掩码", masked, keyText == null ? "no control" : ("text=" + keyText.Text));
        }

        Console.WriteLine();
        Console.WriteLine("== 服务商 CRUD ==");
        int before = providers.Providers.Count;
        var np = new AiProvider { Name = "第二家", BaseUrl = "https://api.example.com", Model = "m", ApiKey = "sk-2", Enabled = true };
        var added = providers.Add(np);              // Add 返回带 Id 的实例
        providers.Save();
        Check("新增服务商", providers.Providers.Count == before + 1, "count=" + providers.Providers.Count);

        var reload = new ProviderStore();
        Check("新增后重载存在", reload.Providers.Count == before + 1, "count=" + reload.Providers.Count);
        Check("Key 加密后仍可解密", reload.Providers.Any(x => x.ApiKey == "sk-2"), "ok");

        providers.SetDefault(added.Id);
        providers.Save();
        var reload2 = new ProviderStore();
        Check("默认服务商已切换", reload2.DefaultProvider != null && reload2.DefaultProvider.Name == "第二家",
            reload2.DefaultProvider == null ? "null" : reload2.DefaultProvider.Name);

        providers.Remove(added.Id);
        providers.Save();
        var reload3 = new ProviderStore();
        Check("删除服务商", reload3.Providers.Count == before, "count=" + reload3.Providers.Count);
        Check("删除后仍有默认服务商", reload3.DefaultProvider != null, reload3.DefaultProvider == null ? "null" : reload3.DefaultProvider.Name);

        var json = File.ReadAllText(Path.Combine(dir, "ai-providers.json"));
        Check("配置文件无明文 Key", !json.Contains("sk-test-1234567890abcdef") && !json.Contains("\"sk-2\""), "json len=" + json.Length);

        Console.WriteLine();
        Console.WriteLine("== 授权目录（fail-closed 基础） ==");
        var workDir = Path.Combine(dir, "work");
        Directory.CreateDirectory(workDir);
        DeskBox.UI.TrustedRoots.Add(workDir);
        var roots = DeskBox.UI.TrustedRoots.Load();
        Check("授权目录已保存", roots.Any(r => string.Equals(r, workDir, StringComparison.OrdinalIgnoreCase)), "count=" + roots.Count);
        DeskBox.UI.TrustedRoots.Remove(workDir);
        Check("授权目录可移除", !DeskBox.UI.TrustedRoots.Load().Any(r => string.Equals(r, workDir, StringComparison.OrdinalIgnoreCase)), "ok");

        form.Close();
        Console.WriteLine();
        Console.WriteLine("==== 设置中心验收: PASS " + pass + " / FAIL " + fail + " ====");
        foreach (var f in fails) Console.WriteLine("  ✗ " + f);
        Environment.Exit(fail == 0 ? 0 : 1);
        return fail == 0 ? 0 : 1;
    }
}
