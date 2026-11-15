using System;
using System.Collections.Generic;
using System.IO;

namespace DeskBox.Shell
{
    public class FolderSession
    {
        public const string DesktopHome = "shell:desktop";
        private readonly Stack<string> _back = new Stack<string>();
        private readonly Stack<string> _forward = new Stack<string>();
        public string Current { get; private set; }
        public event Action Changed;

        public FolderSession() { Current = DesktopHome; }
        public bool CanBack { get { return _back.Count > 0; } }
        public bool CanForward { get { return _forward.Count > 0; } }
        public bool CanUp { get { return Current != DesktopHome && Directory.GetParent(Current) != null; } }

        public bool Navigate(string location)
        {
            if (string.IsNullOrWhiteSpace(location)) return false;
            if (location != DesktopHome && !Directory.Exists(location)) return false;
            var normalized = location == DesktopHome ? DesktopHome : Path.GetFullPath(location);
            if (string.Equals(Current, normalized, StringComparison.OrdinalIgnoreCase)) return true;
            _back.Push(Current);
            _forward.Clear();
            Current = normalized;
            Raise();
            return true;
        }

        public bool Back()
        {
            if (_back.Count == 0) return false;
            _forward.Push(Current); Current = _back.Pop(); Raise(); return true;
        }

        public bool Forward()
        {
            if (_forward.Count == 0) return false;
            _back.Push(Current); Current = _forward.Pop(); Raise(); return true;
        }

        public bool Up()
        {
            if (Current == DesktopHome) return false;
            var parent = Directory.GetParent(Current);
            return parent != null && Navigate(parent.FullName);
        }

        public void Home() { Navigate(DesktopHome); }
        private void Raise() { var h = Changed; if (h != null) h(); }
    }
}
