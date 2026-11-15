using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DeskBox.Core;

namespace DeskBox.Fs
{
    /// <summary>
    /// 真实文件操作：重命名 / 移动 / 复制 / 备份删除，全部可撤销。
    /// 约束：只用 .NET Framework 自带类（零第三方库）；所有路径先 GetFullPath 规范化；
    /// 一律不覆盖已存在的目标；拒绝操作盘符根目录。
    /// </summary>
    public class FileOps
    {
        private const int MaxBatches = 20;

        private readonly Store _store;
        private readonly FileHistory _history;
        private readonly object _gate = new object();
        private readonly List<UndoBatch> _batches = new List<UndoBatch>();

        public FileOps(Store store)
        {
            _store = store;
            _history = new FileHistory(store);
            LoadBatches();
        }

        // ------------------------------------------------------------------
        // 公开状态
        // ------------------------------------------------------------------

        /// <summary>最近一次成功操作的撤销批次（可能已被使用，使用前请看 Used）。</summary>
        public UndoBatch LastUndo
        {
            get { lock (_gate) { return _batches.Count > 0 ? _batches[0] : null; } }
        }

        /// <summary>最近 20 个撤销批次（新的在前，含已使用过的）。</summary>
        public List<UndoBatch> Batches
        {
            get { lock (_gate) { return new List<UndoBatch>(_batches); } }
        }

        private static string DataDir { get { return Store.DataDir; } }
        private static string UndoPath { get { return Path.Combine(DataDir, "undo.json"); } }
        private static string BackupRoot { get { return Path.Combine(DataDir, "backup"); } }

        // ------------------------------------------------------------------
        // 1. 重命名计划
        // ------------------------------------------------------------------

        public RenamePlan PlanRename(List<string> sources, RenameRule rule, bool keepExtension)
        {
            return BuildPlan(sources, rule, keepExtension);
        }

        /// <summary>预览与 PlanRename 完全一致（UI 预览用）。</summary>
        public RenamePlan PlanRenamePreview(List<string> sources, RenameRule rule, bool keepExtension)
        {
            return BuildPlan(sources, rule, keepExtension);
        }

