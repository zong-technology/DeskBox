using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DeskBox.Shell;

namespace DeskBox
{
    /// <summary>Known Folder desktop aggregation plus reusable folder scanning.</summary>
    public static class DesktopScanner
    {
        public static string ResolveDesktopDir(AppSettings settings) { return KnownFolderService.PrimaryDesktop(settings); }
        public static List<string> ResolveDesktopDirs(AppSettings settings) { return KnownFolderService.GetDesktopLocations(settings).Select(x => x.Path).ToList(); }

        /// <summary>Legacy name-only id retained for migration of existing category/hidden overrides.</summary>
        public static string ItemId(string name)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes((name ?? "").ToLowerInvariant()));
                var sb = new StringBuilder();
                for (int i = 0; i < 4; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static string StableItemId(string path)
        {
            var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                var sb = new StringBuilder("p_");
                for (int i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static List<DesktopItem> Scan(AppSettings settings, Dictionary<string, ItemOverride> overrides, IEnumerable<string> categoryIds)
        {
            return ScanDirectories(ResolveDesktopDirs(settings), overrides, categoryIds);
        }

        public static List<DesktopItem> ScanDirectory(string directory, Dictionary<string, ItemOverride> overrides, IEnumerable<string> categoryIds)
        {
            return ScanDirectories(new[] { directory }, overrides, categoryIds);
        }

        public static List<DesktopItem> ScanDirectories(IEnumerable<string> directories, Dictionary<string, ItemOverride> overrides, IEnumerable<string> categoryIds)
        {
            var result = new List<DesktopItem>();
            var known = new HashSet<string>(categoryIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in (directories ?? Enumerable.Empty<string>()).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                FileSystemInfo[] entries;
                try { entries = new DirectoryInfo(directory).GetFileSystemInfos(); }
                catch (Exception ex) { AppLog.Write("warn", "file", "读取目录失败 " + directory + "：" + ex.Message); continue; }

                foreach (var fsi in entries)
                {
                    var name = fsi.Name;
                    if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(fsi.FullName.TrimEnd(Path.DirectorySeparatorChar), Store.DataDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) continue;
                    if (!seen.Add(fsi.FullName)) continue;

                    bool isDirectory = (fsi.Attributes & FileAttributes.Directory) != 0;
                    var extension = isDirectory ? "" : Path.GetExtension(name).ToLowerInvariant();
                    var kind = isDirectory ? "dir" : (extension == ".lnk" || extension == ".url") ? "shortcut" : "file";
                    var stableId = StableItemId(fsi.FullName);
                    var legacyId = ItemId(name);

                    ItemOverride itemOverride = null;
                    if (overrides != null)
                    {
                        if (!overrides.TryGetValue(stableId, out itemOverride)) overrides.TryGetValue(legacyId, out itemOverride);
                        if (itemOverride != null && !overrides.ContainsKey(stableId)) overrides[stableId] = itemOverride;
                    }

                    string categoryId = null;
                    bool hidden = false;
                    if (itemOverride != null)
                    {
                        hidden = itemOverride.Hidden;
                        if (!string.IsNullOrEmpty(itemOverride.CategoryId) && known.Contains(itemOverride.CategoryId)) categoryId = itemOverride.CategoryId;
                    }
                    if (categoryId == null) categoryId = Store.CategoryForExt(extension, kind);

                    long size = 0, modified = 0;
                    try
                    {
                        if (!isDirectory) size = ((FileInfo)fsi).Length;
                        modified = new DateTimeOffset(fsi.LastWriteTimeUtc).ToUnixTimeMilliseconds();
                    }
                    catch { }

                    result.Add(new DesktopItem
                    {
                        Id = stableId,
                        Name = name,
                        Path = fsi.FullName,
                        Ext = extension,
                        Kind = kind,
                        Size = size,
                        Mtime = modified,
                        CategoryId = categoryId,
                        Hidden = hidden,
                        SystemHidden = (fsi.Attributes & FileAttributes.Hidden) != 0,
                        Available = true
                    });
                }
            }
            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return result;
        }
    }
}
