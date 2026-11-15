const fs = require('fs');
const p = 'src/UI/Views/ChatView.cs';
let s = fs.readFileSync(p, 'utf8');
let changed = false;

// 1) DrawMessage 分发到工具卡片
if (!s.includes('DrawToolCard')) {
  const anchor = `            private int DrawMessage(Graphics g, ChatMessage m, int y)
            {
                bool isUser = m.Role == "user";`;
  const replacement = `            private int DrawMessage(Graphics g, ChatMessage m, int y)
            {
                if (m.Role == "tool") return DrawToolCard(g, m, y);
                bool isUser = m.Role == "user";`;
  if (s.includes(anchor)) { s = s.replace(anchor, replacement); changed = true; }
}

// 2) Measure 处理 tool
if (!s.includes('if (m.Role == "tool")') || !s.includes('private int Measure')) {
  const anchor2 = `            private int Measure(Graphics g, ChatMessage m)
            {`;
  const replacement2 = `            private int Measure(Graphics g, ChatMessage m)
            {
                if (m.Role == "tool")
                {
                    int tn = (m.Content ?? "").Replace("\\r\\n", "\\n").Split('\\n').Length;
                    return 26 + tn * 17 + 10;
                }`;
  if (s.includes(anchor2) && !s.includes('int tn = (m.Content')) { s = s.replace(anchor2, replacement2); changed = true; }
}

// 3) DrawToolCard 实现
if (!s.includes('DrawToolCard(Graphics')) {
  const anchor3 = `            protected override void Dispose(bool disposing)`;
  const impl = `            /// <summary>工具执行结果卡片（AI 真实执行的可视化证据）。</summary>
            private int DrawToolCard(Graphics g, ChatMessage m, int y)
            {
                int w = Math.Max(240, Width - 120);
                bool ok = (m.Content ?? "").StartsWith("✅");
                var accent = ok ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113);
                using (var f = new Font("Microsoft YaHei UI", 9F))
                using (var ft = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold))
                {
                    var lines = new List<string>();
                    foreach (var raw in (m.Content ?? "").Replace("\\r\\n", "\\n").Split('\\n'))
                        lines.Add(raw);
                    int h = 26 + lines.Count * 17 + 10;
                    var rect = new Rectangle(20, y, w, h);
                    Palette.FillRounded(g, Color.FromArgb(18, 26, 40), rect, 10);
                    Palette.DrawRounded(g, accent, rect, 10, 1f);
                    using (var bar = new SolidBrush(accent))
                        g.FillRectangle(bar, rect.X, rect.Y + 8, 3, rect.Height - 16);
                    g.DrawString("工具 " + (m.ToolName ?? "tool") + (ok ? " · 成功" : " · 失败"), ft, new SolidBrush(accent), rect.X + 14, rect.Y + 7);
                    int ty = rect.Y + 26;
                    foreach (var line in lines)
                    {
                        g.DrawString(line, f, new SolidBrush(_p.TextDim), rect.X + 14, ty);
                        ty += 17;
                    }
                    return h;
                }
            }

            protected override void Dispose(bool disposing)`;
  if (s.includes(anchor3)) { s = s.replace(anchor3, impl); changed = true; }
}

fs.writeFileSync(p, s, 'utf8');
console.log('changed=' + changed,
  '| dispatch=' + s.includes('if (m.Role == "tool") return DrawToolCard'),
  '| measure=' + s.includes('int tn = (m.Content'),
  '| card=' + s.includes('DrawToolCard(Graphics'));
