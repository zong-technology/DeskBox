using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DeskBox.Native;
using DeskBox.Shell;

namespace DeskBox.UI
{
    public enum BoardView { Grid, List, Compact, Details }

    /// <summary>条目画板：按分类分组绘制、命中测试、拖拽、右键菜单、滚动。</summary>
    public class IconBoard : Control
    {
        private readonly Store _store;
        private List<DesktopItem> _items = new List<DesktopItem>();
        private readonly Dictionary<string, Icon> _iconCache = new Dictionary<string, Icon>();
        private readonly Dictionary<string, Rectangle> _itemRects = new Dictionary<string, Rectangle>();
        private readonly Dictionary<string, Rectangle> _headerRects = new Dictionary<string, Rectangle>();
        private readonly Dictionary<string, Rectangle> _sectionRects = new Dictionary<string, Rectangle>();
        private readonly HashSet<string> _selection = new HashSet<string>();
        private readonly HashSet<string> _collapsed = new HashSet<string>();

        private int _scrollY;
        private int _contentHeight;
        private Point _lastMouse;
        private string _hoverId;
        private string _filter = "all";
        private string _query = "";
        private string _dragId;
        private Point _dragPoint;
        private bool _dragging;

        public event Action<DesktopItem> ItemOpened;
        public event Action<DesktopItem> SelectionChanged;
        public event Action<string, Point> ItemDropped;   // id, 屏幕坐标
        public event Action<DesktopItem, Point> ContextRequested;
        public event Action<string> CommandRequested;
        public event Action<List<string>, bool> ExternalFilesDropped;

        private IThumbnailService _thumbnails;

        public BoardView View { get; set; }
        private bool? _referenceEngineering;
        public bool EngineeringMode { get { return _referenceEngineering ?? _store.Settings.EngineeringDesktop; } }

        internal void UseReferenceDesktop(bool? engineering)
        {
            _referenceEngineering = engineering;
            _scrollY = 0;
            Invalidate();
        }
        public void SetEngineeringMode(bool enabled) { _store.Settings.EngineeringDesktop = enabled; _store.SaveSettingsOnly(); _scrollY = 0; Invalidate(); }

        public static string EngineeringGroup(DesktopItem item)
        {
            var name = (item.Name ?? "").ToUpperInvariant();
            string ext = Path.GetExtension(name);
            if (Regex.IsMatch(name, @"(^|[^A-Z])(FAT|SAT)([^A-Z]|$)")) return "FAT / SAT";
            if (Regex.IsMatch(name, @"(^|[^A-Z])(IQ|OQ|PQ|URS|SIA|VSR|FDS|DDS|GMP|GAMP)([^A-Z]|$)") || name.Contains("验证")) return "VALIDATION / 验证";
            if (Regex.IsMatch(name, @"(^|[^A-Z])IO([^A-Z]|$)") || name.Contains("点表") || name.Contains("IO清单")) return "IO LIST / 信号清单";
            if (name.Contains("PLC") || Regex.IsMatch(ext, @"^\.(Z?AP\d+|SCL|AWL|LAD|DB)$")) return "PLC ENGINEERING";
            if (name.Contains("DCS") || name.Contains("DELTAV") || name.Contains("PCS7") || ext == ".FHX") return "DCS / CONTROL SYSTEM";
            if (name.Contains("HMI") || name.Contains("WINCC") || ext == ".HMI") return "HMI / VISUALIZATION";
            return item.Kind == "dir" ? "PROJECT FOLDERS / 目录" : "DOCUMENTS / 其他文件";
        }

        /// <summary>
        /// 缩略图服务（由 MainForm 注入并负责 Dispose）。只读取，不接管生命周期。
        /// 就绪通知来自后台线程，这里安全地转投到 UI 线程刷新。
        /// </summary>
        public IThumbnailService Thumbnails
        {
            get { return _thumbnails; }
            set
            {
                if (ReferenceEquals(_thumbnails, value)) return;
                if (_thumbnails != null) { try { _thumbnails.Ready -= OnThumbnailReady; } catch { } }
                _thumbnails = value;
                if (_thumbnails != null) { try { _thumbnails.Ready += OnThumbnailReady; } catch { } }
                Invalidate();
            }
        }

