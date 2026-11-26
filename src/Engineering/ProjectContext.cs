using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeskBox.Ai;
using DeskBox.Core;

namespace DeskBox.Engineering
{
    /// <summary>Snapshot of the project boundaries at the start of a view or operation.</summary>
    public sealed class ProjectContext
    {
        private readonly string[] nestedRoots;
        private readonly string legacyTaskName;

        internal ProjectContext(EngineerWorkspace project, List<EngineerWorkspace> projects)
        {
            Id = project.Id;
            Name = project.Name;
            Customer = project.Customer;
            Equipment = project.Equipment;
            System = project.System;
            Kind = project.Kind;
            Stage = project.Stage;
            Root = project.Root;
            PlcPath = project.PlcPath;
            DcsPath = project.DcsPath;
            ValidationPath = project.ValidationPath;
            Version = project.EngineeringVersion;
            CurrentTaskId = project.CurrentTaskId;
            AllowExternalAi = project.AllowExternalAi;
            var others = projects.Where(item => item.Id != Id).ToArray();
            nestedRoots = others.Select(item => item.Root).Where(path => Under(path, Root)).ToArray();
            if (!string.IsNullOrWhiteSpace(Name) && !others.Any(item =>
                string.Equals(item.Name, Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Id, Name, StringComparison.OrdinalIgnoreCase)))
                legacyTaskName = Name;
        }

        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Customer { get; private set; }
        public string Equipment { get; private set; }
        public string System { get; private set; }
        public string Kind { get; private set; }
        public string Stage { get; private set; }
        public string Root { get; private set; }
        public string PlcPath { get; private set; }
        public string DcsPath { get; private set; }
        public string ValidationPath { get; private set; }
        public string Version { get; private set; }
        public string CurrentTaskId { get; private set; }
        public bool AllowExternalAi { get; private set; }

        public bool IncludesTask(TaskItem task)
        {
            return task != null && (task.Project == Id
                || (legacyTaskName != null && string.Equals(task.Project, legacyTaskName, StringComparison.OrdinalIgnoreCase)));
        }

        public bool IncludesPath(string path)
        {
            return Under(path, Root) && !nestedRoots.Any(root => Under(path, root));
        }

        public bool CanAccessPath(string path)
        {
            if (!IncludesPath(path)) return false;
            try
            {
                // Check each existing component first so a redirect cannot lead a caller into another project's files.
                string full = Path.GetFullPath(path);
                var volume = Path.GetPathRoot(full);
                var current = volume;
                foreach (var part in full.Substring(volume.Length).Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
                {
                    current = Path.Combine(current, part);
                    try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false; }
                    catch (FileNotFoundException) { break; }
                    catch (DirectoryNotFoundException) { break; }
                }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        public bool PermitsModel(AiProvider provider)
        {
            if (provider == null) return false;
            Uri endpoint;
            return Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out endpoint)
                && (AllowExternalAi || AiEnvironment.Loopback(endpoint));
        }

        internal bool SameScope(ProjectContext other)
        {
            return other != null && Id == other.Id && Root == other.Root && PlcPath == other.PlcPath
                && DcsPath == other.DcsPath && ValidationPath == other.ValidationPath
                && legacyTaskName == other.legacyTaskName && AllowExternalAi == other.AllowExternalAi
                && nestedRoots.OrderBy(path => path).SequenceEqual(other.nestedRoots.OrderBy(path => path));
        }

        internal string[] NestedRoots { get { return (string[])nestedRoots.Clone(); } }

        internal string TiaDataRoot { get { return Path.Combine(AppPaths.DataDir, "project-tia-" + Uri.EscapeDataString(Id)); } }

        internal static bool Under(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
            try
            {
                var trim = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
                var full = Path.GetFullPath(path).TrimEnd(trim);
                var prefix = Path.GetFullPath(root).TrimEnd(trim);
                var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return string.Equals(full, prefix, comparison) || full.StartsWith(prefix + Path.DirectorySeparatorChar, comparison);
            }
            catch (ArgumentException) { return false; }
            catch (IOException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
