using System;
using System.Collections.Generic;
using System.IO;
using DeskBox.Core;

namespace DeskBox.Fs
{
    /// <summary>收藏、最近访问与标签的资料库持久化。</summary>
    public class FileLibrary
    {
        private const int MaxRecent = 100;
        private const string DefaultGroup = "默认";

        private readonly object _lock = new object();
        private readonly DeskBox.Store _store;
        private List<FavoriteEntry> _favorites = new List<FavoriteEntry>();
        private List<RecentEntry> _recent = new List<RecentEntry>();
        private List<TagEntry> _tags = new List<TagEntry>();
        private List<string> _groups = new List<string>();

        public FileLibrary(DeskBox.Store store)
        {
            _store = store;
            LoadAll();
        }

        public List<FavoriteEntry> Favorites
        {
            get { lock (_lock) { return CloneFavorites(_favorites); } }
        }

        public List<RecentEntry> Recent
        {
            get { lock (_lock) { return CloneRecent(_recent); } }
        }

        public List<string> Groups
        {
            get { lock (_lock) { return new List<string>(_groups); } }
        }

        public bool IsFavorite(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            lock (_lock)
            {
                for (int i = 0; i < _favorites.Count; i++)
                {
                    if (SamePath(_favorites[i].Path, path)) return true;
                }
            }
            return false;
        }

        public void AddFavorite(string path, string group)
        {
            try
            {
                string p = CleanPath(path);
                if (p.Length == 0) return;
                string g = (group ?? "").Trim();
                if (g.Length == 0) g = DefaultGroup;

                bool added = false;
                lock (_lock)
                {
                    FavoriteEntry existing = null;
                    for (int i = 0; i < _favorites.Count; i++)
                    {
                        if (SamePath(_favorites[i].Path, p)) { existing = _favorites[i]; break; }
                    }

                    if (existing == null)
                    {
                        FavoriteEntry f = new FavoriteEntry();
                        f.Path = p;
                        f.Name = NameOf(p);
                        f.Group = g;
                        f.AddedAt = NowMs();
                        _favorites.Insert(0, f);
                        added = true;
                    }
                    else
                    {
                        existing.Group = g;
                        if (string.IsNullOrEmpty(existing.Name)) existing.Name = NameOf(p);
                        if (existing.AddedAt <= 0) existing.AddedAt = NowMs();
                    }
                    RebuildGroupsLocked();
                }

                Save();
                if (_store != null)
                    _store.Log("info", "favorite", (added ? "收藏 " : "更新收藏 ") + p + " [" + g + "]");
            }
            catch (Exception ex)
            {
                Fail("添加收藏失败", ex);
            }
        }

        public void RemoveFavorite(string path)
        {
            try
            {
                string p = CleanPath(path);
                if (p.Length == 0) return;
                bool removed = false;
                lock (_lock)
                {
                    for (int i = _favorites.Count - 1; i >= 0; i--)
                    {
                        if (SamePath(_favorites[i].Path, p))
                        {
                            _favorites.RemoveAt(i);
                            removed = true;
                        }
                    }
                    RebuildGroupsLocked();
                }
                if (!removed) return;
                Save();
                if (_store != null) _store.Log("info", "favorite", "取消收藏 " + p);
            }
            catch (Exception ex)
            {
                Fail("取消收藏失败", ex);
            }
        }

        public void Touch(string path)
        {
            try
            {
                string p = CleanPath(path);
                if (p.Length == 0) return;
                lock (_lock)
                {
                    RecentEntry existing = null;
                    for (int i = 0; i < _recent.Count; i++)
                    {
                        if (SamePath(_recent[i].Path, p)) { existing = _recent[i]; break; }
                    }

                    if (existing == null)
                    {
                        RecentEntry r = new RecentEntry();
                        r.Path = p;
                        r.Name = NameOf(p);
                        r.LastOpenAt = NowMs();
                        r.OpenCount = 1;
                        _recent.Insert(0, r);
                    }
                    else
                    {
                        existing.LastOpenAt = NowMs();
                        existing.OpenCount = existing.OpenCount + 1;
                        if (string.IsNullOrEmpty(existing.Name)) existing.Name = NameOf(p);
                        _recent.Remove(existing);
                        _recent.Insert(0, existing);
                    }

                    if (_recent.Count > MaxRecent)
                        _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
                }

                SaveRecentOnly();
                if (_store != null) _store.Log("info", "open", p);
            }
            catch (Exception ex)
            {
                Fail("记录最近访问失败", ex);
            }
        }

