using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DeskBox.Domain;
using DeskBox.Engineering;
using DeskBox.Engineering.Tia;

namespace DeskBox.Integration
{
    internal static class EngineeringFiles
    {
        internal static string Require(ProjectContext project, string path)
        {
            if (project == null) throw new UnauthorizedAccessException("请先选择工程项目。");
            if (string.IsNullOrWhiteSpace(path) || !project.IncludesPath(path))
                throw new UnauthorizedAccessException("文件超出当前工程范围。");
            var full = PlcEngineering.FullLocalPath(path);
            if (!project.CanAccessPath(full)) throw new UnauthorizedAccessException("文件超出当前工程范围。");
            return full;
        }

        internal static string Relative(ProjectContext project, string full)
        {
            var root = Path.GetFullPath(project.Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (full.Length <= root.Length) throw new ArgumentException("请选择工程根目录内的文件。");
            return full.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
        }

        internal static FileStream Open(ProjectContext project, string path, string kind, string version, long limit,
            CancellationToken token, out EngineeringSource source)
        {
            token.ThrowIfCancellationRequested();
            var full = Require(project, path);
            var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            try
            {
                var length = stream.Length;
                if (length > limit) throw new InvalidDataException("资料文件超过读取限额 " + limit / (1024 * 1024) + " MiB。");
                string sha;
                using (var hash = SHA256.Create())
                {
                    var bytes = new byte[65536];
                    int read;
                    long observed = 0;
                    while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        observed += read;
                        if (observed > limit) throw new InvalidDataException("读取时资料文件超过限额。");
                        hash.TransformBlock(bytes, 0, read, bytes, 0);
                    }
                    if (observed != length) throw new IOException("读取期间文件长度发生变化，请重试。");
                    hash.TransformFinalBlock(new byte[0], 0, 0);
                    sha = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                }
                stream.Position = 0;
                source = new EngineeringSource { Type = kind, Document = Relative(project, full), Version = version ?? "", Bytes = length, Sha256 = sha };
                return stream;
            }
            catch { stream.Dispose(); throw; }
        }

        internal static StreamReader Text(Stream stream, string selected, EngineeringSource source)
        {
            var prefix = new byte[512];
            int count = stream.Read(prefix, 0, prefix.Length), bom = 0;
            Encoding encoding;
            if (count >= 4 && ((prefix[0] == 255 && prefix[1] == 254 && prefix[2] == 0 && prefix[3] == 0) ||
                (prefix[0] == 0 && prefix[1] == 0 && prefix[2] == 254 && prefix[3] == 255)))
                throw new InvalidDataException("不支持 UTF-32 资料。");
            if (count >= 3 && prefix[0] == 239 && prefix[1] == 187 && prefix[2] == 191) { encoding = new UTF8Encoding(false, true); bom = 3; }
            else if (count >= 2 && prefix[0] == 255 && prefix[1] == 254) { encoding = new UnicodeEncoding(false, false, true); bom = 2; }
            else if (count >= 2 && prefix[0] == 254 && prefix[1] == 255) { encoding = new UnicodeEncoding(true, false, true); bom = 2; }
            else if (!string.IsNullOrWhiteSpace(selected) && selected != "auto")
            {
                if (selected != "gb18030" && selected != "windows-1252" && selected != "utf-8"
                    && selected != "utf-16" && selected != "utf-16BE")
                    throw new ArgumentException("资料编码选项无效。");
                encoding = Encoding.GetEncoding(selected, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            }
            else
            {
                int oddZeros = 0, evenZeros = 0;
                for (int i = 0; i < count; i++)
                    if (prefix[i] == 0) { if (i % 2 == 0) evenZeros++; else oddZeros++; }
                if (count >= 8 && oddZeros > count / 5 && evenZeros == 0) encoding = new UnicodeEncoding(false, false, true);
                else if (count >= 8 && evenZeros > count / 5 && oddZeros == 0) encoding = new UnicodeEncoding(true, false, true);
                else encoding = new UTF8Encoding(false, true);
            }
            source.Encoding = encoding.WebName;
            stream.Position = bom;
            return new StreamReader(stream, encoding, false, 32768, true);
        }

        internal static void Export(ProjectContext project, string path, Action<Stream> writer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var full = Require(project, path);
            if (File.Exists(full) || Directory.Exists(full)) throw new IOException("目标已存在。请选择新的报告或导出文件名。");
            var temporary = Path.Combine(Path.GetDirectoryName(full), ".deskbox-export-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    writer(stream);
                    stream.Flush(true);
                }
                token.ThrowIfCancellationRequested();
                Require(project, full);
                File.Move(temporary, full);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