        /// <summary>缩略图只在 Grid 且图标尺寸较大时启用；List/Compact 保持轻量。</summary>
        public static bool ThumbnailsSupported(BoardView view, int iconSize)
        {
            return view == BoardView.Grid && iconSize >= ThumbnailPolicy.MinIconSize;
        }

        // 由 MainForm 注入：绘制壁纸背景（offset = 本控件在主窗体客户区中的偏移）
        public Action<Graphics, Point> BackdropPainter;
        public Palette Palette { get; set; }

        public IconBoard(Store store)
        {
            _store = store;
            Palette = Palette.For(store.Settings.Theme, store.Settings.Accent);
            View = ParseView(store.Settings.View);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Palette.Bg;
            AllowDrop = true;
        }

        public static BoardView ParseView(string v)
        {
            if (string.Equals(v, "list", StringComparison.OrdinalIgnoreCase)) return BoardView.List;
            if (string.Equals(v, "compact", StringComparison.OrdinalIgnoreCase)) return BoardView.Compact;
            if (string.Equals(v, "details", StringComparison.OrdinalIgnoreCase)) return BoardView.Details;
            return BoardView.Grid;
        }

        public List<string> SelectedIds { get { return _selection.ToList(); } }
        public List<DesktopItem> SelectedItems { get { return _items.Where(x => _selection.Contains(x.Id)).ToList(); } }

        public void SelectAllVisible()
        {
            _selection.Clear();
            foreach (var item in VisibleItems()) _selection.Add(item.Id);
            Invalidate();
            var changed = SelectionChanged;
            if (changed != null) changed(SelectedItems.FirstOrDefault());
        }

        public void SetData(List<DesktopItem> items)
        {
            var previous = _items.Where(x => x != null && !string.IsNullOrEmpty(x.Path)).GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First().Mtime, StringComparer.OrdinalIgnoreCase);
            _items = items ?? new List<DesktopItem>();
            var alive = new HashSet<string>(_items.Select(i => i.Id));
            _selection.RemoveWhere(id => !alive.Contains(id));
            // 丢弃已不在当前视图的缩略图，避免缓存被历史目录占满
            if (_thumbnails != null)
            {
                try
                {
                    foreach (var item in _items)
                    {
                        long oldStamp;
                        if (previous.TryGetValue(item.Path, out oldStamp) && oldStamp != item.Mtime) _thumbnails.Invalidate(item.Path);
                    }
                    _thumbnails.Prune(_items.Select(x => x.Path));
                }
                catch { }
            }
            Invalidate();
        }

        public void SetFilter(string filter) { _filter = filter ?? "all"; _scrollY = 0; Invalidate(); }
        public void SetQuery(string q) { _query = (q ?? "").Trim().ToLowerInvariant(); _scrollY = 0; Invalidate(); }

        public void RefreshTheme()
        {
            Palette = Palette.For(_store.Settings.Theme, _store.Settings.Accent);
            View = ParseView(_store.Settings.View);
            Invalidate();
        }

        public void ClearSelection()
        {
            if (_selection.Count == 0) return;
            _selection.Clear();
            Invalidate();
            var h = SelectionChanged; if (h != null) h(null);
        }

        private Icon GetIcon(DesktopItem it)
        {
            Icon ic;
            if (_iconCache.TryGetValue(it.Id, out ic)) return ic;
            ic = Win32.GetShellIcon(it.Path, true);
            if (ic == null && it.Kind == "dir") ic = Win32.GetShellIcon(Environment.GetFolderPath(Environment.SpecialFolder.Windows), true);
            _iconCache[it.Id] = ic;
            return ic;
        }

        public void InvalidateIcons()
        {
            foreach (var i in _iconCache.Values) { try { i.Dispose(); } catch { } }
            _iconCache.Clear();
        }

