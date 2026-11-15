using System;
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
                try { using (var f = new SettingsForm(store, Palette.For(store.Settings.Theme, store.Settings.Accent))) f.ShowDialog(FindForm()); }
                catch (Exception ex) { MessageBox.Show("打开失败：" + ex.Message); }
            };
            Controls.Add(btn);
        }
    }
}
