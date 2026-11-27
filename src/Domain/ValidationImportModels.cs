using System.Collections.Generic;

namespace DeskBox.Domain
{
    public sealed class ValidationImportOptions
    {
        public string Kind = "requirement", Stage = "", DocumentNumber = "", DocumentVersion = "", Worksheet = "", Encoding = "auto", ProtocolId = "";
        public int WordTable;
        public Dictionary<string, int> Mapping;
    }

    public sealed class ValidationImportPreview
    {
        public string ProjectId { get; internal set; }
        public EngineeringSource Source { get; internal set; }
        public IReadOnlyList<ValidationImportRow> Rows { get; internal set; }
        public IReadOnlyList<EngineeringIssue> Issues { get; internal set; }
        internal ValidationImportOptions Options { get; set; }
        internal string Path { get; set; }
        internal string Stamp { get; set; }
        internal string Checksum { get; set; }
    }

    public sealed class ValidationImportRow
    {
        public string Number = "", Title = "", Description = "", Location = "", State = "", ExistingId = "";
        public Dictionary<string, string> Fields = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ValidationImportResult
    {
        public int Added, Updated, Retired, Skipped, SourceRefreshed;
        public List<string> SavedIds = new List<string>();
        public EngineeringSource Source;
    }

    public sealed class ValidationRevisionDiff
    {
        public string Field = "", Before = "", After = "";
    }
}
