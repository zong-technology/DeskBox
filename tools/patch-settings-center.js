const fs = require('fs');

// 1) MainForm：设置项改为打开 SettingsCenterView
let p = 'src/UI/MainForm.cs';
let s = fs.readFileSync(p, 'utf8');

if (!s.includes('SettingsCenterView')) {
  // 找到设置导航的 case 分支
  if (s.includes('case "settings": return BuildSettingsView();')) {
    s = s.replace('case "settings": return BuildSettingsView();', 'case "settings": return BuildSettingsCenter();');
  }
  // 新增构建方法（放在 BuildWeatherView 之后）
  const anchor = `        private UserControl BuildWeatherView()`;
  const insert = `        private DeskBox.UI.Views.SettingsCenterView _settingsCenter;

        private UserControl BuildSettingsCenter()
        {
            try
            {
                _settingsCenter = new DeskBox.UI.Views.SettingsCenterView(_store, _providers);
                _settingsCenter.ProvidersChanged += delegate
                {
                    try { if (_chat != null && _providers != null) _chat.SetProviders(_providers); } catch { }
                };
                _settingsCenter.AppearanceChanged += delegate
                {
                    try
                    {
                        var p2 = Palette.For(_store.Settings.Theme, _store.Settings.Accent);
                        ApplyPaletteEverywhere(p2);
                    }
                    catch (Exception ex) { AppLog.Write("warn", "system", "应用外观失败: " + ex.Message); }
                };
                return _settingsCenter;
            }
            catch (Exception ex)
            {
                AppLog.Write("error", "system", "设置中心创建失败，回退旧设置: " + ex.Message);
                return new DeskBox.UI.SettingsFormHost(_store);
            }
        }

        private void ApplyPaletteEverywhere(Palette p2)
        {
            try
            {
                this.Palette = p2;
                BackColor = p2.Bg;
                foreach (Control c in Controls) ApplyPaletteRecursive(c, p2);
                Invalidate(true);
            }
            catch { }
        }

        private void ApplyPaletteRecursive(Control c, Palette p2)
        {
            try
            {
                var v = c as DeskBox.UI.Views.ViewBase;
                if (v != null) { v.ApplyPalette(p2); return; }
                var m = c.GetType().GetMethod("ApplyPalette", new Type[] { typeof(Palette) });
                if (m != null) { m.Invoke(c, new object[] { p2 }); return; }
                if (c.BackColor != p2.Bg && c.BackColor != p2.Panel && c.BackColor != p2.PanelAlt) c.BackColor = p2.Bg;
                foreach (Control cc in c.Controls) ApplyPaletteRecursive(cc, p2);
            }
            catch { }
        }

        private UserControl BuildWeatherView()`;
  if (s.includes(anchor)) { s = s.replace(anchor, insert); }
  fs.writeFileSync(p, s, 'utf8');
}

console.log('MainForm settings center:', s.includes('BuildSettingsCenter'), '| palette apply:', s.includes('ApplyPaletteEverywhere'));

// 2) 提供 SettingsFormHost 包装（旧设置对话框的 UserControl 版本），避免回退时编译不过
const host = `using System;
using System.Windows.Forms;

namespace DeskBox.UI
{
    /// <summary>旧设置对话框的视图包装：设置中心创建失败时的降级路径。</summary>
    public class SettingsFormHost : UserControl
    {
        public SettingsFormHost(Store store)
        {
            BackColor = Palette.For(store.Settings.Theme, store.Settings.Accent).Bg;
            var btn = new DarkButton("打开旧版设置对话框") { Left = 16, Top = 16, Width = 180 };
            btn.Palette = Palette.For(store.Settings.Theme, store.Settings.Accent);
            btn.Click += delegate
            {
                try { using (var f = new SettingsForm(store)) f.ShowDialog(FindForm()); }
                catch (Exception ex) { MessageBox.Show("打开失败：" + ex.Message); }
            };
            Controls.Add(btn);
        }
    }
}
`;
fs.writeFileSync('src/UI/SettingsFormHost.cs', host, 'utf8');
console.log('SettingsFormHost written');
