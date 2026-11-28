using System.Collections.Generic;

namespace DeskBox.Domain
{
    public sealed class InstrumentAsset
    {
        public string Id = "", ProjectId = "", Number = "", Name = "", Kind = "", Manufacturer = "", Model = "",
            SerialNumber = "", Location = "", Signal = "", Unit = "", TagId = "";
        public decimal? RangeLow, RangeHigh;
        public EngineeringSource Manual;
        public int Revision;
        public string UpdatedBy = "", RevisionToken = "";
        public long UpdatedAt;
    }

    public sealed class InstrumentActivity
    {
        public string Id = "", ProjectId = "", AssetId = "", Kind = "", Date = "", DueDate = "", Notes = "",
            CertificateNumber = "", EvidenceId = "", RecordedBy = "";
        public EngineeringSource Source;
        public long RecordedAt;
    }

    public sealed class InstrumentRegister
    {
        public string ProjectId = "";
        public List<InstrumentAsset> Assets = new List<InstrumentAsset>();
        public List<InstrumentAsset> History = new List<InstrumentAsset>();
        public List<InstrumentActivity> Activities = new List<InstrumentActivity>();
        public bool Recovered { get; internal set; }
    }

    public sealed class CalibrationReminder
    {
        public string ProjectId { get; internal set; }
        public string Key { get; internal set; }
        public bool Recovered { get; internal set; }
        public int Overdue { get; internal set; }
        public int DueToday { get; internal set; }
        public int DueSoon { get; internal set; }
    }
}
