using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DeskBox.Core;
using DeskBox.Domain;

namespace DeskBox.Data
{
    public sealed class AgentTaskStore
    {
        private readonly string root;
        private static readonly ConcurrentDictionary<string, byte> Active = new ConcurrentDictionary<string, byte>();

        public AgentTaskStore() : this(AppPaths.DataDir) { }

        public AgentTaskStore(string dataDirectory)
        {
            root = Path.GetFullPath(Path.Combine(dataDirectory, "agent-tasks"));
        }

        public List<AgentTask> List(string projectId, string environment, int limit = 50)
        {
            var folder = DirectoryFor(projectId, environment);
            if (!Directory.Exists(folder)) return new List<AgentTask>();
            var tasks = new List<AgentTask>();
            // Sort metadata before opening the newest records. No prompts or tool output are stored here.
            foreach (var path in Directory.EnumerateFiles(folder).Where(file => file.EndsWith(".json", StringComparison.Ordinal) ||
                file.EndsWith(".json.bak", StringComparison.Ordinal)).Select(file => file.EndsWith(".bak", StringComparison.Ordinal)
                    ? file.Substring(0, file.Length - 4) : file).Distinct().OrderByDescending(file =>
                    File.Exists(file) ? File.GetLastWriteTimeUtc(file) : File.GetLastWriteTimeUtc(file + ".bak"))
                .Take(Math.Max(1, Math.Min(limit, 100))))
            {
                var task = Read(path, projectId, environment);
                if (task == null) throw new InvalidDataException("Agent 执行记录损坏，无法读取原文件或备份。");
                if (task.State == "running" && !Active.ContainsKey(path))
                {
                    task.State = "interrupted";
                    task.ErrorKind = "interrupted";
                    foreach (var step in task.Steps.Where(step => step.State == "running")) step.State = "interrupted";
                }
                tasks.Add(task);
            }
            return tasks.OrderByDescending(task => task.StartedAt).ToList();
        }

        internal void Begin(AgentTask task)
        {
            var path = RecordPath(task);
            if (!Active.TryAdd(path, 0)) throw new InvalidOperationException("Agent 任务 ID 已使用。");
            try { Write(task); }
            catch { Release(task); throw; }
        }

        internal void Write(AgentTask task) { Json.SaveAtomic(RecordPath(task), task, true); }

        internal void Release(AgentTask task)
        {
            byte ignored;
            Active.TryRemove(RecordPath(task), out ignored);
        }

        private string RecordPath(AgentTask task)
        {
            return Path.Combine(DirectoryFor(task.ProjectId, task.Environment), task.Id + ".json");
        }

        private static AgentTask Read(string path, string projectId, string environment)
        {
            foreach (var candidate in new[] { path, path + ".bak" })
            {
                var task = Json.Load<AgentTask>(candidate, null);
                if (task != null && task.Version == 1 && task.ProjectId == (projectId ?? "") && task.Environment == environment
                    && task.Id == Path.GetFileNameWithoutExtension(path) && task.Steps != null && task.Steps.All(step => step != null))
                    return task;
            }
            return null;
        }

        private string DirectoryFor(string projectId, string environment)
        {
            if (environment != "network" && environment != "local") throw new ArgumentException("AI 环境无效。");
            if (string.IsNullOrEmpty(projectId)) return Path.Combine(root, environment, "unbound");
            using (var sha = SHA256.Create())
            {
                var name = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(projectId))).Replace("-", "").ToLowerInvariant();
                return Path.Combine(root, environment, name);
            }
        }
    }
}
