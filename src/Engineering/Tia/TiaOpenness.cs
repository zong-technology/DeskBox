using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DeskBox.Core;

namespace DeskBox.Engineering.Tia
{
    public sealed class TiaJob
    {
        public string Operation = "inspect", ProjectFile = "", Cpu = "", Block = "", ApprovedHash = "", CatalogQuery = "", ProjectId = "", ExpectedSource = "";
    }

    public sealed class TiaResult
    {
        public string Error = "", Copy = "", Report = "", CompileState = "未运行", SourceHash = "";
        public bool Saved, Compiled;
        public PlcSnapshot Snapshot;
        public List<string> Cpus = new List<string>();
        public List<PlcDiagnostic> Diagnostics = new List<PlcDiagnostic>();
    }

    public static class TiaOpenness
    {
        private const string RequestFile = "approved-operation.json", ResponseFile = "result.json";
        private const string SnapshotFolder = "snapshot", CopyFolder = "work";
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static TiaResult Run(TiaJob job, CancellationToken token)
        {
            ValidateJob(job);
            token.ThrowIfCancellationRequested();
            RequireV20();
            var root = PlcEngineering.FullLocalPath(Path.Combine(AppPaths.DataDir, "tia-jobs"));
            string id = Guid.NewGuid().ToString("N");
            var dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(dir, RequestFile), Json.ToJson(job), Utf8);
            var resultPath = Path.Combine(dir, ResponseFile);
            try
            {
                if (job.Operation != "catalog")
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
                var executable = typeof(TiaOpenness).Assembly.Location;
                using (var process = Process.Start(new ProcessStartInfo(executable, "--tia-worker " + id + " \"" + root.TrimEnd('\\') + "\"") { UseShellExecute = false, CreateNoWindow = true }))
                {
                    var time = Stopwatch.StartNew();
                    while (!process.WaitForExit(150))
                    {
                        if (!token.IsCancellationRequested && time.Elapsed < TimeSpan.FromMinutes(5)) continue;
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        token.ThrowIfCancellationRequested();
                        throw new TimeoutException("Openness 操作超时。工程副本已保留，请检查 TIA 授权提示或此次专用实例。");
                    }
                    token.ThrowIfCancellationRequested();
                    if (!File.Exists(resultPath)) throw new IOException("Openness 工作进程未产生回执，退出码 " + process.ExitCode + "。");
                }
                var result = Json.FromJson<TiaResult>(File.ReadAllText(resultPath));
                if (result == null) throw new InvalidDataException("无法读取 Openness 回执。");
                return result;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(dir, "failed.txt"), DateTime.UtcNow.ToString("O") + "\r\n" + ex.Message, Utf8);
                throw;
            }
        }

        public static int RunWorker(string id, string dataRoot)
        {
            Guid parsed;
            if (!Guid.TryParseExact(id, "N", out parsed) || Path.GetFileName(dataRoot) != "tia-jobs") return 2;
            var directory = PlcEngineering.FullLocalPath(Path.Combine(dataRoot, id));
            var result = new TiaResult();
            try
            {
                var job = Json.Load<TiaJob>(PlcEngineering.FullLocalPath(Path.Combine(directory, RequestFile)), null);
                ValidateJob(job);
                var installation = RequireV20();
                var project = "";
                if (job.Operation != "catalog")
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
                using (var api = new V20Session(installation.AssemblyPath)) api.Execute(job, project, directory, result);
            }
            catch (Exception e)
            {
                while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
                result.Error = e.GetType().Name + " · " + e.Message;
            }
            File.WriteAllText(Path.Combine(directory, ResponseFile), Json.ToJson(result), Utf8);
            return result.Error.Length == 0 ? 0 : 1;
        }

        private static TiaInstallation RequireV20()
        {
            var inspection = TiaEnvironmentInspector.Inspect();
            var installation = inspection.Installations.FirstOrDefault(i => i.Version == 20 && i.AssemblyPath != null);
            if (installation == null) throw new InvalidOperationException("需要 TIA Portal V20 和匹配的 PublicAPI/V20/Siemens.Engineering.dll。" + Environment.NewLine + inspection.Describe());
            if (!inspection.OpennessGroup) throw new InvalidOperationException("当前 Windows 用户令牌未验证为 Siemens TIA Openness 组成员。");
            PlcEngineering.FullLocalPath(installation.AssemblyPath);
            return installation;
        }

        private static void ValidateJob(TiaJob job)
        {
            if (job == null || !new[] { "inspect", "compile", "scale", "catalog" }.Contains(job.Operation))
                throw new InvalidOperationException("仅支持离线读取、编译、缩放模板和硬件目录查询。");
            if (job.Operation == "catalog")
            {
                if (string.IsNullOrWhiteSpace(job.CatalogQuery) || job.CatalogQuery.Length > 120) throw new ArgumentException("请输入目录搜索词或订货号（1–120 字）。");
                return;
            }
            if (!string.Equals(Path.GetExtension(job.ProjectFile), ".ap20", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("此连接器只打开同版本的独立 .ap20 工程。归档、旧版和多用户项目请先在 TIA 中另存独立离线工程。");
            if ((job.Operation == "compile" || job.Operation == "scale") && (string.IsNullOrWhiteSpace(job.Cpu) || string.IsNullOrWhiteSpace(job.ExpectedSource)))
                throw new InvalidOperationException("请先读取工程，并明确选择一个 PLC 对象。");
            if (job.Operation == "scale" && PlcEngineering.Hash(PlcEngineering.AnalogScale(job.Block)) != job.ApprovedHash)
                throw new InvalidOperationException("缩放模板与已批准的源文件不匹配。");
        }

        public static TiaResult ReadRecoveryReceipt(string path)
        {
            var receipt = PlcEngineering.FullLocalPath(path);
            var directory = Path.GetDirectoryName(receipt);
            if (!string.Equals(Path.GetFileName(receipt), ResponseFile, StringComparison.OrdinalIgnoreCase) || !IsJobDirectory(directory))
                throw new InvalidOperationException("请选择当前资料目录中 TIA 作业的 result.json。");
            var requestPath = PlcEngineering.FullLocalPath(Path.Combine(directory, RequestFile));
            var storedResult = new FileInfo(receipt);
            var storedRequest = new FileInfo(requestPath);
            if (!storedResult.Exists || !storedRequest.Exists) throw new InvalidDataException("缺少作业回执或审批记录。");
            if (storedResult.Length > 64L * 1024 * 1024 || storedRequest.Length > 64 * 1024)
                throw new InvalidDataException("作业回执超过可读取大小。");
            var result = Json.Load<TiaResult>(receipt, null);
            var request = Json.Load<TiaJob>(requestPath, null);
            ValidateJob(request);
            var expectedCopy = Path.Combine(directory, CopyFolder, Path.GetFileName(request.ProjectFile));
            if (result == null || !result.Saved || !result.Compiled || result.Error != "" || request.Operation != "scale" ||
                result.SourceHash != request.ExpectedSource || !string.Equals(result.Copy, expectedCopy, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("回执必须与已审批、编译并保存的离线作业一致。");
            return result;
        }

        private static bool IsJobDirectory(string directory)
        {
            var root = PlcEngineering.FullLocalPath(Path.Combine(AppPaths.DataDir, "tia-jobs"));
            Guid id;
            return !string.IsNullOrEmpty(directory) && Guid.TryParseExact(Path.GetFileName(directory), "N", out id) &&
                string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase);
        }

        public static string RestoreSnapshot(TiaResult job, CancellationToken token)
        {
            if (job == null || !job.Saved || !job.Compiled || job.Error != "" || string.IsNullOrWhiteSpace(job.Copy) || string.IsNullOrWhiteSpace(job.SourceHash))
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
            var existing = before.Objects.ToLookup(x => x.Identity, StringComparer.OrdinalIgnoreCase);
            var updated = after.Objects.ToLookup(x => x.Identity, StringComparer.OrdinalIgnoreCase);
            var added = updated.Where(g => !existing.Contains(g.Key)).SelectMany(g => g).ToList();
            if (added.Count != 1 || added[0].Kind != "FB" || added[0].Name != block ||
                added[0].Scope != cpu + "/Blocks" || string.IsNullOrWhiteSpace(added[0].Interface))
                throw new InvalidOperationException("复读未确认目标 PLC 的已批准 FB 接口，停止保存。");
            foreach (var group in existing)
            {
                var previous = group.Select(x => x.Fingerprint).OrderBy(x => x, StringComparer.Ordinal);
                var current = updated[group.Key].Select(x => x.Fingerprint).OrderBy(x => x, StringComparer.Ordinal);
                if (!previous.SequenceEqual(current, StringComparer.Ordinal))
                    throw new InvalidOperationException("复读发现审批以外的对象变化，停止保存：" + group.Key);
            }
        }

        private sealed class V20Session : IDisposable
        {
            private readonly Assembly api;
            private readonly string apiDir;
            private IDisposable portal;
            private object project;
            private int exportSize;
            private const string Ns = "Siemens.Engineering.";
            private const int MaxObjects = 10000;

            public V20Session(string path)
            {
                apiDir = Path.GetDirectoryName(path);
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                try
                {
                    api = Assembly.LoadFrom(path);
                    var mode = Type(Ns + "TiaPortalMode");
                    portal = (IDisposable)Activator.CreateInstance(Type(Ns + "TiaPortal"), Enum.Parse(mode, "WithoutUserInterface"));
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

            public void Execute(TiaJob job, string file, string directory, TiaResult result)
            {
                if (job.Operation == "catalog")
                {
                    var entries = Items(Call(Prop(portal, "HardwareCatalog"), "Find", job.CatalogQuery)).Take(201).ToList();
                    result.Report = "V20 本机硬件目录 · " + job.CatalogQuery + Environment.NewLine + string.Join(Environment.NewLine, entries.Take(200).Select(x => Text(x, "ArticleNumber") + " · " + Text(x, "TypeIdentifier") + " · " + Text(x, "CatalogPath")));
                    if (entries.Count == 0) result.Report += "未找到型号。检查已安装 HSP、GSD/GSDML 和固件。";
                    if (entries.Count > 200) result.Report += Environment.NewLine + "首 200 条，请缩小搜索。";
                    return;
                }
                project = Call(Prop(portal, "Projects"), "Open", new FileInfo(file));
                var cpus = new Dictionary<string, object>(StringComparer.Ordinal);
                var before = Read(directory, cpus);
                result.Snapshot = before;
                result.Cpus = cpus.Keys.OrderBy(x => x).ToList();
                if (job.Operation == "inspect") { result.Report = before.Report(); return; }
                object cpu;
                if (!cpus.TryGetValue(job.Cpu, out cpu)) throw new InvalidOperationException("已批准的 PLC 在当前副本中不存在：" + job.Cpu);
                var compile = Service(cpu, "Compiler.ICompilable");
                if (compile == null) throw new InvalidOperationException("目标 PLC 未开放 ICompilable。");
                if (job.Operation == "scale")
                {
                    if (before.Objects.Any(o => string.Equals(o.Name, job.Block, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("存在同名工程对象；首版只新增块。");
                    var source = Path.Combine(directory, job.Block + ".scl");
                    File.WriteAllText(source, PlcEngineering.AnalogScale(job.Block), Encoding.ASCII);
                    var sources = Prop(Prop(cpu, "ExternalSourceGroup"), "ExternalSources");
                    var external = Call(sources, "CreateFromFile", job.Block + ".scl", source);
                    Call(external, "GenerateBlocksFromSource");
                }
                var compiled = Call(compile, "Compile");
                result.CompileState = Text(compiled, "State");
                int errors = Convert.ToInt32(Prop(compiled, "ErrorCount"));
                result.Compiled = errors == 0 && (result.CompileState == "Success" || result.CompileState == "Warning");
                var messages = new List<string> { "PLC · " + job.Cpu, "编译状态 · " + result.CompileState + " · 错误 " + errors + " · 警告 " + Text(compiled, "WarningCount") };
                AddMessages(compiled, 0, messages, result.Diagnostics);
                result.Report = string.Join(Environment.NewLine, messages);
                if (job.Operation == "scale" && result.Compiled)
                {
                    exportSize = 0;
                    var after = Read(directory, new Dictionary<string, object>(StringComparer.Ordinal));
                    VerifyNewScaleBlock(before, after, job.Cpu, job.Block);
                    result.Report += Environment.NewLine + PlcEngineering.Diff(before, after);
                    File.WriteAllText(Path.Combine(directory, "diff.txt"), result.Report, Utf8);
                    Call(project, "Save"); result.Saved = true; result.Snapshot = after;
                }
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

            private PlcSnapshot Read(string directory, Dictionary<string, object> cpus)
            {
                var snapshot = new PlcSnapshot { Name = Text(project, "Name"), Origin = "TIA V20 Openness · 独立副本" };
                ReadDevices(project, "项目", directory, snapshot, cpus, 0);
                foreach (var subnet in Items(Prop(project, "Subnets", false))) Add(snapshot, new PlcObject { Name = Text(subnet, "Name"), Kind = "Subnet", Scope = "项目", Source = "Openness/Subnets", Content = Text(subnet, "TypeIdentifier") });
                if (snapshot.Objects.Count == 0) snapshot.Notes.Add("该副本未枚举到设备或子网。");
                return snapshot;
            }

            private void ReadDevices(object owner, string scope, string dir, PlcSnapshot snapshot, Dictionary<string, object> cpus, int depth)
            {
                RequireDepth(depth);
                foreach (var device in Items(Prop(owner, "Devices"))) ReadDevice(device, scope, dir, snapshot, cpus, depth + 1, true);
                foreach (var group in Items(Prop(owner, depth == 0 ? "DeviceGroups" : "Groups", false)))
                    ReadDevices(group, scope + "/" + Text(group, "Name"), dir, snapshot, cpus, depth + 1);
                if (depth == 0)
                {
                    var folder = Prop(owner, "UngroupedDevicesGroup", false);
                    if (folder != null) ReadDevices(folder, scope + "/未分组", dir, snapshot, cpus, depth + 1);
                }
            }

            private void ReadDevice(object device, string scope, string dir, PlcSnapshot snapshot, Dictionary<string, object> cpus, int depth, bool top)
            {
                RequireDepth(depth);
                var name = Text(device, "Name");
                var id = scope + "/" + name;
                var hardware = new PlcObject
                {
                    Name = name, Kind = top ? "Device" : "DeviceItem", Scope = scope, Source = "Openness/" + id,
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
                foreach (var child in Items(Prop(device, "DeviceItems"))) ReadDevice(child, id, dir, snapshot, cpus, depth + 1, false);
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
                        ExportBlock(item, scope, dir, snapshot, summary);
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
                try { if (project != null) Call(project, "Close"); }
                finally { try { if (portal != null) portal.Dispose(); } finally { AppDomain.CurrentDomain.AssemblyResolve -= Resolve; } }
            }
        }
    }
}