        public void SetTags(string path, List<string> tags)
        {
            try
            {
                string p = CleanPath(path);
                if (p.Length == 0) return;
                List<string> clean = CleanTags(tags);

                lock (_lock)
                {
                    int index = -1;
                    for (int i = 0; i < _tags.Count; i++)
                    {
                        if (SamePath(_tags[i].Path, p)) { index = i; break; }
                    }

                    if (clean.Count == 0)
                    {
                        if (index >= 0) _tags.RemoveAt(index);
                    }
                    else if (index >= 0)
                    {
                        _tags[index].Tags = clean;
                    }
                    else
                    {
                        TagEntry t = new TagEntry();
                        t.Path = p;
                        t.Tags = clean;
                        _tags.Add(t);
                    }
                }

                SaveTagsOnly();
                if (_store != null)
                    _store.Log("info", "tag", "设置标签 " + p + " -> " + JoinTags(clean));
            }
            catch (Exception ex)
            {
                Fail("设置标签失败", ex);
            }
        }

        public List<string> GetTags(string path)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(path)) return result;
            lock (_lock)
            {
                for (int i = 0; i < _tags.Count; i++)
                {
                    if (SamePath(_tags[i].Path, path))
                    {
                        if (_tags[i].Tags != null) result.AddRange(_tags[i].Tags);
                        break;
                    }
                }
            }
            return result;
        }

        public List<string> AllTags()
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (_lock)
            {
                for (int i = 0; i < _tags.Count; i++)
                {
                    List<string> tags = _tags[i].Tags;
                    if (tags == null) continue;
                    for (int j = 0; j < tags.Count; j++)
                    {
                        string tag = (tags[j] ?? "").Trim();
                        if (tag.Length > 0 && seen.Add(tag)) result.Add(tag);
                    }
                }
            }
            result.Sort(StringComparer.CurrentCultureIgnoreCase);
            return result;
        }

        public List<string> PathsWithTag(string tag)
        {
            List<string> result = new List<string>();
            string target = (tag ?? "").Trim();
            if (target.Length == 0) return result;
            lock (_lock)
            {
                for (int i = 0; i < _tags.Count; i++)
                {
                    if (HasTag(_tags[i].Tags, target)) result.Add(_tags[i].Path);
                }
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>保存三份资料库数据；失败只记日志，不向 UI 抛出。</summary>
        public void Save()
        {
            lock (_lock)
            {
                SaveFavoritesLocked();
                SaveRecentLocked();
                SaveTagsLocked();
            }
        }

        private void SaveFavoritesOnly()
        {
            lock (_lock) { SaveFavoritesLocked(); }
        }

        private void SaveRecentOnly()
        {
            lock (_lock) { SaveRecentLocked(); }
        }

        private void SaveTagsOnly()
        {
            lock (_lock) { SaveTagsLocked(); }
        }

        private void SaveFavoritesLocked()
        {
            try { Json.Save(AppPaths.File("favorites.json"), _favorites); }
            catch (Exception ex) { Fail("保存 favorites.json 失败", ex); }
        }

        private void SaveRecentLocked()
        {
            try { Json.Save(AppPaths.File("recent.json"), _recent); }
            catch (Exception ex) { Fail("保存 recent.json 失败", ex); }
        }

        private void SaveTagsLocked()
        {
            try { Json.Save(AppPaths.File("tags.json"), _tags); }
            catch (Exception ex) { Fail("保存 tags.json 失败", ex); }
        }

        private void LoadAll()
        {
            try { _favorites = Json.Load<List<FavoriteEntry>>(AppPaths.File("favorites.json"), new List<FavoriteEntry>()); }
            catch (Exception ex) { Fail("读取 favorites.json 失败", ex); }
            if (_favorites == null) _favorites = new List<FavoriteEntry>();

            try { _recent = Json.Load<List<RecentEntry>>(AppPaths.File("recent.json"), new List<RecentEntry>()); }
            catch (Exception ex) { Fail("读取 recent.json 失败", ex); }
            if (_recent == null) _recent = new List<RecentEntry>();

            try { _tags = Json.Load<List<TagEntry>>(AppPaths.File("tags.json"), new List<TagEntry>()); }
            catch (Exception ex) { Fail("读取 tags.json 失败", ex); }
            if (_tags == null) _tags = new List<TagEntry>();

            NormalizeFavoritesLocked();
            NormalizeRecentLocked();
            NormalizeTagsLocked();
            RebuildGroupsLocked();
        }

        private void NormalizeFavoritesLocked()
        {
            List<FavoriteEntry> clean = new List<FavoriteEntry>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _favorites.Count; i++)
            {
                FavoriteEntry f = _favorites[i];
                if (f == null || string.IsNullOrWhiteSpace(f.Path)) continue;
                f.Path = CleanPath(f.Path);
                if (f.Path.Length == 0 || !seen.Add(f.Path)) continue;
                if (string.IsNullOrEmpty(f.Name)) f.Name = NameOf(f.Path);
                if (string.IsNullOrWhiteSpace(f.Group)) f.Group = DefaultGroup;
                clean.Add(f);
            }
            _favorites = clean;
        }

        private void NormalizeRecentLocked()
        {
            Dictionary<string, RecentEntry> map = new Dictionary<string, RecentEntry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _recent.Count; i++)
            {
                RecentEntry r = _recent[i];
                if (r == null || string.IsNullOrWhiteSpace(r.Path)) continue;
                r.Path = CleanPath(r.Path);
                if (r.Path.Length == 0) continue;
                if (string.IsNullOrEmpty(r.Name)) r.Name = NameOf(r.Path);
                if (r.LastOpenAt < 0) r.LastOpenAt = 0;
                if (r.OpenCount < 1) r.OpenCount = 1;

                RecentEntry old;
                if (map.TryGetValue(r.Path, out old))
                {
                    if (r.LastOpenAt > old.LastOpenAt)
                    {
                        r.OpenCount = r.OpenCount + old.OpenCount;
                        map[r.Path] = r;
                    }
                    else
                    {
                        old.OpenCount = old.OpenCount + r.OpenCount;
                    }
                }
                else map[r.Path] = r;
            }

            List<RecentEntry> ordered = new List<RecentEntry>(map.Values);
            ordered.Sort(delegate(RecentEntry a, RecentEntry b)
            {
                int c = b.LastOpenAt.CompareTo(a.LastOpenAt);
                if (c != 0) return c;
                return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });
            if (ordered.Count > MaxRecent) ordered.RemoveRange(MaxRecent, ordered.Count - MaxRecent);
            _recent = ordered;
        }

        private void NormalizeTagsLocked()
        {
            List<TagEntry> clean = new List<TagEntry>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _tags.Count; i++)
            {
                TagEntry t = _tags[i];
                if (t == null || string.IsNullOrWhiteSpace(t.Path)) continue;
                t.Path = CleanPath(t.Path);
                if (t.Path.Length == 0 || !seen.Add(t.Path)) continue;
                t.Tags = CleanTags(t.Tags);
                if (t.Tags.Count > 0) clean.Add(t);
            }
            _tags = clean;
        }

        private void RebuildGroupsLocked()
        {
            List<string> groups = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            groups.Add(DefaultGroup);
            seen.Add(DefaultGroup);

            List<string> others = new List<string>();
            for (int i = 0; i < _favorites.Count; i++)
            {
                string g = (_favorites[i].Group ?? "").Trim();
                if (g.Length == 0) g = DefaultGroup;
                if (seen.Add(g)) others.Add(g);
            }
            others.Sort(StringComparer.CurrentCultureIgnoreCase);
            groups.AddRange(others);
            _groups = groups;
        }

        private void Fail(string action, Exception ex)
        {
            string message = action + ": " + (ex == null ? "unknown" : ex.Message);
            DeskBox.Core.AppLog.Write("error", "fs.library", message);
            try
            {
                if (_store != null) _store.Log("error", "library", message);
            }
            catch { }
        }

        private static string CleanPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            string p = path.Trim().Trim('"');
            if (p.Length == 0) return "";
            try { return Path.GetFullPath(p); }
            catch { return p; }
        }

        private static string NameOf(string path)
        {
            try
            {
                string name = Path.GetFileName(path.TrimEnd('\\', '/'));
                return string.IsNullOrEmpty(name) ? path : name;
            }
            catch { return path; }
        }

        private static bool SamePath(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static long NowMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private static List<string> CleanTags(List<string> tags)
        {
            List<string> clean = new List<string>();
            if (tags == null) return clean;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tags.Count; i++)
            {
                string tag = (tags[i] ?? "").Trim();
                if (tag.Length > 0 && seen.Add(tag)) clean.Add(tag);
            }
            return clean;
        }

        private static bool HasTag(List<string> tags, string target)
        {
            if (tags == null) return false;
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals((tags[i] ?? "").Trim(), target, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string JoinTags(List<string> tags)
        {
            if (tags == null || tags.Count == 0) return "";
            return string.Join(", ", tags.ToArray());
        }

        private static List<FavoriteEntry> CloneFavorites(List<FavoriteEntry> source)
        {
            List<FavoriteEntry> list = new List<FavoriteEntry>();
            for (int i = 0; i < source.Count; i++)
            {
                FavoriteEntry f = source[i];
                FavoriteEntry c = new FavoriteEntry();
                c.Path = f.Path;
                c.Name = f.Name;
                c.Group = f.Group;
                c.AddedAt = f.AddedAt;
                list.Add(c);
            }
            return list;
        }

        private static List<RecentEntry> CloneRecent(List<RecentEntry> source)
        {
            List<RecentEntry> list = new List<RecentEntry>();
            for (int i = 0; i < source.Count; i++)
            {
                RecentEntry r = source[i];
                RecentEntry c = new RecentEntry();
                c.Path = r.Path;
                c.Name = r.Name;
                c.LastOpenAt = r.LastOpenAt;
                c.OpenCount = r.OpenCount;
                list.Add(c);
            }
            return list;
        }
    }
}
