using System;
using System.Collections.Generic;
using System.IO;

namespace DeskBox.Core
{
    /// <summary>
    /// 数据目录解析与迁移。
    /// 优先级：环境变量 DESKBOX_HOME → D:\DeskBox\data（安装目录，默认留在 D 盘）
    ///        → %LOCALAPPDATA%\DeskBox\data（D 盘不可用时的回退，保证不依赖 EXE 目录可写）。
    /// 首次切换到新位置时把旧目录内容整体迁移（幂等、失败可回退）。
    /// </summary>
    public static class DataPaths
    {
        public const string EnvHome = "DESKBOX_HOME";
        public static string Resolved { get; private set; }
        public static string Source { get; private set; }

        public static string Resolve()
        {
            var candidates = new List<string>();
            var env = Environment.GetEnvironmentVariable(EnvHome);
            if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env.Trim());

            var legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            candidates.Add(Path.Combine(@"D:\DeskBox", "data"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskBox", "data"));
            candidates.Add(legacy);

            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                try
                {
                    Directory.CreateDirectory(c);
                    if (IsWritable(c)) { Resolved = c; return c; }
                }
                catch { }
            }
            Resolved = legacy;
            return legacy;
        }

        private static bool IsWritable(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".write-probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>若旧位置（EXE 同级 data）有数据而新位置为空，则迁移。</summary>
        public static void MigrateIfNeeded()
        {
            try
            {
                var legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                if (string.IsNullOrEmpty(Resolved)) return;
                if (string.Equals(Path.GetFullPath(Resolved), Path.GetFullPath(legacy), StringComparison.OrdinalIgnoreCase)) return;
                if (!Directory.Exists(legacy)) return;

                bool targetEmpty = Directory.GetFiles(Resolved, "*", SearchOption.TopDirectoryOnly).Length == 0;
                if (!targetEmpty) return;

                Source = legacy;
                foreach (var file in Directory.GetFiles(legacy))
                {
                    try
                    {
                        var dest = Path.Combine(Resolved, Path.GetFileName(file));
                        if (!File.Exists(dest)) File.Copy(file, dest, false);
                    }
                    catch (Exception ex) { AppLog.Write("warn", "system", "迁移文件失败 " + file + ": " + ex.Message); }
                }
                var legacyCache = Path.Combine(legacy, "cache");
                if (Directory.Exists(legacyCache))
                {
                    var destCache = Path.Combine(Resolved, "cache");
                    Directory.CreateDirectory(destCache);
                    foreach (var file in Directory.GetFiles(legacyCache))
                    {
                        try
                        {
                            var dest = Path.Combine(destCache, Path.GetFileName(file));
                            if (!File.Exists(dest)) File.Copy(file, dest, false);
                        }
                        catch { }
                    }
                }
                AppLog.Write("info", "system", "数据目录迁移完成：" + legacy + " → " + Resolved);
            }
            catch (Exception ex) { AppLog.Write("error", "system", "数据迁移失败: " + ex.Message); }
        }
    }
}
