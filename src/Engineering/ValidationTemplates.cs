using System;
using System.Collections.Generic;
using System.Text;

namespace DeskBox.Engineering
{
    public static class ValidationTemplates
    {
        public static readonly string[] Types = { "URS 用户需求", "DQ 设计确认", "IQ 安装确认", "OQ 运行确认", "PQ 性能确认", "CSV 风险评估", "21 CFR Part 11 检查" };

        public static string Generate(string type, string systemName, string owner)
        {
            type = string.IsNullOrWhiteSpace(type) ? Types[0] : type;
            systemName = string.IsNullOrWhiteSpace(systemName) ? "待命名系统" : systemName.Trim();
            owner = string.IsNullOrWhiteSpace(owner) ? "待指定" : owner.Trim();
            var sb = new StringBuilder();
            sb.AppendLine("# " + systemName + " — " + type);
            sb.AppendLine();
            sb.AppendLine("- 文档状态：草案");
            sb.AppendLine("- 负责人：" + owner);
            sb.AppendLine("- 生成日期：" + DateTime.Now.ToString("yyyy-MM-dd"));
            sb.AppendLine("- 适用原则：GAMP 5、数据完整性 ALCOA+、风险导向验证");
            sb.AppendLine();
            foreach (var section in Sections(type))
            {
                sb.AppendLine("## " + section.Key);
                sb.AppendLine();
                foreach (var item in section.Value) sb.AppendLine("- [ ] " + item);
                sb.AppendLine();
            }
            sb.AppendLine("## 接受标准");
            sb.AppendLine();
            sb.AppendLine("- [ ] 每个测试步骤的实际结果与预期结果一致");
            sb.AppendLine("- [ ] 关键报警、联锁、权限和数据完整性控制无未关闭的严重偏差");
            sb.AppendLine("- [ ] 所有原始记录、截图、趋势、日志或校验和可追溯到测试步骤");
            sb.AppendLine("- [ ] 偏差已评估影响、完成纠正并由授权人员批准");
            sb.AppendLine();
            sb.AppendLine("## 偏差与证据");
            sb.AppendLine();
            sb.AppendLine("| 编号 | 描述 | 风险 | 处理 | 证据路径 | 状态 |");
            sb.AppendLine("|---|---|---|---|---|---|");
            sb.AppendLine("| DEV-001 |  |  |  |  | 开放 |");
            return sb.ToString();
        }

        public static List<string> TaskTitles(string type, string systemName)
        {
            var list = new List<string>();
            foreach (var section in Sections(type))
                list.Add(systemName + " · " + section.Key);
            return list;
        }

        private static Dictionary<string, string[]> Sections(string type)
        {
            var d = new Dictionary<string, string[]>();
            if (type.StartsWith("URS"))
            {
                d["工艺与业务需求"] = new[] { "明确工艺边界、关键工艺参数 CPP 与关键质量属性 CQA", "定义操作模式、配方、批次与状态转换", "定义报警、联锁、急停和故障安全状态" };
                d["数据与合规"] = new[] { "定义用户角色、最小权限与审批职责", "定义审计追踪、电子签名、时间同步与数据保留", "定义备份、恢复、归档、导出与灾难恢复" };
                d["接口与维护"] = new[] { "列出 PLC/DCS/SCADA/仪表/上位系统接口", "定义诊断、校准、维护和变更控制需求", "定义网络分区、远程访问与补丁策略" };
            }
            else if (type.StartsWith("DQ"))
            {
                d["设计可追溯性"] = new[] { "URS 与功能/硬件/软件设计逐条追溯", "识别 GxP 关键功能与风险控制", "确认容量、性能、冗余与故障安全设计" };
                d["自动化设计审查"] = new[] { "审查 PLC/DCS 架构、网络拓扑与 I/O 清单", "审查报警哲学、联锁因果矩阵与配方策略", "审查账户、审计追踪、备份恢复与时间同步" };
            }
            else if (type.StartsWith("IQ"))
            {
                d["安装与配置"] = new[] { "核对硬件型号、序列号、固件和安装位置", "核对 PLC/DCS/SCADA 软件版本、许可证与项目校验和", "核对网络地址、交换机、端口、接地、电源和 UPS" };
                d["文档与基线"] = new[] { "归档图纸、I/O 清单、网络拓扑和设备手册", "备份程序、配方、参数、证书和安全配置", "记录校准状态与测试仪器有效期" };
            }
            else if (type.StartsWith("OQ"))
            {
                d["功能测试"] = new[] { "逐项测试操作模式、顺控、状态转换和配方", "测试报警限值、延迟、确认、抑制与历史", "测试联锁、急停、断电、通信中断和故障恢复" };
                d["数据完整性"] = new[] { "测试账户、权限、密码策略和锁定", "测试审计追踪、时间戳、电子签名和报表", "测试备份恢复、数据导出和记录保留" };
            }
            else if (type.StartsWith("PQ"))
            {
                d["性能场景"] = new[] { "使用批准工艺参数执行代表性批次", "挑战上下限、最大负载和最差条件", "确认长期趋势、重复性与关键指标" };
                d["放行准备"] = new[] { "关闭关键偏差并完成风险复评", "确认 SOP、培训、维护和校准已生效", "确认验证总结、追溯矩阵和移交资料完整" };
            }
            else if (type.StartsWith("21"))
            {
                d["访问控制"] = new[] { "唯一用户身份与禁止共享账户", "角色权限、最小授权和定期复核", "密码策略、失败锁定、会话超时和离职停用" };
                d["电子记录与签名"] = new[] { "审计追踪安全、自动、带时间戳且不可由普通用户关闭", "电子签名与记录永久关联并显示姓名/日期/含义", "记录可准确、完整、可读地导出并长期保留" };
                d["运行保障"] = new[] { "时间同步、备份恢复、灾难恢复经过测试", "系统验证、变更控制、周期回顾与事件管理到位", "供应商、基础设施和网络安全风险有控制" };
            }
            else
            {
                d["风险识别"] = new[] { "识别患者安全、产品质量和数据完整性影响", "识别自动化故障、接口故障和人为误操作", "评估严重度、发生概率与可检测性" };
                d["控制与验证"] = new[] { "将风险控制映射到设计、报警、联锁和程序", "定义控制验证方法、测试证据和接受标准", "记录残余风险、偏差和批准结论" };
            }
            return d;
        }
    }
}
