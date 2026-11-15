using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeskBox.Core;

namespace DeskBox.Sync
{
    /// <summary>Local-first record, outbound queue and explicit conflict store.</summary>
    public class SyncStore
    {
        private readonly object _lock = new object();
        private List<SyncRecord> _records;
        private List<SyncQueueItem> _queue;
        private List<SyncConflict> _conflicts;
        private string RecordsFile { get { return Path.Combine(AppPaths.DataDir, "sync-records.json"); } }
        private string QueueFile { get { return Path.Combine(AppPaths.DataDir, "sync-queue.json"); } }
        private string ConflictsFile { get { return Path.Combine(AppPaths.DataDir, "sync-conflicts.json"); } }

        public SyncStore()
        {
            _records = Json.Load(RecordsFile, new List<SyncRecord>()) ?? new List<SyncRecord>();
            _queue = Json.Load(QueueFile, new List<SyncQueueItem>()) ?? new List<SyncQueueItem>();
            _conflicts = Json.Load(ConflictsFile, new List<SyncConflict>()) ?? new List<SyncConflict>();
        }

        public List<SyncRecord> Records { get { lock (_lock) return _records.Select(Clone).ToList(); } }
        public List<SyncQueueItem> Queue { get { lock (_lock) return _queue.Select(Clone).ToList(); } }
        public List<SyncConflict> Conflicts { get { lock (_lock) return _conflicts.Select(Clone).ToList(); } }

        public SyncRecord UpsertLocal(string entityType, string id, string json, string deviceId)
        {
            return UpsertLocal(entityType, id, json, deviceId, -1);
        }

        public SyncRecord UpsertLocal(string entityType, string id, string json, string deviceId, long expectedVersion)
        {
            if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(id)) throw new ArgumentException("entityType/id required");
            lock (_lock)
            {
                var old = Find(entityType, id); long baseVersion = old == null ? 0 : old.Version;
                if (expectedVersion >= 0 && expectedVersion != baseVersion) throw new InvalidOperationException("记录已被另一端更新，请刷新后再保存，当前编辑尚未提交。");
                var record = new SyncRecord { EntityType = entityType, Id = id, BaseVersion = baseVersion, Version = baseVersion + 1, UpdatedAt = Now(), DeviceId = deviceId ?? "", Json = json ?? "", Deleted = false };
                Put(record); Enqueue(record); Save(); return Clone(record);
            }
        }

        public SyncRecord UpsertSnapshot(string entityType, string id, string json, string deviceId)
        {
            lock (_lock) { var old = Find(entityType, id); if (old != null && !old.Deleted && string.Equals(old.Json ?? "", json ?? "", StringComparison.Ordinal)) return Clone(old); }
            return UpsertLocal(entityType, id, json, deviceId);
        }

        public SyncRecord DeleteLocal(string entityType, string id, string deviceId)
        {
            lock (_lock)
            {
                var old = Find(entityType, id); long baseVersion = old == null ? 0 : old.Version;
                var record = new SyncRecord { EntityType = entityType, Id = id, BaseVersion = baseVersion, Version = baseVersion + 1, UpdatedAt = Now(), DeviceId = deviceId ?? "", Deleted = true };
                Put(record); Enqueue(record); Save(); return Clone(record);
            }
        }

        public SyncApplyResult MergeRemote(SyncRecord remote)
        {
            if (remote == null || string.IsNullOrWhiteSpace(remote.EntityType) || string.IsNullOrWhiteSpace(remote.Id) || remote.Version < 1) return SyncApplyResult.Invalid;
            remote=Clone(remote);remote.OriginVersion=remote.Version;remote.OriginBaseVersion=remote.BaseVersion;remote.OriginUpdatedAt=remote.UpdatedAt;
            lock (_lock)
            {
                if (_conflicts.Any(c => c.Status != "open" && c.Remote.EntityType == remote.EntityType && c.Remote.Id == remote.Id && Same(c.Remote, remote))) return SyncApplyResult.Resolved;
                var local = Find(remote.EntityType, remote.Id);
                if (local == null) { Put(remote); Save(); return SyncApplyResult.Applied; }
                if (Same(local, remote)) return SyncApplyResult.Duplicate;
                long originVersion=local.OriginVersion>0?local.OriginVersion:local.Version,originBase=local.OriginVersion>0?local.OriginBaseVersion:local.BaseVersion,originTime=local.OriginVersion>0?local.OriginUpdatedAt:local.UpdatedAt;
                if(!HasPending(remote.EntityType,remote.Id)&&!string.IsNullOrEmpty(remote.DeviceId)&&remote.DeviceId==local.DeviceId&&remote.Version==originVersion&&remote.BaseVersion==originBase)
                {
                    if(remote.UpdatedAt<originTime)return SyncApplyResult.Resolved;
                    if(remote.UpdatedAt==originTime&&remote.Json==local.Json&&remote.Deleted==local.Deleted)return SyncApplyResult.Duplicate;
                    if(remote.UpdatedAt>originTime)
                    {
                        var revision=Clone(remote);revision.BaseVersion=local.Version;revision.Version=local.Version+1;
                        foreach(var conflict in _conflicts.Where(c=>c.Status=="open"&&c.Remote.EntityType==remote.EntityType&&c.Remote.Id==remote.Id&&c.Remote.DeviceId==remote.DeviceId&&c.Remote.Version==originVersion&&c.Remote.BaseVersion==originBase&&c.Remote.UpdatedAt<=remote.UpdatedAt))conflict.Status="superseded";
                        Put(revision);Save();return SyncApplyResult.Applied;
                    }
                }
                if (remote.Version < local.Version && !HasPending(remote.EntityType, remote.Id)) return SyncApplyResult.IgnoredOlder;
                if (HasPending(remote.EntityType, remote.Id) && remote.BaseVersion < local.Version)
                {
                    AddConflict(local, remote); Save(); return SyncApplyResult.Conflict;
                }
                if (remote.Version > local.Version)
                {
                    if(remote.BaseVersion==local.Version && !string.IsNullOrEmpty(remote.DeviceId))
                        foreach(var conflict in _conflicts.Where(c=>c.Status=="open"&&c.Remote.EntityType==remote.EntityType&&c.Remote.Id==remote.Id&&c.Remote.DeviceId==remote.DeviceId&&c.Remote.Version<remote.Version)) conflict.Status="resolved";
                    Put(remote); RemovePending(remote.EntityType, remote.Id, remote.Version); Save(); return SyncApplyResult.Applied;
                }
                AddConflict(local, remote); Save(); return SyncApplyResult.Conflict;
            }
        }