        // ---------- 数据视图 ----------
        private List<DesktopItem> VisibleItems()
        {
            IEnumerable<DesktopItem> q = _items;
            if (!_store.Settings.ShowHidden) q = q.Where(i => !i.Hidden);
            if (_filter == "hidden") q = q.Where(i => i.Hidden);
            else if (_filter == "uncategorized") q = q.Where(i => string.IsNullOrEmpty(i.CategoryId));
            else if (_filter != "all") q = q.Where(i => i.CategoryId == _filter);
            if (_query.Length > 0) q = q.Where(i => i.Name.ToLowerInvariant().Contains(_query));
            var list = q.ToList();
            var by = _store.Settings.SortBy;
            bool desc = _store.Settings.SortDir == "desc";
            Comparison<DesktopItem> cmp;
            if (by == "mtime") cmp = (a, b) => a.Mtime.CompareTo(b.Mtime);
            else if (by == "size") cmp = (a, b) => a.Size.CompareTo(b.Size);
            else if (by == "kind") cmp = (a, b) => string.Compare(a.Kind, b.Kind, StringComparison.Ordinal);
            else cmp = (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            list.Sort(cmp);
            if (desc) list.Reverse();
            return list;
        }

        private List<Category> VisibleCategories(List<DesktopItem> items)
        {
            var cats = _store.Categories.OrderBy(c => c.Order).ToList();
            if (_filter == "hidden" || _filter == "uncategorized" || _filter == "all") return cats;
            return cats.Where(c => c.Id == _filter).ToList();
        }

        // ---------- 绘制 ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (_referenceEngineering == true) g.Clear(Palette.CardBg);
            else if (EngineeringMode)
            {
                g.Clear(Palette.Bg);
                using (var grid = new Pen(Color.FromArgb(14, Palette.Accent)))
                {
                    for (int x = 0; x < Width; x += 36) g.DrawLine(grid, x, 0, x, Height);
                    for (int lineY = 0; lineY < Height; lineY += 36) g.DrawLine(grid, 0, lineY, Width, lineY);
                }
            }
            else if (BackdropPainter != null) BackdropPainter(g, Point.Empty); else g.Clear(Palette.Bg);

            _itemRects.Clear();
            _headerRects.Clear();
            _sectionRects.Clear();

            var items = VisibleItems();
            if (_referenceEngineering == true && View == BoardView.Grid && items.Count != 0)
            {
                DrawReferenceGrid(g, items);
                return;
            }
            var cats = VisibleCategories(items);
            var grouped = items.GroupBy(i => i.CategoryId ?? "").ToDictionary(gr => gr.Key, gr => gr.ToList());
            if (EngineeringMode)
            {
                string[] groups = { "PROJECT FOLDERS / 目录", "PLC ENGINEERING", "DCS / CONTROL SYSTEM", "HMI / VISUALIZATION", "IO LIST / 信号清单", "FAT / SAT", "VALIDATION / 验证", "DOCUMENTS / 其他文件" };
                cats = groups.Select((name, i) => new Category { Id = "engineering-" + i, Name = name, Icon = "", Color = i == 6 ? "#A78BFA" : "#22D3EE" }).ToList();
                grouped = items.GroupBy(EngineeringGroup).ToDictionary(gr => "engineering-" + Array.IndexOf(groups, gr.Key), gr => gr.ToList());
            }

            int pad = 14;
            int y = pad - _scrollY;
            int boardW = Width - pad * 2;

            if (items.Count == 0)
            {
                DrawEmpty(g, pad, pad, boardW);
                _contentHeight = Height;
                return;
            }

            int column = 0, rowBottom = y;
            foreach (var cat in cats)
            {
                List<DesktopItem> list;
                if (!grouped.TryGetValue(cat.Id, out list) || list.Count == 0) continue;
                if (EngineeringMode && View == BoardView.Grid && boardW >= 640 && list.Count <= 12)
                {
                    int tileWidth = (boardW - 18) / 2;
                    rowBottom = Math.Max(rowBottom, DrawSection(g, cat, list, pad + column * (tileWidth + 18), y, tileWidth));
                    column++;
                    if (column == 2) { y = rowBottom + 18; rowBottom = y; column = 0; }
                }
                else
                {
                    if (column > 0) { y = rowBottom + 18; column = 0; }
                    y = DrawSection(g, cat, list, pad, y, boardW) + 18; rowBottom = y;
                }
            }
            if (column > 0) y = rowBottom + 18;
            // 未分类兜底
            var orphan = EngineeringMode ? new List<DesktopItem>() : items.Where(i => string.IsNullOrEmpty(i.CategoryId)).ToList();
            if (orphan.Count > 0)
            {
                var cat = new Category { Id = "", Name = "未分类", Icon = "📥", Color = "#94A3B8" };
                y = DrawSection(g, cat, orphan, pad, y, boardW);
            }

            _contentHeight = y + _scrollY + pad;
            DrawScrollbar(g);
        }

