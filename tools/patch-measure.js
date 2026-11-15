const fs = require('fs');
const p = 'src/UI/Views/ChatView.cs';
let s = fs.readFileSync(p, 'utf8');

const anchor = `            private int Measure(Graphics g, ChatMessage m)
            {
                int maxW = Math.Max(240, Width - 120);`;
const replacement = `            private int Measure(Graphics g, ChatMessage m)
            {
                if (m.Role == "tool")
                {
                    int tn = (m.Content ?? "").Replace("\\r\\n", "\\n").Split('\\n').Length;
                    return 26 + tn * 17 + 10;
                }
                int maxW = Math.Max(240, Width - 120);`;

if (s.includes(anchor)) {
  s = s.replace(anchor, replacement);
  fs.writeFileSync(p, s, 'utf8');
  console.log('measure patched: true');
} else {
  console.log('measure anchor NOT found');
}
