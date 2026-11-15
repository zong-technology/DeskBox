using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace DeskBox.Shell
{
    public class DesktopLocation
    {
        public string Id = "";
        public string Name = "";
        public string Path = "";
        public bool IsPublic;
        public bool IsCloud;
    }

    /// <summary>Resolves real Windows desktop locations without hard-coded user names.</summary>
    public static class KnownFolderService
    {
        public static List<DesktopLocation> GetDesktopLocations(AppSettings settings)
        {
            var list = new List<DesktopLocation>();
            if (settings != null && !string.IsNullOrWhiteSpace(settings.DesktopDir) && Directory.Exists(settings.DesktopDir))
            {
                Add(list, "custom", "自定义桌面", settings.DesktopDir, false, IsCloudPath(settings.DesktopDir));
                return list;
            }

            string redirected = null;
            try
            {
                var raw = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", "Desktop", null) as string;
                if (!string.IsNullOrWhiteSpace(raw)) redirected = Environment.ExpandEnvironmentVariables(raw);
            }
            catch { }
            Add(list, "user", "用户桌面", redirected, false, IsCloudPath(redirected));
            Add(list, "user-known", "用户桌面", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), false, false);
            Add(list, "public", "公共桌面", Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), true, false);

            var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrWhiteSpace(oneDrive))
            {
                Add(list, "onedrive-desktop", "OneDrive Desktop", Path.Combine(oneDrive, "Desktop"), false, true);
                Add(list, "onedrive-cn", "OneDrive 桌面", Path.Combine(oneDrive, "桌面"), false, true);
            }
            return list;
        }

        public static string PrimaryDesktop(AppSettings settings)
        {
            var first = GetDesktopLocations(settings).FirstOrDefault(x => !x.IsPublic) ?? GetDesktopLocations(settings).FirstOrDefault();
            return first == null ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : first.Path;
        }

        private static void Add(List<DesktopLocation> list, string id, string name, string path, bool isPublic, bool isCloud)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { path = Path.GetFullPath(path); } catch { return; }
            if (!Directory.Exists(path)) return;
            if (list.Any(x => string.Equals(x.Path.TrimEnd(Path.DirectorySeparatorChar), path.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))) return;
            list.Add(new DesktopLocation { Id = id, Name = name, Path = path, IsPublic = isPublic, IsCloud = isCloud });
        }

        private static bool IsCloudPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return path.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