        private void DrawEmpty(Graphics g, int x, int y, int w)
        {
            using (var f = new Font("Microsoft YaHei UI", 11F))
            using (var f2 = new Font("Microsoft YaHei UI", 9F))
            {
                var txt = "这里空空如也";
                var sub = _query.Length > 0 ? "没有匹配「" + _query + "」的条目" : "换个分类看看，或点右上角刷新";
                var s1 = g.MeasureString(txt, f);
                g.DrawString(txt, f, new SolidBrush(Palette.TextDim), x + (w - s1.Width) / 2, y + 80);
                var s2 = g.MeasureString(sub, f2);
                g.DrawString(sub, f2, new SolidBrush(Palette.TextFaint), x + (w - s2.Width) / 2, y + 110);
            }
        }

        private int DrawSection(Graphics g, Category cat, List<DesktopItem> list, int x, int y, int w)
        {
            int headerH = EngineeringMode ? 44 : 30;
            var headerRect = new Rectangle(x, y, w, headerH);
            _headerRects[cat.Id ?? ""] = headerRect;
            bool collapsed = _collapsed.Contains(cat.Id ?? "");
            var color = Palette.ParseColor(cat.Color, Palette.Accent);

            // 先算高度，再画半透明底板（保证壁纸上的文字可读，Fences 那种浮起卡片感）
            int cellW = Math.Max(78, _store.Settings.IconSize + 46);
            int cellH = ThumbnailsSupported(View, _store.Settings.IconSize)
                ? Math.Max(84, _store.Settings.IconSize + 60)
                : Math.Max(84, _store.Settings.IconSize + 46);
            int cols = Math.Max(1, (w - 4) / cellW);
            int rows = (list.Count + cols - 1) / cols;
            int contentH = 0;
            if (collapsed) contentH = headerH;
            else if (View == BoardView.Grid) contentH = headerH + rows * cellH + 10;
            else if (View == BoardView.List) contentH = headerH + list.Count * 42 + 10;
            else if (View == BoardView.Details) contentH = headerH + 24 + list.Count * 34 + 10;
            else contentH = headerH + list.Count * 27 + 10;

            int panelAlpha = Palette.IsLight ? 240 : 212;
            Palette.FillRounded(g, Color.FromArgb(panelAlpha, Palette.Panel), new Rectangle(x - 4, y - 4, w + 8, contentH + 4), 14);
            Palette.DrawRounded(g, Color.FromArgb(Palette.IsLight ? 60 : 46, Palette.Line), new Rectangle(x - 4, y - 4, w + 8, contentH + 4), 14, 1f);
            if (EngineeringMode)
            {
                using (var pen = new Pen(Color.FromArgb(90, color))) g.DrawLine(pen, x + 12, y - 3, x + Math.Min(w - 12, 260), y - 3);
            }

            using (var f = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold))
            using (var fs = new Font("Microsoft YaHei UI", 8.5F))
            {
                // 分类色条
                using (var b = new SolidBrush(color))
                    g.FillRectangle(b, x, y + 8, 3, 14);
                g.DrawString((collapsed ? "▸ " : "▾ ") + (cat.Icon ?? "📁"), fs, new SolidBrush(Palette.TextDim), x + 8, y + 7);
                g.DrawString(cat.Name, f, new SolidBrush(Palette.Text), x + 30, y + 4);
                var cnt = list.Count.ToString();
                g.DrawString(cnt, fs, new SolidBrush(Palette.TextFaint), x + 30 + g.MeasureString(cat.Name, f).Width + 8, y + 7);
            }
            y += headerH;
            if (collapsed) return y;

