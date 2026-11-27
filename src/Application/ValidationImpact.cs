using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using DeskBox.Core;
using DeskBox.Domain;
using DeskBox.Engineering;
using DeskBox.Engineering.Tia;
using DeskBox.Integration;

namespace DeskBox.AppServices
{
    public static partial class ValidationTrace
    {
        public static ValidationBaseline CaptureBaseline(ValidationService service, ProjectContext project, string title,
            string actor, CancellationToken token)
        {
            DemandInput(service, project, title, actor);
            return service.Transact(project, token, snapshot =>
            {
                var graph = new Graph(project, snapshot, service, token, true);
                if (graph.RecoveredTags) throw new InvalidDataException("Tag 台账从备份读取。请先恢复台账再建立基线。");
                var records = snapshot.Records.Where(record => !(record is ValidationBaseline))
                    .Concat(snapshot.All<TestCase>().SelectMany(test => test.Steps))
                    .GroupBy(record => record.Id, StringComparer.Ordinal).Select(group => group.First())
                    .OrderBy(record => record.GetType().Name, StringComparer.Ordinal)
                    .ThenBy(record => record.Id, StringComparer.Ordinal).ToList();
                var baseline = new ValidationBaseline
                {
                    Title = title.Trim(), Status = "captured", Source = new ValidationSource { Type = "snapshot", Version = snapshot.Stamp },
                    CreatedBy = actor.Trim(), UpdatedBy = actor.Trim(),
                    StateStamp = snapshot.Stamp, InvalidLinkIds = graph.InvalidLinkIds,
                    Records = records.Select(record => { token.ThrowIfCancellationRequested(); return Pin(record, graph.Decision(record)); }).ToList(),
                    TagRevisions = graph.Tags.Values.OrderBy(tag => tag.TagId, StringComparer.Ordinal)
                        .Select(tag => { token.ThrowIfCancellationRequested(); return TagPin(tag); }).ToList()
                };
                ValidationService.Put(snapshot, baseline, 0);
                return baseline;
            });
        }

        public static List<ValidationBaselineDifference> CompareBaselines(ProjectContext project, ValidationBaseline before,
            ValidationBaseline after, CancellationToken token)
        {
            RequireOwned(project, before);
            RequireOwned(project, after);
            var first = before.Records.Concat(before.TagRevisions).ToDictionary(PinKey, StringComparer.Ordinal);
            var second = after.Records.Concat(after.TagRevisions).ToDictionary(PinKey, StringComparer.Ordinal);
            var differences = new List<ValidationBaselineDifference>();
            foreach (string id in first.Keys.Union(second.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                ValidationBaselineEntry left, right;
                first.TryGetValue(id, out left);
                second.TryGetValue(id, out right);
                if (left != null && right != null && Json.ToJson(left) == Json.ToJson(right)) continue;
                var key = left ?? right;
                differences.Add(new ValidationBaselineDifference { Id = key.Id, Kind = key.Kind, Difference = left == null ? "added" :
                    right == null ? "removed" : "modified", Before = left, After = right });
            }
            return differences;
        }

        public static void ExportBaseline(ProjectContext project, ValidationBaseline baseline, string destination, CancellationToken token)
        {
            RequireOwned(project, baseline);
            if (!string.Equals(Path.GetExtension(destination), ".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("验证基线导出需要 .json 路径。");
            EngineeringFiles.Export(project, destination, stream =>
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 32768, true)) writer.Write(Json.ToJson(baseline));
            }, token);
        }

        public static ChangeImpact RecordImpact(ValidationService service, ProjectContext project, EngineeringDiff diff,
            string title, string actor, CancellationToken token)
        {
            DemandInput(service, project, title, actor);
            VerifyDiff(project, diff, token);
            return service.Transact(project, token, snapshot =>
            {
                var graph = new Graph(project, snapshot, service, token, true);
                if (graph.RecoveredTags) throw new InvalidDataException("Tag 台账从备份读取。请先恢复台账再建立影响记录。");
                var impact = NewImpact(diff, title, actor);
                MatchTags(graph, impact);
                RecordAffected(graph, impact, true);
                ValidationService.Put(snapshot, impact, 0);
                return impact;
            });
        }

