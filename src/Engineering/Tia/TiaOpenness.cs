using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using DeskBox.Core;

namespace DeskBox.Engineering.Tia
{
    public sealed class TiaJob
    {
        public string Operation = "inspect", ProjectFile = "", Cpu = "", Block = "", ApprovedHash = "", CatalogQuery = "", ProjectId = "", ExpectedSource = "";
        public string Source = "", BlockKind = "FB";
        public bool ReadBlockContent;
        public int PortalProcessId;
    }

    public sealed class TiaOpenProject
    {
        public int ProcessId;
        public string Name = "", Path = "", Version = "V20";
    }

    public sealed class TiaResult
    {
        public string Error = "", Copy = "", Report = "", CompileState = "未运行", SourceHash = "";
        public string OpennessVersion = "", SavedCopyHash = "", OpennessStage = "", OpennessErrorDetails = "";
        public string CompileStartedUtc = "", CompileFinishedUtc = "", VerifyStartedUtc = "";
        public bool Saved, Compiled, WriteStarted, OpenedReadOnly, Verified;
        public PlcSnapshot Snapshot;
        public string McpAudit = "";
        public int CompatibleProcesses;
        public List<TiaOpenProject> OpenProjects = new List<TiaOpenProject>();
        public List<string> Cpus = new List<string>();
        public List<PlcDiagnostic> Diagnostics = new List<PlcDiagnostic>();
    }

