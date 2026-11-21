using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Engineering.Tia;

namespace DeskBox.UI.Views
{
    internal sealed class TiaTopologyCanvas : Panel
    {
        private readonly Panel drawing = new Panel();
        private readonly Label empty = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true };
        private readonly Dictionary<string, DarkButton> nodes = new Dictionary<string, DarkButton>();
        private PlcTopologyLink[] links = new PlcTopologyLink[0];
        private PlcTopologyLink selected;
        private Palette palette = Palette.For("cyber", null);
        private int percent = 100;
        internal int ZoomPercent { get { return percent; } }
        internal event Action<PlcObject> EndpointSelected;

        internal TiaTopologyCanvas()
        {
            Name = "tia-topology-canvas";
            AccessibleName = "网络拓扑画布";
            Dock = DockStyle.Fill;
            AutoScroll = true;
            drawing.Paint += DrawConnections;
            empty.Text = "未读取到可绘制的连接";
            Controls.Add(drawing);
            Controls.Add(empty);
            drawing.Visible = false;
        }

        internal void LoadNetwork(IDictionary<string, PlcObject> assets, IEnumerable<PlcTopologyLink> source)
        {
            selected = null;
            foreach (var node in nodes.Values) node.Dispose();
            nodes.Clear();
            var valid = source.Where(link => assets.ContainsKey(link.First) && assets.ContainsKey(link.Second)).ToArray();
            var endpoints = valid.SelectMany(link => new[] { link.First, link.Second }).Distinct().Take(48).ToArray();
            var visible = new HashSet<string>(endpoints, StringComparer.Ordinal);
            links = valid.Where(link => visible.Contains(link.First) && visible.Contains(link.Second)).ToArray();
            foreach (string key in endpoints)
            {
                var asset = assets[key];
                var node = new DarkButton(asset.Name) { Palette = palette, AccessibleName = asset.Name, AccessibleDescription = asset.Identity + " · " + asset.Source };
                node.Click += delegate { if (EndpointSelected != null) EndpointSelected(asset); };
                drawing.Controls.Add(node);
                nodes.Add(key, node);
            }
            AccessibleDescription = endpoints.Length + " 个端点 · " + links.Length + " 条可见连接。完整来源见连接清单。";
            ResetZoom();
            bool loaded = nodes.Count != 0;
            drawing.Visible = loaded;
            empty.Visible = !loaded;
        }

        internal void SelectLink(PlcTopologyLink link)
        {
            selected = link;
            foreach (var node in nodes)
            {
                node.Value.Primary = link != null && (node.Key == link.First || node.Key == link.Second);
                node.Value.Invalidate();
            }
            drawing.Invalidate();
        }

        internal void ResetZoom() { percent = 100; PositionNodes(); }
        internal void ZoomBy(int step)
        {
            percent = Math.Max(60, Math.Min(180, percent + 20 * step));
            PositionNodes();
        }

        private void PositionNodes()
        {
            const int columns = 3;
            int stepX = Dpi.D(this, 162) * percent / 100;
            int stepY = Dpi.D(this, 90) * percent / 100;
            int pad = Dpi.D(this, 14);
            int index = 0;
            drawing.SuspendLayout();
            foreach (var node in nodes.Values)
            {
                node.SetBounds(pad + index % columns * stepX, pad + index / columns * stepY, stepX - pad, Math.Max(25, stepY * 2 / 3));
                index++;
            }
            drawing.Size = new Size(Math.Min(columns, nodes.Count) * stepX + pad, ((nodes.Count + columns - 1) / columns) * stepY + pad);
            AutoScrollMinSize = nodes.Count == 0 ? Size.Empty : drawing.Size;
            drawing.ResumeLayout();
            drawing.Invalidate();
        }

        private void DrawConnections(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var line = new Pen(palette.Line, Dpi.D(this, 2)))
            using (var active = new Pen(palette.Accent, Dpi.D(this, 2)))
                foreach (var link in links.OrderBy(link => link == selected))
                {
                    var a = nodes[link.First].Bounds;
                    var b = nodes[link.Second].Bounds;
                    e.Graphics.DrawLine(link == selected ? active : line, a.Left + a.Width / 2, a.Top + a.Height / 2, b.Left + b.Width / 2, b.Top + b.Height / 2);
                }
        }

        internal void ApplyPalette(Palette colors)
        {
            palette = colors;
            BackColor = drawing.BackColor = empty.BackColor = colors.CardBg;
            empty.ForeColor = colors.TextDim;
            foreach (var node in nodes.Values) node.ApplyPalette(colors);
            drawing.Invalidate();
        }
    }
}