        public static ChangeImpact RecordTagImpact(ValidationService service, ProjectContext project, string tagId,
            int beforeRevision, int afterRevision, string title, string actor, CancellationToken token)
        {
            DemandInput(service, project, title, actor);
            var versions = service.Tags.History(project, tagId, token);
            var before = versions.SingleOrDefault(tag => tag.Revision == beforeRevision);
            var after = versions.SingleOrDefault(tag => tag.Revision == afterRevision);
            if (before == null || after == null || beforeRevision >= afterRevision)
                throw new ArgumentException("请选择同一 Tag 已保存的两个版本，较早版本在前。");
            var diff = service.Tags.CompareRevisions(project, tagId, beforeRevision, afterRevision, token);
            return service.Transact(project, token, snapshot =>
            {
                var graph = new Graph(project, snapshot, service, token, true);
                if (graph.RecoveredTags) throw new InvalidDataException("Tag 台账从备份读取。请先恢复台账再建立影响记录。");
                var impact = NewImpact(diff, title, actor);
                impact.Source = new ValidationSource { Type = "tag-revision", Location = tagId,
                    Version = after.Revision.ToString(CultureInfo.InvariantCulture), Sha256 = PlcEngineering.Hash(Json.ToJson(after)) };
                impact.TagIds.Add(tagId);
                impact.Matches.Add(new ValidationImpactMatch { ChangeKey = tagId, ChangeState = diff.Modified == 0 ? "unchanged" : "modified",
                    TagId = tagId, Side = "tag-ledger", SourceRevisionBefore = before.Revision,
                    SourceRevisionAfter = after.Revision, Document = before.SourceDocument,
                    Sha256 = before.SourceSha256, Location = before.SourceLocation });
                impact.BeforeSnapshotSha256 = PlcEngineering.Hash(Json.ToJson(before));
                impact.AfterSnapshotSha256 = impact.Source.Sha256;
                RecordAffected(graph, impact, false);
                ValidationService.Put(snapshot, impact, 0);
                return impact;
            });
        }

        private static void VerifyDiff(ProjectContext project, EngineeringDiff diff, CancellationToken token)
        {
            if (diff == null || diff.ProjectId != project.Id)
                throw new UnauthorizedAccessException("工程差异与当前工程不一致。");
            if (diff.Changes == null || diff.Issues == null || diff.Issues.Any(issue => issue == null || issue.Blocking) ||
                diff.Changes.Any(item => item == null) || diff.Changes.Count > 10000)
                throw new InvalidDataException("工程差异含阻塞错误或明细超限。");
            VerifySource(project, diff.Before, token);
            VerifySource(project, diff.After, token);
        }