    public static class TiaOpenness
    {
        private const string RequestFile = "approved-operation.json", ResponseFile = "result.json";
        private const string WriteCheckpoint = "write-started.json";
        private const string SnapshotFolder = "snapshot", CopyFolder = "work";
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static TiaResult Run(TiaJob job, CancellationToken token)
        {
            ValidateJob(job);
            token.ThrowIfCancellationRequested();
            if (NeedsCopy(job.Operation)) RequireV20();
            var root = PlcEngineering.FullLocalPath(Path.Combine(AppPaths.DataDir, "tia-jobs"));
            string id = Guid.NewGuid().ToString("N");
            var dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(dir, RequestFile), Json.ToJson(job), Utf8);
            var resultPath = Path.Combine(dir, ResponseFile);
            try
            {
                if (NeedsCopy(job.Operation))
                {
                    var file = PlcEngineering.FullLocalPath(job.ProjectFile);
                    var parent = Path.GetDirectoryName(file);
                    if (!File.Exists(file) || string.Equals(parent, Path.GetPathRoot(file), StringComparison.OrdinalIgnoreCase) || Inside(parent, dir))
                        throw new InvalidOperationException("请选择有效的独立工程目录，工作区不得位于工程目录下。");
                    var baseline = Path.Combine(dir, SnapshotFolder);
                    var first = CopyTree(parent, baseline, token);
                    var second = CopyTree(parent, null, token);
                    if (!first.SequenceEqual(second)) throw new IOException("源工程在复制期间发生变化。请关闭 TIA 中的源工程后重试。");
                    if (job.ExpectedSource.Length != 0 && job.ExpectedSource != PlcEngineering.Hash(string.Join("\n", first)))
                        throw new IOException("工程已变化，请重新读取并审批。");
                    File.WriteAllLines(Path.Combine(dir, "source-sha256.txt"), first, Utf8);
                    if (!first.SequenceEqual(CopyTree(baseline, Path.Combine(dir, CopyFolder), token)))
                        throw new IOException("工作副本与原始快照不一致。");
                }
                token.ThrowIfCancellationRequested();
                bool succeeded = TiaMcp.Run(root, id, job, token);
                if (!File.Exists(resultPath)) throw new IOException("TIA MCP 未保存工程回执。");
                var result = Json.FromJson<TiaResult>(File.ReadAllText(resultPath));
                if (result == null || succeeded != (result.Error.Length == 0)) throw new InvalidDataException("TIA MCP 状态与本地 Openness 回执不一致。");
                result.McpAudit = Path.Combine(dir, "mcp-audit.jsonl");
                return result;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(dir, "failed.txt"), DateTime.UtcNow.ToString("O") + "\r\n" + ex.Message, Utf8);
                string checkpoint = Path.Combine(dir, WriteCheckpoint);
                if (!(ex is OperationCanceledException) && File.Exists(checkpoint))
                    throw new IOException(ex.Message + Environment.NewLine + "恢复回执 · " + checkpoint, ex);
                throw;
            }
        }

        internal static int RunMcpServer(string id, string dataRoot)
        {
            Guid parsed;
            if (!Guid.TryParseExact(id, "N", out parsed) || id != parsed.ToString("N") || Path.GetFileName(dataRoot) != "tia-jobs") return 2;
            var directory = PlcEngineering.FullLocalPath(Path.Combine(dataRoot, id));
            var job = Json.Load<TiaJob>(PlcEngineering.FullLocalPath(Path.Combine(directory, RequestFile)), null);
            ValidateJob(job);
            using (var input = new StreamReader(Console.OpenStandardInput(), Utf8))
            using (var output = new StreamWriter(Console.OpenStandardOutput(), Utf8))
                return TiaMcp.Serve(dataRoot, id, job, input, output);
        }

        internal static int RunWorker(string id, string dataRoot, TiaJob job)
        {
            var directory = PlcEngineering.FullLocalPath(Path.Combine(dataRoot, id));
            using (var start = new FileStream(Path.Combine(directory, "started.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            var result = new TiaResult();
            try
            {
                ValidateJob(job);
                bool live = job.Operation == "discover" || job.Operation == "read-open";
                if (live) result.OpennessStage = "检查 V20 安装与当前用户权限";
                var installation = RequireV20();
                result.OpennessVersion = FileVersionInfo.GetVersionInfo(installation.AssemblyPath).FileVersion ?? "";
                var project = "";
                if (NeedsCopy(job.Operation))
                {
                    project = PlcEngineering.FullLocalPath(Path.Combine(directory, CopyFolder, Path.GetFileName(job.ProjectFile)));
                    var baseline = PlcEngineering.FullLocalPath(Path.Combine(directory, SnapshotFolder, Path.GetFileName(job.ProjectFile)));
                    if (!File.Exists(project) || !File.Exists(baseline) || !File.Exists(Path.Combine(directory, "source-sha256.txt")))
                        throw new InvalidOperationException("缺少工作副本、原始快照或清单。");
                    result.Copy = project;
                    result.SourceHash = PlcEngineering.Hash(string.Join("\n", File.ReadAllLines(Path.Combine(directory, "source-sha256.txt"))));
                    if (job.ExpectedSource.Length != 0 && result.SourceHash != job.ExpectedSource)
                        throw new IOException("工程快照指纹与审批不一致。");
                }
                if (live) result.OpennessStage = "加载 V20 Openness";
                using (var api = new V20Session(installation.AssemblyPath, !live)) api.Execute(job, project, directory, result);
                if (result.Saved) result.SavedCopyHash = PlcEngineering.Hash(string.Join("\n", CopyTree(Path.GetDirectoryName(project), null, CancellationToken.None)));
            }
            catch (Exception e)
            {
                if (result.OpennessStage.Length != 0) result.OpennessErrorDetails = e.ToString();
                while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
                result.Error = (result.OpennessStage.Length == 0 ? "" : result.OpennessStage + "失败 · ") + e.GetType().Name + " · " + e.Message;
                if (result.WriteStarted && result.CompileState == "未运行" && job != null)
                    result.Diagnostics.Add(new PlcDiagnostic { Severity = "Error", Source = job.Block + ".scl", Message = e.Message });
            }
            File.WriteAllText(Path.Combine(directory, ResponseFile), Json.ToJson(result), Utf8);
            if (result.Copy.Length != 0)
                File.WriteAllText(Path.Combine(directory, "engineering-report.md"), "# TIA V20 Openness\n\n" + result.OpennessVersion +
                    "\n\n原工程 SHA-256 · " + result.SourceHash + "\n工作副本 · " + result.Copy + "\n\n" + result.Report +
                    (result.Error.Length == 0 ? "" : "\n\n失败 · " + result.Error), Utf8);
            return result.Error.Length == 0 ? 0 : 1;
        }

        private static TiaInstallation RequireV20()
        {
            var inspection = TiaEnvironmentInspector.Inspect();
            if (!inspection.Windows) throw new PlatformNotSupportedException(inspection.Describe());
            var installation = inspection.Installations.FirstOrDefault(i => i.Version == 20 && i.AssemblyPath != null);
            if (installation == null) throw new InvalidOperationException("需要 TIA Portal V20 和匹配的 PublicAPI/V20/Siemens.Engineering.dll。" + Environment.NewLine + inspection.Describe());
            if (!inspection.OpennessGroup) throw new InvalidOperationException("当前 Windows 用户令牌未验证为 Siemens TIA Openness 组成员。");
            PlcEngineering.FullLocalPath(installation.AssemblyPath);
            return installation;
        }

        private static bool NeedsCopy(string operation) { return operation != "catalog" && operation != "discover" && operation != "read-open"; }

        private static bool WritesBlock(string operation) { return operation == "scale" || operation == "scl"; }

        public static TiaJob PrepareSclInsertion(string project, string cpu, string expectedSource, string source, string projectId = "")
        {
            var job = new TiaJob { Operation = "scl", ProjectFile = project, Cpu = cpu, ExpectedSource = expectedSource, ProjectId = projectId,
                Source = source, ApprovedHash = PlcEngineering.Hash(source) };
            var block = VerifySclSource(source);
            job.Block = block.Name;
            job.BlockKind = block.Kind;
            ValidateJob(job);
            return job;
        }

        private static PlcObject VerifySclSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source) || source.Length > 20000) throw new ArgumentException("请选择 1–20000 字的 SCL 草稿。");
            if (source.Any(c => c > '~' || c < ' ' && c != '\r' && c != '\n' && c != '\t'))
                throw new InvalidDataException("V20 Openness 从外部源生成块仅接受 ASCII SCL。请调整源文本后重新预览和批准。");
            var review = PlcEngineering.ReviewScl(source, "审批草稿");
            var block = review.Objects.Count == 1 ? review.Objects[0] : null;
            var masked = SclEditing.Mask(source, false);
            const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            if (block == null || (block.Kind != "FB" && block.Kind != "FC") ||
                !Regex.IsMatch(block.Name, @"\A[A-Za-z_][A-Za-z0-9_]{0,62}\z") || review.Diagnostics.Any(d => d.Severity == "Error"))
                throw new InvalidDataException("仅接受通过静态错误检查的单个 SCL FC 或 FB。");
            string header = block.Kind == "FB" ? "FUNCTION_BLOCK" : "FUNCTION";
            string footer = "END_" + header;
            if (!Regex.IsMatch(masked, @"\A\s*" + header + @"[ \t]+[\s\S]*?\b" + footer + @"[ \t]*;?\s*\z", options) ||
                Regex.Matches(masked, @"\bEND_FUNCTION(?:_BLOCK)?\b", options).Count != 1)
                throw new InvalidDataException("SCL 文件只能包含已批准的单个完整块。");
            return block;
        }

        private static void ValidateJob(TiaJob job)
        {
            if (job == null || !new[] { "discover", "read-open", "inspect", "compile", "scale", "scl", "catalog" }.Contains(job.Operation))
                throw new InvalidOperationException("仅支持工程发现与读取、离线副本新增和编译、硬件目录查询。");
            if (job.Operation == "discover") return;
            if (job.Operation == "catalog")
            {
                if (string.IsNullOrWhiteSpace(job.CatalogQuery) || job.CatalogQuery.Length > 120) throw new ArgumentException("请输入目录搜索词或订货号（1–120 字）。");
                return;
            }
            if (!string.Equals(Path.GetExtension(job.ProjectFile), ".ap20", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("此连接器只打开同版本的独立 .ap20 工程。归档、旧版和多用户项目请先在 TIA 中另存独立离线工程。");
            if (job.Operation == "read-open")
            {
                if (job.PortalProcessId <= 0 || !Path.IsPathRooted(job.ProjectFile) || job.ReadBlockContent)
                    throw new ArgumentException("读取已打开的 V20 工程需要确认实例 PID 和完整路径，只允许元数据读取。");
                return;
            }
            if ((job.Operation == "compile" || WritesBlock(job.Operation)) && (string.IsNullOrWhiteSpace(job.Cpu) || string.IsNullOrWhiteSpace(job.ExpectedSource)))
                throw new InvalidOperationException("请先读取工程，并明确选择一个 PLC 对象。");
            if (job.Operation == "scale" && PlcEngineering.Hash(PlcEngineering.AnalogScale(job.Block)) != job.ApprovedHash)
                throw new InvalidOperationException("缩放模板与已批准的源文件不匹配。");
            if (job.Operation == "scl")
            {
                if (job.ApprovedHash != PlcEngineering.Hash(job.Source))
                    throw new InvalidOperationException("SCL 源与批准记录不一致。");
                var approved = VerifySclSource(job.Source);
                if (job.Block != approved.Name || job.BlockKind != approved.Kind)
                    throw new InvalidOperationException("块名或类型与批准记录不一致。");
            }
        }

        public static TiaResult ReadRecoveryReceipt(string path)
        {
            var receipt = PlcEngineering.FullLocalPath(path);
            var directory = Path.GetDirectoryName(receipt);
            if (!new[] { ResponseFile, WriteCheckpoint }.Contains(Path.GetFileName(receipt), StringComparer.OrdinalIgnoreCase) || !IsJobDirectory(directory))
                throw new InvalidOperationException("请选择当前资料目录中 TIA 作业的 result.json 或 write-started.json。");
            var requestPath = PlcEngineering.FullLocalPath(Path.Combine(directory, RequestFile));
            var storedResult = new FileInfo(receipt);
            var storedRequest = new FileInfo(requestPath);
            if (!storedResult.Exists || !storedRequest.Exists) throw new InvalidDataException("缺少作业回执或审批记录。");
            if (storedResult.Length > 64L * 1024 * 1024 || storedRequest.Length > 256 * 1024)
                throw new InvalidDataException("作业回执超过可读取大小。");
            var result = Json.Load<TiaResult>(receipt, null);
            var request = Json.Load<TiaJob>(requestPath, null);
            ValidateJob(request);
            var expectedCopy = Path.Combine(directory, CopyFolder, Path.GetFileName(request.ProjectFile));
            if (result == null || !CanRestore(result) || !WritesBlock(request.Operation) ||
                result.SourceHash != request.ExpectedSource || !string.Equals(result.Copy, expectedCopy, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("回执必须与已审批且开始修改的离线作业一致。");
            return result;
        }

        private static bool CanRestore(TiaResult job) { return job != null && (job.WriteStarted || job.Saved && job.Compiled && job.Error == ""); }

        private static bool IsJobDirectory(string directory)
        {
            var root = PlcEngineering.FullLocalPath(Path.Combine(AppPaths.DataDir, "tia-jobs"));
            Guid id;
            return !string.IsNullOrEmpty(directory) && Guid.TryParseExact(Path.GetFileName(directory), "N", out id) &&
                string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase);
        }

        public static string RestoreSnapshot(TiaResult job, CancellationToken token)
        {
            if (!CanRestore(job) || string.IsNullOrWhiteSpace(job.Copy) || string.IsNullOrWhiteSpace(job.SourceHash))
                throw new InvalidOperationException("找不到可恢复的 TIA 作业快照。");
            var copy = PlcEngineering.FullLocalPath(job.Copy);
            var work = Path.GetDirectoryName(copy);
            var directory = work == null ? null : Path.GetDirectoryName(work);
            if (!IsJobDirectory(directory) || Path.GetFileName(work) != CopyFolder ||
                !string.Equals(Path.GetExtension(copy), ".ap20", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("只能恢复当前资料目录中已记录的 TIA 工程作业。");
            var snapshot = PlcEngineering.FullLocalPath(Path.Combine(directory, SnapshotFolder));
            var manifestPath = PlcEngineering.FullLocalPath(Path.Combine(directory, "source-sha256.txt"));
            var expected = File.ReadAllLines(manifestPath, Utf8);
            if (!File.Exists(PlcEngineering.FullLocalPath(Path.Combine(snapshot, Path.GetFileName(copy)))) ||
                PlcEngineering.Hash(string.Join("\n", expected)) != job.SourceHash ||
                !CopyTree(snapshot, null, token).SequenceEqual(expected))
                throw new IOException("原始快照与记录的 SHA-256 清单不一致。");

            string suffix = Guid.NewGuid().ToString("N");
            string staging = Path.Combine(directory, "restoring-" + suffix);
            string target = Path.Combine(directory, "restored-" + suffix);
            string project = Path.Combine(target, Path.GetFileName(copy));
            string receipt = target + ".txt";
            try
            {
                if (!CopyTree(snapshot, staging, token).SequenceEqual(expected) || !CopyTree(staging, null, token).SequenceEqual(expected))
                    throw new IOException("恢复副本与原始快照不一致。");
                token.ThrowIfCancellationRequested();
                File.WriteAllText(receipt, DateTime.UtcNow.ToString("O") + "\r\n快照 SHA-256 " + job.SourceHash +
                    "\r\n恢复工程 " + project, Utf8);
                Directory.Move(staging, target);
                return project;
            }
            catch
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { }
                try { if (File.Exists(receipt)) File.Delete(receipt); } catch (IOException) { }
                throw;
            }
        }

        public static string[] CopyTree(string source, string destination, CancellationToken token)
        {
            source = PlcEngineering.FullLocalPath(source);
            if (destination != null)
            {
                destination = PlcEngineering.FullLocalPath(destination);
                if (Inside(source, destination) || Inside(destination, source) || Directory.Exists(destination))
                    throw new InvalidOperationException("复制目的地必须是源工程之外的新目录。");
            }
            var files = new List<string>();
            long bytes = 0;
            CopyFolderTree(source, destination, "", files, ref bytes, token, 0);
            return files.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }

        private static void CopyFolderTree(string source, string destination, string relative, List<string> files, ref long bytes, CancellationToken token, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 64 || files.Count > 25000) throw new IOException("工程目录层级或文件数量超过边界。");
            PlcEngineering.FullLocalPath(source);
            if (destination != null) Directory.CreateDirectory(destination);
            files.Add("DIR  " + relative);
            foreach (string file in Directory.EnumerateFiles(source))
            {
                token.ThrowIfCancellationRequested();
                if (files.Count >= 25000) throw new IOException("工程目录超过 25000 项。");
                PlcEngineering.FullLocalPath(file);
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = destination == null ? null : new FileStream(Path.Combine(destination, Path.GetFileName(file)), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var sha = SHA256.Create())
                {
                    var buffer = new byte[65536];
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if ((bytes += count) > 2L * 1024 * 1024 * 1024) throw new IOException("工程体积超过 2 GB。");
                        sha.TransformBlock(buffer, 0, count, buffer, 0);
                        if (output != null) output.Write(buffer, 0, count);
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    files.Add(BitConverter.ToString(sha.Hash).Replace("-", "") + "  " + Path.Combine(relative, Path.GetFileName(file)));
                }
            }
            foreach (string folder in Directory.EnumerateDirectories(source))
            {
                var leaf = Path.GetFileName(folder);
                CopyFolderTree(folder, destination == null ? null : Path.Combine(destination, leaf), Path.Combine(relative, leaf), files, ref bytes, token, depth + 1);
            }
        }

        private static bool Inside(string root, string candidate)
        {
            return string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static void VerifyNewScaleBlock(PlcSnapshot before, PlcSnapshot after, string cpu, string block)
        {
            VerifyNewBlock(before, after, cpu, block, "FB");
        }

        public static PlcObject VerifyNewBlock(PlcSnapshot before, PlcSnapshot after, string cpu, string block, string kind)
        {
            var existing = before.Objects.ToLookup(x => x.Identity, StringComparer.OrdinalIgnoreCase);
            var updated = after.Objects.ToLookup(x => x.Identity, StringComparer.OrdinalIgnoreCase);
            var added = updated.Where(g => !existing.Contains(g.Key)).SelectMany(g => g).ToList();
            if (added.Count != 1 || (kind != "FC" && kind != "FB") || added[0].Kind != kind || added[0].Name != block ||
                added[0].Scope != cpu + "/Blocks" || string.IsNullOrWhiteSpace(added[0].Interface))
                throw new InvalidOperationException("复读未确认目标 PLC 的已批准 SCL 块接口，停止保存。");
            foreach (var group in existing)
            {
                var previous = group.Select(x => x.Fingerprint).OrderBy(x => x, StringComparer.Ordinal);
                var current = updated[group.Key].Select(x => x.Fingerprint).OrderBy(x => x, StringComparer.Ordinal);
                if (!previous.SequenceEqual(current, StringComparer.Ordinal))
                    throw new InvalidOperationException("复读发现审批以外的对象变化，停止保存：" + group.Key);
            }
            return added[0];
        }

        private sealed class V20Session : IDisposable
        {
            private readonly Assembly api;
            private readonly string apiDir;
            private IDisposable portal;
            private object project;
            private readonly bool ownsProject;
            private int exportSize;
            private const string Ns = "Siemens.Engineering.";
            private const int MaxObjects = 10000;

            public V20Session(string path, bool dedicated)
            {
                apiDir = Path.GetDirectoryName(path);
                ownsProject = dedicated;
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                try
                {
                    api = Assembly.LoadFrom(path);
                    if (dedicated)
                    {
                        var mode = Type(Ns + "TiaPortalMode");
                        portal = (IDisposable)Activator.CreateInstance(Type(Ns + "TiaPortal"), Enum.Parse(mode, "WithoutUserInterface"));
                    }
                }
                catch { AppDomain.CurrentDomain.AssemblyResolve -= Resolve; throw; }
            }

            private Assembly Resolve(object sender, ResolveEventArgs args)
            {
                var name = new AssemblyName(args.Name);
                if (!name.Name.StartsWith("Siemens.", StringComparison.Ordinal) || Path.GetFileName(name.Name) != name.Name) return null;
                var path = Path.Combine(apiDir, name.Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(PlcEngineering.FullLocalPath(path)) : null;
            }

            private Type Type(string name) { return api.GetType(name, true); }
            private static object Prop(object target, string name, bool required = true)
            {
                var p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p == null && required) throw new MissingMemberException(target.GetType().FullName, name);
                return p == null ? null : p.GetValue(target, null);
            }
            private static string Text(object target, string name) { return Convert.ToString(Prop(target, name, false)) ?? ""; }
            private static IEnumerable<object> Items(object value) { return value == null ? Enumerable.Empty<object>() : ((IEnumerable)value).Cast<object>(); }
            private static object Call(object target, string name, params object[] args)
            {
                return target.GetType().InvokeMember(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.InvokeMethod, null, target, args);
            }
            private object Service(object target, string name)
            {
                var provider = Type(Ns + "IEngineeringServiceProvider");
                if (!provider.IsInstanceOfType(target)) return null;
                return provider.GetMethods().Single(m => m.Name == "GetService" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0).MakeGenericMethod(Type(Ns + name)).Invoke(target, null);
            }

            private static string ProjectPath(object process)
            {
                var path = Prop(process, "ProjectPath") as FileInfo;
                return path == null ? "" : path.FullName;
            }

            private object CallPortal(string name, Type[] parameterTypes, params object[] args)
            {
                var type = Type(Ns + "TiaPortal");
                var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null);
                if (method == null) throw new MissingMethodException(type.FullName, name);
                return method.Invoke(null, args);
            }

            private void Discover(TiaResult result)
            {
                result.OpennessStage = "枚举正在运行的 TIA 实例";
                var processes = CallPortal("GetProcesses", System.Type.EmptyTypes);
                if (processes == null) throw new InvalidOperationException("V20 Openness 的 GetProcesses 返回空结果。");

                result.OpennessStage = "读取打开的工程路径";
                var unavailable = new List<string>();
                bool readProcess = false;
                foreach (var process in Items(processes))
                {
                    if (process == null)
                    {
                        unavailable.Add("V20 Openness 返回了空的实例条目。");
                        continue;
                    }
                    result.CompatibleProcesses++;
                    int pid = 0;
                    try
                    {
                        pid = Convert.ToInt32(Prop(process, "Id", false) ?? Prop(process, "ID"));
                        if (pid <= 0) throw new InvalidDataException("TIA 实例未提供有效 PID。");
                        var path = ProjectPath(process);
                        if (string.Equals(Path.GetExtension(path), ".ap20", StringComparison.OrdinalIgnoreCase))
                            result.OpenProjects.Add(new TiaOpenProject { ProcessId = pid, Path = path, Name = Path.GetFileNameWithoutExtension(path) });
                        readProcess = true;
                    }
                    catch (TargetInvocationException error)
                    {
                        unavailable.Add((pid == 0 ? "TIA 实例" : "PID " + pid) + " · " + (error.InnerException ?? error).Message);
                    }
                    catch (InvalidDataException error)
                    {
                        unavailable.Add(error.Message);
                    }
                }
                result.Report = "V20 Openness 枚举实例 " + result.CompatibleProcesses + " · 已打开的独立 .ap20 主工程 " + result.OpenProjects.Count;
                if (unavailable.Count != 0) result.Report += Environment.NewLine + string.Join(Environment.NewLine, unavailable);
                if (unavailable.Count != 0 && !readProcess)
                    result.Error = "无法读取已发现的 TIA 实例。请检查实例是否仍打开及访问权限。";
            }

            private void ReadOpen(TiaJob job, string directory, TiaResult result)
            {
                result.OpennessStage = "核对所选 TIA 实例和工程路径";
                var process = CallPortal("GetProcess", new[] { typeof(int) }, job.PortalProcessId);
                var expected = PlcEngineering.FullLocalPath(job.ProjectFile);
                if (process == null || !string.Equals(ProjectPath(process), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("所选 TIA 工程已关闭或切换。请重新发现工程。");

                result.OpennessStage = "附加到所选 TIA 实例";
                portal = Call(process, "Attach") as IDisposable;
                if (portal == null) throw new InvalidOperationException("V20 Openness 没有返回可用的附加会话。");
                var attachedProcess = Call(portal, "GetCurrentProcess");
                if (attachedProcess == null || !string.Equals(ProjectPath(attachedProcess), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("附加后的工程与所选路径不一致。");

                result.OpennessStage = "读取打开工程的元数据";
                project = Items(Prop(portal, "Projects")).SingleOrDefault(x => x != null && Convert.ToBoolean(Prop(x, "IsPrimary")));
                if (project == null) throw new InvalidOperationException("TIA 实例没有可读取的主工程。");
                var cpus = new Dictionary<string, object>(StringComparer.Ordinal);
                var snapshot = Read(directory, cpus, false);
                var currentProcess = Call(portal, "GetCurrentProcess");
                if (currentProcess == null || !string.Equals(ProjectPath(currentProcess), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("工程在读取期间关闭或切换。请重新发现工程。");
                snapshot.Origin = "TIA V20 Openness · 已打开工程 · 只读";
                snapshot.Notes.Clear();
                if (snapshot.Objects.Count == 0) snapshot.Notes.Add("该工程未枚举到设备或子网。");
                snapshot.Notes.Add("已读取设备、程序块和 PLC 变量元数据。关闭源工程后可读取有指纹的独立副本并申请离线写入。");
                result.OpenedReadOnly = true;
                result.Snapshot = snapshot;
                result.Cpus = cpus.Keys.OrderBy(x => x).ToList();
                result.Report = expected + Environment.NewLine + "PID " + job.PortalProcessId + Environment.NewLine + snapshot.Report();
            }

            public void Execute(TiaJob job, string file, string directory, TiaResult result)
            {
                if (job.Operation == "discover") { Discover(result); return; }
                if (job.Operation == "read-open") { ReadOpen(job, directory, result); return; }
                if (job.Operation == "catalog")
                {
                    var entries = Items(Call(Prop(portal, "HardwareCatalog"), "Find", job.CatalogQuery)).Take(201).ToList();
                    result.Report = "V20 本机硬件目录 · " + job.CatalogQuery + Environment.NewLine + string.Join(Environment.NewLine, entries.Take(200).Select(x => Text(x, "ArticleNumber") + " · " + Text(x, "TypeIdentifier") + " · " + Text(x, "CatalogPath")));
                    if (entries.Count == 0) result.Report += "未找到型号。检查已安装 HSP、GSD/GSDML 和固件。";
                    if (entries.Count > 200) result.Report += Environment.NewLine + "首 200 条，请缩小搜索。";
                    return;
                }
                var projects = Prop(portal, "Projects");
                bool reading = job.Operation == "inspect";
                if (reading && !job.ReadBlockContent)
                {
                    project = Call(projects, "Open", new FileInfo(file), null, Enum.Parse(Type(Ns + "ProjectOpenMode"), "Secondary"));
                    if (Convert.ToBoolean(Prop(project, "IsPrimary"))) throw new InvalidOperationException("TIA 未以只读模式打开此副本。");
                    result.OpenedReadOnly = true;
                }
                else project = Call(projects, "Open", new FileInfo(file));
                var cpus = new Dictionary<string, object>(StringComparer.Ordinal);
                var before = Read(directory, cpus, !result.OpenedReadOnly);
                result.Snapshot = before;
                result.Cpus = cpus.Keys.OrderBy(x => x).ToList();
                if (reading) { result.Report = before.Report(); return; }
                object cpu;
                if (!cpus.TryGetValue(job.Cpu, out cpu)) throw new InvalidOperationException("已批准的 PLC 在当前副本中不存在：" + job.Cpu);
                var compile = Service(cpu, "Compiler.ICompilable");
                if (compile == null) throw new InvalidOperationException("目标 PLC 未开放 ICompilable。");
                bool writesBlock = WritesBlock(job.Operation);
                if (writesBlock)
                {
                    if (before.Objects.Any(o => string.Equals(o.Name, job.Block, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("存在同名工程对象，无法新增该块。");
                    var approvedSource = job.Operation == "scale" ? PlcEngineering.AnalogScale(job.Block) : job.Source;
                    var source = Path.Combine(directory, job.Block + ".scl");
                    File.WriteAllText(source, approvedSource, Encoding.ASCII);
                    File.WriteAllText(Path.Combine(directory, "approved-source-diff.txt"), SclEditing.Diff("", approvedSource), Utf8);
                    var sources = Prop(Prop(cpu, "ExternalSourceGroup"), "ExternalSources");
                    var checkpoint = new TiaResult { Copy = file, SourceHash = result.SourceHash, WriteStarted = true,
                        Error = "已开始离线副本写入，操作尚无完整回执。", OpennessVersion = result.OpennessVersion };
                    File.WriteAllText(Path.Combine(directory, WriteCheckpoint), Json.ToJson(checkpoint), Utf8);
                    result.WriteStarted = true;
                    var external = Call(sources, "CreateFromFile", "DeskBox-" + Path.GetFileName(directory) + ".scl", source);
                    Call(external, "GenerateBlocksFromSource");
                    Call(external, "Delete");
                }
                object compiled;
                result.CompileStartedUtc = DateTime.UtcNow.ToString("O");
                try { compiled = Call(compile, "Compile"); }
                finally { result.CompileFinishedUtc = DateTime.UtcNow.ToString("O"); }
                result.CompileState = Text(compiled, "State");
                int errors = Convert.ToInt32(Prop(compiled, "ErrorCount"));
                result.Compiled = errors == 0 && (result.CompileState == "Success" || result.CompileState == "Warning");
                var messages = new List<string> { "PLC · " + job.Cpu, "编译状态 · " + result.CompileState + " · 错误 " + errors + " · 警告 " + Text(compiled, "WarningCount") };
                AddMessages(compiled, 0, messages, result.Diagnostics);
                result.Report = string.Join(Environment.NewLine, messages);
                if (writesBlock && result.Compiled)
                {
                    exportSize = 0;
                    var after = Read(directory, new Dictionary<string, object>(StringComparer.Ordinal), true);
                    result.VerifyStartedUtc = DateTime.UtcNow.ToString("O");
                    string kind = job.Operation == "scale" ? "FB" : job.BlockKind;
                    var written = VerifyNewBlock(before, after, job.Cpu, job.Block, kind);
                    result.Report += Environment.NewLine + PlcEngineering.Diff(before, after);
                    File.WriteAllText(Path.Combine(directory, "diff.txt"), result.Report, Utf8);
                    Call(project, "Save"); result.Saved = true;
                    Call(project, "Close"); project = null;
                    project = Call(projects, "Open", new FileInfo(file));
                    exportSize = 0;
                    var reopened = Read(directory, new Dictionary<string, object>(StringComparer.Ordinal), true);
                    var persisted = VerifyNewBlock(before, reopened, job.Cpu, job.Block, kind);
                    if (persisted.Language != written.Language || persisted.Interface != written.Interface || persisted.Content != written.Content)
                        throw new InvalidDataException("重新打开副本后，目标块的语言、接口或导出正文与保存前不一致。");
                    result.Snapshot = reopened;
                    result.Verified = true;
                    result.Report += Environment.NewLine + "已重新打开保存的副本，目标块语言、接口和导出正文一致。";
                }
                if (!result.Compiled) result.Error = "TIA 离线编译未通过 · " + result.CompileState + " · 错误 " + errors;
                result.Report += Environment.NewLine + "保存副本 · " + (result.Saved ? "已保存" : "未保存修改") + "。仿真、工程师审核、现场验证未执行。";
            }

            private static void AddMessages(object node, int depth, List<string> lines, List<PlcDiagnostic> diagnostics)
            {
                if (depth > 20) return;
                foreach (var m in Items(Prop(node, "Messages", false)))
                {
                    if (lines.Count >= 1000) return;
                    var issue = new PlcDiagnostic { Severity = Text(m, "State"), Source = Text(m, "Path"), Message = Text(m, "Description") };
                    diagnostics.Add(issue);
                    lines.Add(new string(' ', depth * 2) + issue.Severity + " · " + issue);
                    AddMessages(m, depth + 1, lines, diagnostics);
                }
            }

            private PlcSnapshot Read(string directory, Dictionary<string, object> cpus, bool includeSource)
            {
                var snapshot = new PlcSnapshot { Name = Text(project, "Name"), Origin = includeSource ? "TIA V20 Openness · 独立副本" : "TIA V20 Openness · 只读副本" };
                if (!includeSource) snapshot.Notes.Add("只读读取设备、程序块和变量元数据。需要源代码时可授权从独立副本读取块正文。");
                ReadDevices(project, "项目", includeSource ? directory : null, snapshot, cpus, 0);
                foreach (var subnet in Items(Prop(project, "Subnets", false))) Add(snapshot, new PlcObject { Name = Text(subnet, "Name"), Kind = "Subnet", Scope = "项目", Source = "Openness/Subnets", Content = Text(subnet, "TypeIdentifier") });
                if (snapshot.Objects.Count == 0) snapshot.Notes.Add("该副本未枚举到设备或子网。");
                return snapshot;
            }

            private void ReadDevices(object owner, string scope, string dir, PlcSnapshot snapshot, Dictionary<string, object> cpus, int depth)
            {
                RequireDepth(depth);
                foreach (var device in Items(Prop(owner, "Devices"))) ReadDevice(device, scope, dir, snapshot, cpus, depth + 1, "");
                foreach (var group in Items(Prop(owner, depth == 0 ? "DeviceGroups" : "Groups", false)))
                    ReadDevices(group, scope + "/" + Text(group, "Name"), dir, snapshot, cpus, depth + 1);
                if (depth == 0)
                {
                    var folder = Prop(owner, "UngroupedDevicesGroup", false);
                    if (folder != null) ReadDevices(folder, scope + "/未分组", dir, snapshot, cpus, depth + 1);
                }
            }

            private void ReadDevice(object device, string scope, string dir, PlcSnapshot snapshot, Dictionary<string, object> cpus, int depth, string parentId)
            {
                RequireDepth(depth);
                var name = Text(device, "Name");
                var id = scope + "/" + name;
                bool top = parentId.Length == 0;
                var hardware = new PlcObject
                {
                    Name = name, Kind = top ? "Device" : "DeviceItem", Scope = scope, Source = "Openness/" + id,
                    AssetId = "v20:" + snapshot.Objects.Count, ParentAssetId = parentId,
                    HardwareType = Text(device, "TypeIdentifier"), Slot = top ? "" : Text(device, "PositionNumber")
                };
                AmlTopology.ApplyTypeIdentifier(hardware);
                hardware.Content = hardware.HardwareType + (hardware.Slot.Length == 0 ? "" : "\n槽位 " + hardware.Slot);
                Add(snapshot, hardware);
                if (!top)
                {
                    var container = Service(device, "HW.Features.SoftwareContainer");
                    var software = container == null ? null : Prop(container, "Software");
                    if (software != null && Type(Ns + "SW.PlcSoftware").IsInstanceOfType(software))
                    {
                        cpus.Add(id, software);
                        ReadPlcGroup(Prop(software, "BlockGroup"), id + "/Blocks", id, "Blocks", dir, snapshot, 0);
                        ReadPlcGroup(Prop(software, "TypeGroup"), id + "/Types", id, "Types", dir, snapshot, 0);
                        ReadPlcGroup(Prop(software, "TagTableGroup"), id + "/Tags", id, "TagTables", dir, snapshot, 0);
                    }
                }
                foreach (var child in Items(Prop(device, "DeviceItems"))) ReadDevice(child, id, dir, snapshot, cpus, depth + 1, hardware.AssetId);
            }

            private void ReadPlcGroup(object group, string scope, string controller, string composition, string dir, PlcSnapshot snapshot, int depth)
            {
                RequireDepth(depth);
                foreach (var item in Items(Prop(group, composition)))
                {
                    var name = Text(item, "Name");
                    if (composition == "TagTables")
                    {
                        Add(snapshot, new PlcObject { Name = name, Kind = "PlcTagTable", Scope = scope, Controller = controller, Source = "Openness/" + scope });
                        foreach (var tag in Items(Prop(item, "Tags"))) Add(snapshot, new PlcObject { Name = Text(tag, "Name"), Kind = "PlcTag", Scope = scope + "/" + name, Controller = controller, Source = "Openness/" + scope + "/" + name, Address = Text(tag, "LogicalAddress"), DataType = Text(tag, "DataTypeName") });
                    }
                    else
                    {
                        var summary = new PlcObject { Kind = item.GetType().Name, Name = name, Scope = scope, Controller = controller, Source = "Openness/" + scope + "/" + name, Language = Text(item, "ProgrammingLanguage"), Content = "块号 " + Text(item, "Number") };
                        if (dir != null) ExportBlock(item, scope, dir, snapshot, summary);
                        Add(snapshot, summary);
                    }
                }
                foreach (var sub in Items(Prop(group, "Groups", false))) ReadPlcGroup(sub, scope + "/" + Text(sub, "Name"), controller, composition, dir, snapshot, depth + 1);
            }

            private void ExportBlock(object item, string scope, string dir, PlcSnapshot snapshot, PlcObject summary)
            {
                if (exportSize >= 20 * 1024 * 1024) { summary.Content += " · 正文导出达到上限"; return; }
                string export = Path.Combine(dir, "exports"); Directory.CreateDirectory(export);
                string path = Path.Combine(export, Guid.NewGuid().ToString("N") + ".xml");
                try
                {
                    Call(item, "Export", new FileInfo(path), Enum.Parse(Type(Ns + "ExportOptions"), "None"));
                    var length = new FileInfo(path).Length;
                    if (length > 4 * 1024 * 1024) { File.Delete(path); summary.Content += " · 单块正文超过上限"; return; }
                    exportSize += (int)length;
                    var exported = PlcEngineering.ReadExports(new[] { path }, CancellationToken.None).Objects.FirstOrDefault(x => x.Name == summary.Name);
                    if (exported == null) return;
                    summary.Kind = exported.Kind; summary.Interface = exported.Interface; summary.Content = exported.Content;
                    summary.References = exported.References; summary.Fingerprint = exported.Fingerprint;
                    summary.Calls = exported.Calls;
                    summary.Source += " · exports/" + exported.Source;
                }
                catch (Exception e) { summary.Content += " · 正文未读取：" + (e.InnerException ?? e).Message; }
            }

            private static void RequireDepth(int depth) { if (depth > 64) throw new InvalidDataException("Openness 结构深度超出检查范围。"); }
            private static void Add(PlcSnapshot snapshot, PlcObject obj)
            {
                if (snapshot.Objects.Count >= MaxObjects) throw new InvalidDataException("Openness 读取上限为 " + MaxObjects + " 个对象。");
                if (obj.Fingerprint.Length == 0) obj.Fingerprint = PlcEngineering.Hash(obj.Content + "\n" + obj.Language + "\n" + obj.Address + "\n" + obj.DataType);
                snapshot.Objects.Add(obj);
            }

            public void Dispose()
            {
                try { if (ownsProject && project != null) Call(project, "Close"); }
                finally { try { if (portal != null) portal.Dispose(); } finally { AppDomain.CurrentDomain.AssemblyResolve -= Resolve; } }
            }
        }
    }
}
