using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeskBox.Core;

namespace DeskBox
{
    /// <summary>应用内通知中心。通知写入 notifications.json，最多保留 500 条。</summary>
    public class NotificationStore
    {
        private readonly object _lock = new object();
        private readonly Store _store;
        private List<AppNotification> _items;

        private string FilePath { get { return Path.Combine(Store.DataDir, "notifications.json"); } }
        public event Action<AppNotification> Published;
        public event Action Changed;

        public NotificationStore(Store store)
        {
            _store = store;
            _items = Json.Load(FilePath, new List<AppNotification>()) ?? new List<AppNotification>();
            _items = _items.Where(x => x != null).OrderByDescending(x => x.Ts).Take(500).ToList();
        }

        public List<AppNotification> Items
        {
            get { lock (_lock) { return _items.Select(Clone).ToList(); } }
        }

        public int UnreadCount
        {
            get { lock (_lock) { return _items.Count(x => !x.Read); } }
        }

        public AppNotification Publish(string title, string message, string kind, string sourceId)
        {
            var item = new AppNotification
            {
                Id = "n_" + Guid.NewGuid().ToString("N").Substring(0, 10),
                Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Title = title ?? "DeskBox",
                Message = message ?? "",
                Kind = string.IsNullOrEmpty(kind) ? "info" : kind,
                SourceId = sourceId ?? "",
                Read = false
            };
            lock (_lock)
            {
                _items.Insert(0, item);
                if (_items.Count > 500) _items.RemoveRange(500, _items.Count - 500);
                SaveLocked();
            }
            if (_store != null) _store.Log(item.Kind == "error" ? "error" : "info", "notification", item.Title + "：" + item.Message);
            var p = Published;
            if (p != null) p(Clone(item));
            RaiseChanged();
            return Clone(item);
        }

        public void MarkRead(string id, bool read)
        {
            lock (_lock)
            {
                var item = _items.FirstOrDefault(x => x.Id == id);
                if (item == null || item.Read == read) return;
                item.Read = read;
                SaveLocked();
            }
            RaiseChanged();
        }

        public void MarkAllRead()
        {
            lock (_lock)
            {
                foreach (var item in _items) item.Read = true;
                SaveLocked();
            }
            RaiseChanged();
        }

        public void ClearRead()
        {
            lock (_lock)
            {
                _items.RemoveAll(x => x.Read);
                SaveLocked();
            }
            RaiseChanged();
        }

        private void SaveLocked() { Json.Save(FilePath, _items); }
        private void RaiseChanged() { var h = Changed; if (h != null) h(); }

        private static AppNotification Clone(AppNotification x)
        {
            return new AppNotification { Id = x.Id, Ts = x.Ts, Title = x.Title, Message = x.Message, Kind = x.Kind, SourceId = x.SourceId, Read = x.Read };
        }
    }
}