        private static RenamePlan BuildPlan(List<string> sources, RenameRule rule, bool keepExtension)
        {
            var plan = new RenamePlan();
            if (sources == null || sources.Count == 0)
            {
                plan.Errors = "没有选择文件";
                return plan;
            }
            if (rule == null) rule = new RenameRule();

            Regex re = null;
            string regexError = null;
            if (rule.UseRegex && !string.IsNullOrEmpty(rule.Find))
            {
                try { re = new Regex(rule.Find, RegexOptions.CultureInvariant); }
                catch (Exception ex) { regexError = "正则表达式非法：" + ex.Message; }
            }
            if (regexError != null) plan.Errors = regexError;

            var items = new List<RenamePlanItem>();
            int seqIndex = 0;
            foreach (string raw in sources)
            {
                int seq = seqIndex++;
                var item = new RenamePlanItem();
                string full;
                if (!TryFullPath(raw, out full))
                {
                    item.Source = raw == null ? "" : raw;
                    item.OldName = "";
                    item.NewName = "";
                    Mark(item, "路径无效");
                    items.Add(item);
                    continue;
                }

                item.Source = full;
                item.OldName = Path.GetFileName(full);
                item.TargetDir = Path.GetDirectoryName(full);
                if (string.IsNullOrEmpty(item.TargetDir)) item.TargetDir = Path.GetPathRoot(full);

                if (regexError != null)
                {
                    item.NewName = item.OldName;
                    Mark(item, regexError);
                }
                else
                {
                    item.NewName = ApplyRule(item.OldName, rule, keepExtension, seq, re);
                }
                items.Add(item);
            }
            plan.Items = items;

            // 单条校验（存在性、占用、根目录）
            foreach (var item in items)
            {
                if (item.Conflict) continue;
                if (string.IsNullOrEmpty(item.OldName)) { Mark(item, "无法确定文件名"); continue; }
                if (string.IsNullOrEmpty(item.TargetDir)) { Mark(item, "无法确定目标目录"); continue; }
                if (!Directory.Exists(item.TargetDir)) { Mark(item, "目标目录不存在"); continue; }
                if (IsDriveRoot(item.Source)) { Mark(item, "拒绝操作盘符根目录"); continue; }
                if (!PathExists(item.Source)) { Mark(item, "源文件不存在"); continue; }
                if (item.Changed && File.Exists(item.Source) && IsFileLocked(item.Source))
                {
                    Mark(item, "源文件被占用");
                }
            }

            // 计划内重名：两条新名字一样
            var sourceMap = new Dictionary<string, RenamePlanItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.Source)) continue;
                if (!sourceMap.ContainsKey(item.Source)) sourceMap[item.Source] = item;
            }

            var targetMap = new Dictionary<string, List<RenamePlanItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (item.Conflict || !item.Changed) continue;
                string target = item.NewPath;
                List<RenamePlanItem> list;
                if (!targetMap.TryGetValue(target, out list))
                {
                    list = new List<RenamePlanItem>();
                    targetMap[target] = list;
                }
                list.Add(item);
            }
            foreach (var kv in targetMap)
            {
                if (kv.Value.Count < 2) continue;
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    Mark(kv.Value[i], "计划内目标重名：" + Path.GetFileName(kv.Key));
                }
            }

            // 目标已存在：如果占用者本身也在本次计划里且会被改名让出，则允许（两阶段执行）
            bool again = true;
            int guard = 0;
            while (again && guard++ <= items.Count + 2)
            {
                again = false;
                foreach (var item in items)
                {
                    if (item.Conflict || !item.Changed) continue;
                    string target = item.NewPath;
                    if (!PathExists(target)) continue;

                    RenamePlanItem occupant;
                    if (sourceMap.TryGetValue(target, out occupant) &&
                        !ReferenceEquals(occupant, item) &&
                        occupant.Changed && !occupant.Conflict)
                    {
                        continue; // 会被让出
                    }
                    Mark(item, "目标已存在：" + Path.GetFileName(target));
                    again = true;
                }
            }

            plan.ChangedCount = 0;
            plan.ConflictCount = 0;
            foreach (var item in items)
            {
                if (item.Conflict) plan.ConflictCount++;
                else if (item.Changed) plan.ChangedCount++;
            }
            return plan;
        }

        // ------------------------------------------------------------------
        // 2. 执行重命名（两阶段 + 失败回滚）
        // ------------------------------------------------------------------

        public bool ExecuteRename(RenamePlan plan, out string message)
        {
            message = "";
            if (plan == null || plan.Items == null || plan.Items.Count == 0)
            {
                message = "重命名计划为空";
                return false;
            }
            if (plan.ConflictCount > 0)
            {
                RenamePlanItem first = null;
                for (int i = 0; i < plan.Items.Count; i++)
                {
                    if (plan.Items[i] != null && plan.Items[i].Conflict) { first = plan.Items[i]; break; }
                }
                message = "存在 " + plan.ConflictCount + " 项冲突，已取消执行";
                if (first != null) message += "（" + first.OldName + "：" + first.ConflictReason + "）";
                return false;
            }

            var items = new List<RenamePlanItem>();
            foreach (var it in plan.Items)
            {
                if (it != null && it.Changed) items.Add(it);
            }
            if (items.Count == 0)
            {
                message = "没有需要重命名的文件";
                return true;
            }

            string pre;
            if (!PrecheckRename(items, out pre))
            {
                message = pre;
                return false;
            }

            var temps = new List<string>();
            for (int k = 0; k < items.Count; k++) temps.Add(null);

            // 阶段一：全部改成临时名，先把所有旧名字腾出来
            for (int k = 0; k < items.Count; k++)
            {
                var it = items[k];
                try
                {
                    string temp = MakeTempPath(it.Source);
                    File.Move(it.Source, temp);
                    temps[k] = temp;
                }
                catch (Exception ex)
                {
                    string rb = RollbackTemps(items, temps);
                    message = "重命名失败（第 " + (k + 1) + " 项 " + it.OldName + "）：" + ex.Message;
                    if (rb.Length > 0) message += "；回滚异常：" + rb;
                    Log("error", "rename", message);
                    RecordHistory("rename", it.Source, it.NewPath, false, it.OldName + " → " + it.NewName + " 失败：" + ex.Message, "");
                    return false;
                }
            }

            // 阶段二：临时名 → 目标名
            var done = new List<RenamePlanItem>();
            for (int k = 0; k < items.Count; k++)
            {
                var it = items[k];
                try
                {
                    if (PathExists(it.NewPath))
                    {
                        throw new IOException("目标已存在: " + it.NewPath);
                    }
                    File.Move(temps[k], it.NewPath);
                    temps[k] = null;
                    done.Add(it);
                }
                catch (Exception ex)
                {
                    string rb = RollbackRenames(items, temps, done);
                    message = "重命名失败（第 " + (k + 1) + " 项 " + it.OldName + "）：" + ex.Message;
                    if (rb.Length > 0) message += "；回滚异常：" + rb;
                    Log("error", "rename", message);
                    RecordHistory("rename", it.Source, it.NewPath, false, it.OldName + " → " + it.NewName + " 失败：" + ex.Message, "");
                    return false;
                }
            }

            var batch = new UndoBatch
            {
                Id = NewBatchId(),
                Ts = NowMs(),
                Title = "重命名 " + done.Count + " 项",
                Actions = new List<UndoAction>()
            };
            foreach (var it in done)
            {
                batch.Actions.Add(new UndoAction { Kind = "rename", From = it.NewPath, To = it.Source });
                RecordHistory("rename", it.Source, it.NewPath, true, it.OldName + " → " + it.NewName, batch.Id);
            }
            AddBatch(batch);

            message = "已重命名 " + done.Count + " 项";
            Log("info", "rename", message);
            return true;
        }

        private static bool PrecheckRename(List<RenamePlanItem> items, out string message)
        {
            message = "";
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in items) sources.Add(it.Source);

            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                string no = "第 " + (i + 1) + " 项 " + (it.OldName == null ? "" : it.OldName);
                if (string.IsNullOrEmpty(it.TargetDir) || !Directory.Exists(it.TargetDir))
                {
                    message = "目标目录不存在（" + no + "）";
                    return false;
                }
                if (string.IsNullOrEmpty(it.NewName))
                {
                    message = "新名称为空（" + no + "）";
                    return false;
                }
                if (IsDriveRoot(it.Source) || IsDriveRoot(it.NewPath))
                {
                    message = "拒绝操作盘符根目录（" + no + "）";
                    return false;
                }
                if (!PathExists(it.Source))
                {
                    message = "源文件不存在（" + no + "）";
                    return false;
                }
                if (PathExists(it.NewPath) && !sources.Contains(it.NewPath))
                {
                    message = "目标已存在（" + no + "）：" + it.NewPath;
                    return false;
                }
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 3. 移动
        // ------------------------------------------------------------------

        public bool MoveFiles(List<string> sources, string targetDir, out string message)
        {
            message = "";
            List<string> srcs;
            if (!NormalizeSources(sources, out srcs, out message)) return false;

            string targetFull;
            if (!ValidateTargetDir(targetDir, out targetFull, out message)) return false;

            var pairs = new List<string[]>();
            foreach (string s in srcs)
            {
                string name = Path.GetFileName(s.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(name))
                {
                    message = "无法确定文件名：" + s;
                    return false;
                }
                string dest = Path.Combine(targetFull, name);
                if (PathEquals(dest, s))
                {
                    message = "源和目标是同一个位置：" + name;
                    return false;
                }
                if (PathExists(dest))
                {
                    message = "目标已存在：" + dest;
                    return false;
                }
                if (Directory.Exists(s) && IsUnder(targetFull, s))
                {
                    message = "不能把文件夹移动到它自己的子目录：" + name;
                    return false;
                }
                pairs.Add(new string[] { s, dest });
            }

            var moved = new List<string[]>();
            foreach (var p in pairs)
            {
                try
                {
                    MoveCore(p[0], p[1]);
                    moved.Add(p);
                }
                catch (Exception ex)
                {
                    string rb = RollbackMoves(moved);
                    message = "移动失败（" + Path.GetFileName(p[0]) + "）：" + ex.Message;
                    if (moved.Count > 0) message += "；已回滚 " + moved.Count + " 项";
                    if (rb.Length > 0) message += "；回滚异常：" + rb;
                    Log("error", "move", message);
                    RecordHistory("move", p[0], p[1], false, ex.Message, "");
                    return false;
                }
            }

            var batch = new UndoBatch
            {
                Id = NewBatchId(),
                Ts = NowMs(),
                Title = "移动 " + moved.Count + " 项到 " + Path.GetFileName(targetFull.TrimEnd(Path.DirectorySeparatorChar)),
                Actions = new List<UndoAction>()
            };
            foreach (var p in moved)
            {
                batch.Actions.Add(new UndoAction { Kind = "move", From = p[1], To = p[0] });
                RecordHistory("move", p[0], p[1], true, "移动到 " + targetFull, batch.Id);
            }
            AddBatch(batch);

            message = "已移动 " + moved.Count + " 项到 " + targetFull;
            Log("info", "move", message);
            return true;
        }

        // ------------------------------------------------------------------
        // 4. 复制
        // ------------------------------------------------------------------

        public bool CopyFiles(List<string> sources, string targetDir, out string message)
        {
            message = "";
            List<string> srcs;
            if (!NormalizeSources(sources, out srcs, out message)) return false;

            string targetFull;
            if (!ValidateTargetDir(targetDir, out targetFull, out message)) return false;

            var pairs = new List<string[]>();
            foreach (string s in srcs)
            {
                string name = Path.GetFileName(s.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(name))
                {
                    message = "无法确定文件名：" + s;
                    return false;
                }
                if (PathEquals(Path.GetDirectoryName(s), targetFull))
                {
                    message = "源已经在目标目录中：" + name;
                    return false;
                }
                string dest = Path.Combine(targetFull, name);
                if (PathExists(dest))
                {
                    message = "目标已存在：" + dest;
                    return false;
                }
                if (Directory.Exists(s) && IsUnder(targetFull, s))
                {
                    message = "不能把文件夹复制到它自己的子目录：" + name;
                    return false;
                }
                pairs.Add(new string[] { s, dest });
            }

            var copied = new List<string[]>();
            foreach (var p in pairs)
            {
                try
                {
                    CopyCore(p[0], p[1]);
                    copied.Add(p);
                }
                catch (Exception ex)
                {
                    string rb = RollbackCopies(copied);
                    message = "复制失败（" + Path.GetFileName(p[0]) + "）：" + ex.Message;
                    if (copied.Count > 0) message += "；已清除 " + copied.Count + " 个未完成的副本";
                    if (rb.Length > 0) message += "；回滚异常：" + rb;
                    Log("error", "copy", message);
                    RecordHistory("copy", p[0], p[1], false, ex.Message, "");
                    return false;
                }
            }

            var batch = new UndoBatch
            {
                Id = NewBatchId(),
                Ts = NowMs(),
                Title = "复制 " + copied.Count + " 项到 " + Path.GetFileName(targetFull.TrimEnd(Path.DirectorySeparatorChar)),
                Actions = new List<UndoAction>()
            };
            foreach (var p in copied)
            {
                batch.Actions.Add(new UndoAction { Kind = "copy", From = p[1], To = p[0] });
                RecordHistory("copy", p[0], p[1], true, "复制到 " + targetFull, batch.Id);
            }
            AddBatch(batch);

            message = "已复制 " + copied.Count + " 项到 " + targetFull;
            Log("info", "copy", message);
            return true;
        }

        // ------------------------------------------------------------------
        // 5. 移到备份目录（伪删除，可撤销）
        // ------------------------------------------------------------------

        public bool DeleteToBackup(List<string> sources, out string message)
        {
            message = "";
            List<string> srcs;
            if (!NormalizeSources(sources, out srcs, out message)) return false;

            string folder;
            try
            {
                Directory.CreateDirectory(BackupRoot);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                folder = Path.Combine(BackupRoot, stamp + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                message = "无法创建备份目录：" + ex.Message;
                return false;
            }

            foreach (string s in srcs)
            {
                if (IsUnder(folder, s))
                {
                    message = "不能删除包含备份目录的文件夹：" + s;
                    return false;
                }
            }

            var moved = new List<string[]>();
            foreach (string s in srcs)
            {
                string name = Path.GetFileName(s.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(name)) name = "item";
                string dest = UniqueDest(folder, name);
                try
                {
                    MoveCore(s, dest);
                    moved.Add(new string[] { s, dest });
                }
                catch (Exception ex)
                {
                    string rb = RollbackMoves(moved);
                    message = "移除失败（" + Path.GetFileName(s) + "）：" + ex.Message;
                    if (moved.Count > 0) message += "；已回滚 " + moved.Count + " 项";
                    if (rb.Length > 0) message += "；回滚异常：" + rb;
                    Log("error", "delete", message);
                    RecordHistory("delete", s, dest, false, ex.Message, "");
                    return false;
                }
            }

            var batch = new UndoBatch
            {
                Id = NewBatchId(),
                Ts = NowMs(),
                Title = "删除 " + moved.Count + " 项（已备份）",
                Actions = new List<UndoAction>()
            };
            foreach (var p in moved)
            {
                batch.Actions.Add(new UndoAction { Kind = "trash", From = p[1], To = p[0] });
                RecordHistory("delete", p[0], p[1], true, "移到备份目录 " + folder, batch.Id);
            }
            AddBatch(batch);

            message = "已移除 " + moved.Count + " 项到备份目录（可撤销）：" + folder;
            Log("info", "delete", message);
            return true;
        }

        // ------------------------------------------------------------------
        // 6. 撤销
        // ------------------------------------------------------------------

        public bool Undo(string batchId, out string message)
        {
            message = "";
            if (string.IsNullOrEmpty(batchId))
            {
                message = "撤销批次为空";
                return false;
            }

            UndoBatch batch;
            lock (_gate) { batch = _batches.Find(b => b != null && b.Id == batchId); }
            if (batch == null)
            {
                message = "找不到撤销批次：" + batchId;
                return false;
            }
            if (batch.Used)
            {
                message = "该批次已经撤销过了：" + batch.Title;
                return false;
            }
            if (batch.Actions == null || batch.Actions.Count == 0)
            {
                batch.Used = true;
                SaveBatches();
                message = "批次没有可撤销的动作";
                return true;
            }

            var actions = batch.Actions;

            // 预检：From 必须存在；To 只有在本次批次会让出时才允许存在
            var moveFroms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                if (a == null || a.Kind == "copy") continue;
                if (!string.IsNullOrEmpty(a.From)) moveFroms.Add(a.From);
            }
            var targetSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                var a = actions[i];
                int no = i + 1;
                if (a == null || string.IsNullOrEmpty(a.From))
                {
                    message = "撤销失败：第 " + no + " 条动作无效";
                    return false;
                }
                if (a.Kind == "copy")
                {
                    if (!PathExists(a.From))
                    {
                        message = "撤销失败：第 " + no + " 条找不到复制出的文件：" + a.From;
                        return false;
                    }
                    continue;
                }
                if (!PathExists(a.From))
                {
                    message = "撤销失败：第 " + no + " 条找不到要还原的文件：" + a.From;
                    return false;
                }
                if (string.IsNullOrEmpty(a.To) || IsDriveRoot(a.To))
                {
                    message = "撤销失败：第 " + no + " 条目标路径无效";
                    return false;
                }
                if (!targetSeen.Add(a.To))
                {
                    message = "撤销失败：第 " + no + " 条目标重复：" + a.To;
                    return false;
                }
                if (PathExists(a.To) && !moveFroms.Contains(a.To))
                {
                    message = "撤销失败：第 " + no + " 条目标位置已被占用：" + a.To;
                    return false;
                }
            }

            // (1) 复制批次：删除复制出来的副本
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                var a = actions[i];
                if (a.Kind != "copy") continue;
                try { DeletePath(a.From); }
                catch (Exception ex)
                {
                    message = "撤销失败：第 " + (i + 1) + " 条（删除副本）：" + ex.Message;
                    Log("error", "undo", message);
                    return false;
                }
            }

            // (2) 移动类：两阶段，先把所有新位置腾成临时名，再还原到旧位置
            var moves = new List<UndoAction>();
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                var a = actions[i];
                if (a != null && a.Kind != "copy") moves.Add(a);
            }
            var temps = new List<string>();
            for (int k = 0; k < moves.Count; k++) temps.Add(null);

            for (int k = 0; k < moves.Count; k++)
            {
                var a = moves[k];
                int no = ActionNo(actions, a);
                try
                {
                    string temp = MakeTempPath(a.From);
                    File.Move(a.From, temp);
                    temps[k] = temp;
                }
                catch (Exception ex)
                {
                    string rb = RollbackTemps(moves, temps);
                    message = "撤销失败：第 " + no + " 条（准备还原）：" + ex.Message;
                    if (rb.Length > 0) message += "；回滚异常：" + rb;
                    Log("error", "undo", message);
                    return false;
                }
            }

            var landed = new List<int>();
            for (int k = 0; k < moves.Count; k++)
            {
                var a = moves[k];
                int no = ActionNo(actions, a);
                try
                {
                    string dir = Path.GetDirectoryName(a.To);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    if (PathExists(a.To)) throw new IOException("目标已存在: " + a.To);
                    File.Move(temps[k], a.To);
                    landed.Add(k);
                }
                catch (Exception ex)
                {
                    string rb = RollbackUndoMoves(moves, temps, landed);
                    message = "撤销失败：第 " + no + " 条（还原到原位置）：" + ex.Message;
                    if (rb.Length > 0) message += "；回滚异常：" + rb;
                    Log("error", "undo", message);
                    return false;
                }
            }

            batch.Used = true;
            SaveBatches();
            RecordHistory("undo", "", "", true, "撤销 " + batch.Title + "（" + moves.Count + " 项）", batch.Id);
            message = "已撤销：" + batch.Title;
            Log("info", "undo", message);
            return true;
        }

        private static int ActionNo(List<UndoAction> actions, UndoAction a)
        {
            for (int i = 0; i < actions.Count; i++)
            {
                if (ReferenceEquals(actions[i], a)) return i + 1;
            }
            return 0;
        }

        private static string RollbackTemps(List<UndoAction> moves, List<string> temps)
        {
            var sb = new StringBuilder();
            for (int k = moves.Count - 1; k >= 0; k--)
            {
                string t = temps[k];
                if (string.IsNullOrEmpty(t) || !PathExists(t)) continue;
                try { File.Move(t, moves[k].From); }
                catch (Exception ex) { sb.Append("[还原 " + Path.GetFileName(moves[k].From) + " 失败: " + ex.Message + "] "); }
            }
            return sb.ToString();
        }

        private static string RollbackUndoMoves(List<UndoAction> moves, List<string> temps, List<int> landed)
        {
            var sb = new StringBuilder();
            for (int i = landed.Count - 1; i >= 0; i--)
            {
                int k = landed[i];
                try
                {
                    if (PathExists(moves[k].To)) File.Move(moves[k].To, temps[k]);
                }
                catch (Exception ex) { sb.Append("[退回临时名失败: " + ex.Message + "] "); }
            }
            sb.Append(RollbackTemps(moves, temps));
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 规则引擎
        // ------------------------------------------------------------------

        private static string ApplyRule(string oldName, RenameRule rule, bool keepExtension, int seqIndex, Regex re)
        {
            string main, ext;
            SplitName(oldName, keepExtension, out main, out ext);

            if (!string.IsNullOrEmpty(rule.Find))
            {
                string replace = rule.Replace == null ? "" : rule.Replace;
                if (re != null) main = re.Replace(main, replace);
                else if (!rule.UseRegex) main = main.Replace(rule.Find, replace);
            }

            if (rule.LowerCase) main = main.ToLowerInvariant();
            else if (rule.UpperCase) main = main.ToUpperInvariant();

            if (rule.TrimSpace) main = CollapseSpaces(main);

            if (!string.IsNullOrEmpty(rule.NumberPattern))
            {
                long seq = (long)rule.NumberStart + (long)seqIndex * rule.NumberStep;
                string seqText = seq.ToString(CultureInfo.InvariantCulture);
                if (seqText.Length < 2) seqText = seqText.PadLeft(2, '0');
                main = main + rule.NumberPattern + seqText;
            }

            main = SanitizeFileName(main);
            return main + ext;
        }

        private static void SplitName(string name, bool keepExtension, out string main, out string ext)
        {
            if (name == null) name = "";
            if (!keepExtension)
            {
                main = name;
                ext = "";
                return;
            }
            int dot = name.LastIndexOf('.');
            if (dot <= 0 || dot == name.Length - 1)
            {
                main = name;
                ext = "";
            }
            else
            {
                main = name.Substring(0, dot);
                ext = name.Substring(dot);
            }
        }

        private static string CollapseSpaces(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.IndexOf("  ", StringComparison.Ordinal) < 0) return s;
            return Regex.Replace(s, " {2,}", " ");
        }

        private static string SanitizeFileName(string name)
        {
            if (name == null) return "_";
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                bool bad = false;
                for (int i = 0; i < invalid.Length; i++)
                {
                    if (invalid[i] == c) { bad = true; break; }
                }
                sb.Append(bad ? '_' : c);
            }
            string s = sb.ToString().TrimEnd('.', ' ');
            if (s.Length == 0 || s.Trim().Length == 0) return "_";
            return s;
        }

        private static void Mark(RenamePlanItem item, string reason)
        {
            item.Conflict = true;
            item.ConflictReason = reason;
        }

        // ------------------------------------------------------------------
        // 底层文件动作
        // ------------------------------------------------------------------

        private static void MoveCore(string from, string to)
        {
            if (!PathExists(from)) throw new FileNotFoundException("源不存在：" + from, from);
            if (PathEquals(from, to)) return;
            if (PathExists(to)) throw new IOException("目标已存在：" + to);

            try
            {
                File.Move(from, to);
                return;
            }
            catch (IOException)
            {
                if (PathExists(to)) throw new IOException("目标已存在：" + to);
            }

            // 跨盘兜底：复制 + 删除
            CopyCore(from, to);
            try
            {
                DeletePath(from);
            }
            catch
            {
                try { DeletePath(to); } catch { }
                throw;
            }
        }

        private static void CopyCore(string from, string to)
        {
            if (Directory.Exists(from))
            {
                Directory.CreateDirectory(to);
                foreach (string f in Directory.GetFiles(from))
                {
                    File.Copy(f, Path.Combine(to, Path.GetFileName(f)), false);
                }
                foreach (string d in Directory.GetDirectories(from))
                {
                    CopyCore(d, Path.Combine(to, Path.GetFileName(d)));
                }
                return;
            }
            File.Copy(from, to, false);
        }

        private static void DeletePath(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, true);
        }

        private static string RollbackRenames(List<RenamePlanItem> items, List<string> temps, List<RenamePlanItem> done)
        {
            var sb = new StringBuilder();
            for (int i = done.Count - 1; i >= 0; i--)
            {
                var it = done[i];
                try
                {
                    if (PathExists(it.NewPath)) File.Move(it.NewPath, it.Source);
                }
                catch (Exception ex) { sb.Append("[还原 " + it.OldName + " 失败: " + ex.Message + "] "); }
            }
            for (int i = items.Count - 1; i >= 0; i--)
            {
                string t = temps[i];
                if (string.IsNullOrEmpty(t) || !PathExists(t)) continue;
                try { File.Move(t, items[i].Source); }
                catch (Exception ex) { sb.Append("[还原 " + items[i].OldName + " 失败: " + ex.Message + "] "); }
            }
            return sb.ToString();
        }

        private static string RollbackTemps(List<RenamePlanItem> items, List<string> temps)
        {
            var sb = new StringBuilder();
            for (int i = items.Count - 1; i >= 0; i--)
            {
                string t = temps[i];
                if (string.IsNullOrEmpty(t) || !PathExists(t)) continue;
                try { File.Move(t, items[i].Source); }
                catch (Exception ex) { sb.Append("[还原 " + items[i].OldName + " 失败: " + ex.Message + "] "); }
            }
            return sb.ToString();
        }

        private static string RollbackMoves(List<string[]> moved)
        {
            var sb = new StringBuilder();
            for (int i = moved.Count - 1; i >= 0; i--)
            {
                try { MoveCore(moved[i][1], moved[i][0]); }
                catch (Exception ex) { sb.Append("[回滚 " + Path.GetFileName(moved[i][0]) + " 失败: " + ex.Message + "] "); }
            }
            return sb.ToString();
        }

        private static string RollbackCopies(List<string[]> copied)
        {
            var sb = new StringBuilder();
            for (int i = copied.Count - 1; i >= 0; i--)
            {
                try { DeletePath(copied[i][1]); }
                catch (Exception ex) { sb.Append("[清除副本 " + Path.GetFileName(copied[i][1]) + " 失败: " + ex.Message + "] "); }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 路径 / 校验工具
        // ------------------------------------------------------------------

        private static bool TryFullPath(string raw, out string full)
        {
            full = "";
            if (string.IsNullOrWhiteSpace(raw)) return false;
            try
            {
                full = Path.GetFullPath(raw);
                return true;
            }
            catch { return false; }
        }

        private static bool IsDriveRoot(string full)
        {
            if (string.IsNullOrEmpty(full)) return false;
            try
            {
                string root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root)) return false;
                string a = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string b = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool PathExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return File.Exists(path) || Directory.Exists(path); }
            catch { return false; }
        }

        private static bool PathEquals(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnder(string path, string dir)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(dir)) return false;
            string p = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string d = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return p.StartsWith(d, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFileLocked(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
            catch { return false; }
        }

        private static string MakeTempPath(string source)
        {
            string dir = Path.GetDirectoryName(source);
            if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
            for (int i = 0; i < 100; i++)
            {
                string candidate = Path.Combine(dir, ".__dbx_tmp_" + Guid.NewGuid().ToString("N").Substring(0, 12));
                if (!PathExists(candidate)) return candidate;
            }
            return Path.Combine(dir, ".__dbx_tmp_" + Guid.NewGuid().ToString("N"));
        }

        private static string UniqueDest(string dir, string name)
        {
            string dest = Path.Combine(dir, name);
            if (!PathExists(dest)) return dest;
            string baseName = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            for (int i = 1; i < 1000; i++)
            {
                dest = Path.Combine(dir, baseName + "_" + i + ext);
                if (!PathExists(dest)) return dest;
            }
            return Path.Combine(dir, baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ext);
        }

        private static bool NormalizeSources(List<string> sources, out List<string> fulls, out string message)
        {
            fulls = new List<string>();
            message = "";
            if (sources == null || sources.Count == 0)
            {
                message = "没有选择文件";
                return false;
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in sources)
            {
                string full;
                if (!TryFullPath(raw, out full))
                {
                    message = "路径无效：" + (raw == null ? "" : raw);
                    return false;
                }
                if (IsDriveRoot(full))
                {
                    message = "拒绝操作盘符根目录：" + full;
                    return false;
                }
                if (!PathExists(full))
                {
                    message = "源文件不存在：" + full;
                    return false;
                }
                if (!seen.Add(full)) continue;
                fulls.Add(full);
            }
            if (fulls.Count == 0)
            {
                message = "没有有效文件";
                return false;
            }
            return true;
        }

        private static bool ValidateTargetDir(string targetDir, out string full, out string message)
        {
            full = "";
            message = "";
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                message = "目标目录为空";
                return false;
            }
            if (!TryFullPath(targetDir, out full))
            {
                message = "目标目录无效：" + targetDir;
                return false;
            }
            if (!Directory.Exists(full))
            {
                message = "目标目录不存在：" + full;
                return false;
            }
            if (IsDriveRoot(full))
            {
                message = "拒绝操作系统盘根目录";
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 撤销批次持久化 / 历史 / 日志
        // ------------------------------------------------------------------

        private void LoadBatches()
        {
            try
            {
                var list = Json.Load<List<UndoBatch>>(UndoPath, null);
                if (list == null) return;
                list.RemoveAll(b => b == null);
                list.Sort((a, b) => b.Ts.CompareTo(a.Ts));
                lock (_gate)
                {
                    _batches.Clear();
                    for (int i = 0; i < list.Count && i < MaxBatches; i++) _batches.Add(list[i]);
                }
            }
            catch { }
        }

        private void AddBatch(UndoBatch batch)
        {
            lock (_gate)
            {
                _batches.Insert(0, batch);
                while (_batches.Count > MaxBatches) _batches.RemoveAt(_batches.Count - 1);
            }
            SaveBatches();
        }

        private void SaveBatches()
        {
            try
            {
                List<UndoBatch> copy;
                lock (_gate) { copy = new List<UndoBatch>(_batches); }
                Json.Save(UndoPath, copy);
            }
            catch { }
        }

        private void RecordHistory(string op, string source, string target, bool ok, string detail, string undoId)
        {
            try
            {
                _history.Record(new FileOpRecord
                {
                    Op = op,
                    Source = source,
                    Target = target,
                    Ok = ok,
                    Detail = detail,
                    UndoId = undoId == null ? "" : undoId
                });
            }
            catch { }
        }

        private void Log(string level, string action, string message)
        {
            try
            {
                if (_store != null) _store.Log(level, action, message);
                else DeskBox.Core.AppLog.Write(level, action, message);
            }
            catch { }
        }

        private static string NewBatchId()
        {
            return "u_" + DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static long NowMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
