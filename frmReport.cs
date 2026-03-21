using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace uhr.info.detector
{
    public partial class frmReport : Form
    {
        private class MergeFileDisplayItem
        {
            public string Module { get; set; }
            public string File { get; set; }
            public List<string> Versions { get; set; }
        }

        private string orgName;
        private string fwVersion;
        private string coreVersion;
        private string salaryVersion;
        private string shoteateVersion;
        private string nenchoVersion;
        private int customizeFileCount;
        private List<string> mergeFiles;

        public frmReport(string orgName, string fwVersion, string coreVersion, string salaryVersion, string shoteateVersion, string nenchoVersion, int customizeFileCount, List<string> mergeFiles)
        {
            InitializeComponent();
            this.orgName = orgName;
            this.fwVersion = fwVersion;
            this.coreVersion = coreVersion;
            this.salaryVersion = salaryVersion;
            this.shoteateVersion = shoteateVersion;
            this.nenchoVersion = nenchoVersion;
            this.customizeFileCount = customizeFileCount;
            this.mergeFiles = mergeFiles;

            var mergeFileDisplayItems = BuildMergeFileDisplayItems(mergeFiles);

            // RTF转义处理：处理Unicode字符为RTF兼容格式
            string EscapeRtfText(string text)
            {
                if (string.IsNullOrEmpty(text)) return text;
                
                StringBuilder sb = new StringBuilder();
                foreach (char c in text)
                {
                    if (c == '\\')
                        sb.Append("\\\\");
                    else if (c == '{')
                        sb.Append("\\{");
                    else if (c == '}')
                        sb.Append("\\}");
                    else if (c < 128)  // ASCII字符
                        sb.Append(c);
                    else  // Unicode字符，使用\u编码
                        sb.Append($"\\u{(int)c}?");
                }
                return sb.ToString();
            }

            string FormatMergeFileDisplay(MergeFileDisplayItem item)
            {
                return $"{item.Module} {item.File} ({string.Join(", ", item.Versions)})";
            }

            // 生成RTF报告内容，对所有Unicode字符进行正确转义
            string report =
                "{\\rtf1\\ansi\\deff0 " +
                $"{{\\b {EscapeRtfText(orgName)}}}の調査結果は以下となります：\\line" +
                "{\\b UHRバージョン：}\\line" +
                $"　　{{\\b フレームワークバージョン：}} {EscapeRtfText(fwVersion)}\\line" +
                $"　　{{\\b 共通機能バージョン：}} {EscapeRtfText(coreVersion)}\\line" +
                $"　　{{\\b 給与明細バージョン：}} {EscapeRtfText(salaryVersion)}\\line" +
                $"　　{{\\b 諸手当バージョン：}} {EscapeRtfText(shoteateVersion)}\\line" +
                $"　　{{\\b 年末調整バージョン：}} {EscapeRtfText(nenchoVersion)}\\line" +
                $"{{\\b カスタマイズファイル：}} {(customizeFileCount > 0 ? customizeFileCount.ToString() : "なし")}\\line" +
                "{\\b マージが必要ファイル：}\\line　　" +
                (mergeFileDisplayItems.Count > 0 ? string.Join("\\line　　", mergeFileDisplayItems.Select(x => EscapeRtfText(FormatMergeFileDisplay(x)))) : "なし") +
                "\\line}";
            rtbResult.Rtf = report;
        }

        private void cmdCopyReturn_Click(object sender, EventArgs e)
        {
            var mergeFileDisplayItems = BuildMergeFileDisplayItems(mergeFiles);

            string FormatMergeFileDisplay(MergeFileDisplayItem item)
            {
                return $"{item.Module} {item.File} ({string.Join(", ", item.Versions)})";
            }

            // 生成HTML正文
            string htmlBody = $@"
<b>{orgName}</b>の調査結果は以下となります：<br>
<b>UHRバージョン：</b><br>
　　<b>　　フレームワークバージョン：</b> {fwVersion}<br>
　　<b>　　共通機能バージョン：</b> {coreVersion}<br>
　　<b>　　給与明細バージョン：</b> {salaryVersion}<br>
　　<b>　　諸手当バージョン：</b> {shoteateVersion}<br>
　　<b>　　年末調整バージョン：</b> {nenchoVersion}<br>
<b>カスタマイズファイル：</b> {(customizeFileCount > 0 ? customizeFileCount.ToString() : "なし")}<br>
<b>マージが必要ファイル：</b><br>　　{(mergeFileDisplayItems.Count > 0 ? string.Join("<br>　　", mergeFileDisplayItems.Select(FormatMergeFileDisplay)) : "なし")}
";

            // 标准HTML剪贴板格式
            string htmlTemplate = 
@"Version:1.0
StartHTML:########
EndHTML:########
StartFragment:########
EndFragment:########
<html>
<body>
<!--StartFragment-->
{0}
<!--EndFragment-->
</body>
</html>";

            string html = string.Format(htmlTemplate, htmlBody);

            // 计算各个位置
            int startHTML = html.IndexOf("<html>", StringComparison.Ordinal);
            int endHTML = html.LastIndexOf("</html>", StringComparison.Ordinal) + "</html>".Length;
            int startFragment = html.IndexOf("<!--StartFragment-->", StringComparison.Ordinal) + "<!--StartFragment-->".Length;
            int endFragment = html.IndexOf("<!--EndFragment-->", StringComparison.Ordinal);

            // 替换占位符
            html = html
                .Replace("StartHTML:########", $"StartHTML:{startHTML:D8}")
                .Replace("EndHTML:########", $"EndHTML:{endHTML:D8}")
                .Replace("StartFragment:########", $"StartFragment:{startFragment:D8}")
                .Replace("EndFragment:########", $"EndFragment:{endFragment:D8}");

            DataObject data = new DataObject();
            data.SetData(DataFormats.Html, html);
            data.SetData(DataFormats.Text, rtbResult.Text); // 兼容纯文本
            Clipboard.SetDataObject(data, true);
            this.Close();
        }

        private void cmdReturn_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private List<MergeFileDisplayItem> BuildMergeFileDisplayItems(List<string> rawMergeFiles)
        {
            var result = new List<MergeFileDisplayItem>();
            if (rawMergeFiles == null || rawMergeFiles.Count == 0)
                return result;

            var parsedItems = rawMergeFiles
                .SelectMany(ParseMergeFileItems)
                .ToList();

            var grouped = parsedItems
                .GroupBy(x => new { x.Module, x.File })
                .OrderBy(g => g.Key.File)
                .ThenBy(g => g.Key.Module);

            foreach (var group in grouped)
            {
                var versions = group
                    .Select(x => x.Version)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, Comparer<string>.Create(VersionCompareHelper.CompareVersionStringSmartAsc))
                    .ToList();

                result.Add(new MergeFileDisplayItem
                {
                    Module = group.Key.Module,
                    File = group.Key.File,
                    Versions = versions
                });
            }

            return result;
        }

        private IEnumerable<(string Version, string Module, string File)> ParseMergeFileItems(string item)
        {
            if (string.IsNullOrWhiteSpace(item))
                yield break;

            var rawMatch = System.Text.RegularExpressions.Regex.Match(
                item,
                @"^(?<ver>\S+)\s+(?<module>Core|Salary|Shoteate|YearAdjust)\s+(?<file>.+)$");
            if (rawMatch.Success)
            {
                yield return (
                    rawMatch.Groups["ver"].Value.Trim(),
                    rawMatch.Groups["module"].Value.Trim(),
                    rawMatch.Groups["file"].Value.Trim());
                yield break;
            }

            var groupedMatch = System.Text.RegularExpressions.Regex.Match(
                item,
                @"^(?<module>Core|Salary|Shoteate|YearAdjust)\s+(?<file>.+?)\s+\((?<versions>[^()]*)\)$");
            if (!groupedMatch.Success)
                yield break;

            string module = groupedMatch.Groups["module"].Value.Trim();
            string file = groupedMatch.Groups["file"].Value.Trim();
            string versions = groupedMatch.Groups["versions"].Value.Trim();

            foreach (var version in versions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x)))
            {
                yield return (version, module, file);
            }
        }
    }
}
