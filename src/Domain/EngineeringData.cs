using System;
using System.Collections.Generic;

namespace DeskBox.Domain
{
    public sealed class EngineeringSource
    {
        public string Type = "", Document = "", Sha256 = "", Version = "", Encoding = "";
        public long Bytes;
    }

    public sealed class EngineeringIssue
    {
        public string Location = "", Message = "";
        public bool Blocking;
    }

    public sealed class EngineeringRecord
    {
        public string Key = "", Kind = "", Name = "", Parent = "", Location = "";
        public int StartLine, EndLine;
        public bool Recognized = true;
        public Dictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class EngineeringDocument
    {
        public EngineeringSource Source;
        public List<EngineeringRecord> Records = new List<EngineeringRecord>();
        public List<EngineeringIssue> Issues = new List<EngineeringIssue>();
    }

    public sealed class EngineeringDelta
    {
        public string Key = "", State = "", Field = "", Before = "", After = "", BeforeLocation = "", AfterLocation = "";
    }

    public sealed class EngineeringDiff
    {
        public string ProjectId = "";
        public EngineeringSource Before, After;
        public List<EngineeringDelta> Changes = new List<EngineeringDelta>();
        public List<EngineeringIssue> Issues = new List<EngineeringIssue>();
        public int Added, Removed, Modified, Unclassified, Unavailable;
        internal int DetailCharacters;
    }

    public sealed class TagSourceLink
    {
        public string SourceType = "", SourceDocument = "", SourceLocation = "", SourceVersion = "", Sha256 = "", Encoding = "";
    }

    public sealed class TagRelation
    {
        public string Kind = "", EntityId = "";
    }

    public sealed class EngineeringTag
    {
        public string TagId = "", ProjectId = "", TagName = "", SystemId = "", EquipmentId = "";
        public string Description = "", DataType = "", SignalType = "", EngineeringUnit = "";
        public decimal? RangeLow, RangeHigh;
        public string SourceType = "", SourceDocument = "", SourceLocation = "", SourceVersion = "", SourceSha256 = "";
        public string Address = "";
        public long CreatedAt, UpdatedAt;
        public int Revision;
        public List<TagSourceLink> Sources = new List<TagSourceLink>();
        public List<TagRelation> Relations = new List<TagRelation>();
    }

    public sealed class TagImportRow
    {
        public EngineeringTag Value;
        public string State = "", ExistingId = "";
        internal List<string> Fields = new List<string>();
    }

    public sealed class TagImportPreview
    {
        public string ProjectId { get; internal set; }
        public EngineeringSource Source { get; internal set; }
        public IReadOnlyList<TagImportRow> Rows { get; internal set; }
        public IReadOnlyList<EngineeringIssue> Issues { get; internal set; }
        internal string SourcePath, StoreStamp, PayloadHash;
    }

    public sealed class TagImportResult
    {
        public string ProjectId = "";
        public EngineeringSource Source;
        public int Added, Updated, Linked, Unchanged;
        public List<string> TagIds = new List<string>();
    }

    public sealed class TagPage
    {
        public int Total;
        public bool Recovered;
        public List<EngineeringTag> Items = new List<EngineeringTag>();
    }
}
