using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace DeskBox.Engineering.Tia
{
    public sealed class TiaInstallation
    {
        public int Version;
        public string Root;
        public string AssemblyPath;
        public string ProjectExtension { get { return ".ap" + Version; } }
    }

    public sealed class TiaEnvironment
    {
        public bool Windows;
        public bool WebView2;
        public bool PlcSim;
        public bool OpennessGroup;
        public readonly List<string> Products = new List<string>();
        public readonly List<string> HardwareSupportPaths = new List<string>();
        public readonly List<int> RunningProcesses = new List<int>();
        public readonly List<TiaInstallation> Installations = new List<TiaInstallation>();
        public readonly List<string> Warnings = new List<string>();

        public string Describe()
        {
            if (!Windows) return "当前系统不是 Windows，无法运行 TIA Portal Openness。可读取导出文件、检查变量及生成草稿。";
            var lines = Installations.Select(i => "V" + i.Version + " · " + i.ProjectExtension + " · " + (i.AssemblyPath ?? "未发现对应 Openness 程序集")).ToList();
            if (lines.Count == 0) lines.Add("未找到支持探测的 TIA Portal V18–V20 安装目录。");
            lines.AddRange(Products);
            lines.Add("PLCSIM 安装线索：" + (PlcSim ? "已发现，运行能力待验证" : "未发现"));
            lines.Add("Openness 用户组：" + (OpennessGroup ? "当前令牌已包含" : "未确认，检查用户组及重新登录"));
            lines.Add("TIA Portal 进程：" + (RunningProcesses.Count == 0 ? "无" : string.Join(", ", RunningProcesses)));
            lines.Add("WebView2 注册：" + (WebView2 ? "已发现" : "未发现"));
            lines.AddRange(HardwareSupportPaths.Select(p => "硬件支持包目录线索：" + p));
            lines.Add("STEP 7 许可、已安装 HSP 和目录型号需在实际 TIA 环境核验。");
            lines.AddRange(Warnings);
            return string.Join(Environment.NewLine, lines);
        }
    }

    public static class TiaEnvironmentInspector
    {
        public static TiaEnvironment Inspect()
        {
            var result = new TiaEnvironment { Windows = Environment.OSVersion.Platform == PlatformID.Win32NT };
            if (!result.Windows) return result;
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    {
                        ReadProducts(machine, result);
                        ReadWebView(machine, result);
                    }
                }
                catch (Exception e) { result.Warnings.Add("安装登记读取失败（" + view + "）：" + e.Message); }
            }
            foreach (int version in new[] { 18, 19, 20 })
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Siemens", "Automation", "Portal V" + version);
                if (!Directory.Exists(root)) continue;
                var dll = Path.Combine(root, "PublicAPI", "V" + version, "Siemens.Engineering.dll");
                result.Installations.Add(new TiaInstallation { Version = version, Root = root, AssemblyPath = File.Exists(dll) ? dll : null });
                foreach (var candidate in new[] { Path.Combine(root, "SupportPackages"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Siemens", "Automation", "Portal V" + version, "SupportPackages") })
                    if (Directory.Exists(candidate)) result.HardwareSupportPaths.Add(candidate);
            }
            result.RunningProcesses.AddRange(RunningPortalIds());
            try
            {
                var group = (System.Security.Principal.SecurityIdentifier)new System.Security.Principal.NTAccount(Environment.MachineName, "Siemens TIA Openness").Translate(typeof(System.Security.Principal.SecurityIdentifier));
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                    result.OpennessGroup = new System.Security.Principal.WindowsPrincipal(identity).IsInRole(group);
            }
            catch (Exception e) { result.Warnings.Add("Openness 用户组无法验证：" + e.Message); }
            return result;
        }

        internal static int[] RunningPortalIds()
        {
            return Process.GetProcessesByName("Siemens.Automation.Portal").Select(process =>
            {
                using (process) return process.Id;
            }).ToArray();
        }

        private static void ReadProducts(RegistryKey hive, TiaEnvironment result)
        {
            using (var entries = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
            {
                if (entries == null) return;
                foreach (string key in entries.GetSubKeyNames())
                using (var entry = entries.OpenSubKey(key))
                {
                    if (entry == null) continue;
                    var name = entry.GetValue("DisplayName") as string ?? "";
                    if (new[] { "TIA Portal", "STEP 7", "PLCSIM", "SIMATIC S7" }.Any(x => name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        if (name.IndexOf("PLCSIM", StringComparison.OrdinalIgnoreCase) >= 0) result.PlcSim = true;
                        var label = (name + " " + (entry.GetValue("DisplayVersion") as string ?? "")).Trim();
                        if (!result.Products.Contains(label)) result.Products.Add(label);
                    }
                }
            }
        }

        private static void ReadWebView(RegistryKey hive, TiaEnvironment result)
        {
            using (var clients = hive.OpenSubKey(@"SOFTWARE\Microsoft\EdgeUpdate\Clients"))
            {
                if (clients == null) return;
                foreach (var client in clients.GetSubKeyNames())
                using (var key = clients.OpenSubKey(client))
                    if (key != null && ((key.GetValue("name") as string) ?? "").IndexOf("WebView2", StringComparison.OrdinalIgnoreCase) >= 0)
                        result.WebView2 = true;
            }
        }
    }
}
