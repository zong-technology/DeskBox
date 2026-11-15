using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DeskBox.Engineering
{
    public class ValidationStep
    {
        public string Id = "", Name = "", Notes = "", Start = "", End = "";
        public double? Percent = 0;
        public double Weight = 1;
        public long UpdatedAt;
    }

    public static class ValidationProgress
    {
        public static List<ValidationStep> Create()
        {
            string[] names = { "项目启动", "URS 签批", "SIA 签批", "CA 签批", "VP 签批", "设计文件签批（FDS/DDS/软硬件）", "DQ 方案签批", "执行 DQ 测试", "DQ 报告签批", "DQ 通过？", "FAT 方案签批", "执行 FAT 测试", "FAT 报告签批", "SAT 方案签批", "执行 SAT 测试", "SAT 报告签批", "IQ 方案签批", "执行 IQ 测试", "IQ 报告签批", "OQ 方案签批", "执行 OQ 测试", "OQ 报告签批", "PQ 方案签批（可选）", "执行 PQ 测试", "PQ 报告签批", "验证总结报告 VSR 签批", "项目交付" };
            return names.Select((name, i) => new ValidationStep { Id = "v" + i, Name = name }).ToList();
        }

        public static double? Overall(EngineerWorkspace project)
        {
            if (project == null) return null;
            if (project.ValidationSteps != null && project.ValidationSteps.Count > 0)
            {
                var values = project.ValidationSteps.Where(x => x.Percent.HasValue && x.Weight > 0).ToArray();
                return values.Length == 0 ? (double?)null : values.Sum(x=>x.Percent.Value*x.Weight)/values.Sum(x=>x.Weight);
            }
            if (project.StageProgress == null || project.StageWeights == null) return 0;
            double sum = 0, weight = 0;
            foreach (var stage in project.StageProgress) { double w; if (project.StageWeights.TryGetValue(stage.Key, out w)) { sum += Math.Max(0, Math.Min(100, stage.Value)) * w; weight += w; } }
            return weight <= 0 ? 0 : sum / weight;
        }

        public static string Display(EngineerWorkspace project) { var value = Overall(project); return value.HasValue ? value.Value.ToString("0.#") + "%" : "N/A"; }

        public static void CheckDates(string start, string end)
        {
            DateTime a, b;
            if (!string.IsNullOrWhiteSpace(start) && !DateTime.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out a)) throw new ArgumentException("开始日期格式应为 yyyy-MM-dd");
            if (!string.IsNullOrWhiteSpace(end) && !DateTime.TryParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out b)) throw new ArgumentException("结束日期格式应为 yyyy-MM-dd");
            if (DateTime.TryParse(start, out a) && DateTime.TryParse(end, out b) && b < a) throw new ArgumentException("结束日期不能早于开始日期");
        }
    }
}
