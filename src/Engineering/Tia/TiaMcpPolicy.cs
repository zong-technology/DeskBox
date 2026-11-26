using System;
using System.IO;
using DeskBox.Core;

namespace DeskBox.Engineering.Tia
{
    public sealed class TiaMcpPolicy
    {
        private static string FilePath { get { return AppPaths.File("tia-mcp-policy.json"); } }
        public bool Enabled = true;
        public bool OfflineEngineeringEnabled = true;

        public static TiaMcpPolicy Load()
        {
            if (!File.Exists(FilePath)) return new TiaMcpPolicy();
            return Json.Load<TiaMcpPolicy>(FilePath, null)
                ?? new TiaMcpPolicy { Enabled = false, OfflineEngineeringEnabled = false };
        }

        public void Save() { Json.SaveAtomic(FilePath, this, true); }

        internal void Require(string operation)
        {
            if (!Enabled) throw new InvalidOperationException("本机 TIA MCP 已关闭。请在 AI 工程中心的 MCP 页面核对设置。");
            if (!OfflineEngineeringEnabled && TiaMcp.RequiresOfflineApproval(operation))
                throw new InvalidOperationException("MCP 策略未启用离线编译及工程写入。");
        }
    }
}
