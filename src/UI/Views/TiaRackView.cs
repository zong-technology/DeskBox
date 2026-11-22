using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Engineering.Tia;

namespace DeskBox.UI.Views
{
    internal sealed class TiaRackView : UserControl
    {
        internal readonly ComboBox Containers = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true, AccessibleName = "机架容器" };
        internal event Action<PlcObject> AssetSelected;
        private readonly FlowLayoutPanel modules = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, AutoEllipsis = true, UseMnemonic = false };
        private Dictionary<string, PlcObject> assets = new Dictionary<string, PlcObject>();
        private ILookup<string, PlcObject> children = new PlcObject[0].ToLookup(x => x.ParentAssetId);
        private PlcObject selected;
        private Palette palette = Palette.For("midnight", "#55D9F5");

        internal TiaRackView()
        {
            Dock = DockStyle.Fill;
            AccessibleName = "机架槽位视图";
            var header = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Height = TiaWorkbenchStyle.Pixels(this, 30), Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TiaWorkbenchStyle.Pixels(this, 105)));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.Controls.Add(new Label { Text = "设备 / 机架", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            header.Controls.Add(Containers, 1, 0);
            status.Padding = new Padding(6, 0, 0, 0);
            status.Height = TiaWorkbenchStyle.Pixels(this, 19);
            modules.Padding = new Padding(6, 0, 0, 0);
            Controls.Add(modules);
            Controls.Add(status);
            Controls.Add(header);
            Containers.Format += delegate(object sender, ListControlConvertEventArgs e)
            {
                var asset = (PlcObject)e.ListItem;
                e.Value = asset.Scope.Length == 0 ? asset.Name : asset.Scope + " / " + asset.Name;
            };
            Containers.SelectedIndexChanged += delegate { RenderModules(); };
            Containers.SelectionChangeCommitted += delegate { NotifySelection(Containers.SelectedItem as PlcObject); };
            RenderModules();
        }

        internal void LoadAssets(IEnumerable<PlcObject> objects)
        {
            selected = null;
            var supported = objects.Where(x => x.AssetId.Length != 0 && (PlcEngineering.IsHardware(x) || x.Kind == "NetworkInterface")).ToArray();
            assets = supported.ToDictionary(x => x.AssetId);
            children = supported.Where(x => PlcEngineering.IsHardware(x) && assets.ContainsKey(x.ParentAssetId)).ToLookup(x => x.ParentAssetId);
            var containers = supported.Where(x => x.Kind == "Device" || children.Contains(x.AssetId)).ToArray();
            Containers.Items.Clear();
            Containers.Items.AddRange(containers);
            Containers.Enabled = containers.Length != 0;
            Containers.SelectedItem = containers.FirstOrDefault(x => children[x.AssetId].Any(y => y.Slot.Length != 0)) ?? containers.FirstOrDefault();
            if (containers.Length == 0) RenderModules();
        }

        internal void FocusAsset(PlcObject asset)
        {
            selected = asset;
            if (Containers.SelectedItem != asset)
            {
                PlcObject parent;
                var container = asset.Kind != "Device" && assets.TryGetValue(asset.ParentAssetId, out parent) ? parent : asset;
                Containers.SelectedIndex = Containers.Items.IndexOf(container);
            }
            ApplyPalette(palette);
        }

        private void RenderModules()
        {
            modules.SuspendLayout();
            try
            {
                foreach (Control control in modules.Controls.Cast<Control>().ToArray()) control.Dispose();
                var container = Containers.SelectedItem as PlcObject;
                var slots = container == null ? new PlcObject[0] : children[container.AssetId]
                    .OrderBy(x => SortSlot(x.Slot)).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(200).ToArray();
                foreach (var item in slots)
                {
                    var title = item.Slot.Length == 0 ? "未记录槽位" : "槽位 " + item.Slot;
                    var card = new Button { Text = title + "\r\n" + item.Name, Width = TiaWorkbenchStyle.Pixels(this, 112), Height = TiaWorkbenchStyle.Pixels(this, 62),
                        Margin = new Padding(0, 2, 5, 0), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, UseMnemonic = false,
                        AutoEllipsis = true, AccessibleName = title + " · " + item.Name, AccessibleDescription = item.Source, Tag = item };
                    card.Click += delegate { NotifySelection(item); };
                    modules.Controls.Add(card);
                }
                status.Text = container == null ? "载入含设备层级的工程资料。" : slots.Length == 0 ? "当前来源未记录此设备的模块层级。" :
                    slots.Length + " 个可见模块 · " + container.Source + (children[container.AssetId].Count() > slots.Length ? " · 其余请查看硬件清单" : "");
                ApplyPalette(palette);
            }
            finally { modules.ResumeLayout(); }
        }

        private static int SortSlot(string slot)
        {
            int value;
            return int.TryParse(slot, NumberStyles.None, CultureInfo.InvariantCulture, out value) ? value : int.MaxValue;
        }

        private void NotifySelection(PlcObject item)
        {
            if (item != null && AssetSelected != null) AssetSelected(item);
        }

        internal void ApplyPalette(Palette colors)
        {
            palette = colors;
            BackColor = colors.Bg;
            modules.BackColor = colors.CardBg;
            Containers.BackColor = colors.CardBg;
            Containers.ForeColor = colors.Text;
            status.BackColor = colors.CardBg;
            status.ForeColor = colors.TextDim;
            foreach (Button card in modules.Controls)
            {
                bool active = card.Tag == selected;
                card.FlatAppearance.BorderColor = active ? colors.Accent : colors.Line;
                card.FlatAppearance.MouseOverBackColor = colors.Hover;
                card.BackColor = active ? UiKit.Mix(colors.PanelAlt, colors.Accent, 0.18) : colors.PanelAlt;
                card.ForeColor = colors.Text;
            }
        }
    }
}
