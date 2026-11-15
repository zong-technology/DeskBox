const fs = require('fs');
const p = 'src/UI/MainForm.cs';
let s = fs.readFileSync(p, 'utf8');

const method = `
        /// <summary>默认索引范围：桌面 + 文档 + 下载 + D:\\桌面（存在才加），后台重建。</summary>
        private void AutoConfigureIndex()
        {
            try
            {
                var roots = new List<string>();
                var desktop = DeskBox.DesktopScanner.ResolveDesktopDir(_store.Settings);
                if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop)) roots.Add(desktop);
                if (Directory.Exists(@"D:\\桌面")) roots.Add(@"D:\\桌面");
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(docs) && Directory.Exists(docs)) roots.Add(docs);
                var down = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(down)) roots.Add(down);

                var excludes = new List<string>();
                excludes.Add(@"C:\\Windows");
                excludes.Add(@"C:\\Program Files");
                excludes.Add(@"C:\\Program Files (x86)");
                excludes.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

                if (_index.Roots.Count == 0) _index.Configure(roots, excludes);
                if (_index.Count == 0 && !_index.IsIndexing) _index.Rebuild();
            }
            catch (Exception ex) { AppLog.Write("warn", "system", "索引配置失败: " + ex.Message); }
        }

`;

if (!s.includes('AutoConfigureIndex')) {
  s = s.replace('                _index = DeskBox.Fs.FileIndex.Instance;\n            }',
    '                _index = DeskBox.Fs.FileIndex.Instance;\n                AutoConfigureIndex();\n            }');
  if (!s.includes('AutoConfigureIndex()')) {
    // CRLF 变体
    s = s.replace('                _index = DeskBox.Fs.FileIndex.Instance;\r\n            }',
      '                _index = DeskBox.Fs.FileIndex.Instance;\r\n                AutoConfigureIndex();\r\n            }');
  }
  s = s.replace('        public void SwitchView(string id)', method + '        public void SwitchView(string id)');
}

if (!s.includes('using System.IO;')) {
  s = s.replace('using DeskBox.UI.Views;', 'using DeskBox.UI.Views;\nusing System.IO;');
}

fs.writeFileSync(p, s, 'utf8');
console.log('AutoConfigureIndex:', s.includes('AutoConfigureIndex') ? 'ok' : 'FAILED',
  '| using IO:', s.includes('using System.IO;') ? 'ok' : 'FAILED');