            if (View == BoardView.Grid)
            {
                int i = 0;
                foreach (var it in list)
                {
                    int col = i % cols, row = i / cols;
                    var r = new Rectangle(x + 2 + col * cellW, y + row * cellH, cellW - 6, cellH - 6);
                    DrawGridItem(g, it, r, color);
                    i++;
                }
                y += rows * cellH + 6;
            }
            else if (View == BoardView.List)
            {
                foreach (var it in list)
                {
                    var r = new Rectangle(x, y, w, 40);
                    DrawListItem(g, it, r, color);
                    y += 42;
                }
                y += 4;
            }
            else if (View == BoardView.Details)
            {
                DrawDetailsHeader(g, new Rectangle(x, y, w, 22));
                y += 24;
                foreach (var it in list)
                {
                    var r = new Rectangle(x, y, w, 32);
                    DrawDetailsItem(g, it, r, color);
                    y += 34;
                }
                y += 4;
            }
            else
            {
                foreach (var it in list)
                {
                    var r = new Rectangle(x, y, w, 26);
                    DrawCompactItem(g, it, r, color);
                    y += 27;
                }
                y += 4;
            }
            return y;
        }

        private void DrawReferenceGrid(Graphics g, List<DesktopItem> items)
        {
            const int padding = 10, gap = 12, height = 100;
            int available = Width - 2 * padding;
            if (available < 1) return;
            int columns = Math.Max(1, Math.Min(4, (available + gap) / (180 + gap)));
            int width = (available - (columns - 1) * gap) / columns;
            int rows = (items.Count + columns - 1) / columns;
            _contentHeight = padding * 2 + rows * (height + gap) - gap;
            _scrollY = Math.Min(_scrollY, Math.Max(0, _contentHeight - Height));

            using (var name = new Font("Microsoft YaHei UI", 10, FontStyle.Bold))
            using (var detail = new Font("Microsoft YaHei UI", 8.5f))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var rect = new Rectangle(padding + i % columns * (width + gap),
                        padding + i / columns * (height + gap) - _scrollY, width, height);
                    _itemRects[item.Id] = rect;
                    if (rect.Bottom < 0 || rect.Top >= Height) continue;
                    bool selected = _selection.Contains(item.Id), hover = _hoverId == item.Id;
                    Palette.FillRounded(g, selected ? Palette.Hover : hover ? Palette.Hover : Palette.PanelAlt, rect, 8);
                    Palette.DrawRounded(g, selected ? Palette.Accent : hover ? Palette.TextDim : Palette.Line,
                        Rectangle.Inflate(rect, -1, -1), 8, selected ? 2 : 1);
                    if (selected && Focused)
                        ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(rect, -5, -5), Palette.Accent, Palette.Hover);
                    var icon = GetIcon(item);
                    if (icon != null)
                        g.DrawIcon(icon, new Rectangle(rect.X + 12, rect.Y + 11, 24, 24));
                    var format = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                    TextRenderer.DrawText(g, item.Name, name,
                        new Rectangle(rect.X + 11, rect.Y + 43, rect.Width - 22, 21),
                        item.Hidden ? Palette.TextFaint : Palette.Text, format);
                    var kind = item.Kind == "dir" ? "文件夹" :
                        string.Equals(Path.GetExtension(item.Path), ".lnk", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetExtension(item.Path), ".url", StringComparison.OrdinalIgnoreCase)
                        ? "快捷方式" : item.SizeText;
                    if (item.Hidden) kind = "已隐藏 · " + kind;
                    TextRenderer.DrawText(g, kind, detail,
                        new Rectangle(rect.X + 11, rect.Y + 67, rect.Width - 22, 19), Palette.TextDim, format);
                }
            }
            DrawScrollbar(g);
        }

        private void DrawGridItem(Graphics g, DesktopItem it, Rectangle r, Color catColor)
        {
            bool selected = _selection.Contains(it.Id);
            bool hover = _hoverId == it.Id;
            if (selected) Palette.FillRounded(g, Color.FromArgb(46, Palette.Accent), r, 10);
            else if (hover) Palette.FillRounded(g, Palette.Hover, r, 10);

            bool wantThumb = ThumbnailsSupported(View, _store.Settings.IconSize) && _thumbnails != null
                             && ThumbnailPolicy.ShouldUseThumbnail(it.Path, it.Kind == "dir");
            int size;
            if (wantThumb)
            {
                size = Math.Min(Math.Min(ThumbnailPolicy.MaxSize, _store.Settings.IconSize), Math.Max(32, r.Width - 12));
                int tx = r.X + (r.Width - size) / 2;
                int ty = r.Y + 10;
                var thumbRect = new Rectangle(tx, ty, size, size);
                if (!TryDrawThumbnail(g, it, thumbRect))
                {
                    // 未就绪时保持真实 Shell 图标，不让桌面出现空色块；就绪后无闪烁替换。
                    DrawThumbnailFallback(g, it, thumbRect);
                    RequestThumbnail(it, size);
                }
            }
            else
            {
                size = Math.Min(48, _store.Settings.IconSize);
                var icon = GetIcon(it);
                if (icon != null)
                {
                    int ix = r.X + (r.Width - size) / 2;
                    int iy = r.Y + 10;
                    try { g.DrawIcon(icon, new Rectangle(ix, iy, size, size)); } catch { }
                }
                else
                {
                    using (var b = new SolidBrush(Palette.PanelAlt))
                        g.FillRectangle(b, r.X + (r.Width - size) / 2, r.Y + 10, size, size);
                }
            }

            using (var f = new Font("Microsoft YaHei UI", 8.5F))
            {
                var lines = Palette.Wrap(g, it.Name, f, r.Width - 10, 2);
                float ty = r.Y + size + (wantThumb ? 10 : 16);
                var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
                foreach (var line in lines)
                {
                    g.DrawString(line, f, new SolidBrush(it.Hidden ? Palette.TextFaint : Palette.Text),
                        new RectangleF(r.X + 4, ty, r.Width - 8, 16), center);
                    ty += 15;
                }
            }

            // 隐藏标记 / 系统隐藏
            if (it.Hidden)
            {
                using (var f = new Font("Segoe UI", 7.5F))
                    g.DrawString("🙈", f, new SolidBrush(Palette.TextDim), r.X + 4, r.Y + 4);
            }
            // 分类色点
            using (var b = new SolidBrush(catColor))
                g.FillEllipse(b, r.Right - 12, r.Y + 5, 6, 6);

            _itemRects[it.Id] = r;
        }

        private void DrawListItem(Graphics g, DesktopItem it, Rectangle r, Color catColor)
        {
            bool selected = _selection.Contains(it.Id);
            if (selected) Palette.FillRounded(g, Color.FromArgb(46, Palette.Accent), r, 8);
            else if (_hoverId == it.Id) Palette.FillRounded(g, Palette.Hover, r, 8);

            var icon = GetIcon(it);
            if (icon != null) { try { g.DrawIcon(icon, new Rectangle(r.X + 8, r.Y + 8, 24, 24)); } catch { } }
            using (var f = new Font("Microsoft YaHei UI", 9.5F))
            using (var fs = new Font("Microsoft YaHei UI", 8.5F))
            {
                g.DrawString(Palette.Ellipsis(g, it.Name, f, r.Width - 300), f,
                    new SolidBrush(it.Hidden ? Palette.TextFaint : Palette.Text), r.X + 40, r.Y + 11);
                g.DrawString(it.SizeText, fs, new SolidBrush(Palette.TextDim), r.X + r.Width - 250, r.Y + 12);
                g.DrawString(it.MtimeText, fs, new SolidBrush(Palette.TextFaint), r.X + r.Width - 150, r.Y + 12);
            }
            using (var b = new SolidBrush(catColor)) g.FillEllipse(b, r.Right - 12, r.Y + 17, 6, 6);
            _itemRects[it.Id] = r;
        }

        private void DrawCompactItem(Graphics g, DesktopItem it, Rectangle r, Color catColor)
        {
            if (_selection.Contains(it.Id)) Palette.FillRounded(g, Color.FromArgb(46, Palette.Accent), r, 6);
            else if (_hoverId == it.Id) Palette.FillRounded(g, Palette.Hover, r, 6);
            var icon = GetIcon(it);
            if (icon != null) { try { g.DrawIcon(icon, new Rectangle(r.X + 6, r.Y + 5, 16, 16)); } catch { } }
            using (var f = new Font("Microsoft YaHei UI", 9F))
            using (var fs = new Font("Microsoft YaHei UI", 8F))
            {
                g.DrawString(Palette.Ellipsis(g, it.Name, f, r.Width - 220), f,
                    new SolidBrush(it.Hidden ? Palette.TextFaint : Palette.Text), r.X + 28, r.Y + 5);
                g.DrawString(it.MtimeText, fs, new SolidBrush(Palette.TextFaint), r.X + r.Width - 190, r.Y + 6);
            }
            using (var b = new SolidBrush(catColor)) g.FillEllipse(b, r.Right - 12, r.Y + 10, 5, 5);
            _itemRects[it.Id] = r;
        }

        private void DrawDetailsHeader(Graphics g, Rectangle r)
        {
            using (var f = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold))
            using (var b = new SolidBrush(Palette.TextFaint))
            {
                int nameW = Math.Max(260, r.Width * 34 / 100);
                g.DrawString("名称", f, b, r.X + 38, r.Y + 3);
                g.DrawString("类型", f, b, r.X + nameW, r.Y + 3);
                g.DrawString("大小", f, b, r.X + nameW + 130, r.Y + 3);
                g.DrawString("修改时间", f, b, r.X + nameW + 230, r.Y + 3);
                g.DrawString("路径", f, b, r.X + nameW + 380, r.Y + 3);
            }
            using (var p = new Pen(Palette.Line)) g.DrawLine(p, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
        }

        private void DrawDetailsItem(Graphics g, DesktopItem it, Rectangle r, Color catColor)
        {
            if (_selection.Contains(it.Id)) Palette.FillRounded(g, Color.FromArgb(46, Palette.Accent), r, 5);
            else if (_hoverId == it.Id) Palette.FillRounded(g, Palette.Hover, r, 5);
            var icon = GetIcon(it);
            if (icon != null) { try { g.DrawIcon(icon, new Rectangle(r.X + 7, r.Y + 6, 20, 20)); } catch { } }
            int nameW = Math.Max(260, r.Width * 34 / 100);
            string type = it.Kind == "dir" ? "文件夹" : string.IsNullOrEmpty(it.Ext) ? "文件" : it.Ext.TrimStart('.').ToUpperInvariant();
            using (var f = new Font("Microsoft YaHei UI", 8.7F))
            using (var fs = new Font("Microsoft YaHei UI", 8F))
            {
                g.DrawString(Palette.Ellipsis(g, it.Name, f, nameW - 48), f, new SolidBrush(it.Hidden ? Palette.TextFaint : Palette.Text), r.X + 34, r.Y + 7);
                g.DrawString(type, fs, new SolidBrush(Palette.TextDim), r.X + nameW, r.Y + 8);
                g.DrawString(it.SizeText, fs, new SolidBrush(Palette.TextDim), r.X + nameW + 130, r.Y + 8);
                g.DrawString(it.MtimeText, fs, new SolidBrush(Palette.TextFaint), r.X + nameW + 230, r.Y + 8);
                g.DrawString(Palette.Ellipsis(g, it.Path, fs, Math.Max(80, r.Width - nameW - 390)), fs, new SolidBrush(Palette.TextFaint), r.X + nameW + 380, r.Y + 8);
            }
            using (var b = new SolidBrush(catColor)) g.FillEllipse(b, r.Right - 10, r.Y + 13, 5, 5);
            _itemRects[it.Id] = r;
        }

        // ---------- 缩略图 ----------
        private bool TryDrawThumbnail(Graphics g, DesktopItem it, Rectangle dest)
        {
            if (_thumbnails == null || _thumbnails.IsDisposed) return false;
            try { return _thumbnails.TryDraw(g, it.Path, dest.Width, it.Mtime, dest); }
            catch { return false; }
        }

        private void DrawThumbnailFallback(Graphics g, DesktopItem item, Rectangle dest)
        {
            var icon = GetIcon(item);
            if (icon != null)
            {
                int size = Math.Min(48, Math.Min(dest.Width, dest.Height));
                try { g.DrawIcon(icon, new Rectangle(dest.X + (dest.Width - size) / 2, dest.Y + (dest.Height - size) / 2, size, size)); return; } catch { }
            }
            Palette.FillRounded(g, Palette.PanelAlt, dest, 8);
        }

        private void RequestThumbnail(DesktopItem it, int size)
        {
            if (_thumbnails == null || _thumbnails.IsDisposed) return;
            try { _thumbnails.Request(it.Path, size, it.Kind == "dir", it.Mtime); }
            catch { }
        }

        /// <summary>后台线程回调：就绪后安全地触发 UI 刷新（窗体已释放时静默忽略）。</summary>
        private void OnThumbnailReady(string path, int size)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (IsDisposed) return;
                    Invalidate();
                }));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_thumbnails != null) { try { _thumbnails.Ready -= OnThumbnailReady; } catch { } _thumbnails = null; }
                InvalidateIcons();
            }
            base.Dispose(disposing);
        }

        private void DrawScrollbar(Graphics g)
        {
            if (_contentHeight <= Height) return;
            int trackH = Height - 8;
            int barH = Math.Max(40, (int)(trackH * (double)Height / _contentHeight));
            int maxScroll = Math.Max(1, _contentHeight - Height);
            int barY = 4 + (int)((trackH - barH) * (double)_scrollY / maxScroll);
            using (var b = new SolidBrush(Color.FromArgb(90, Palette.TextFaint)))
                Palette.FillRounded(g, Color.FromArgb(80, Palette.TextFaint), new Rectangle(Width - 8, barY, 4, barH), 2);
        }

        // ---------- 命中测试与交互 ----------
        private string HitTest(Point p)
        {
            foreach (var kv in _itemRects)
                if (kv.Value.Contains(p)) return kv.Key;
            return null;
        }

        private string HitHeader(Point p)
        {
            foreach (var kv in _headerRects)
                if (kv.Value.Contains(p)) return kv.Key;
            return null;
        }

        private DesktopItem ItemById(string id)
        {
            return _items.FirstOrDefault(i => i.Id == id);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _lastMouse = e.Location;
            if (_dragging && _dragId != null)
            {
                _dragPoint = e.Location;
                Invalidate();
                return;
            }
            if (_dragId != null && (e.Button & MouseButtons.Left) == MouseButtons.Left &&
                (Math.Abs(e.X - _dragPoint.X) > SystemInformation.DragSize.Width / 2 || Math.Abs(e.Y - _dragPoint.Y) > SystemInformation.DragSize.Height / 2))
            {
                _dragging = true;
                _dragPoint = e.Location;
                Invalidate();
                return;
            }
            var id = HitTest(e.Location);
            if (id != _hoverId) { _hoverId = id; Cursor = id != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverId != null) { _hoverId = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            var id = HitTest(e.Location);
            var header = HitHeader(e.Location);

            if (header != null && e.Button == MouseButtons.Left)
            {
                if (_collapsed.Contains(header)) _collapsed.Remove(header); else _collapsed.Add(header);
                Invalidate();
                return;
            }
            if (id == null)
            {
                if (e.Button == MouseButtons.Left) { _selection.Clear(); Invalidate(); var h = SelectionChanged; if (h != null) h(null); }
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
                bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
                if (ctrl) { if (_selection.Contains(id)) _selection.Remove(id); else _selection.Add(id); }
                else if (!_selection.Contains(id)) { _selection.Clear(); _selection.Add(id); }
                _dragId = id;
                _dragPoint = e.Location;
                Invalidate();
                var h = SelectionChanged; if (h != null) h(_selection.Contains(id) ? ItemById(id) : SelectedItems.FirstOrDefault());
            }
            else if (e.Button == MouseButtons.Right && !_selection.Contains(id))
            {
                _selection.Clear(); _selection.Add(id); Invalidate();
                var h = SelectionChanged; if (h != null) h(_selection.Contains(id) ? ItemById(id) : SelectedItems.FirstOrDefault());
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging && _dragId != null)
            {
                var h = ItemDropped; if (h != null) h(_dragId, PointToScreen(e.Location));
                _dragging = false; _dragId = null; Invalidate();
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                var item = ItemById(HitTest(e.Location));
                var h = ContextRequested; if (h != null) h(item, e.Location);
            }
            _dragId = null;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            var id = HitTest(e.Location);
            if (id == null) return;
            var it = ItemById(id);
            if (it != null) { var h = ItemOpened; if (h != null) h(it); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int max = Math.Max(0, _contentHeight - Height + 20);
            _scrollY = Math.Max(0, Math.Min(max, _scrollY - e.Delta));
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Delete && _selection.Count > 0)
            {
                var h = CommandRequested; if (h != null) h(e.Shift ? "permanent-delete" : "delete");
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter && _selection.Count == 1)
            {
                var it = ItemById(_selection.First());
                if (it != null) { var h = ItemOpened; if (h != null) h(it); }
                e.Handled = true;
            }
        }

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            base.OnDragEnter(drgevent);
            drgevent.Effect = drgevent.Data != null && drgevent.Data.GetDataPresent(DataFormats.FileDrop)
                ? (((drgevent.KeyState & 4) != 0) ? DragDropEffects.Move : DragDropEffects.Copy)
                : DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            base.OnDragDrop(drgevent);
            if (drgevent.Data == null || !drgevent.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = ((string[])drgevent.Data.GetData(DataFormats.FileDrop)).ToList();
            var h = ExternalFilesDropped;
            if (h != null) h(paths, drgevent.Effect == DragDropEffects.Move);
        }

        // 拖拽幽灵
        protected override void OnPaintBackground(PaintEventArgs e) { }
    }
}