        private static void VerifySource(ProjectContext project, EngineeringSource source, CancellationToken token)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.Document) || string.IsNullOrWhiteSpace(source.Sha256))
                throw new InvalidDataException("工程差异缺少文件标识或原始哈希。");
            EngineeringSource checkedSource;
            using (EngineeringFiles.Open(project, Path.Combine(project.Root, source.Document), source.Type, source.Version,
                128 * 1024 * 1024, token, out checkedSource))
                if (!Is(checkedSource.Sha256, source.Sha256)) throw new IOException("工程文件在比较后发生变化，请重新比较。");
        }

        private static ChangeImpact NewImpact(EngineeringDiff diff, string title, string actor)
        {
            return new ChangeImpact
            {
                Title = title.Trim(), Status = "pending-assessment",
                CreatedBy = actor.Trim(), UpdatedBy = actor.Trim(), Before = diff.Before, After = diff.After,
                Changes = diff.Changes.ToList(), Issues = diff.Issues.ToList(),
                Added = diff.Added, Removed = diff.Removed, Modified = diff.Modified,
                Unclassified = diff.Unclassified, Unavailable = diff.Unavailable,
                Source = new ValidationSource { Type = diff.After.Type, Document = diff.After.Document,
                    Version = diff.After.Version, Sha256 = diff.After.Sha256 }
            };
        }

        private static void MatchTags(Graph graph, ChangeImpact impact)
        {
            var before = IndexLocations(graph, impact.Before);
            var after = IndexLocations(graph, impact.After);
            foreach (var change in impact.Changes.GroupBy(row => row.Key, StringComparer.Ordinal).Select(group => group.First()))
            {
                graph.Token.ThrowIfCancellationRequested();
                var oldTags = FindTags(before, change.BeforeLocation);
                var newTags = FindTags(after, change.AfterLocation);
                var combined = oldTags.Union(newTags, StringComparer.Ordinal).ToList();
                if (combined.Count != 1)
                {
                    impact.UnresolvedChangeKeys.Add(change.Key);
                    continue;
                }

                string id = combined[0];
                if (oldTags.Count == 1) impact.Matches.Add(MatchFor(change, impact.Before, id, "before", change.BeforeLocation));
                if (newTags.Count == 1) impact.Matches.Add(MatchFor(change, impact.After, id, "after", change.AfterLocation));
                impact.TagIds.Add(id);
            }
            impact.TagIds = Ids(impact.TagIds);
            impact.UnresolvedChangeKeys = Ids(impact.UnresolvedChangeKeys);
        }

        private static Dictionary<string, List<string>> IndexLocations(Graph graph, EngineeringSource source)
        {
            var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var tag in graph.Tags.Values)
            {
                graph.Token.ThrowIfCancellationRequested();
                var existing = tag.Sources ?? new List<TagSourceLink>();
                var matching = existing.Where(link => ExactSource(link.SourceDocument, link.Sha256, source))
                    .Select(link => link.SourceLocation);
                if (ExactSource(tag.SourceDocument, tag.SourceSha256, source)) matching = matching.Concat(new[] { tag.SourceLocation });
                foreach (var point in matching.Select(NormalizeLocation).Where(location => location.Length != 0).Distinct(StringComparer.Ordinal))
                {
                    List<string> ids;
                    if (!index.TryGetValue(point, out ids)) index.Add(point, ids = new List<string>());
                    ids.Add(tag.TagId);
                }
            }
            return index;
        }

        private static readonly Regex SourceLine = new Regex(@"^L[0-9]+$", RegexOptions.CultureInvariant);

        private static string NormalizeLocation(string position)
        {
            string source = (position ?? "").Trim();
            if (source.Length == 0) return "";
            // Both alpha.3 APIs share worksheet/line positions. For FHX compare/import the full column/span must agree.
            return SourceLine.IsMatch(source) ? source.ToUpperInvariant() : source;
        }

        private static List<string> FindTags(Dictionary<string, List<string>> index, string location)
        {
            List<string> tags;
            return index.TryGetValue(NormalizeLocation(location), out tags) ? tags : new List<string>();
        }

        private static bool ExactSource(string document, string hash, EngineeringSource source)
        {
            if (source == null || string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(document) ||
                string.IsNullOrWhiteSpace(source.Document) || !Is(hash, source.Sha256)) return false;
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(document.Replace('\\', '/'), source.Document.Replace('\\', '/'), comparison);
        }

        private static ValidationImpactMatch MatchFor(EngineeringDelta change, EngineeringSource source, string id, string side, string location)
        {
            return new ValidationImpactMatch { TagId = id, ChangeKey = change.Key, ChangeState = change.State,
                Side = side, Document = source.Document, Sha256 = source.Sha256, Location = location };
        }

        private static void RecordAffected(Graph graph, ChangeImpact impact, bool matchFiles)
        {
            var seeds = new HashSet<string>(impact.TagIds.SelectMany(id => graph.Incoming[id].Where(link => Is(link.Kind, "tag"))
                .Select(link => link.FromId)), StringComparer.Ordinal);

            if (matchFiles && impact.Added + impact.Removed + impact.Modified > 0)
            {
                foreach (var record in graph.Records.Values.Where(item => !(item is TraceLink) && !(item is ValidationBaseline) &&
                    !(item is ChangeImpact) && !(item is TestExecution) && !(item is ReviewRecord)))
                {
                    graph.Token.ThrowIfCancellationRequested();
                    var source = record.Source;
                    if (source == null || !ExactSource(source.Document, source.Sha256, impact.Before) &&
                        !ExactSource(source.Document, source.Sha256, impact.After)) continue;
                    seeds.Add(record.Id);
                    if (record is ValidationDocument)
                    {
                        seeds.UnionWith(graph.Snapshot.All<ValidationRequirement>().Where(req => req.DocumentId == record.Id).Select(req => req.Id));
                        seeds.UnionWith(graph.Snapshot.All<DesignReference>().Where(design => design.DocumentId == record.Id).Select(design => design.Id));
                        seeds.UnionWith(graph.Snapshot.All<TestProtocol>().Where(protocol => protocol.DocumentId == record.Id).Select(protocol => protocol.Id));
                    }
                }
            }
            var affected = new HashSet<string>(StringComparer.Ordinal);
            foreach (string seed in seeds)
            {
                if (!graph.Records.ContainsKey(seed)) continue;
                graph.Token.ThrowIfCancellationRequested();
                affected.Add(seed);
                affected.UnionWith(graph.RequirementAncestors(seed).Select(record => record.Id));
                affected.UnionWith(graph.CoveragePath(seed).Select(record => record.Id));
                affected.UnionWith(graph.Neighbors<ValidationRecord>(seed, "document").Select(record => record.Id));
                var protocol = graph.Records[seed] as TestProtocol;
                if (protocol != null) affected.UnionWith(graph.Snapshot.All<TestCase>().Where(test => test.ProtocolId == protocol.Id).Select(test => test.Id));
                var evidence = graph.Records[seed] as Evidence;
                if (evidence != null)
                {
                    if (graph.Records.ContainsKey(evidence.TestCaseId)) affected.Add(evidence.TestCaseId);
                    if (graph.Records.ContainsKey(evidence.ExecutionId)) affected.Add(evidence.ExecutionId);
                }
            }
            foreach (var record in affected.Select(id => graph.Records[id]).ToList())
            {
                var test = record as TestCase;
                if (test != null)
                {
                    graph.Token.ThrowIfCancellationRequested();
                    affected.UnionWith(graph.RequirementAncestors(test.Id).Select(req => req.Id));
                    affected.UnionWith(graph.Executions[test.Id].Select(execution => execution.Id));
                    affected.UnionWith(graph.Snapshot.All<Evidence>().Where(evidence => evidence.TestCaseId == test.Id).Select(evidence => evidence.Id));
                    affected.UnionWith(graph.Snapshot.All<Deviation>().Where(deviation => deviation.TestCaseId == test.Id).Select(deviation => deviation.Id));
                }
            }
            foreach (var execution in affected.Select(id => graph.Records[id]).OfType<TestExecution>().ToList())
            {
                graph.Token.ThrowIfCancellationRequested();
                affected.UnionWith(graph.EvidenceByExecution[execution.Id].Select(evidence => evidence.Id));
                affected.UnionWith(graph.DeviationsByExecution[execution.Id].Select(deviation => deviation.Id));
            }
            foreach (var id in Ids(affected))
            {
                var record = graph.Records[id];
                impact.Affected.Add(Pin(record, graph.Decision(record)));
            }
        }

        private static ValidationBaselineEntry Pin(ValidationRecord record, string review)
        {
            var link = record as TraceLink;
            return new ValidationBaselineEntry { Id = record.Id, Kind = record.GetType().Name, Number = record.Number, Status = record.Status,
                Revision = record.Revision, Source = record.Source, ReviewStatus = review,
                FromId = link == null ? "" : link.FromId, ToId = link == null ? "" : link.ToId, LinkKind = link == null ? "" : link.Kind };
        }

        private static ValidationBaselineEntry TagPin(EngineeringTag tag)
        {
            return new ValidationBaselineEntry
            {
                Id = tag.TagId, Kind = "Tag", Number = tag.TagName, Revision = tag.Revision,
                Source = new ValidationSource { Type = tag.SourceType, Document = tag.SourceDocument, Sha256 = tag.SourceSha256,
                    Version = tag.SourceVersion, Location = tag.SourceLocation }
            };
        }

        private static string PinKey(ValidationBaselineEntry entry) { return entry.Kind + ":" + entry.Id; }

        private static void RequireOwned(ProjectContext project, ValidationRecord record)
        {
            if (project == null || record == null || record.ProjectId != project.Id)
                throw new UnauthorizedAccessException("验证基线不属于当前工程。");
        }

        private static void DemandInput(ValidationService service, ProjectContext project, string title, string actor)
        {
            if (service == null) throw new ArgumentNullException("service");
            if (project == null) throw new UnauthorizedAccessException("请先选择工程项目。");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(actor))
                throw new ArgumentException("请填写记录标题和记录人。");
        }
    }
}
