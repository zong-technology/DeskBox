using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using DeskBox.Engineering;
using DeskBox.Core;

namespace DeskBox.Sync
{
    public sealed class LocalSyncCoordinator : IDisposable
    {
        private readonly Store _store;
        private X509Certificate2 _certificate;
        private LocalSyncServer _server;
        private LocalSyncDiscovery _discovery;
        private readonly WorkspaceStore _workspaces;
        private HashSet<string> _publishedTaskIds = new HashSet<string>();
        public SyncStore Sync { get; private set; }
        public DeviceTrustStore Devices { get; private set; }
        public event Action Changed;

        public LocalSyncCoordinator(Store store, WorkspaceStore workspaces = null) { _store = store; Sync = new SyncStore(); Devices = new DeviceTrustStore(); _workspaces = workspaces ?? new WorkspaceStore(); _publishedTaskIds=new HashSet<string>(_store.Tasks.Select(t=>t.Id)); }
        public bool Running { get { return _server != null && _server.Running; } }
        public int Port { get { return _server == null ? 0 : _server.Port; } }
        public string Thumbprint { get { return _server == null ? "" : _server.CertificateThumbprint; } }

        public void Start()
        {
            if (Running) return;
            try
            {
            _certificate = LocalSyncCertificate.LoadOrCreate(Store.DataDir);
            _server = new LocalSyncServer(_certificate, Sync, Devices, IPAddress.Any, Math.Max(1024, _store.Settings.LocalSyncPort)); _server.Start();
            _server.BeforePull += PublishWindowsSnapshot; _server.RecordMerged += ApplyRemote;
            var name = string.IsNullOrWhiteSpace(_store.Settings.SyncDeviceName) ? Environment.MachineName : _store.Settings.SyncDeviceName;
            _discovery = new LocalSyncDiscovery(name, _server.Port, Thumbprint, IPAddress.Any, Math.Max(1024, _store.Settings.DiscoveryPort)); _discovery.Start();
            }
            catch { Stop(); throw; }
            Raise();
        }

        public void Stop()
        {
            Devices.CancelAllPairings();
            if (_discovery != null) { _discovery.Dispose(); _discovery = null; }
            if (_server != null) { _server.Dispose(); _server = null; }
            if (_certificate != null) { _certificate.Dispose(); _certificate = null; }
            Raise();
        }

