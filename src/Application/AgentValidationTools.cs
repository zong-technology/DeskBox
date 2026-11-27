using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.Domain;

namespace DeskBox.AppServices
{
    internal static class AgentValidationTools
    {
        internal const string Name = "validation.query";
        internal const string Description = "validation.query {view,query?,id?,stage?,system?,equipment?,tag?,risk?,fromRevision?,toRevision?}。"
            + @"view 支持 requirements,testcases,rtm,uncovered,evidence,missing-evidence,open-deviations,history,tags,impact,deliverables,summary-draft。"
            + "引用真实记录 ID 与来源。工具只读，摘要为待审核草稿。";

        internal static ToolResult Run(ToolCall call, ToolContext context, CancellationToken token)
        {
            if (context.Project == null) return ToolResult.Fail("先选择验证项目。");
            var service = new ValidationService();
            var project = context.Project;
            var data = service.Read(project, token);
            var filter = new ValidationTraceFilter { Stage = Get(call, "stage"), SystemId = Get(call, "system"),
                EquipmentId = Get(call, "equipment"), TagId = Get(call, "tag"), RiskLevel = Get(call, "risk") };
            string query = Get(call, "query");
            string selected = Get(call, "id");
            Func<ValidationRecord, bool> match = record => (selected.Length == 0 || record.Id == selected) &&
                (filter.Stage.Length == 0 || string.Equals(record.Stage, filter.Stage, StringComparison.OrdinalIgnoreCase)) &&
                (filter.SystemId.Length == 0 || string.Equals(record.SystemId, filter.SystemId, StringComparison.OrdinalIgnoreCase)) &&
                (filter.EquipmentId.Length == 0 || string.Equals(record.EquipmentId, filter.EquipmentId, StringComparison.OrdinalIgnoreCase)) &&
                (query.Length == 0 || (record.Number + " " + record.Title + " " + record.Description).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            object result;
            switch (Get(call, "view"))
            {
                case "requirements": result = Describe(data.All<ValidationRequirement>().Where(match)); break;
                case "testcases": result = Describe(data.All<TestCase>().Where(match)); break;
                case "open-deviations": result = Describe(data.All<Deviation>().Where(item => item.Status != "closed" && item.Status != "withdrawn").Where(match)); break;
                case "impact":
                    result = data.All<ChangeImpact>().Where(item => match(item)).Take(12).Select(item => new
                    { item.Id, item.Revision, item.Status, item.Before, item.After, item.TagIds, Affected = item.Affected.Take(30) }).ToArray();
                    break;
                case "evidence":
                    result = data.All<Evidence>().Where(item => match(item)).Take(12).Select(item =>
                    {
                        token.ThrowIfCancellationRequested();
                        return new { item.Id, item.Revision, item.FileName, item.ExecutionId, item.Source,
                            Integrity = service.VerifyEvidence(project, item, token) };
                    }).ToArray();
                    break;
                case "tags":
                    result = selected.Length == 0 ? service.Tags.Query(project, query, filter.SystemId, "name", false, 0, 15, token).Items.Select(tag =>
                        new { tag.TagId, tag.TagName, tag.SystemId, tag.SourceDocument, tag.SourceLocation }).ToArray() :
                        (object)ValidationTrace.Related(project, data, selected, token, service).Where(link => link.Kind == "tag")
                        .Select(link => new { link.Id, link.FromId, link.ToId }).ToArray();
                    break;
                case "history":
                    int before, after;
                    if (!int.TryParse(Get(call, "fromRevision"), out before) || !int.TryParse(Get(call, "toRevision"), out after))
                        return ToolResult.Fail("请输入需求稳定 ID 和两个版本号。");
                    result = new { RequirementId = selected, Before = before, After = after,
                        Differences = new ValidationImport(service).CompareRequirement(project, selected, before, after, token).Take(20) };
                    break;
                case "rtm":
                case "uncovered":
                case "missing-evidence":
                    var trace = ValidationTrace.Query(project, data, filter, token, service);
                    var mode = Get(call, "view");
                    result = mode == "uncovered" ? (object)Describe(data.All<ValidationRequirement>().Where(item => trace.UncoveredRequirementIds.Contains(item.Id))) :
                        mode == "missing-evidence" ? Describe(data.All<TestExecution>().Where(item => trace.MissingEvidenceExecutionIds.Contains(item.Id))) :
                        (object)new { Total = trace.Rows.Count, Rows = trace.Rows.Take(12).Select(row => new
                        {
                            row.RequirementId, row.RequirementNumber, row.RequirementRevision, row.ObjectSource, row.TestCaseId, row.TestCaseNumber,
                            row.ExecutionId, row.ExecutionStatus, row.EvidenceIds, row.OpenDeviationIds, row.TagIds
                        }), Invalid = trace.InvalidLinkIds.Take(40) };
                    break;
                case "deliverables":
                case "summary-draft":
                    var stats = ValidationTrace.Statistics(project, data, filter, token, service);
                    result = new { Label = "待审核草稿 · 未签核", ProjectId = project.Id, Stamp = data.Stamp, stats.Items,
                        Deliverables = Describe(data.All<Deliverable>().Where(match)),
                        Requirements = Describe(data.All<ValidationRequirement>().Where(match).Take(5)),
                        Executions = Describe(data.All<TestExecution>().Where(match).Take(5)) };
                    break;
                default: return ToolResult.Fail("未知验证视图。此工具仅支持已列出的只读查询。");
            }
            token.ThrowIfCancellationRequested();
            return ToolResult.Success(Json.ToJson(new { project.Id, data.Stamp, data.Recovered, Result = result }));
        }

        private static object Describe(IEnumerable<ValidationRecord> records)
        {
            var items = records.ToList();
            return new { Total = items.Count, Items = items.Take(20).Select(item => new
            { item.Id, item.Number, item.Title, item.Stage, item.Revision, item.Status, Description = EngineeringDocuments.Clip(item.Description, 300), item.Source }) };
        }

        private static string Get(ToolCall call, string name)
        {
            string value;
            return call.Args.TryGetValue(name, out value) ? value ?? "" : "";
        }
    }
}
