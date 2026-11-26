using System;
using System.IO;
using DeskBox.Core;

namespace DeskBox.Engineering.Tia
{
    public sealed class TiaMcpPolicy
    {
        private const string FileName = "tia-mcp-policy.json";
        public bool Enabled = true;
        public bool OfflineEngineeringEnabled = true;

        public static TiaMcpPolicy Load() { return LoadFrom(AppPaths.DataDir); }

        internal static TiaMcpPolicy LoadFrom(string directory)
        {
            string path = Path.Combine(directory, FileName);
            if (!File.Exists(path)) return new TiaMcpPolicy();
            return Json.Load<TiaMcpPolicy>(path, null)
                ?? new TiaMcpPolicy { Enabled = false, OfflineEngineeringEnabled = false };
        }

        public void Save() { Json.SaveAtomic(AppPaths.File(FileName), this, true); }

        internal void Require(string operation)
        {
            if (!Enabled) throw new InvalidOperationException("本机 TIA MCP 已关闭。请在 AI 工程中心的 MCP 页面核对设置。");
            if (!OfflineEngineeringEnabled && TiaMcp.RequiresOfflineApproval(operation))
                throw new InvalidOperationException("MCP 策略未启用离线编译及工程写入。");
        }
    }
}
