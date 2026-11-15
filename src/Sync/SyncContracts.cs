using System;
using System.Collections.Generic;

namespace DeskBox.Sync
{
    public class SyncRecord
    {
        public string EntityType = "";
        public string Id = "";
        public long Version;
        public long BaseVersion;
        public long UpdatedAt;
        public string DeviceId = "";
        public bool Deleted;
        public string Json = "";
        public long OriginVersion, OriginBaseVersion, OriginUpdatedAt;
    }

    public class SyncQueueItem
    {
        public string Id = "";
        public long CreatedAt;
        public int Attempts;
        public string LastError = "";
        public SyncRecord Record = new SyncRecord();
    }

    public class SyncConflict
    {
        public string Id = "";
        public long CreatedAt;
        public string Status = "open";
        public SyncRecord Local = new SyncRecord();
        public SyncRecord Remote = new SyncRecord();
    }

    public enum SyncApplyResult { Applied, Duplicate, IgnoredOlder, Conflict, Invalid, Resolved }

    public class MobileProjectDto
    {
        public string Id = ""; public string Name = ""; public string Number = ""; public string Customer = "";
        public string Equipment = ""; public string Owner = ""; public string Deadline = ""; public double Progress;
        public bool ProgressApplicable = true;
        public string Risk = "medium"; public string SyncScope = "local"; public long UpdatedAt;
    }

    public class MobileTaskDto
    {
        public string Id = ""; public string ProjectId = ""; public string Title = ""; public string Status = "open";
        public string Priority = "normal"; public string Due = ""; public string Owner = ""; public string Notes = ""; public long UpdatedAt;
    }

    public class IncidentDto
    {
        public string Id = ""; public string ProjectId = ""; public string Equipment = ""; public string Severity = "S3";
        public string Status = "open"; public string Description = ""; public string Cause = ""; public string Solution = "";
        public string Owner = ""; public long OccurredAt; public long UpdatedAt; public List<string> AttachmentIds = new List<string>();
    }

    public class CommentDto { public string Id = ""; public string ParentType = ""; public string ParentId = ""; public string Author = ""; public string Text = ""; public long UpdatedAt; }
    public class AttachmentDto { public string Id = ""; public string ParentType = ""; public string ParentId = ""; public string FileName = ""; public string ContentType = ""; public long Size; public string Sha256 = ""; public string LocalPath = ""; public long UpdatedAt; }
    public class MobileNotificationDto { public string Id = ""; public string Kind = ""; public string Title = ""; public string Message = ""; public string ProjectId = ""; public bool Read; public long UpdatedAt; }
}
