using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeskBox.Core;
using DeskBox.Domain;

namespace DeskBox
{
    /// <summary>应用内通知中心。通知写入 notifications.json，最多保留 500 条。</summary>
    public class NotificationStore
    {
        private readonly object _lock = new object();
        private readonly Store _store;
        private List<AppNotification> _items;
        private Dictionary<string, CalibrationNotice> _calibration;
        private string _projectId = "";

        private string FilePath { get { return Path.Combine(Store.DataDir, "notifications.json"); } }
        private string CalibrationPath { get { return Path.Combine(Store.DataDir, "calibration-notifications.json"); } }
        public event Action<AppNotification> Published;
        public event Action Changed;

        public NotificationStore(Store store)
        {
            _store = store;
            _items = Json.Load(FilePath, new List<AppNotification>()) ?? new List<AppNotification>();
            _items = _items.Where(x => x != null).OrderByDescending(x => x.Ts).Take(500).ToList();
            _calibration = Json.Load<Dictionary<string, CalibrationNotice>>(CalibrationPath, null)
                ?? Json.Load(CalibrationPath + ".bak", new Dictionary<string, CalibrationNotice>());
        }

        private sealed class CalibrationNotice
        {
            public string Key;
            public bool Dismissed;
            public AppNotification Notice;
        }

        private CalibrationNotice CurrentCalibration()
        {
            CalibrationNotice notice;
            return _calibration.TryGetValue(_projectId, out notice) && notice != null && notice.Notice != null && !notice.Dismissed
                ? notice : null;
        }

        public List<AppNotification> Items
        {
            get
            {
                lock (_lock)
                {
                    var result = _items.Select(Clone).ToList();
                    var calibration = CurrentCalibration();
                    if (calibration != null) result.Add(Clone(calibration.Notice));
                    return result.OrderByDescending(item => item.Ts).ToList();
                }
            }
        }

        public int UnreadCount
        {
            get
            {
                lock (_lock)
                {
                    var notice = CurrentCalibration();
                    return _items.Count(x => !x.Read) + (notice != null && !notice.Notice.Read ? 1 : 0);
                }
            }
        }

        public void SetCalibrationProject(string projectId)
        {
            lock (_lock)
            {
                projectId = projectId ?? "";
                if (_projectId == projectId) return;
                _projectId = projectId;
            }
            RaiseChanged();
        }

        public void UpdateCalibration(CalibrationReminder reminder)
        {
            if (reminder == null || reminder.Recovered || string.IsNullOrEmpty(reminder.ProjectId)) return;
            AppNotification item = null;
            lock (_lock)
            {
                if (_projectId != reminder.ProjectId) return;
                CalibrationNotice previous;
                _calibration.TryGetValue(_projectId, out previous);
                if (string.IsNullOrEmpty(reminder.Key))
                {
                    if (!_calibration.ContainsKey(_projectId)) return;
                    SaveCalibration(null);
                }
                else
                {
                    if (previous != null && previous.Notice != null && previous.Key == reminder.Key) return;
                    item = new AppNotification
                    {
                        Id = "n_" + Guid.NewGuid().ToString("N"),
                        Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Title = "仪表校准提醒",
                        Message = "已逾期 " + reminder.Overdue + " 项 · 今日到期 " + reminder.DueToday
                            + " 项 · 未来 30 天 " + reminder.DueSoon + " 项",
                        Kind = "calibration",
                        SourceId = reminder.ProjectId
                    };
                    SaveCalibration(new CalibrationNotice { Key = reminder.Key, Notice = item });
                }
            }
            var p = Published;
            if (item != null && p != null) p(Clone(item));
            RaiseChanged();
        }

        private void SaveCalibration(CalibrationNotice value)
        {
            var next = new Dictionary<string, CalibrationNotice>(_calibration);
            if (value == null) next.Remove(_projectId);
            else next[_projectId] = value;
            Json.SaveAtomic(CalibrationPath, next, true);
            _calibration = next;
        }

        private void UpdateCalibrationRead(CalibrationNotice previous, bool read, bool dismiss)
        {
            var notice = Clone(previous.Notice);
            notice.Read = read;
            SaveCalibration(new CalibrationNotice { Key = previous.Key, Notice = notice, Dismissed = dismiss });
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
                if (item != null)
                {
                    if (item.Read == read) return;
                    item.Read = read;
                    SaveLocked();
                }
                else
                {
                    var notice = CurrentCalibration();
                    if (notice == null || notice.Notice.Id != id || notice.Notice.Read == read) return;
                    UpdateCalibrationRead(notice, read, false);
                }
            }
            RaiseChanged();
        }

        public void MarkAllRead()
        {
            lock (_lock)
            {
                var notice = CurrentCalibration();
                if (notice != null && !notice.Notice.Read) UpdateCalibrationRead(notice, true, false);
                foreach (var item in _items) item.Read = true;
                SaveLocked();
            }
            RaiseChanged();
        }

        public void ClearRead()
        {
            lock (_lock)
            {
                var notice = CurrentCalibration();
                if (notice != null && notice.Notice.Read) UpdateCalibrationRead(notice, true, true);
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