        public void Acknowledge(string queueId) { lock (_lock) { _queue.RemoveAll(x => x.Id == queueId); Save(); } }
        public void MarkAttempt(string queueId, string error) { lock (_lock) { var x = _queue.FirstOrDefault(q => q.Id == queueId); if (x != null) { x.Attempts++; x.LastError = error ?? ""; Save(); } } }

        public SyncApplyResult ResolveConflict(string conflictId, bool useRemote, string deviceId, long expectedVersion = -1)
        {
            lock (_lock)
            {
                var c = _conflicts.FirstOrDefault(x => x.Id == conflictId && x.Status == "open"); if (c == null) return SyncApplyResult.Invalid;
                var current = Find(c.Local.EntityType, c.Local.Id) ?? c.Local;
                if (expectedVersion >= 0 && current.Version != expectedVersion) return SyncApplyResult.Conflict;
                var chosen = Clone(useRemote ? c.Remote : current);
                chosen.BaseVersion = Math.Max(current.Version, c.Remote.Version); chosen.Version = chosen.BaseVersion + 1; chosen.UpdatedAt = Now(); chosen.DeviceId = deviceId ?? "windows"; Put(chosen); Enqueue(chosen);
                c.Status = useRemote ? "resolved-remote" : "resolved-local"; Save(); return SyncApplyResult.Applied;
            }
        }

        private SyncRecord Find(string type, string id) { return _records.FirstOrDefault(x => x.EntityType == type && x.Id == id); }
        private void Put(SyncRecord value) { _records.RemoveAll(x => x.EntityType == value.EntityType && x.Id == value.Id); _records.Add(Clone(value)); }
        private bool HasPending(string type, string id) { return _queue.Any(x => x.Record.EntityType == type && x.Record.Id == id); }
        private void RemovePending(string type, string id, long upToVersion) { _queue.RemoveAll(x => x.Record.EntityType == type && x.Record.Id == id && x.Record.Version <= upToVersion); }
        private void Enqueue(SyncRecord record) { _queue.RemoveAll(x => x.Record.EntityType == record.EntityType && x.Record.Id == record.Id); _queue.Add(new SyncQueueItem { Id = "sq_" + Guid.NewGuid().ToString("N"), CreatedAt = Now(), Record = Clone(record) }); }
        private void AddConflict(SyncRecord local, SyncRecord remote) { if (_conflicts.Any(x => x.Status == "open" && x.Local.EntityType == local.EntityType && x.Local.Id == local.Id && Same(x.Remote, remote))) return; _conflicts.Add(new SyncConflict { Id = "sc_" + Guid.NewGuid().ToString("N"), CreatedAt = Now(), Local = Clone(local), Remote = Clone(remote) }); }
        private void Save() { Json.Save(RecordsFile, _records); Json.Save(QueueFile, _queue); Json.Save(ConflictsFile, _conflicts); }
        private static long Now() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
        private static bool Same(SyncRecord a, SyncRecord b) { return a.Version == b.Version && a.Deleted == b.Deleted && string.Equals(a.Json ?? "", b.Json ?? "", StringComparison.Ordinal); }
        private static SyncRecord Clone(SyncRecord x) { return new SyncRecord { EntityType = x.EntityType, Id = x.Id, Version = x.Version, BaseVersion = x.BaseVersion, UpdatedAt = x.UpdatedAt, DeviceId = x.DeviceId, Deleted = x.Deleted, Json = x.Json,OriginVersion=x.OriginVersion,OriginBaseVersion=x.OriginBaseVersion,OriginUpdatedAt=x.OriginUpdatedAt }; }
        private static SyncQueueItem Clone(SyncQueueItem x) { return new SyncQueueItem { Id = x.Id, CreatedAt = x.CreatedAt, Attempts = x.Attempts, LastError = x.LastError, Record = Clone(x.Record) }; }
        private static SyncConflict Clone(SyncConflict x) { return new SyncConflict { Id = x.Id, CreatedAt = x.CreatedAt, Status = x.Status, Local = Clone(x.Local), Remote = Clone(x.Remote) }; }
    }
}
