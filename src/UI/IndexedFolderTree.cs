using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.Fs;

namespace DeskBox.UI
{
    internal sealed class IndexedFolderTree : TreeView
    {
        private string[] _roots = new string[0];
        private bool _selecting;
        public event Action<string, string> ScopeSelected;
        private DeskBox.Engineering.ProjectContext _project;

        public IndexedFolderTree(Palette palette)
        {
            BackColor = palette.CardBg;
            ForeColor = palette.Text;
            BorderStyle = BorderStyle.None;
            HideSelection = false;
            ShowNodeToolTips = true;
            AfterExpand += async (sender, args) => await LoadChildren(args.Node);
            AfterSelect += delegate(object sender, TreeViewEventArgs args)
            {
                var selected = ScopeSelected;
                var path = args.Node.Tag as string;
                if (!_selecting && selected != null && path != null) selected(path, args.Node.Text);
            };
        }

        public void RefreshRoots(IEnumerable<string> roots, DeskBox.Engineering.ProjectContext project = null)
        {
            var next = roots.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (Nodes.Count != 0 && next.SequenceEqual(_roots, StringComparer.OrdinalIgnoreCase) && (_project == null ? project == null : _project.SameScope(project))) return;
            _project = project;
            _roots = next;
            _selecting = true;
            BeginUpdate();
            try
            {
                Nodes.Clear();
                if (_project == null) Nodes.Add(new TreeNode("全部已索引目录") { Tag = "" });
                foreach (var root in _roots) Nodes.Add(Folder(root));
            }
            finally { EndUpdate(); _selecting = false; }
        }

        public void ShowScope(string path)
        {
            if (string.Equals(SelectedNode == null ? null : SelectedNode.Tag as string, path ?? "", StringComparison.OrdinalIgnoreCase)) return;
            _selecting = true;
            try { SelectedNode = FindScope(Nodes, path ?? ""); }
            finally { _selecting = false; }
        }

        private static TreeNode FindScope(TreeNodeCollection nodes, string path)
        {
            foreach (TreeNode node in nodes)
            {
                if (string.Equals(node.Tag as string, path, StringComparison.OrdinalIgnoreCase)) return node;
                var child = FindScope(node.Nodes, path);
                if (child != null) return child;
            }
            return null;
        }

        private static TreeNode Folder(string path)
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var node = new TreeNode(string.IsNullOrEmpty(name) ? path : name) { Tag = path, ToolTipText = path };
            node.Nodes.Add(new TreeNode("展开加载") { Name = "pending" });
            return node;
        }

        private async Task LoadChildren(TreeNode node)
        {
            if (node.Nodes.Count != 1 || node.Nodes[0].Name != "pending") return;
            node.Nodes[0].Name = "loading";
            node.Nodes[0].Text = "正在读取目录…";
            var path = (string)node.Tag;
            var excludes = FileIndex.Instance.Excludes;
            var project = _project;
            try
            {
                var folders = await Task.Run(() => new DirectoryInfo(path).EnumerateDirectories()
                    .Where(dir => (dir.Attributes & FileAttributes.ReparsePoint) == 0 &&
                        !FileIndex.IsExcluded(dir.FullName, dir.Name, excludes) &&
                        (project == null || project.IncludesPath(dir.FullName)))
                    .Take(401).OrderBy(dir => dir.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(dir => dir.FullName).ToArray());
                if (IsDisposed || node.TreeView != this) return;
                node.Nodes.Clear();
                foreach (var folder in folders.Take(400)) node.Nodes.Add(Folder(folder));
                if (folders.Length > 400) node.Nodes.Add(new TreeNode("仅显示前 400 个子目录"));
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException)) throw;
                if (IsDisposed || node.TreeView != this) return;
                node.Nodes.Clear();
                node.Nodes.Add(new TreeNode("暂时无法读取，重新展开可重试") { Name = "pending" });
            }
        }
    }
}
