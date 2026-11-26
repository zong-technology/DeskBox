using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeskBox.Core;
using DeskBox.Domain;

namespace DeskBox.Data
{
    internal sealed class McpStore
    {
        internal sealed class Settings
        {
            public int Version = 1;
            public List<McpServer> Servers = new List<McpServer>();
        }

        private readonly string root;
        internal readonly string ConfigPath;

        internal McpStore(string dataDirectory)
        {
            root = Path.GetFullPath(Path.Combine(dataDirectory, "mcp-gateway"));
            ConfigPath = Path.Combine(root, "servers.json");
        }

        internal Settings Load(out bool recovered)
        {
            var settings = ReadSettings(ConfigPath);
            recovered = false;
            if (Valid(settings)) return settings;
            var backup = ConfigPath + ".bak";
            if (!File.Exists(ConfigPath) && !File.Exists(backup)) return new Settings();
            settings = ReadSettings(backup);
            if (!Valid(settings)) throw new InvalidDataException("MCP 配置损坏，找不到可用备份。");
            recovered = true;
            foreach (var server in settings.Servers)
            {
                server.Enabled = false;
                server.Grants.Clear();
            }
            return settings;
        }

        private static Settings ReadSettings(string file)
        {
            if (!File.Exists(file)) return null;
            var json = File.ReadAllText(file);
            var envelope = Json.FromJson<Dictionary<string, object>>(json);
            object version, servers;
            if (envelope == null || !envelope.TryGetValue("Version", out version) || !(version is int)) return null;
            if ((int)version != 1) throw new InvalidDataException("MCP 配置版本不受支持。");
            if (!envelope.TryGetValue("Servers", out servers) || !(servers is System.Collections.IList)) return null;
            return Json.FromJson<Settings>(json);
        }

        private static bool Valid(Settings value)
        {
            if (value == null || value.Servers == null) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var server in value.Servers)
            {
                Guid id;
                if (server == null || !Guid.TryParseExact(server.Id, "N", out id) || !ids.Add(server.Id) ||
                    server.ProjectId == null || server.Grants == null || server.Grants.Any(grant => grant == null ||
                        string.IsNullOrEmpty(grant.Tool) || string.IsNullOrEmpty(grant.Fingerprint)))
                    return false;
            }
            return true;
        }

        internal void Save(Settings settings, bool recovered) { Json.SaveAtomic(ConfigPath, settings, !recovered); }

        internal void SaveReceipt(McpReceipt receipt)
        {
            Json.SaveAtomic(Path.Combine(ReceiptDirectory(receipt.ServerId), receipt.Id + ".json"), receipt, true);
        }

        internal List<McpReceipt> ReadReceipts(string projectId, string serverId, bool active)
        {
            var folder = ReceiptDirectory(serverId);
            var receipts = new List<McpReceipt>();
            if (!Directory.Exists(folder)) return receipts;
            foreach (var path in Recent(folder).Take(50))
            {
                var record = ReadReceipt(path, projectId, serverId) ?? ReadReceipt(path + ".bak", projectId, serverId);
                if (record == null) throw new InvalidDataException("MCP 调用记录损坏，找不到可用备份。");
                if (!active && record.State == "running") { record.State = "interrupted"; record.ErrorKind = "interrupted"; }
                receipts.Add(record);
            }
            return receipts.OrderByDescending(receipt => receipt.StartedAt).ToList();
        }

        internal void PruneReceipts(string serverId)
        {
            foreach (var path in Recent(ReceiptDirectory(serverId)).Skip(250))
            {
                File.Delete(path);
                File.Delete(path + ".bak");
            }
        }

        private static IEnumerable<string> Recent(string folder)
        {
            return Directory.EnumerateFiles(folder, "*.json").OrderByDescending(File.GetLastWriteTimeUtc);
        }

        private string ReceiptDirectory(string serverId) { return Path.Combine(root, "receipts", serverId); }

        private static McpReceipt ReadReceipt(string path, string projectId, string serverId)
        {
            var value = Json.Load<McpReceipt>(path, null);
            var id = Path.GetFileName(path).Split('.')[0];
            return value != null && value.Version == 1 && value.Id == id && value.ProjectId == projectId && value.ServerId == serverId ? value : null;
        }
    }
}