        public PairingTicket CreatePairing(int minutes) { var ticket = Devices.CreatePairingTicket(minutes); Raise(); return ticket; }
        public void Revoke(string id) { Devices.Revoke(id); Raise(); }
        public void Remove(string id) { Devices.Remove(id); Raise(); }
        public List<IncidentDto> Incidents
        {
            get { return Sync.Records.Where(r => r.EntityType=="incident" && !r.Deleted).Select(r => Json.FromJson<IncidentDto>(r.Json)).Where(x => x!=null).OrderByDescending(x => x.UpdatedAt).ToList(); }
        }
        public static bool ValidIncident(IncidentDto item)
        {
            return item!=null && !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Description)
                && new[]{"S1","S2","S3","S4"}.Contains(item.Severity) && new[]{"open","investigating","resolved","closed"}.Contains(item.Status)
                && ((item.Status!="resolved" && item.Status!="closed") || !string.IsNullOrWhiteSpace(item.Solution));
        }
        public void SaveIncident(IncidentDto item, long expectedVersion)
        {
            if (!ValidIncident(item)) throw new ArgumentException("请填写事件描述、有效严重程度及状态；解决或关闭前需填写处理措施。");
            item=Json.FromJson<IncidentDto>(Json.ToJson(item));
            item.UpdatedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); if(item.OccurredAt<=0)item.OccurredAt=item.UpdatedAt;
            Sync.UpsertLocal("incident",item.Id,Json.ToJson(item),"windows",expectedVersion); Raise();
        }
        public SyncApplyResult MergeFromCloud(SyncRecord record)
        {
            if (record == null || (record.EntityType != "task" && record.EntityType != "incident")) return SyncApplyResult.Invalid;
            if (!record.Deleted && record.EntityType == "task") { var task = Json.FromJson<MobileTaskDto>(record.Json); if (task == null || task.Id != record.Id) return SyncApplyResult.Invalid; }
            if (!record.Deleted && record.EntityType == "incident") { var item=Json.FromJson<IncidentDto>(record.Json); if(!ValidIncident(item)||item.Id!=record.Id)return SyncApplyResult.Invalid; }
            var result = Sync.MergeRemote(record);
            if (result == SyncApplyResult.Applied) ApplyRemote(record, result);
            Raise(); return result;
        }
        public SyncApplyResult ResolveConflict(string id, bool useRemote, long expectedVersion)
        {
            PublishWindowsSnapshot();
            var conflict = Sync.Conflicts.FirstOrDefault(x => x.Id == id && x.Status == "open");
            if (conflict == null) return SyncApplyResult.Invalid;
            var chosen = useRemote ? conflict.Remote : Sync.Records.First(x => x.Id == conflict.Local.Id && x.EntityType == conflict.Local.EntityType);
            if (!chosen.Deleted && chosen.EntityType == "task") { var dto = Json.FromJson<MobileTaskDto>(chosen.Json); if (dto == null || dto.Id != chosen.Id) return SyncApplyResult.Invalid; }
            if (!chosen.Deleted && chosen.EntityType == "project") { var dto = Json.FromJson<MobileProjectDto>(chosen.Json); if (dto == null || dto.Id != chosen.Id) return SyncApplyResult.Invalid; }
            var result = Sync.ResolveConflict(id, useRemote, "windows", expectedVersion);
            if (result == SyncApplyResult.Applied) ApplyRemote(Sync.Records.First(x => x.Id == conflict.Local.Id && x.EntityType == conflict.Local.EntityType), result);
            Raise(); return result;
        }

        public List<string> LocalAddresses()
        {
            var list = new List<string>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback).OrderByDescending(AddressPriority))
                foreach (var address in ni.GetIPProperties().UnicastAddresses.Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork))
                    if (!IPAddress.IsLoopback(address.Address) && !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal)) list.Add(address.Address.ToString());
            return list.Distinct().ToList();
        }

        private static int AddressPriority(NetworkInterface adapter)
        {
            int score = adapter.GetIPProperties().GatewayAddresses.Any(x => !x.Address.Equals(IPAddress.Any)) ? 100 : 0;
            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 200;
            var name = (adapter.Name + " " + adapter.Description).ToLowerInvariant();
            if (new[] { "vmware", "virtual", "vethernet", "tap", "tun", "vpn", "docker", "wsl" }.Any(name.Contains)) score -= 1000;
            return score;
        }

        public string PairingUri(PairingTicket ticket, string selectedAddress = null)
        {
            var addresses = LocalAddresses();
            var host = string.IsNullOrEmpty(selectedAddress) ? addresses.FirstOrDefault() : selectedAddress;
            if (host == null || !addresses.Contains(host)) throw new InvalidOperationException("请选择电脑当前有效的 WLAN 或以太网 IPv4 地址");
            return "deskbox://pair?host=" + host + "&port=" + Port + "&code=" + ticket.Code + "&fp=" + Thumbprint;
        }

        public void PublishWindowsSnapshot()
        {
            foreach (var p in _workspaces.Items)
            {
                var dto = new MobileProjectDto { Id=p.Id,Name=p.Name,Number=p.Number,Customer=p.Customer,Equipment=p.Equipment,Owner=p.Owner,Deadline=p.Deadline,Risk=p.Risk,Progress=Overall(p),ProgressApplicable=ValidationProgress.Overall(p).HasValue,UpdatedAt=p.UpdatedAt };
                Sync.UpsertSnapshot("project",p.Id,Json.ToJson(dto),"windows");
            }
            var tasks = _store.Tasks.ToArray();
            foreach (var t in tasks)
            {
                var previous=Sync.Records.FirstOrDefault(r=>r.EntityType=="task"&&r.Id==t.Id&&!r.Deleted);
                var old=previous==null?null:Json.FromJson<MobileTaskDto>(previous.Json);
                var dto=new MobileTaskDto{Id=t.Id,ProjectId=t.Project,Title=t.Title,Status=t.Done?"done":"open",Priority=t.Priority,Due=t.Due,Notes=t.Notes,
                    Owner=t.Owner??(old==null?"":old.Owner??""),UpdatedAt=old==null?Math.Max(t.CreatedAt,t.DoneAt):old.UpdatedAt};
                if(old!=null && Json.ToJson(dto)==Json.ToJson(old)){
                    // Keep the original wire representation: Swift and C# can order JSON fields differently.
                    Sync.UpsertSnapshot("task",t.Id,previous.Json,"windows");
                }else{
                    dto.UpdatedAt=Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),dto.UpdatedAt+1);
                    Sync.UpsertSnapshot("task",t.Id,Json.ToJson(dto),"windows");
                }
            }
            var taskIds = new HashSet<string>(tasks.Select(x => x.Id));
            foreach (var removed in _publishedTaskIds.Where(x => !taskIds.Contains(x))) Sync.DeleteLocal("task", removed, "windows");
            _publishedTaskIds = taskIds;
        }

        private void ApplyRemote(SyncRecord record, SyncApplyResult result)
        {
            if(record==null)return;
            if(record.Deleted){if(record.EntityType=="task"){_store.Tasks.RemoveAll(x=>x.Id==record.Id);_store.SaveTasksOnly();}if(record.EntityType=="project")_workspaces.Remove(record.Id);Raise();return;}
            if(record.EntityType=="task") { var dto=Json.FromJson<MobileTaskDto>(record.Json); if(dto==null || dto.Id != record.Id)return; var timestamp=dto.UpdatedAt>0?dto.UpdatedAt:record.UpdatedAt>0?record.UpdatedAt:DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); var t=_store.Tasks.FirstOrDefault(x=>x.Id==dto.Id); if(t==null){t=new TaskItem{Id=dto.Id,CreatedAt=timestamp};_store.Tasks.Add(t);} t.Title=dto.Title;t.Project=dto.ProjectId;t.Priority=dto.Priority;t.Due=dto.Due;t.Notes=dto.Notes;t.Owner=dto.Owner??"";t.Done=dto.Status=="done";t.DoneAt=t.Done?timestamp:0;_store.SaveTasksOnly(); }
            if(record.EntityType=="project") _workspaces.ApplyMobileProject(Json.FromJson<MobileProjectDto>(record.Json));
            Raise();
        }

        private static double Overall(EngineerWorkspace p){return ValidationProgress.Overall(p) ?? 0;}

        private void Raise() { var h = Changed; if (h != null) h(); }
        public void Dispose() { Stop(); }
    }
}
