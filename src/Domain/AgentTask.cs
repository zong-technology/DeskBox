using System.Collections.Generic;

namespace DeskBox.Domain
{
    public sealed class AgentTask
    {
        public int Version = 1;
        public string Id = "", ProjectId = "", SessionId = "", Environment = "";
        public string ProviderId = "", Model = "", Mode = "", State = "running", ErrorKind = "";
        public long StartedAt, EndedAt;
        public List<AgentStep> Steps = new List<AgentStep>();
    }

    public sealed class AgentStep
    {
        public string Kind = "", Name = "", State = "running", ErrorKind = "", ReceiptId = "";
        public long StartedAt, ElapsedMs;
        public int HttpStatus, OutputCharacters;
        public int? InputTokens, OutputTokens;
        public bool TerminalFrame, StreamedText;
        public int Attempts;
    }

    public sealed class ModelProbe
    {
        public bool Ok, StreamedText, TerminalFrame;
        public string Model = "", Protocol = "", Error = "", ErrorKind = "";
        public int HttpStatus;
        public long ElapsedMs;
        public int? InputTokens, OutputTokens;
    }
}
