using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using DeskBox.Core;

namespace DeskBox.Fs
{
    /// <summary>
    /// 真实预览：图片走 GDI+（先复制到临时文件避免锁源文件），文本做编码探测，
    /// CSV/XLSX 解析出表格，PDF 只给元信息 + 提示由系统默认程序打开（不假装渲染）。
    /// 任何异常都转成 Kind="meta" + Error，绝不向外抛。
    /// </summary>
    public static class PreviewService
    {
        private const int MaxTableRows = 200;
        private const int MaxTableCols = 30;
        private const int HugeImageSide = 8000;
        private const int ThumbSide = 1600;
        private const long HugeImagePixels = 40000000L;
        private const int PdfScanBytes = 8 * 1024 * 1024;

        private static readonly HashSet<string> ImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico"
        };

        private static readonly HashSet<string> CodeExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".json", ".xml", ".yml", ".yaml", ".ps1", ".bat", ".cmd", ".cs", ".js", ".ts",
            ".html", ".css", ".sql", ".py", ".java", ".cpp", ".h"
        };

        private static readonly HashSet<string> TextExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".log", ".ini"
        };

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        public static PreviewResult Produce(string path, int maxChars = 200000)
        {
            try
            {
                if (maxChars <= 0) maxChars = 200000;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return Fail("路径为空");
                }

                string full;
                try { full = Path.GetFullPath(path); }
                catch (Exception ex) { return Fail("路径无效：" + ex.Message); }

                if (Directory.Exists(full)) return ProduceFolder(full);
                if (!File.Exists(full)) return Fail("文件不存在：" + full);

                var fi = new FileInfo(full);
                string ext = (fi.Extension ?? "").ToLowerInvariant();

                var r = new PreviewResult { Title = fi.Name, Subtitle = "" };

                if (ImageExts.Contains(ext)) return ProduceImage(fi, r, ext);
                if (ext == ".csv" || ext == ".tsv") return ProduceDelimited(fi, r, ext, maxChars);
                if (ext == ".xlsx") return ProduceXlsx(fi, r);
                if (ext == ".pdf") return ProducePdf(fi, r);
                if (CodeExts.Contains(ext)) return ProduceText(fi, r, ext, maxChars, true);
                if (TextExts.Contains(ext)) return ProduceText(fi, r, ext, maxChars, false);

                return ProduceMeta(fi, r, ext);
            }
            catch (Exception ex)
            {
                return Fail("预览失败：" + ex.Message);
            }
        }

        /// <summary>清空缩略图缓存目录（不删缓存目录本身）。失败静默。</summary>
        public static void ClearThumbCache()
        {
            try
            {
                string dir = AppPaths.ThumbDir;
                if (!Directory.Exists(dir)) return;
                foreach (string f in Directory.GetFiles(dir))
                {
                    try { File.Delete(f); } catch { }
                }
                foreach (string d in Directory.GetDirectories(dir))
                {
                    try { Directory.Delete(d, true); } catch { }
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------
        // 图片
        // ------------------------------------------------------------------

        private static PreviewResult ProduceImage(FileInfo fi, PreviewResult r, string ext)
        {
            string tmp = null;
            try
            {
                tmp = Path.Combine(Path.GetTempPath(), "deskbox_prev_" + Guid.NewGuid().ToString("N") + ext);
                File.Copy(fi.FullName, tmp, true);

                using (var src = Image.FromFile(tmp))
                {
                    int w = src.Width;
                    int h = src.Height;
                    bool huge = w > HugeImageSide || h > HugeImageSide || (long)w * h > HugeImagePixels;

                    double scale = 1.0;
                    if (huge)
                    {
                        int longSide = Math.Max(w, h);
                        if (longSide > 0) scale = Math.Min(1.0, (double)ThumbSide / longSide);
                    }

                    r.Image = CloneImage(src, scale);
                    r.Kind = "image";
                    r.Subtitle = ext.TrimStart('.').ToUpperInvariant() + " 图片";
                    r.Meta.Add("尺寸: " + w + " × " + h + " px");
                    r.Meta.Add("大小: " + SizeText(fi.Length));
                    r.Meta.Add("修改时间: " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                    if (huge)
                    {
                        r.Meta.Add("已生成缩略图: 长边 ≤ " + ThumbSide + " px");
                    }
                }
                return r;
            }
            finally
            {
                if (tmp != null)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
            }
        }

        private static Image CloneImage(Image src, double scale)
        {
            if (scale >= 0.999)
            {
                return new Bitmap(src);
            }
            int nw = Math.Max(1, (int)Math.Round(src.Width * scale));
            int nh = Math.Max(1, (int)Math.Round(src.Height * scale));
            var bmp = new Bitmap(nw, nh);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, nw, nh));
            }
            return bmp;
        }

        // ------------------------------------------------------------------
        // 文本 / 代码
        // ------------------------------------------------------------------

        private static PreviewResult ProduceText(FileInfo fi, PreviewResult r, string ext, int maxChars, bool isCode)
        {
            string encLabel;
            bool truncated;
            string text = ReadTextHead(fi, maxChars, out encLabel, out truncated);

            int lines = CountLines(text);
            r.Kind = isCode ? "code" : "text";
            r.Subtitle = (isCode ? "代码" : "文本") + " · " + encLabel + " · " + lines + " 行";
            r.Text = isCode ? NumberLines(text) : text;
            r.Truncated = truncated;
            r.Meta.Add("大小: " + SizeText(fi.Length));
            r.Meta.Add("修改时间: " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
            r.Meta.Add("编码: " + encLabel);
            r.Meta.Add("行数: " + lines);
            if (truncated) r.Meta.Add("内容已截断: 仅显示前 " + maxChars + " 字符");
            return r;
        }

        private static string ReadTextHead(FileInfo fi, int maxChars, out string encodingLabel, out bool truncated)
        {
            bool fileCut;
            byte[] data = ReadHead(fi, maxChars, out fileCut);
            int bom;
            Encoding enc = DetectEncoding(data, data.Length, out bom, out encodingLabel);
            string text = data.Length > bom ? enc.GetString(data, bom, data.Length - bom) : "";
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            text = text.TrimEnd('\uFFFD'); // 截断造成的半个多字节字符

            truncated = fileCut;
            if (text.Length > maxChars)
            {
                text = text.Substring(0, maxChars);
                truncated = true;
            }
            return text;
        }

        private static byte[] ReadHead(FileInfo fi, int maxChars, out bool truncated)
        {
            long want = Math.Max(64L * 1024, (long)maxChars * 4 + 8);
            if (want > 16L * 1024 * 1024) want = 16L * 1024 * 1024;
            if (want >= fi.Length)
            {
                want = fi.Length;
                truncated = false;
            }
            else
            {
                truncated = true;
            }

            var buf = new byte[(int)want];
            using (var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int read = 0;
                while (read < buf.Length)
                {
                    int n = fs.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < buf.Length) Array.Resize(ref buf, read);
            }
            return buf;
        }

        private static Encoding DetectEncoding(byte[] data, int len, out int bomLength, out string label)
        {
            bomLength = 0;
            label = "UTF-8";
            if (len >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                bomLength = 3;
                label = "UTF-8 BOM";
                return new UTF8Encoding(false);
            }
            if (len >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            {
                bomLength = 2;
                label = "UTF-16 LE";
                return Encoding.Unicode;
            }
            if (len >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            {
                bomLength = 2;
                label = "UTF-16 BE";
                return Encoding.BigEndianUnicode;
            }
            if (IsValidUtf8(data, len))
            {
                label = "UTF-8";
                return new UTF8Encoding(false);
            }
            try
            {
                label = "GBK";
                return Encoding.GetEncoding(936);
            }
            catch
            {
                label = "UTF-8（GBK 不可用回退）";
                return new UTF8Encoding(false);
            }
        }

        private static bool IsValidUtf8(byte[] b, int len)
        {
            int i = 0;
            while (i < len)
            {
                byte c = b[i];
                if (c < 0x80) { i++; continue; }

                int need;
                if ((c & 0xE0) == 0xC0) need = 1;
                else if ((c & 0xF0) == 0xE0) need = 2;
                else if ((c & 0xF8) == 0xF0) need = 3;
                else return false;

                if (i + need >= len) return true; // 尾部不完整（读取截断），按合法处理
                for (int k = 1; k <= need; k++)
                {
                    if ((b[i + k] & 0xC0) != 0x80) return false;
                }
                i += need + 1;
            }
            return true;
        }

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int n = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n') n++;
            }
            if (text[text.Length - 1] == '\n') n--;
            return n < 0 ? 0 : n;
        }

        private static string NumberLines(string text)
        {
            var sb = new StringBuilder(text.Length + text.Length / 8 + 16);
            int start = 0;
            int lineNo = 1;
            for (int i = 0; i <= text.Length; i++)
            {
                if (i != text.Length && text[i] != '\n') continue;
                string line = text.Substring(start, i - start);
                if (line.EndsWith("\r", StringComparison.Ordinal)) line = line.Substring(0, line.Length - 1);
                sb.Append(lineNo.ToString(CultureInfo.InvariantCulture).PadLeft(4)).Append(" │ ").Append(line);
                if (i < text.Length) sb.Append('\n');
                start = i + 1;
                lineNo++;
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // CSV / TSV
        // ------------------------------------------------------------------

        private static PreviewResult ProduceDelimited(FileInfo fi, PreviewResult r, string ext, int maxChars)
        {
            string encLabel;
            bool fileCut;
            string text = ReadTextHead(fi, Math.Max(maxChars, 200000), out encLabel, out fileCut);
            char delim = DetectDelimiter(text, ext);
            bool parseCut;
            List<string[]> rows = ParseDelimited(text, delim, out parseCut);

            r.Kind = "table";
            r.Rows = rows;
            r.Truncated = fileCut || parseCut;
            r.Subtitle = (ext == ".tsv" ? "TSV" : "CSV") + " 表格 · " + rows.Count + " 行 × 最多 " + MaxTableCols + " 列";
            r.Meta.Add("大小: " + SizeText(fi.Length));
            r.Meta.Add("修改时间: " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
            r.Meta.Add("编码: " + encLabel);
            r.Meta.Add("分隔符: " + (delim == '\t' ? "Tab" : delim.ToString()));
            r.Meta.Add("显示行数: " + rows.Count);
            if (r.Truncated) r.Meta.Add("已截断: 仅显示前 " + MaxTableRows + " 行 × " + MaxTableCols + " 列");
            return r;
        }

        private static char DetectDelimiter(string text, string ext)
        {
            if (ext == ".tsv") return '\t';
            int end = text.IndexOf('\n');
            string first = end > 0 ? text.Substring(0, end) : text;
            int comma = 0, tab = 0, semi = 0;
            for (int i = 0; i < first.Length; i++)
            {
                if (first[i] == ',') comma++;
                else if (first[i] == '\t') tab++;
                else if (first[i] == ';') semi++;
            }
            if (tab > comma && tab >= semi) return '\t';
            if (semi > comma) return ';';
            return ',';
        }

        private static List<string[]> ParseDelimited(string text, char delim, out bool truncated)
        {
            truncated = false;
            var rows = new List<string[]>();
            var row = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            int i = 0;

            while (i < text.Length)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                            continue;
                        }
                        inQuotes = false;
                        i++;
                        continue;
                    }
                    sb.Append(c);
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                    i++;
                    continue;
                }

                if (c == delim)
                {
                    AddField(row, sb, ref truncated);
                    i++;
                    continue;
                }

                if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    AddField(row, sb, ref truncated);
                    rows.Add(row.ToArray());
                    row.Clear();
                    i++;
                    if (rows.Count >= MaxTableRows)
                    {
                        if (i < text.Length) truncated = true;
                        return rows;
                    }
                    continue;
                }

                sb.Append(c);
                i++;
            }

            if (sb.Length > 0 || row.Count > 0)
            {
                AddField(row, sb, ref truncated);
                rows.Add(row.ToArray());
            }
            return rows;
        }

        private static void AddField(List<string> row, StringBuilder sb, ref bool truncated)
        {
            if (row.Count < MaxTableCols) row.Add(sb.ToString());
            else truncated = true;
            sb.Length = 0;
        }

        // ------------------------------------------------------------------
        // XLSX（只用 System.IO.Compression + XmlReader，不依赖 Excel COM）
        // ------------------------------------------------------------------

        private static PreviewResult ProduceXlsx(FileInfo fi, PreviewResult r)
        {
            try
            {
                var rows = new List<string[]>();
                string sheetName = "";
                bool cut = false;

                using (var zip = ZipFile.OpenRead(fi.FullName))
                {
                    sheetName = ReadFirstSheetName(zip);
                    var shared = new List<string>();
                    var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
                    if (ssEntry != null)
                    {
                        using (var s = ssEntry.Open())
                        {
                            shared = ReadSharedStrings(s);
                        }
                    }

                    ZipArchiveEntry sheet = zip.GetEntry("xl/worksheets/sheet1.xml");
                    if (sheet == null)
                    {
                        foreach (var e in zip.Entries)
                        {
                            if (e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                            {
                                sheet = e;
                                break;
                            }
                        }
                    }
                    if (sheet == null)
                    {
                        r.Kind = "meta";
                        r.Error = "xlsx 中没有找到工作表";
                        AddBasicMeta(r, fi);
                        return r;
                    }
                    using (var s = sheet.Open())
                    {
                        ParseSheet(s, shared, rows, out cut);
                    }
                }

                if (rows.Count == 0)
                {
                    r.Kind = "meta";
                    r.Error = "工作簿为空或没有可解析的单元格";
                    AddBasicMeta(r, fi);
                    return r;
                }

                r.Kind = "table";
                r.Rows = rows;
                r.Truncated = cut;
                r.Subtitle = "Excel 表格" + (string.IsNullOrEmpty(sheetName) ? "" : " · " + sheetName) +
                             " · " + rows.Count + " 行";
                AddBasicMeta(r, fi);
                r.Meta.Add("工作表: " + (string.IsNullOrEmpty(sheetName) ? "sheet1" : sheetName));
                r.Meta.Add("显示行数: " + rows.Count);
                if (cut) r.Meta.Add("已截断: 仅显示前 " + MaxTableRows + " 行 × " + MaxTableCols + " 列");
                return r;
            }
            catch (Exception ex)
            {
                r.Kind = "meta";
                r.Error = "xlsx 解析失败：" + ex.Message;
                AddBasicMeta(r, fi);
                return r;
            }
        }

        private static XmlReaderSettings ReaderSettings()
        {
            var settings = new XmlReaderSettings();
            settings.IgnoreWhitespace = true;
            settings.IgnoreComments = true;
            settings.DtdProcessing = DtdProcessing.Ignore;
            settings.CloseInput = false;
            return settings;
        }

        private static string ReadFirstSheetName(ZipArchive zip)
        {
            try
            {
                var e = zip.GetEntry("xl/workbook.xml");
                if (e == null) return "";
                using (var s = e.Open())
                using (var xr = XmlReader.Create(s, ReaderSettings()))
                {
                    while (xr.Read())
                    {
                        if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "sheet")
                        {
                            return xr.GetAttribute("name") ?? "";
                        }
                    }
                }
            }
            catch { }
            return "";
        }

        private static List<string> ReadSharedStrings(Stream stream)
        {
            var shared = new List<string>();
            using (var xr = XmlReader.Create(stream, ReaderSettings()))
            {
                if (!xr.Read()) return shared;
                StringBuilder cur = null;
                while (true)
                {
                    bool advanced = false;
                    if (xr.NodeType == XmlNodeType.Element)
                    {
                        if (xr.LocalName == "si")
                        {
                            cur = new StringBuilder();
                            if (xr.IsEmptyElement)
                            {
                                shared.Add("");
                                cur = null;
                            }
                        }
                        else if (xr.LocalName == "t" && cur != null)
                        {
                            cur.Append(xr.ReadElementContentAsString());
                            advanced = true;
                        }
                    }
                    else if (xr.NodeType == XmlNodeType.EndElement && xr.LocalName == "si" && cur != null)
                    {
                        shared.Add(cur.ToString());
                        cur = null;
                    }
                    if (!advanced && !xr.Read()) break;
                }
            }
            return shared;
        }

        private static void ParseSheet(Stream stream, List<string> shared, List<string[]> rows, out bool truncated)
        {
            truncated = false;
            using (var xr = XmlReader.Create(stream, ReaderSettings()))
            {
                if (!xr.Read()) return;
                List<string> row = null;
                int rowDepth = -1;
                while (true)
                {
                    if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "row")
                    {
                        if (rows.Count >= MaxTableRows)
                        {
                            truncated = true;
                            break;
                        }
                        row = new List<string>();
                        rowDepth = xr.Depth;
                        if (xr.IsEmptyElement)
                        {
                            rows.Add(row.ToArray());
                            row = null;
                        }
                    }
                    else if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "c" && row != null)
                    {
                        ParseCell(xr, row, shared);
                    }
                    else if (xr.NodeType == XmlNodeType.EndElement && xr.LocalName == "row" && row != null && xr.Depth == rowDepth)
                    {
                        rows.Add(NormalizeRow(row));
                        row = null;
                    }
                    if (!xr.Read()) break;
                }
            }
        }

        private static void ParseCell(XmlReader xr, List<string> row, List<string> shared)
        {
            string cellRef = xr.GetAttribute("r");
            string type = xr.GetAttribute("t");
            int col = ColumnIndex(cellRef);
            if (col < 0) col = row.Count;

            string value = "";
            if (!xr.IsEmptyElement)
            {
                int cellDepth = xr.Depth;
                string v = null;
                var inline = new StringBuilder();
                if (xr.Read())
                {
                    while (true)
                    {
                        if (xr.NodeType == XmlNodeType.EndElement && xr.LocalName == "c" && xr.Depth == cellDepth) break;

                        bool advanced = false;
                        if (xr.NodeType == XmlNodeType.Element)
                        {
                            if (xr.LocalName == "v")
                            {
                                v = xr.ReadElementContentAsString();
                                advanced = true;
                            }
                            else if (xr.LocalName == "t")
                            {
                                inline.Append(xr.ReadElementContentAsString());
                                advanced = true;
                            }
                        }
                        if (!advanced && !xr.Read()) break;
                    }
                }
                value = DecodeCell(type, v, inline.ToString(), shared);
            }

            if (col >= MaxTableCols) return;
            while (row.Count <= col) row.Add("");
            row[col] = value;
        }

        private static string DecodeCell(string type, string v, string inline, List<string> shared)
        {
            if (type == "s")
            {
                int idx;
                if (!string.IsNullOrEmpty(v) &&
                    int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) &&
                    idx >= 0 && idx < shared.Count)
                {
                    return shared[idx];
                }
                return "";
            }
            if (type == "inlineStr") return inline ?? "";
            return v ?? "";
        }

        private static int ColumnIndex(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return -1;
            int n = 0;
            int letters = 0;
            foreach (char c in cellRef)
            {
                if (c >= 'A' && c <= 'Z') { n = n * 26 + (c - 'A' + 1); letters++; }
                else if (c >= 'a' && c <= 'z') { n = n * 26 + (c - 'a' + 1); letters++; }
                else break;
            }
            if (letters == 0) return -1;
            return n - 1;
        }

        private static string[] NormalizeRow(List<string> row)
        {
            int count = row.Count;
            while (count > 0 && string.IsNullOrEmpty(row[count - 1])) count--;
            var arr = new string[count];
            for (int i = 0; i < count; i++) arr[i] = row[i] == null ? "" : row[i];
            return arr;
        }

        // ------------------------------------------------------------------
        // PDF（只给元信息，不渲染）
        // ------------------------------------------------------------------

        private static PreviewResult ProducePdf(FileInfo fi, PreviewResult r)
        {
            int pages = 0;
            try
            {
                int take = (int)Math.Min(fi.Length, (long)PdfScanBytes);
                if (take > 0)
                {
                    var buf = new byte[take];
                    using (var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        int read = 0;
                        while (read < take)
                        {
                            int n = fs.Read(buf, read, take - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        if (read < take) Array.Resize(ref buf, read);
                    }
                    string raw = Encoding.GetEncoding(28591).GetString(buf);
                    pages = Regex.Matches(raw, @"/Type\s*/Page(?![A-Za-z])").Count;
                    if (pages == 0)
                    {
                        Match m = Regex.Match(raw, @"/Count\s+(\d+)");
                        int c;
                        if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out c) &&
                            c > 0 && c < 1000000)
                        {
                            pages = c;
                        }
                    }
                }
            }
            catch { /* 页数只是估算，失败不影响预览 */ }

            r.Kind = "pdf";
            r.Subtitle = pages > 0 ? "PDF 文档 · 约 " + pages + " 页" : "PDF 文档";
            r.Text = "PDF 预览需要外部程序，点下方按钮用系统默认程序打开";
            AddBasicMeta(r, fi);
            if (pages > 0) r.Meta.Add("页数: 约 " + pages + " 页");
            else r.Meta.Add("页数: 未能识别");
            return r;
        }

        // ------------------------------------------------------------------
        // 其他
        // ------------------------------------------------------------------

        private static PreviewResult ProduceMeta(FileInfo fi, PreviewResult r, string ext)
        {
            r.Kind = "meta";
            r.Subtitle = string.IsNullOrEmpty(ext) ? "文件" : ext.TrimStart('.').ToUpperInvariant() + " 文件";
            AddBasicMeta(r, fi);
            r.Meta.Add("扩展名: " + (string.IsNullOrEmpty(ext) ? "(无)" : ext));
            r.Meta.Add("所在目录: " + fi.DirectoryName);
            r.Text = "该类型暂不支持内嵌预览，可用系统默认程序打开。";
            return r;
        }

        private static PreviewResult ProduceFolder(string path)
        {
            var r = new PreviewResult { Kind = "meta", Title = Path.GetFileName(path), Subtitle = "文件夹" };
            try { r.Meta.Add("文件数: " + Directory.GetFiles(path).Length); } catch { }
            try { r.Meta.Add("子目录数: " + Directory.GetDirectories(path).Length); } catch { }
            r.Meta.Add("所在目录: " + Path.GetDirectoryName(path));
            r.Text = "文件夹不支持预览。";
            return r;
        }

        private static void AddBasicMeta(PreviewResult r, FileInfo fi)
        {
            r.Meta.Add("大小: " + SizeText(fi.Length));
            r.Meta.Add("修改时间: " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
        }

        private static PreviewResult Fail(string error)
        {
            return new PreviewResult { Kind = "meta", Error = error };
        }

        private static string SizeText(long size)
        {
            if (size < 1024) return size + " B";
            if (size < 1024L * 1024) return (size / 1024.0).ToString("0.#") + " KB";
            if (size < 1024L * 1024 * 1024) return (size / 1024.0 / 1024).ToString("0.#") + " MB";
            return (size / 1024.0 / 1024 / 1024).ToString("0.##") + " GB";
        }
    }
}
