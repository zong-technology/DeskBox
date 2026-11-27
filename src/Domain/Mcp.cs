using System.Collections.Generic;

namespace DeskBox.Domain
{
    public sealed class McpServer
    {
        public string Id = "", ProjectId = "", Name = "", Transport = "http", Address = "", Arguments = "";
        public bool Enabled;
        public int TimeoutMs = 15000;
        public int Revision;
        public List<McpGrant> Grants = new List<McpGrant>();
    }

    public sealed class McpGrant
    {
        public string Tool = "", Fingerprint = "", InputSchema = "", Description = "";
        public bool AllowAgent;
    }

    public sealed class McpAgentTool
    {
        public string ServerId { get; internal set; }
        public string ServerName { get; internal set; }
        public string Name { get; internal set; }
        public string Description { get; internal set; }
        public string InputSchema { get; internal set; }
        internal string Fingerprint;
    }

    public sealed class McpTool
    {
        public string Name { get; internal set; }
        public string Description { get; internal set; }
        public string InputSchema { get; internal set; }
        public string OutputSchema { get; internal set; }
        public string Annotations { get; internal set; }
        public bool ProhibitedByHints { get; internal set; }
        public bool AgentReadOnly { get; internal set; }
        internal string Fingerprint;
    }

    public sealed class McpCatalog
    {
        internal McpCatalog() { }
        public string ServerId { get; internal set; }
        public string ProjectId { get; internal set; }
        public string ServerName { get; internal set; }
        public string ServerVersion { get; internal set; }
        public string ProtocolVersion { get; internal set; }
        public IReadOnlyList<McpTool> Tools { get; internal set; }
        internal string Profile;
    }

    public sealed class McpReceipt
    {
        public int Version = 1;
        public string Id = "", ProjectId = "", ServerId = "", Operation = "", Tool = "", State = "running", ErrorKind = "";
        public string ProtocolVersion = "";
        public long StartedAt, ElapsedMs;
        public int HttpStatus, ToolCount, OutputCharacters;
        public int? RpcCode;
        public bool Dispatched;
    }

    public sealed class McpResult
    {
        public McpReceipt Receipt { get; internal set; }
        public McpCatalog Catalog { get; internal set; }
        public string Output { get; internal set; }
        public string Error { get; internal set; }
    }
}
