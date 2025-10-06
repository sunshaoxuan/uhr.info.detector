using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace uhr.info.detector
{
    public partial class frmMain : Form
    {
        // using System.Diagnostics;
        // using System.Threading.Tasks;
        // using System.IO;
        // using System.Text;

        private sealed class SvnResult
        {
            public int ExitCode { get; set; }
            public string Stdout { get; set; }
            public string Stderr { get; set; }
            public bool TimedOut { get; set; }
        }

        private static readonly Encoding SvnConsoleEncoding = Encoding.GetEncoding(932); // Shift_JIS (CP932)
                                                                                         // 两个常用编码
        private static readonly Encoding EncSjis = Encoding.GetEncoding(932);              // CP932 / Shift_JIS
        private static readonly Encoding EncUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private const string FilePrepareLogName = "FilePrepare.log";

        // 自动判断：优先 UTF-8（含 BOM 或无损解码），否则退回 CP932
        private static string DecodeSvnText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            // 1) BOM 直判 UTF-8
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return EncUtf8.GetString(bytes, 3, bytes.Length - 3);

            // 2) 尝试 UTF-8 无损解码（throwOnInvalidBytes=true 会在遇到非法序列时抛异常）
            try
            {
                var s = EncUtf8.GetString(bytes);
                // 避免某些容错实现塞入 U+FFFD（保险起见再检查一次）
                if (!s.Contains('\uFFFD')) return s;
            }
            catch { /* 不是 UTF-8 */ }

            // 3) 退回 CP932
            return EncSjis.GetString(bytes);
        }

        // 文本类后缀（失败可降级 svn cat）
        private static bool IsTextLikeByExt(string pathOrName)
        {
            var ext = Path.GetExtension(pathOrName)?.ToLowerInvariant();
            return ext == ".htm" || ext == ".html" || ext == ".jsp" || ext == ".js" ||
                   ext == ".css" || ext == ".xml" || ext == ".json" || ext == ".txt" ||
                   ext == ".md" || ext == ".dicon";
        }

        // 稳妥拉取：先 export（带超时和重试），不行且是文本就 cat；全部失败返回 false
        private bool TryFetchToFile(string svnExe, string fullSvnPath, string localPath, int exportTimeoutMs, int catTimeoutMs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? AppDomain.CurrentDomain.BaseDirectory);

            // export 尝试两次
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                var res = RunSvn(svnExe,
                    $"export \"{fullSvnPath}\" \"{localPath}\" --force --quiet --non-interactive --trust-server-cert",
                    exportTimeoutMs);

                if (!res.TimedOut && res.ExitCode == 0)
                {
                    for (int i = 0; i < 25 && !File.Exists(localPath); i++) System.Threading.Thread.Sleep(20);
                    if (File.Exists(localPath))
                    {
                        long a = new FileInfo(localPath).Length; System.Threading.Thread.Sleep(200);
                        long b = new FileInfo(localPath).Length; int tries = 0;
                        while (a != b && tries++ < 3) { a = b; System.Threading.Thread.Sleep(200); b = new FileInfo(localPath).Length; }
                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] export OK: {fullSvnPath} -> {localPath} size={b}\n");
                        return true;
                    }
                }

                SafeLog("ErrorLog.txt",
                    $"[{DateTime.Now:HH:mm:ss}] export NG({attempt}/2) Exit={res.ExitCode} Timeout={res.TimedOut}\nURL={fullSvnPath}\nSTDERR:\n{res.Stderr}\n");
                Application.DoEvents();
            }

            // 文本类降级 cat
            if (IsTextLikeByExt(fullSvnPath))
            {
                var resCat = RunSvn(svnExe, $"cat \"{fullSvnPath}\" --non-interactive --trust-server-cert", catTimeoutMs);
                if (!resCat.TimedOut && resCat.ExitCode == 0 && !string.IsNullOrEmpty(resCat.Stdout))
                {
                    File.WriteAllText(localPath, resCat.Stdout, Encoding.UTF8);
                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] cat OK: {fullSvnPath} -> {localPath} len={resCat.Stdout.Length}\n");
                    return true;
                }
                SafeLog("ErrorLog.txt",
                    $"[{DateTime.Now:HH:mm:ss}] cat NG Exit={resCat.ExitCode} Timeout={resCat.TimedOut}\nURL={fullSvnPath}\nSTDERR:\n{resCat.Stderr}\n");
            }

            return false;
        }

        // 多一个可选编码参数：专用于 --xml 场景强制 UTF-8
        private SvnResult RunSvn(string exe, string args, int timeoutMs, Encoding forcedEncoding = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // 如果指定了强制编码，就用它；否则保持你现有的编码策略
                StandardOutputEncoding = forcedEncoding ?? SvnConsoleEncoding,
                StandardErrorEncoding = forcedEncoding ?? SvnConsoleEncoding,
            };

            using (var p = Process.Start(psi))
            {
                if (p == null) return new SvnResult { ExitCode = -1, Stderr = "Process.Start returned null" };

                var msOut = new MemoryStream();
                var msErr = new MemoryStream();
                var tOut = Task.Run(() => { try { p.StandardOutput.BaseStream.CopyTo(msOut); } catch { } });
                var tErr = Task.Run(() => { try { p.StandardError.BaseStream.CopyTo(msErr); } catch { } });

                var start = DateTime.UtcNow; bool timedOut = false;
                while (!p.WaitForExit(100))
                {
                    if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                    {
                        timedOut = true; try { p.Kill(); p.WaitForExit(3000); } catch { }
                        break;
                    }
                    Application.DoEvents();
                }
                try { Task.WaitAll(new[] { tOut, tErr }, 2000); } catch { }

                // 这里用 psi 上指定的编码来解码
                var enc = forcedEncoding ?? SvnConsoleEncoding;
                return new SvnResult
                {
                    ExitCode = timedOut ? -1 : p.ExitCode,
                    Stdout = enc.GetString(msOut.ToArray()),
                    Stderr = enc.GetString(msErr.ToArray()),
                    TimedOut = timedOut
                };
            }
        }


        // 元の機関リストをキャッシュ
        private List<string> orgListCache = new List<string>();
        // フォームメンバーとしてキャンセル用トークンソースを追加
        private CancellationTokenSource orgChangeCts;
        // MODULE_INFO表のデータをキャッシュ
        private Dictionary<string, ModuleFileInfo> moduleInfoCache = new Dictionary<string, ModuleFileInfo>();

        // MODULE_INFO表的字段定义
        public class ModuleFileInfo
        {
            public string Filename { get; set; }
            public string HashValue { get; set; }
            public string KikanCode { get; set; }
            public string KikanName { get; set; }
            public string FunctionVersion { get; set; }
            public int CustomizedFlag { get; set; }
        }
        public frmMain()
        {
            InitializeComponent();
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            // プロジェクトディレクトリ下の svn.exe が存在するかチェック
            string svnPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
            if (!System.IO.File.Exists(svnPath))
            {
                MessageBox.Show(AppConfig.MSG_SVN_EXE_NOT_FOUND, AppConfig.TITLE_DEPENDENCY_MISSING, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 1. SVN パスを定義
            string svnUrl = AppConfig.SVN_CUSTOMIZED_PATH;
            var res = RunSvn(svnPath, $"list \"{svnUrl}\"", AppConfig.SVN_COMMAND_TIMEOUT_MS);
            if (res.TimedOut)
            {
                MessageBox.Show(AppConfig.MSG_SVN_COMMAND_FAILED + " (timeout)", AppConfig.TITLE_SVN_ERROR, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (res.ExitCode != 0 || !string.IsNullOrWhiteSpace(res.Stderr))
            {
                MessageBox.Show(AppConfig.MSG_SVN_COMMAND_FAILED + res.Stderr, AppConfig.TITLE_SVN_ERROR, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 3. 出力を解析し、_HRDB/ で終わるフォルダを抽出
            var lines = res.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var orgList = new List<string>();
            foreach (var line in lines)
            {
                if (line.EndsWith(AppConfig.HRDB_FOLDER_SUFFIX))
                {
                    var folder = line.TrimEnd('/').Replace("_HRDB", "");
                    int idx2 = folder.IndexOf('_');
                    if (idx2 > 0)
                    {
                        string orgCode2 = folder.Substring(0, idx2);
                        string orgName2 = folder.Substring(idx2 + 1);
                        orgList.Add($"{orgCode2} {orgName2}");
                    }
                }
            }
            orgListCache = orgList;
            lstOrgs.Items.Clear();
            lstOrgs.Items.AddRange(orgList.ToArray());

            // 5. ターゲットバージョンリストの初期化
            InitTargetVersionList();

            // 6. コントロールのロック等は保持（原コード続行）
            txtOrgCode.ReadOnly = true; txtOrgCode.BackColor = Color.White;
            txtOrgName.ReadOnly = true; txtOrgName.BackColor = Color.White;
            txtFWVersion.ReadOnly = true; txtFWVersion.BackColor = Color.White;
            txtSalaryVersion.ReadOnly = true; txtSalaryVersion.BackColor = Color.White;
            txtCoreVersion.ReadOnly = true; txtCoreVersion.BackColor = Color.White;
            txtYearAdjustVersion.ReadOnly = true; txtYearAdjustVersion.BackColor = Color.White;
            cmdShowReport.Enabled = false;
        }

        private void InitTargetVersionList()
        {
            // 1. cboFWTargetVersion を固定値に設定：4.18.4、変更不可
            cboFWTargetVersion.Items.Clear();
            cboFWTargetVersion.Items.Add(AppConfig.FIXED_FW_TARGET_VERSION);
            cboFWTargetVersion.SelectedIndex = AppConfig.DEFAULT_SELECTED_INDEX;
            cboFWTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;

            // 2. SVN ルートパス（従来の共有フォルダの代替）
            string svnBasePath = AppConfig.SVN_RELEASE_ARTIFACTS_PATH;
            string svnPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);

            // 3. Core バージョン：「共通機能」を含むディレクトリを検索
            try
            {
                var coreRoot = GetSvnSubDirectories(svnBasePath, svnPath).FirstOrDefault(d => d.Contains(AppConfig.COMMON_FUNCTION_FOLDER_KEYWORD));
                if (!string.IsNullOrEmpty(coreRoot))
                {
                    string corePath = $"{svnBasePath}/{coreRoot}{AppConfig.CORE_MODULE_PATH}";
                    var dirs = GetSvnSubDirectories(corePath, svnPath)
                        .Select(name => name.StartsWith(AppConfig.VERSION_PREFIX, StringComparison.OrdinalIgnoreCase) ? name.Substring(1) : name)
                        .ToList();
                    dirs.Sort(CompareVersionStringSmartAsc); // 数字桁数優先ソート
                    // ComboBox 表示は降順（新→旧）
                    var displayDirs = dirs.OrderByDescending(x => x, Comparer<string>.Create(CompareVersionStringSmartAsc)).ToList();
                    cboCoreTargetVersion.Items.Clear();
                    foreach (var d in displayDirs)
                        cboCoreTargetVersion.Items.Add(d);
                    if (displayDirs.Count > 0)
                        cboCoreTargetVersion.SelectedIndex = AppConfig.DEFAULT_SELECTED_INDEX;
                }
                else
                {
                    cboCoreTargetVersion.Items.Clear();
                }
                cboCoreTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            }
            catch (Exception ex)
            {
                MessageBox.Show(AppConfig.MSG_CORE_VERSION_GET_FAILED + ex.Message, AppConfig.TITLE_SVN_ERROR, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cboCoreTargetVersion.Items.Clear();
                cboCoreTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            }

            // 4. Salary バージョン：「Web給与明細」を含むディレクトリを検索
            try
            {
                var salaryRoot = GetSvnSubDirectories(svnBasePath, svnPath).FirstOrDefault(d => d.Contains(AppConfig.WEB_SALARY_FOLDER_KEYWORD));
                if (!string.IsNullOrEmpty(salaryRoot))
                {
                    string salaryPath = $"{svnBasePath}/{salaryRoot}{AppConfig.SALARY_MODULE_PATH}";
                    var dirs = GetSvnSubDirectories(salaryPath, svnPath)
                        .Select(name => name.StartsWith(AppConfig.VERSION_PREFIX, StringComparison.OrdinalIgnoreCase) ? name.Substring(1) : name)
                        .ToList();
                    dirs.Sort(CompareVersionStringSmartAsc);
                    // ComboBox 表示は降順（新→旧）
                    var displayDirs = dirs.OrderByDescending(x => x, Comparer<string>.Create(CompareVersionStringSmartAsc)).ToList();
                    cboSalaryTargetVersion.Items.Clear();
                    foreach (var d in displayDirs)
                        cboSalaryTargetVersion.Items.Add(d);
                    if (displayDirs.Count > 0)
                        cboSalaryTargetVersion.SelectedIndex = AppConfig.DEFAULT_SELECTED_INDEX;
                }
                else
                {
                    cboSalaryTargetVersion.Items.Clear();
                }
                cboSalaryTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            }
            catch (Exception ex)
            {
                MessageBox.Show(AppConfig.MSG_SALARY_VERSION_GET_FAILED + ex.Message, AppConfig.TITLE_SVN_ERROR, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cboSalaryTargetVersion.Items.Clear();
                cboSalaryTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            }

            // 5. Nencho バージョン：「年末調整」を含むディレクトリを検索
            try
            {
                var nenchoRoot = GetSvnSubDirectories(svnBasePath, svnPath).FirstOrDefault(d => d.Contains(AppConfig.YEAR_ADJUST_FOLDER_KEYWORD));
                if (!string.IsNullOrEmpty(nenchoRoot))
                {
                    string nenchoPath = $"{svnBasePath}/{nenchoRoot}{AppConfig.YEAR_ADJUST_MODULE_PATH}";
                    var dirs = GetSvnSubDirectories(nenchoPath, svnPath)
                        .Select(name => name.StartsWith(AppConfig.VERSION_PREFIX, StringComparison.OrdinalIgnoreCase) ? name.Substring(1) : name)
                        .ToList();
                    dirs.Sort(CompareVersionStringSmartAsc);
                    // ComboBox 表示は降順（新→旧）
                    var displayDirs = dirs.OrderByDescending(x => x, Comparer<string>.Create(CompareVersionStringSmartAsc)).ToList();
                    cboYearAdjustTargetVersion.Items.Clear();
                    foreach (var d in displayDirs)
                        cboYearAdjustTargetVersion.Items.Add(d);
                    if (displayDirs.Count > 0)
                        cboYearAdjustTargetVersion.SelectedIndex = AppConfig.DEFAULT_SELECTED_INDEX;
                }
                else
                {
                    cboYearAdjustTargetVersion.Items.Clear();
                }
                cboYearAdjustTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            }
            catch (Exception ex)
            {
                MessageBox.Show(AppConfig.MSG_YEAR_ADJUST_VERSION_GET_FAILED + ex.Message, AppConfig.TITLE_SVN_ERROR, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cboYearAdjustTargetVersion.Items.Clear();
                cboYearAdjustTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            }
        }

        // SVN ディレクトリ下のサブディレクトリリストを取得
        private List<string> GetSvnSubDirectories(string svnUrl, string svnExePath)
        {
            try
            {
                var res = RunSvn(svnExePath, $"list --xml \"{svnUrl}\"", AppConfig.SVN_COMMAND_TIMEOUT_MS, Encoding.UTF8);
                if (res.TimedOut || res.ExitCode != 0) throw new Exception(res.Stderr ?? "SVN timeout");
                var xdoc = System.Xml.Linq.XDocument.Parse(res.Stdout);
                return xdoc.Descendants("entry")
                           .Where(e => (string)e.Attribute("kind") == "dir")
                           .Select(e => (string)e.Element("name"))
                           .ToList();

            }
            catch (Exception ex)
            {
                throw new Exception(AppConfig.MSG_SVN_SUBDIRECTORY_GET_FAILED + ex.Message);
            }
        }

        // 递归列出某个 URL 下的所有“文件”（不含目录），返回相对路径（含子目录）
        private List<string> GetSvnFileListXmlRecursive(string svnUrl, string svnExePath, int timeoutMs = 120000)
        {
            var res = RunSvn(svnExePath, $"list --xml -R \"{svnUrl}\"", timeoutMs, Encoding.UTF8);
            if (res.TimedOut) throw new Exception("SVN列表命令超时");
            if (res.ExitCode != 0) throw new Exception($"SVN列表命令失败(ExitCode={res.ExitCode}): {res.Stderr}");
            if (string.IsNullOrWhiteSpace(res.Stdout)) return new List<string>();

            var xdoc = System.Xml.Linq.XDocument.Parse(res.Stdout);
            // <entry kind="file"><name>path/to/file.ext</name></entry>
            var files = xdoc.Descendants("entry")
                            .Where(e => (string)e.Attribute("kind") == "file")
                            .Select(e => (string)e.Element("name"))
                            .Where(n => !string.IsNullOrWhiteSpace(n))
                            .ToList();
            return files;
        }

        // SVN ディレクトリ下の「ファイル」リストを取得（再帰、相対パスを返す）
        private List<string> GetSvnFileList(string svnUrl, string svnExePath)
        {
            try
            {
                // -R で再帰、--xml は UTF-8 固定出力
                var res = RunSvn(svnExePath, $"list --xml -R \"{svnUrl}\"", AppConfig.SVN_COMMAND_TIMEOUT_MS, Encoding.UTF8);
                if (res.TimedOut) throw new Exception("SVN一覧タイムアウト");
                if (res.ExitCode != 0) throw new Exception($"SVN一覧失敗(ExitCode={res.ExitCode}): {res.Stderr}");
                if (string.IsNullOrWhiteSpace(res.Stdout)) return new List<string>();

                var xdoc = System.Xml.Linq.XDocument.Parse(res.Stdout);
                // <entry kind="file"><name>path/to/file.ext</name></entry>
                return xdoc.Descendants("entry")
                           .Where(e => (string)e.Attribute("kind") == "file")
                           .Select(e => (string)e.Element("name"))
                           .Where(n => !string.IsNullOrWhiteSpace(n))
                           .ToList();
            }
            catch (Exception ex)
            {
                throw new Exception(AppConfig.MSG_SVN_FILE_LIST_GET_FAILED + ex.Message);
            }
        }


        // 汎用バージョン比較とファイル衝突チェックメソッド
        private Task CheckModuleVersionConflicts(List<(string file, string ver, string module)> mergeFiles,
            List<string> customizedFiles,
            string currentVersion,
            string targetVersion,
            string moduleName,
            string svnBasePath,
            string svnPath,
            string folderKeyword,
            string modulePath,
            CancellationToken token)
        {
            return Task.Run(() =>
            {
                CheckModuleVersionConflictsSync(mergeFiles, customizedFiles, currentVersion, targetVersion, moduleName, svnBasePath, svnPath, folderKeyword, modulePath, token);
            }, token);
        }

        private void CheckModuleVersionConflictsSync(List<(string file, string ver, string module)> mergeFiles,
            List<string> customizedFiles,
            string currentVersion,
            string targetVersion,
            string moduleName,
            string svnBasePath,
            string svnPath,
            string folderKeyword,
            string modulePath,
            CancellationToken token)
        {
            if (string.IsNullOrEmpty(currentVersion) || string.IsNullOrEmpty(targetVersion) || currentVersion == targetVersion)
                return;

            tslStatus.Text = $"{moduleName}バージョン比較中: {currentVersion} → {targetVersion}";

            var rootDirectories = GetSvnSubDirectories(svnBasePath, svnPath);
            System.Diagnostics.Debug.WriteLine($"{moduleName}検索対象ディレクトリ: {string.Join(", ", rootDirectories)}");
            System.Diagnostics.Debug.WriteLine($"{moduleName}検索キーワード: {folderKeyword}");

            var moduleRoot = rootDirectories.FirstOrDefault(d => d.Contains(folderKeyword));
            if (!string.IsNullOrEmpty(moduleRoot))
            {
                System.Diagnostics.Debug.WriteLine($"{moduleName}ルートディレクトリ見つかりました: {moduleRoot}");

                string moduleFullPath = $"{svnBasePath}/{moduleRoot}{modulePath}";
                System.Diagnostics.Debug.WriteLine($"{moduleName}パス: {moduleFullPath}");

                var versionDirectories = GetSvnSubDirectories(moduleFullPath, svnPath);
                System.Diagnostics.Debug.WriteLine($"{moduleName}バージョンディレクトリ: {string.Join(", ", versionDirectories)}");

                // バージョン名と元のフォルダ名のマッピングを作成
                var versionToFolderMap = new Dictionary<string, string>();
                var allVersions = new List<string>();

                foreach (var folderName in versionDirectories)
                {
                    string versionName = folderName.StartsWith(AppConfig.VERSION_PREFIX, StringComparison.OrdinalIgnoreCase)
                        ? folderName.Substring(1)
                        : folderName;
                    versionToFolderMap[versionName] = folderName;
                    allVersions.Add(versionName);
                }

                // バージョンリストを昇順でソート
                allVersions.Sort(CompareVersionStringSmartAsc);
                System.Diagnostics.Debug.WriteLine($"{moduleName}全バージョン: {string.Join(", ", allVersions)}");

                // スマートバージョンマッチング
                int idxCur = FindSmartVersionIndex(allVersions, currentVersion, moduleName);
                int idxTarget = FindSmartVersionIndex(allVersions, targetVersion, moduleName);
                System.Diagnostics.Debug.WriteLine($"{moduleName}現在バージョンインデックス: {idxCur}, 目標バージョンインデックス: {idxTarget}");

                if (idxCur >= 0 && idxTarget >= 0 && idxCur != idxTarget)
                {
                    List<string> needVers;
                    if (idxCur < idxTarget)
                    {
                        // アップグレード：(cur, target]
                        needVers = allVersions.GetRange(idxCur + 1, idxTarget - idxCur);
                    }
                    else
                    {
                        // ダウングレード：(target, cur]
                        needVers = allVersions.GetRange(idxTarget + 1, idxCur - idxTarget);
                    }

                    System.Diagnostics.Debug.WriteLine($"{moduleName}確認が必要なバージョン: {string.Join(", ", needVers)}");

                    foreach (var ver in needVers)
                    {
                        token.ThrowIfCancellationRequested();
                        tslStatus.Text = $"{moduleName} {ver} のファイルを確認中...";

                        // 元のフォルダ名を使用してパスを構築
                        if (versionToFolderMap.TryGetValue(ver, out string originalFolderName))
                        {
                            string versionPath = $"{moduleFullPath}/{originalFolderName}";
                            System.Diagnostics.Debug.WriteLine($"{moduleName}使用パス: {versionPath}");

                            var files = GetSvnFileListXmlRecursive(versionPath, svnPath);
                            System.Diagnostics.Debug.WriteLine($"{moduleName} {ver} ファイル数: {files.Count}");

                            foreach (var file in files)
                            {
                                string fileName = System.IO.Path.GetFileName(file);
                                if (customizedFiles.Contains(fileName))
                                {
                                    System.Diagnostics.Debug.WriteLine($"{moduleName}衝突ファイル発見: {fileName} ({ver})");
                                    mergeFiles.Add((fileName, ver, moduleName));
                                }
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"{moduleName}フォルダ名マッピングが見つかりません: {ver}");
                        }
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"{moduleName}ルートディレクトリが見つかりませんでした。キーワード: {folderKeyword}");
            }
        }

        // 汎用スマートバージョンマッチング（年末調整以外でも使用可能）
        private int FindSmartVersionIndex(List<string> availableVersions, string targetVersion, string moduleName)
        {
            if (string.IsNullOrEmpty(targetVersion))
                return -1;

            // 1. 完全一致を試す
            int exactMatch = availableVersions.FindIndex(v => v.Equals(targetVersion, StringComparison.OrdinalIgnoreCase));
            if (exactMatch >= 0)
            {
                System.Diagnostics.Debug.WriteLine($"{moduleName}完全一致: {targetVersion} -> {availableVersions[exactMatch]}");
                return exactMatch;
            }

            // 2. ベースバージョン（+記号より前の部分）でマッチング
            string baseVersion = targetVersion.Split('+')[0].Trim();
            int baseMatch = availableVersions.FindIndex(v => v.StartsWith(baseVersion, StringComparison.OrdinalIgnoreCase));
            if (baseMatch >= 0)
            {
                System.Diagnostics.Debug.WriteLine($"{moduleName}ベースバージョン一致: {targetVersion} -> {availableVersions[baseMatch]} (ベース: {baseVersion})");
                return baseMatch;
            }

            // 3. 部分一致（最も近いバージョンを見つける）
            var baseVersionParts = baseVersion.Split('.');
            int bestMatch = -1;
            int bestScore = -1;

            for (int i = 0; i < availableVersions.Count; i++)
            {
                string availableVersion = availableVersions[i];
                var availableParts = availableVersion.Split('.');

                // 主要バージョン番号（最初の2つ）でスコア計算
                int score = 0;
                int minParts = Math.Min(baseVersionParts.Length, availableParts.Length);

                for (int j = 0; j < Math.Min(minParts, 2); j++) // 最初の2つの部分のみ比較
                {
                    if (int.TryParse(baseVersionParts[j], out int baseNum) &&
                        int.TryParse(availableParts[j], out int availableNum))
                    {
                        if (baseNum == availableNum)
                            score += 10; // 完全一致
                        else if (Math.Abs(baseNum - availableNum) == 1)
                            score += 5;  // 1つ違い
                        else
                            score += Math.Max(0, 5 - Math.Abs(baseNum - availableNum)); // 距離に応じて減点
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = i;
                }
            }

            if (bestMatch >= 0 && bestScore > 0)
            {
                System.Diagnostics.Debug.WriteLine($"{moduleName}最適マッチ: {targetVersion} -> {availableVersions[bestMatch]} (スコア: {bestScore})");
                return bestMatch;
            }

            System.Diagnostics.Debug.WriteLine($"{moduleName}マッチなし: {targetVersion}");
            return -1;
        }

        // 年末調整用のスマートバージョンマッチング（後方互換性のため残す）
        private int FindYearAdjustVersionIndex(List<string> availableVersions, string targetVersion)
        {
            return FindSmartVersionIndex(availableVersions, targetVersion, "YearAdjust");
        }

        // バージョン文字列の昇順比較（例: 2.9.1 < 2.10.4 < 2.11.3）
        private int CompareVersionStringSmartAsc(string a, string b)
        {
            var pa = a.Split('.');
            var pb = b.Split('.');
            int len = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                int va = i < pa.Length && int.TryParse(pa[i], out var tva) ? tva : 0;
                int vb = i < pb.Length && int.TryParse(pb[i], out var tvb) ? tvb : 0;
                if (va != vb) return va.CompareTo(vb); // 昇順
            }
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase); // 昇順
        }

        private void txtOrgFilter_TextChanged(object sender, EventArgs e)
        {
            string filterText = txtOrgFilter.Text.Trim();
            lstOrgs.Items.Clear();

            if (string.IsNullOrEmpty(filterText))
            {
                // フィルタが空の場合、すべてのアイテムを表示
                lstOrgs.Items.AddRange(orgListCache.ToArray());
            }
            else
            {
                // フィルタテキストに基づいて項目を絞り込み
                var filteredItems = orgListCache.Where(item =>
                    item.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                lstOrgs.Items.AddRange(filteredItems.ToArray());
            }

            // フィルタ後に項目がある場合、最初の項目を選択
            if (lstOrgs.Items.Count > 0)
            {
                lstOrgs.SelectedIndex = AppConfig.FIRST_ITEM_INDEX;
            }
        }

        private void lstOrgs_DoubleClick(object sender, EventArgs e)
        {
            if (lstOrgs.SelectedItem != null)
            {
                cmdSearch_Click(sender, e);
            }
        }

        private void lstOrgs_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && lstOrgs.SelectedItem != null)
            {
                cmdSearch_Click(sender, e);
            }
        }

        private async void cmdSearch_Click(object sender, EventArgs e)
        {
            // 前回のキャンセルを実行
            orgChangeCts?.Cancel();
            orgChangeCts = new CancellationTokenSource();
            var token = orgChangeCts.Token;
            SwitchFilePrepareButton(false, 3); // ボタンを非表示に

            try
            {
                // 選択項目の確認
                if (lstOrgs.SelectedItem == null)
                {
                    MessageBox.Show("機関を選択してください。", "選択エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 1. 選択項目から機関コードと機関名を取得
                string item = lstOrgs.SelectedItem.ToString();
                int idx = item.IndexOf(' ');
                if (idx <= 0)
                {
                    MessageBox.Show("機関情報の形式が正しくありません。", "データエラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string orgCode = item.Substring(0, idx);
                string orgName = item.Substring(idx + 1);

                // 2. 機関情報を設定
                txtOrgCode.Text = orgCode;
                txtOrgName.Text = orgName;

                // 3. バージョン情報とファイルリスト関連の TextBox と ListBox の内容をクリア
                txtFWVersion.Clear();
                txtSalaryVersion.Clear();
                txtCoreVersion.Clear();
                txtYearAdjustVersion.Clear();
                lstCustomizedFile.Items.Clear();
                lstMergeNeedsFile.Items.Clear();

                // 重要：背景色を即座にリセット
                txtFWVersion.BackColor = Color.White;
                txtSalaryVersion.BackColor = Color.White;
                txtCoreVersion.BackColor = Color.White;
                txtYearAdjustVersion.BackColor = Color.White;
                lblCustomizedFileCount.Text = "";
                lblFWCustomized.Text = ""; // 追加：FWカスタマイズラベルもクリア

                // 追加：レポートボタンを無効化
                cmdShowReport.Enabled = false;

                // ProgressBar のテキストクリア
                tslStatus.Text = "";

                // 4. 最初にバージョン情報を取得（非同期処理）
                await GatherModuleVersionAsync();
                Application.DoEvents(); // 界面更新

                token.ThrowIfCancellationRequested();

                // 5. 次にカスタマイズファイルリストを取得（非同期処理）GetSvnFileListXmlRecursive
                var customizedFiles = await GatherCustomizedFileListAsync();
                token.ThrowIfCancellationRequested();
                if (lstCustomizedFile.Items.Count == 0)
                {
                    // カスタマイズファイルがない場合、終了
                    tslStatus.Text = "カスタマイズファイルが見つかりませんでした。";
                    SwitchFilePrepareButton(false, 1); // マージファイル準備ボタンを非表示に
                    return;
                }
                else
                {
                    SwitchFilePrepareButton(true, 1); // カスタマイズファイル準備ボタンを表示
                    tslStatus.Text = $"カスタマイズファイルが {lstCustomizedFile.Items.Count} 個見つかりました。";

                    // 6. 最後にファイル比較を実行（非同期処理）
                    await CompareFiles(customizedFiles, token);

                    if (lstMergeNeedsFile.Items.Count > 0)
                    {
                        SwitchFilePrepareButton(true, 2); // マージファイル準備ボタンを表示
                        tslStatus.Text = $"カスタマイズファイルと競合するファイルが {lstMergeNeedsFile.Items.Count} 個見つかりました。";
                    }
                    else
                    {
                        SwitchFilePrepareButton(false, 2); // マージファイル準備ボタンを非表示に
                        tslStatus.Text = "カスタマイズファイルと競合するファイルは見つかりませんでした。";
                    }
                }

            }
            catch (OperationCanceledException)
            {
                SwitchFilePrepareButton(false, 3); // キャンセルされた場合、ボタンを非表示に戻す

                // キャンセルされた場合、状態をクリア
                tslStatus.Text = "操作がキャンセルされました。";
            }
            catch (Exception ex)
            {
                SwitchFilePrepareButton(false, 3); // エラー時にボタンを非表示に戻す

                tslStatus.Text = "エラーが発生しました: " + ex.Message;
                MessageBox.Show(ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SwitchFilePrepareButton(bool enabled, int buttonSpecific)
        {
            if (cmdFilePrepare.Enabled != enabled && (buttonSpecific == 1 || buttonSpecific == 3))
            {
                cmdFilePrepare.Enabled = enabled;
            }

            if (cmdMergeFilePrepare.Enabled != enabled && (buttonSpecific == 2 || buttonSpecific == 3))
            {
                cmdMergeFilePrepare.Enabled = enabled;
            }
        }

        private async Task<List<string>> GatherCustomizedFileListAsync()
        {
            var result = new List<string>();
            string orgCode = txtOrgCode.Text.Trim();
            if (string.IsNullOrEmpty(orgCode)) return result;

            // 检查缓存中是否已经有数据，如果没有则从数据库读取完整信息
            if (!await ReadModuleInfoFromDatabase(orgCode))
                return result;

            tslStatus.Text = "カスタマイズファイルを読み込み中...";
            Application.UseWaitCursor = true;
            lstCustomizedFile.Items.Clear();

            // 从缓存中提取客户化文件并显示
            result = moduleInfoCache.Values
                .Where(info => info.CustomizedFlag == 1)
                .OrderBy(info => info.Filename)
                .Select(info => info.Filename)
                .ToList();

            // 界面显示
            Application.UseWaitCursor = false;
            this.Cursor = Cursors.Default;

            if (result.Count > 0)
            {
                foreach (var item in result)
                    lstCustomizedFile.Items.Add(item);
                tslStatus.Text = $"カスタマイズファイル: {result.Count} 個が見つかりました。";
                lblCustomizedFileCount.Text = $"({result.Count})";

                // 追加：smart-company開始のjarファイルをチェック
                bool hasSmartCompanyJar = result.Any(file =>
                    file.ToLower().EndsWith(".jar") &&
                    System.IO.Path.GetFileName(file).ToLower().StartsWith("smart-company"));

                if (hasSmartCompanyJar)
                {
                    lblFWCustomized.Text = "カスタマイズ済";
                }
            }
            else
            {
                lstCustomizedFile.Items.Add("カスタマイズファイル情報が見つかりませんでした");
                tslStatus.Text = "カスタマイズファイルが見つかりませんでした。";
                lblCustomizedFileCount.Text = "";
            }

            return result;
        }

        // 从数据库读取MODULE_INFO表完整数据并缓存
        private async Task<bool> ReadModuleInfoFromDatabase(string orgCode)
        {
            string tableName = $"module_info_{orgCode}";
            string connStr = AppConfig.ORACLE_CONNECTION_STRING;
            string sql = $"select FILENAME, HASHVALUE, KIKANCD, KIKANNM, FUNCTIONVER, CUSTOMIZEDFLG from {tableName} order by FILENAME";

            tslStatus.Text = "MODULE_INFOデータを読み込み中...";
            Application.UseWaitCursor = true;

            try
            {
                using (var conn = new OracleConnection(connStr))
                {
                    await conn.OpenAsync();
                    using (var cmd = new OracleCommand(sql, conn))
                    {
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            moduleInfoCache.Clear(); // 清空之前缓存
                            while (await reader.ReadAsync())
                            {
                                var fileInfo = new ModuleFileInfo
                                {
                                    Filename = reader["FILENAME"]?.ToString() ?? "",
                                    HashValue = reader["HASHVALUE"]?.ToString() ?? "",
                                    KikanCode = reader["KIKANCD"]?.ToString() ?? "",
                                    KikanName = reader["KIKANNM"]?.ToString() ?? "",
                                    FunctionVersion = reader["FUNCTIONVER"]?.ToString() ?? "",
                                    CustomizedFlag = Convert.ToInt32(reader["CUSTOMIZEDFLG"] ?? 0)
                                };
                                moduleInfoCache[fileInfo.Filename] = fileInfo;
                            }
                        }
                    }
                }
                return true;
            }
            catch (OracleException ex)
            {
                // ORA-00942: 表またはビューが存在しません / ORA-04043: オブジェクトが存在しません
                if (ex.Message.Contains(AppConfig.ORACLE_ERROR_TABLE_NOT_EXISTS) || ex.Message.Contains(AppConfig.ORACLE_ERROR_OBJECT_NOT_EXISTS))
                {
                    // テーブルが存在しない場合、别の形式を试す
                    tableName = tableName.ToUpper();
                    sql = $"select FILENAME, HASHVALUE, KIKANCD, KIKANNM, FUNCTIONVER, CUSTOMIZEDFLG from {tableName} order by FILENAME";

                    try
                    {
                        using (var conn = new OracleConnection(connStr))
                        {
                            await conn.OpenAsync();
                            using (var cmd = new OracleCommand(sql, conn))
                            {
                                using (var reader = await cmd.ExecuteReaderAsync())
                                {
                                    moduleInfoCache.Clear(); // 清空之前缓存
                                    while (await reader.ReadAsync())
                                    {
                                        var fileInfo = new ModuleFileInfo
                                        {
                                            Filename = reader["FILENAME"]?.ToString() ?? "",
                                            HashValue = reader["HASHVALUE"]?.ToString() ?? "",
                                            KikanCode = reader["KIKANCD"]?.ToString() ?? "",
                                            KikanName = reader["KIKANNM"]?.ToString() ?? "",
                                            FunctionVersion = reader["FUNCTIONVER"]?.ToString() ?? "",
                                            CustomizedFlag = Convert.ToInt32(reader["CUSTOMIZEDFLG"] ?? 0)
                                        };
                                        moduleInfoCache[fileInfo.Filename] = fileInfo;
                                    }
                                }
                            }
                        }
                        return true;
                    }
                    catch (OracleException)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            finally
            {
                Application.UseWaitCursor = false;
                this.Cursor = Cursors.Default;
            }
        }

        // MD5 HASH値を計算（改良版、より多くの保護機能を追加）
        private string CalculateFileHash(string filePath)
        {
            try
            {
                // ファイルの存在とアクセス可能性を複数チェック
                if (string.IsNullOrEmpty(filePath))
                {
                    System.Diagnostics.Debug.WriteLine("CalculateFileHash: 文件路径为空");
                    return "";
                }

                if (!File.Exists(filePath))
                {
                    System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 文件不存在: {filePath}");
                    return "";
                }

                // 检查文件大小，避免处理过大的文件
                FileInfo fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 50 * 1024 * 1024) // 50MB限制
                {
                    System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 文件过大({fileInfo.Length / 1024 / 1024}MB): {filePath}");
                    return "";
                }

                // 对于极小的文件，也可能是空文件
                if (fileInfo.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 文件为空: {filePath}");
                    return "empty_file_hash";
                }

                System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 开始计算 {filePath} (大小: {fileInfo.Length} bytes)");

                // 等待文件释放，防止被其他进程占用
                int retryCount = 0;
                while (retryCount < 5)
                {
                    try
                    {
                        // 尝试多种方式读取文件
                        byte[] fileBytes = null;

                        // 方法1: 直接读字节（最安全的方式）
                        try
                        {
                            fileBytes = File.ReadAllBytes(filePath);
                            System.Diagnostics.Debug.WriteLine($"CalculateFileHash: バイト読み込み成功: {fileBytes.Length} bytes");
                        }
                        catch (Exception readEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"CalculateFileHash: バイト読み込み失敗: {readEx.Message}");

                            // 方法2: 尝试stream读取
                            using (var stream = File.OpenRead(filePath))
                            {
                                using (var ms = new MemoryStream())
                                {
                                    stream.CopyTo(ms);
                                    fileBytes = ms.ToArray();
                                }
                            }
                        }

                        // 对文件字节内容计算HASH
                        using (var md5 = MD5.Create())
                        {
                            byte[] hashBytes = md5.ComputeHash(fileBytes);
                            string hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                            System.Diagnostics.Debug.WriteLine($"CalculateFileHash: HASH计算成功: {hash}");
                            return hash;
                        }
                    }
                    catch (IOException ioEx) when (retryCount < 4)
                    {
                        // 文件被占用，等待后重试
                        retryCount++;
                        System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 文件被占用，重试 {retryCount}/5: {ioEx.Message}");
                        System.Threading.Thread.Sleep(500); // 等待500ms
                    }
                    catch (UnauthorizedAccessException authEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 访问权限拒绝: {authEx.Message}");
                        return "";
                    }
                    catch (Exception otherEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 其他错误: {otherEx.Message}");
                        break;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 重试次数用尽，HASH计算失败");
                return "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 总体异常: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 堆栈: {ex.StackTrace}");
                // 不显示MessageBox，改为记录调试信息，避免阻塞
                return "";
            }
        }

        // 从SVN下载文件到临时目录（同步 + 并发排水STDOUT/STDERR + 引号 + 超时 + --quiet）
        private string DownloadFileFromSVN(string fullSvnPath, string fileName)
        {
            string svnExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
            string tempDir = Path.Combine(Path.GetTempPath(), "uhr_svn_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempDir);
            string targetPath = Path.Combine(tempDir, fileName);

            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] export開始: {fileName}\n  URL={fullSvnPath}\n  OUT={targetPath}\n");

            if (!File.Exists(svnExe))
            {
                SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVN実行ファイル不存在: {svnExe}\n");
                return "";
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = svnExe,
                Arguments = $"export \"{fullSvnPath}\" \"{targetPath}\" --force --quiet --non-interactive --trust-server-cert",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = SvnConsoleEncoding,   // ★ 新增：按 CP932 解码
                StandardErrorEncoding = SvnConsoleEncoding    // ★ 新增
            };

            try
            {
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    if (proc == null)
                    {
                        SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] プロセス起動失敗\n");
                        return "";
                    }

                    // ★ 新增：给 timeoutMs 一个明确的值（用配置或常量）
                    int timeoutMs = AppConfig.SVN_COMMAND_TIMEOUT_MS * 2; // 比 list 稍长一些；你也可以用 120_000

                    var stdoutMs = new MemoryStream();
                    var stderrMs = new MemoryStream();

                    var stdoutTask = System.Threading.Tasks.Task.Run(() =>
                    {
                        try { proc.StandardOutput.BaseStream.CopyTo(stdoutMs); } catch { }
                    });
                    var stderrTask = System.Threading.Tasks.Task.Run(() =>
                    {
                        try { proc.StandardError.BaseStream.CopyTo(stderrMs); } catch { }
                    });

                    var start = DateTime.UtcNow;
                    while (!proc.WaitForExit(100))
                    {
                        if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                        {
                            try { proc.Kill(); proc.WaitForExit(5000); } catch { }

                            // ★★★ 超时兜底：文本类降级走 svn cat，成功就直接当成下载成功 ★★★
                            if (IsTextLikeByExt(fullSvnPath))
                            {
                                string svnExeLocal = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
                                var catRes = RunSvn(svnExeLocal,
                                                    $"cat \"{fullSvnPath}\" --non-interactive --trust-server-cert",
                                                    AppConfig.SVN_COMMAND_TIMEOUT_MS);
                                if (!catRes.TimedOut && catRes.ExitCode == 0 && !string.IsNullOrEmpty(catRes.Stdout))
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? Path.GetTempPath());
                                    File.WriteAllText(targetPath, catRes.Stdout, Encoding.UTF8);
                                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] cat OK: {fileName} -> {targetPath}\n");
                                    return targetPath; // ★ 成功：把 cat 的结果当作下载成功
                                }

                                // cat 失败也记录下来
                                SafeLog("ErrorLog.txt",
                                    $"[{DateTime.Now:HH:mm:ss}] cat NG Exit={catRes.ExitCode} Timeout={catRes.TimedOut}\nURL={fullSvnPath}\nSTDERR:\n{catRes.Stderr}\n");
                            }

                            // 原有行为：记录 export 超时并返回空
                            SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] svn export タイムアウト: {fullSvnPath}\n");
                            return "";
                        }
                        Application.DoEvents();
                    }


                    // … 等 stdoutTask / stderrTask 收尾之后：
                    try { stdoutTask.Wait(2000); } catch { }
                    try { stderrTask.Wait(2000); } catch { }

                    byte[] rawOut = stdoutMs.ToArray();
                    byte[] rawErr = stderrMs.ToArray();

                    string stdoutStr = DecodeSvnText(rawOut);
                    string stderrStr = DecodeSvnText(rawErr);

                    if (proc.ExitCode != 0)
                    {
                        SafeLog("ErrorLog.txt",
                            $"[{DateTime.Now:HH:mm:ss}] export失敗(ExitCode={proc.ExitCode}) URL={fullSvnPath}\n" +
                            $"STDERR:\n{(stderrStr.Length > 400 ? stderrStr.Substring(0, 400) + "..." : stderrStr)}\n");
                        return "";
                    }
                }

                for (int i = 0; i < 25 && !File.Exists(targetPath); i++) System.Threading.Thread.Sleep(20);
                if (!File.Exists(targetPath))
                {
                    SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] export終了だが出力ファイル不存在: {targetPath}\n");
                    return "";
                }

                long a = new FileInfo(targetPath).Length;
                System.Threading.Thread.Sleep(200);
                long b = new FileInfo(targetPath).Length;
                int tries = 0;
                while (a != b && tries++ < 3)
                {
                    a = b; System.Threading.Thread.Sleep(200); b = new FileInfo(targetPath).Length;
                }

                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] export成功: {fileName} size={new FileInfo(targetPath).Length}\n");
                return targetPath;
            }
            catch (Exception ex)
            {
                SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] DownloadFileFromSVN 例外: {ex.Message}\n");
                return "";
            }
        }


        // 查找机构对应的SVN文件夹
        private string FindOrgSvnFolder(string orgCode)
        {
            try
            {
                string svnPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
                string baseSvnUrl = AppConfig.SVN_CUSTOMIZED_PATH;

                // 用 --xml 固定 UTF-8 输出；RunSvn 强制 UTF-8 解码
                var res = RunSvn(svnPath, $"list --xml \"{baseSvnUrl}\"", AppConfig.SVN_COMMAND_TIMEOUT_MS * 5, Encoding.UTF8);
                if (res.TimedOut) throw new Exception("SVN列表命令超时");
                if (res.ExitCode != 0) throw new Exception($"SVN列表命令失败(ExitCode={res.ExitCode}): {res.Stderr}");
                if (string.IsNullOrWhiteSpace(res.Stdout)) throw new Exception($"SVN未返回任何结果: {baseSvnUrl}");

                // 解析 XML：<entry kind="dir"><name>xxxx</name></entry>
                var xdoc = System.Xml.Linq.XDocument.Parse(res.Stdout);
                var names = xdoc.Descendants("entry")
                                .Where(e => (string)e.Attribute("kind") == "dir")
                                .Select(e => (string)e.Element("name"))
                                .Where(s => !string.IsNullOrEmpty(s))
                                .ToList();

                // 优先 *_HRAP，其次 *_HRDB
                string orgFolder =
                    names.FirstOrDefault(n => n.StartsWith($"{orgCode}_", StringComparison.OrdinalIgnoreCase) && n.EndsWith("_HRAP")) ??
                    names.FirstOrDefault(n => n.StartsWith($"{orgCode}_", StringComparison.OrdinalIgnoreCase) && n.EndsWith("_HRDB"));

                if (string.IsNullOrEmpty(orgFolder))
                    throw new Exception($"未找到机构代码 '{orgCode}' 对应的SVN文件夹。可用文件夹: {string.Join(", ", names)}");

                System.Diagnostics.Debug.WriteLine($"找到的机构文件夹: {orgFolder}");
                return orgFolder; // ← 仍然返回
            }
            catch (Exception ex)
            {
                throw new Exception($"查找机构SVN文件夹失败: {ex.Message}");
            }
        }



        // SVNキャッシュ関連フィールド
        private Dictionary<string, List<string>> svnFileListCache = new Dictionary<string, List<string>>();

        // SVN URLパスを取得（動的に正しいフォルダーを検索）
        private string GetSvnUrl()
        {
            string orgCode = txtOrgCode.Text.Trim();
            string orgFolder = FindOrgSvnFolder(orgCode);
            System.Diagnostics.Debug.WriteLine($"見つけたorgFolderの元の値: '{orgFolder}'");

            string baseOrgUrl = $"{AppConfig.SVN_CUSTOMIZED_PATH}{orgFolder}";
            System.Diagnostics.Debug.WriteLine($"基機関URL: '{baseOrgUrl}'");

            // 機構フォルダ以下の構造を確認
            string svnPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
            var subdirs = GetSvnSubDirectories(baseOrgUrl, svnPath);
            System.Diagnostics.Debug.WriteLine($"機構フォルダのサブディレクトリ: {string.Join(", ", subdirs)}");

            // uhrとROOTの両方をチェックして優先順位を決める
            string uhrPath = "";
            string rootPath = "";

            foreach (var subdir in subdirs)
            {
                if (subdir.Equals("uhr", StringComparison.OrdinalIgnoreCase))
                {
                    uhrPath = $"{baseOrgUrl}/uhr";
                }
                else if (subdir.Equals("ROOT", StringComparison.OrdinalIgnoreCase))
                {
                    rootPath = $"{baseOrgUrl}/ROOT";
                }
            }

            // 優先順位: uhr > ROOT > 基ディレクトリ
            if (!string.IsNullOrEmpty(uhrPath))
            {
                System.Diagnostics.Debug.WriteLine($"uhrサブディレクトリを使用: {uhrPath}");
                return uhrPath;
            }
            else if (!string.IsNullOrEmpty(rootPath))
            {
                System.Diagnostics.Debug.WriteLine($"ROOTサブディレクトリを使用: {rootPath}");
                return rootPath;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"サブディレクトリが見つからない、基ディレクトリを使用: {baseOrgUrl}");
                return baseOrgUrl;
            }
        }

        // 获取SVN中的所有文件列表
        private List<string> GetAllFilesFromSvn(string svnUrl, string svnPath)
        {
            try
            {
                var res = RunSvn(svnPath, $"list --xml -R \"{svnUrl}\"",
                                 AppConfig.SVN_COMMAND_TIMEOUT_MS * 5, Encoding.UTF8);
                if (res.TimedOut) throw new Exception("SVN列表命令超时");
                if (res.ExitCode != 0) throw new Exception($"SVN列表命令失败(ExitCode={res.ExitCode}): {res.Stderr}");
                if (string.IsNullOrWhiteSpace(res.Stdout))
                    throw new Exception($"SVN命令未返回任何结果，请检查路径是否正确: {svnUrl}");

                var xdoc = System.Xml.Linq.XDocument.Parse(res.Stdout);
                return xdoc.Descendants("entry")
                           .Where(e => (string)e.Attribute("kind") == "file")
                           .Select(e => (string)e.Element("name"))
                           .Where(n => !string.IsNullOrWhiteSpace(n))
                           .ToList();
            }
            catch (Exception ex)
            {
                SafeLog("DebugLog.txt", $"获取SVN文件列表失败: {ex.Message}\n");
                return new List<string>();
            }
        }

        // 查找与HASH匹配的文件在SVN中的完整路径
        private string FindFileInSVNByHash(string fileName, string targetHash, string orgCode)
        {
            try
            {
                string svnPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);

                // 先检查SVN可用性
                if (string.IsNullOrEmpty(GetSVNCodePath()))
                {
                    MessageBox.Show("SVNのパスが取得できません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return "";
                }

                // 构建SVN URL
                string svnBaseUrl = GetSvnUrl();
                System.Diagnostics.Debug.WriteLine($"SVN URL: {svnBaseUrl}");

                // 缓存文件列表映射
                string cacheKey = svnBaseUrl;

                if (!svnFileListCache.TryGetValue(cacheKey, out List<string> allFiles))
                {
                    tslStatus.Text = $"初回SVN全ファイルリスト取得中: {fileName}";
                    Application.DoEvents();

                    allFiles = GetAllFilesFromSvn(svnBaseUrl, svnPath);
                    svnFileListCache[cacheKey] = allFiles;

                    tslStatus.Text = $"SVNファイルキャッシュ完了: {allFiles.Count}個のファイルを発見";
                    Application.DoEvents();
                }
                else
                {
                    tslStatus.Text = $"SVNキャッシュ命中: {fileName} ({allFiles.Count}個のファイル)";
                    Application.DoEvents();
                }

                // 查找所有同名 файлов
                var candidates = allFiles.Where(file => System.IO.Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase)).ToList();

                // デバッグ情報：SVNファイル一覧の内容を確認
                System.Diagnostics.Debug.WriteLine($"SVNファイル一覧の最初の10個のファイル：");
                foreach (var file in allFiles.Take(10))
                {
                    System.Diagnostics.Debug.WriteLine($"  - {file}");
                }

                if (!candidates.Any())
                {
                    // SVNキャッシュでファイルが見つからない、デバッグ情報を出力
                    System.Diagnostics.Debug.WriteLine($"ファイル'{fileName}'がSVNファイル一覧で見つかりません。総ファイル数: {allFiles.Count}");
                    return "";
                }

                tslStatus.Text = $"HASH比較中: {fileName} ({candidates.Count}個の候補)";
                Application.DoEvents();

                // 检查每个候选文件的HASH
                for (int idx = 0; idx < candidates.Count; idx++)
                {
                    try
                    {
                        var candidate = candidates[idx];
                        tslStatus.Text = $"HASH比較中: {fileName} ({idx + 1}/{candidates.Count})";
                        Application.DoEvents();

                        // URL构造时要小心处理斜杠和编码
                        string fullSvnPath = svnBaseUrl.TrimEnd('/') + "/" + candidate;

                        if (string.IsNullOrWhiteSpace(fullSvnPath))
                        {
                            SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] 検索結果为空，跳过\n");
                            continue;
                        }

                        // 详细的URL调试信息 - 特别标记hituyoushorui.htm
                        if (fileName.ToLower().Contains("hituyoushorui"))
                        {
                            SafeLog("DebugLog.txt", $"[{DateTime.Now}] *** 特殊ファイル *** URL構築: svnBaseUrl='{svnBaseUrl}', candidate='{candidate}', fullSvnPath='{fullSvnPath}'\n");
                            System.Diagnostics.Debug.WriteLine($"*** 特殊ファイル hituyoushorui *** URL: {fullSvnPath}");
                        }
                        else
                        {
                            SafeLog("DebugLog.txt", $"[{DateTime.Now}] URL構築: svnBaseUrl='{svnBaseUrl}', candidate='{candidate}', fullSvnPath='{fullSvnPath}'\n");
                        }
                        System.Diagnostics.Debug.WriteLine($"下载完整SVN路径: '{fullSvnPath}'");
                        System.Diagnostics.Debug.WriteLine($"candidate文件名: '{candidate}'");

                        // URL 构造完得到 fullSvnPath 之后，用下面这一段替换原来的 tempFile 逻辑
                        string fileHash = "";
                        bool ok = false;

                        // 文本/脚本类优先走 cat
                        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
                        bool preferCat = ext == ".js" || ext == ".css" || ext == ".htm" || ext == ".html" ||
                                         ext == ".json" || ext == ".xml" || ext == ".txt" || ext == ".md";

                        if (preferCat)
                        {
                            ok = SafeTryCatHash(fullSvnPath, out fileHash);
                            if (!ok) SafeLog("DebugLog.txt", $"[hash] cat失败→回退export: {fullSvnPath}\n");
                        }

                        // 回退 export 求 hash
                        if (!ok)
                        {
                            ok = SafeTryExportHash(fullSvnPath, fileName, out fileHash);
                        }

                        // 失败就跳过，绝不继续后续操作
                        if (!ok || string.IsNullOrEmpty(fileHash))
                        {
                            SafeLog("ErrorLog.txt", $"[hash] 取 hash 失败，跳过: {fullSvnPath}\n");
                            continue;
                        }

                        // 命中即返回
                        if (!string.IsNullOrEmpty(targetHash) &&
                            string.Equals(fileHash, targetHash, StringComparison.OrdinalIgnoreCase))
                        {
                            SafeLog("DebugLog.txt", $"[match] 命中: {fileName} == {fullSvnPath}\n");
                            return fullSvnPath;
                        }

                    }
                    catch (Exception ex)
                    {
                        SafeLog("ErrorLog.txt", $"[candidate] 未知异常（已跳过）: {candidates[idx]}\n{ex}\n");
                        continue;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"未找到匹配HASH的文件'{fileName}'，目标HASH: {targetHash}");
                return "";
            }
            catch (Exception ex)
            {
                SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] FindFileInSVNByHash 例外: {fileName}, {ex.Message}\n");
                return "";
            }
        }

        private bool TryParseMergeListItem(string item, out string ver, out string module, out string file)
        {
            ver = module = file = "";
            if (string.IsNullOrWhiteSpace(item)) return false;
            // 版本（连续非空白） + 空格 + 模块名（Core|Salary|YearAdjust）+ 空格 + 文件名（其余全部）
            var m = System.Text.RegularExpressions.Regex.Match(item, @"^(?<ver>\S+)\s+(?<module>Core|Salary|YearAdjust)\s+(?<file>.+)$");
            if (!m.Success) return false;
            ver = m.Groups["ver"].Value.Trim();
            module = m.Groups["module"].Value.Trim();
            file = m.Groups["file"].Value.Trim();
            return true;
        }

        private string ResolveModuleVersionUrl(string moduleName, string version)
        {
            string svnBasePath = AppConfig.SVN_RELEASE_ARTIFACTS_PATH;
            string svnExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);

            // 选择该模块的关键字与子路径
            string folderKeyword, modulePath;
            switch (moduleName)
            {
                case "Core":
                    folderKeyword = AppConfig.COMMON_FUNCTION_FOLDER_KEYWORD;
                    modulePath = AppConfig.CORE_MODULE_PATH;
                    break;
                case "Salary":
                    folderKeyword = AppConfig.WEB_SALARY_FOLDER_KEYWORD;
                    modulePath = AppConfig.SALARY_MODULE_PATH;
                    break;
                case "YearAdjust":
                    folderKeyword = AppConfig.YEAR_ADJUST_FOLDER_KEYWORD;
                    modulePath = AppConfig.YEAR_ADJUST_MODULE_PATH;
                    break;
                default:
                    throw new Exception($"未知模块: {moduleName}");
            }

            // 1) 在发布库根下找到该模块的根目录（包含关键字的那个目录名）
            var rootDirectories = GetSvnSubDirectories(svnBasePath, svnExe);  // 已是 XML + UTF-8 解析
            var moduleRoot = rootDirectories.FirstOrDefault(d => d.Contains(folderKeyword));
            if (string.IsNullOrEmpty(moduleRoot))
                throw new Exception($"{moduleName} 的根目录未找到（关键字: {folderKeyword}）");

            // 2) 拼出模块的版本父目录
            string moduleFullPath = $"{svnBasePath}/{moduleRoot}{modulePath}";

            // 3) 列出该模块下的所有“版本目录”，并建立 显示版本 -> 真正目录名 的映射
            var versionDirs = GetSvnSubDirectories(moduleFullPath, svnExe); // 纯目录名列表
            if (versionDirs.Count == 0)
                throw new Exception($"{moduleName} 没有任何版本目录");

            string prefix = AppConfig.VERSION_PREFIX;   // 你项目里已有
            var versionToFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folderName in versionDirs)
            {
                string displayName = (!string.IsNullOrEmpty(prefix) && folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                     ? folderName.Substring(prefix.Length)
                                     : folderName;
                // 后写覆盖前写无所谓，名称一般唯一
                versionToFolder[displayName] = folderName;
            }

            if (!versionToFolder.TryGetValue(version, out string folder))
                throw new Exception($"{moduleName} 未找到版本 '{version}' 对应的目录（可用: {string.Join(", ", versionToFolder.Keys)}）");

            // 4) 返回最终的版本目录 URL
            return $"{moduleFullPath}/{folder}";
        }


        private async Task CompareFiles(List<string> customizedFiles, CancellationToken token)
        {
            Application.UseWaitCursor = true;
            tslStatus.Text = "ファイルを比較中...";

            try
            {
                // 1. 現在のバージョンとターゲットバージョンを取得
                string currentCoreVer = txtCoreVersion.Text.Trim();
                string targetCoreVer = cboCoreTargetVersion.SelectedItem?.ToString() ?? "";
                string currentSalaryVer = txtSalaryVersion.Text.Trim();
                string targetSalaryVer = cboSalaryTargetVersion.SelectedItem?.ToString() ?? "";
                string currentYearAdjustVer = txtYearAdjustVersion.Text.Trim();
                string targetYearAdjustVer = cboYearAdjustTargetVersion.SelectedItem?.ToString() ?? "";

                // 2. SVN ルートパス（従来の共有フォルダの代替）
                string svnBasePath = AppConfig.SVN_RELEASE_ARTIFACTS_PATH;
                string svnPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);

                var mergeFiles = new List<(string file, string ver, string module)>();

                // 3. Core バージョン比較
                await CheckModuleVersionConflicts(mergeFiles, customizedFiles, currentCoreVer, targetCoreVer,
                    "Core", svnBasePath, svnPath, AppConfig.COMMON_FUNCTION_FOLDER_KEYWORD,
                    AppConfig.CORE_MODULE_PATH, token);

                // 4. Salary バージョン比較
                await CheckModuleVersionConflicts(mergeFiles, customizedFiles, currentSalaryVer, targetSalaryVer,
                    "Salary", svnBasePath, svnPath, AppConfig.WEB_SALARY_FOLDER_KEYWORD,
                    AppConfig.SALARY_MODULE_PATH, token);

                // 5. YearAdjust バージョン比較
                await CheckModuleVersionConflicts(mergeFiles, customizedFiles, currentYearAdjustVer, targetYearAdjustVer,
                    "YearAdjust", svnBasePath, svnPath, AppConfig.YEAR_ADJUST_FOLDER_KEYWORD,
                    AppConfig.YEAR_ADJUST_MODULE_PATH, token);

                // 6. 結果を表示
                lstMergeNeedsFile.Items.Clear();
                if (mergeFiles.Any())
                {
                    int maxVerLen = mergeFiles.Max(x => x.ver.Length);
                    int maxModuleLen = mergeFiles.Max(x => x.module.Length);

                    foreach (var (file, ver, module) in mergeFiles.OrderBy(x => x.file).ThenBy(x => x.ver))
                    {
                        string verPadded = ver.PadRight(maxVerLen);
                        string modulePadded = module.PadRight(maxModuleLen);
                        lstMergeNeedsFile.Items.Add($"{verPadded} {modulePadded} {file}");
                    }
                }

                // 6. レポートボタンを有効化
                cmdShowReport.Enabled = true;
                tslStatus.Text = mergeFiles.Any() ? $"マージが必要なファイル: {mergeFiles.Count} 個" : "マージが必要なファイルはありません。";
            }
            catch (OperationCanceledException)
            {
                // キャンセル時は何もしない
                tslStatus.Text = "操作がキャンセルされました。";
            }
            catch (Exception ex)
            {
                MessageBox.Show(AppConfig.MSG_FILE_COMPARE_FAILED + ex.Message, AppConfig.TITLE_ERROR, MessageBoxButtons.OK, MessageBoxIcon.Error);
                tslStatus.Text = "ファイル比較でエラーが発生しました。";
            }
            finally
            {
                Application.UseWaitCursor = false;
                this.Cursor = Cursors.Default;
            }
        }

        private async Task GatherModuleVersionAsync()
        {
            tslStatus.Text = "バージョン情報を取得中...";
            Application.UseWaitCursor = true;

            string orgCode = txtOrgCode.Text.Trim();
            string orgFolder = $"{orgCode}_{txtOrgName.Text.Trim()}_HRDB";

            // SVN パス：SQL ファイル
            string sqlSvnPath = $"{AppConfig.SVN_CUSTOMIZED_PATH}{orgFolder}{AppConfig.SQL_FILE_PATH_FORMAT}";
            string svnPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);

            try
            {
                string sqlContent = await Task.Run(() =>
                {
                    var res = RunSvn(svnPath, $"cat \"{sqlSvnPath}\"", AppConfig.SVN_COMMAND_TIMEOUT_MS);
                    if (res.TimedOut) return null;
                    if (res.ExitCode != 0) return null;
                    if (!string.IsNullOrWhiteSpace(res.Stderr)) return null;
                    return res.Stdout;
                });

                if (string.IsNullOrEmpty(sqlContent))
                {
                    txtFWVersion.Text = "(SVN取得失敗)";
                    txtSalaryVersion.Text = "(SVN取得失敗)";
                    txtCoreVersion.Text = "(SVN取得失敗)";
                    txtYearAdjustVersion.Text = "(SVN取得失敗)";
                    return;
                }

                // SQL ファイルから特定の属性名を検索
                var lines = sqlContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                int versionCount = 0;

                foreach (var line in lines)
                {
                    if (line.ToUpper().Contains("INSERT") && line.ToUpper().Contains("CONF_SYSCONTROL"))
                    {
                        string propertyName = ExtractPropertyName(line);

                        // FrameVersion を検索（大文字小文字無視）
                        if (propertyName.Equals("FrameVersion", StringComparison.OrdinalIgnoreCase))
                        {
                            string value = ExtractValue(line);
                            if (!string.IsNullOrEmpty(value))
                            {
                                txtFWVersion.Text = value;
                                versionCount++;
                            }
                        }
                        // UhrCore_Version を検索（大文字小文字無視）
                        else if (propertyName.Equals("UhrCore_Version", StringComparison.OrdinalIgnoreCase))
                        {
                            string value = ExtractValue(line);
                            if (!string.IsNullOrEmpty(value))
                            {
                                txtCoreVersion.Text = value;
                                versionCount++;
                            }
                        }
                        // UhrSalary_Version を検索（大文字小文字無視）
                        else if (propertyName.Equals("UhrSalary_Version", StringComparison.OrdinalIgnoreCase))
                        {
                            string value = ExtractValue(line);
                            if (!string.IsNullOrEmpty(value))
                            {
                                txtSalaryVersion.Text = value;
                                versionCount++;
                            }
                        }
                        // UhrNencho_Version を検索（大文字小文字無視）
                        else if (propertyName.Equals("UhrNencho_Version", StringComparison.OrdinalIgnoreCase))
                        {
                            string value = ExtractValue(line);
                            if (!string.IsNullOrEmpty(value))
                            {
                                txtYearAdjustVersion.Text = value;
                                versionCount++;
                            }
                        }
                    }
                }

                tslStatus.Text = $"バージョン情報 {versionCount} 個を取得しました。";

                // バージョン取得後、即座に背景色をチェック
                SetVersionBackgroundColors();
            }
            catch (Exception ex)
            {
                txtFWVersion.Text = "(SVN取得失敗)";
                txtSalaryVersion.Text = "(SVN取得失敗)";
                txtCoreVersion.Text = "(SVN取得失敗)";
                txtYearAdjustVersion.Text = "(SVN取得失敗)";
                tslStatus.Text = $"バージョン取得エラー: {ex.Message}";
            }
            finally
            {
                Application.UseWaitCursor = false;
                this.Cursor = Cursors.Default;
            }
        }

        // 補助メソッド：INSERT 文から CS_CPROPERTYVALUE フィールドの値を抽出（フィールド順序可変、フィールド名大文字小文字無視）
        private string ExtractValue(string insertLine)
        {
            // 1. フィールド名部分とフィールド値部分を抽出
            // 想定フォーマット：INSERT INTO ... (<fields>) VALUES (<values>);
            var fieldsMatch = System.Text.RegularExpressions.Regex.Match(insertLine, "\\(([^\\)]*)\\)\\s*VALUES", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var valuesMatch = System.Text.RegularExpressions.Regex.Match(insertLine, "VALUES\\s*\\(([^\\)]*)\\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!fieldsMatch.Success || !valuesMatch.Success)
                return string.Empty;

            var fields = fieldsMatch.Groups[1].Value.Split(',').Select(f => f.Trim().Trim('"', '\'', '`')).ToList();
            var values = valuesMatch.Groups[1].Value.Split(',').Select(v => v.Trim()).ToList();

            // 2. CS_CPROPERTYVALUE のインデックスを検索（大文字小文字無視）
            int idx = fields.FindIndex(f => f.Equals("CS_CPROPERTYVALUE", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx < values.Count)
            {
                // 単一引用符を削除
                var val = values[idx];
                if ((val.StartsWith("'") && val.EndsWith("'")) || (val.StartsWith("\"") && val.EndsWith("\"")))
                    val = val.Substring(1, val.Length - 2);
                return val;
            }
            return string.Empty;
        }

        private string ExtractPropertyName(string insertLine)
        {
            // 1. フィールド名部分とフィールド値部分を抽出
            // 想定フォーマット：INSERT INTO ... (<fields>) VALUES (<values>);
            var fieldsMatch = System.Text.RegularExpressions.Regex.Match(insertLine, "\\(([^\\)]*)\\)\\s*VALUES", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var valuesMatch = System.Text.RegularExpressions.Regex.Match(insertLine, "VALUES\\s*\\(([^\\)]*)\\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!fieldsMatch.Success || !valuesMatch.Success)
                return string.Empty;

            var fields = fieldsMatch.Groups[1].Value.Split(',').Select(f => f.Trim().Trim('"', '\'', '`')).ToList();
            var values = valuesMatch.Groups[1].Value.Split(',').Select(v => v.Trim()).ToList();

            // 2. CS_CPROPERTYNAME のインデックスを検索（大文字小文字無視）
            int idx = fields.FindIndex(f => f.Equals("CS_CPROPERTYNAME", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx < values.Count)
            {
                // 単一引用符を削除
                var val = values[idx];
                if ((val.StartsWith("'") && val.EndsWith("'")) || (val.StartsWith("\"") && val.EndsWith("\"")))
                    val = val.Substring(1, val.Length - 2);
                return val;
            }
            return string.Empty;
        }

        // バージョン背景色設定（旧コードと同じロジック）
        private void SetVersionBackgroundColors()
        {
            // フレームワークバージョンのチェック
            if (txtFWVersion.Text == cboFWTargetVersion.Text)
                txtFWVersion.BackColor = Color.LightGreen;
            else
                txtFWVersion.BackColor = Color.White;

            // 共通機能バージョンのチェック
            if (txtCoreVersion.Text == cboCoreTargetVersion.Text)
                txtCoreVersion.BackColor = Color.LightGreen;
            else
                txtCoreVersion.BackColor = Color.White;

            // Web給与明細バージョンのチェック
            if (txtSalaryVersion.Text == cboSalaryTargetVersion.Text)
                txtSalaryVersion.BackColor = Color.LightGreen;
            else
                txtSalaryVersion.BackColor = Color.White;

            // 年末調整バージョンのチェック
            if (txtYearAdjustVersion.Text == cboYearAdjustTargetVersion.Text)
                txtYearAdjustVersion.BackColor = Color.LightGreen;
            else
                txtYearAdjustVersion.BackColor = Color.White;
        }

        private void cmdShowReport_Click(object sender, EventArgs e)
        {
            // 変数を収集
            string orgName = txtOrgName.Text.Trim();
            string fwVersion = txtFWVersion.Text.Trim();
            string coreVersion = txtCoreVersion.Text.Trim();
            string salaryVersion = txtSalaryVersion.Text.Trim();
            string nenchoVersion = txtYearAdjustVersion.Text.Trim();

            // 修正：客户化文件数量判断逻辑
            int customizeFileCount = 0;
            if (!string.IsNullOrEmpty(lblCustomizedFileCount.Text) && lblCustomizedFileCount.Text != "")
            {
                if (int.TryParse(lblCustomizedFileCount.Text.Replace("(", "").Replace(")", ""), out int cnt))
                {
                    customizeFileCount = cnt;
                }
            }
            // 如果lblCustomizedFileCount为空，表示没有客户化文件，count应该是0

            var mergeFiles = new List<string>();
            foreach (var item in lstMergeNeedsFile.Items)
                mergeFiles.Add(item.ToString());
            // レポートウィンドウを開く
            using (var dlg = new frmReport(orgName, fwVersion, coreVersion, salaryVersion, nenchoVersion, customizeFileCount, mergeFiles))
            {
                dlg.ShowDialog(this);
            }
        }

        private string GetSVNCodePath()
        {
            // TODO: 1. SVN存在性のチェック
            if (!CheckSVNAvailable()) { return string.Empty; }

            // TODO: 2. 機関コードに基づいてモジュール位置を特定します。規則：XXXX_YYYYYYY_HRAP\uhr
            //          XXXX は顧客コードです。名称が必ずしも正確とは限らないため、コードであいまい一致し、
            //          末尾が「_HRAP」のフォルダを特定してください。
            //          その配下の「uhr」フォルダが当該機関のモジュール位置です。
            // SVNのサーバー接続確認
            string result = string.Empty;
            string svnPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
            string orgCode = txtOrgCode.Text.Trim();
            string orgFolder = $"{orgCode}_{txtOrgName.Text.Trim()}_HRAP";
            string sqlSvnPath = $"{AppConfig.SVN_CUSTOMIZED_PATH}{orgFolder}{AppConfig.UHR_MODULE_PATH}";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = svnPath,
                Arguments = $"list \"{sqlSvnPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                MessageBox.Show(AppConfig.MSG_SVN_EXE_NOT_FOUND, AppConfig.TITLE_DEPENDENCY_MISSING, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return result;
            }
            else
            {
                result = svnPath;
            }

            return result;
        }

        // 清理临时文件夹
        private void CleanupTempFolders()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var tempFolders = Directory.GetDirectories(baseDir, "temp_*");
                foreach (var tempFolder in tempFolders)
                {
                    try
                    {
                        Directory.Delete(tempFolder, true);
                        System.Diagnostics.Debug.WriteLine($"デレートされた一時フォルダー: {tempFolder}");
                    }
                    catch
                    {
                        // 一时フォルダーの削除に失敗した場合は無視
                    }
                }
            }
            catch
            {
                // 全体のクリーンアップが失敗した場合は無視
            }
        }

        // 下载 lstMergeNeedsFile 的全部项（不是选中）——真正落地时只用 TryFetchToFile 这一个通道
        private void DownloadAllFromMergeList()
        {
            if (lstMergeNeedsFile.Items.Count == 0)
            {
                MessageBox.Show("列表为空，没有可下载的文件。");
                return;
            }

            string svnExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
            string outRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "UHR_MERGE_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(outRoot);

            int exportTimeout = AppConfig.SVN_COMMAND_TIMEOUT_MS * 3; // 大件给长一点
            int catTimeout = AppConfig.SVN_COMMAND_TIMEOUT_MS;

            int ok = 0, ng = 0;

            // 为减少远端调用：缓存 版本目录URL → 文件清单
            var versionUrlCache = new Dictionary<(string module, string ver), string>((IEqualityComparer<(string module, string ver)>)StringComparer.OrdinalIgnoreCase);
            var fileListCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lstMergeNeedsFile.Items.Count; i++)
            {
                try
                {
                    Application.DoEvents();

                    // 列表项格式："{verPadded} {modulePadded} {file}"
                    if (!TryParseMergeListItem(lstMergeNeedsFile.Items[i].ToString(), out var ver, out var module, out var fileName))
                    {
                        SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] 解析列表项失败，跳过：{lstMergeNeedsFile.Items[i]}\n");
                        ng++;
                        continue;
                    }


                    // 1) 定位“模块+版本”的远端版本目录 URL（已实现的工具函数）
                    if (!versionUrlCache.TryGetValue((module, ver), out var versionUrl))
                    {
                        versionUrl = ResolveModuleVersionUrl(module, ver); // e.g. .../<模块>/<v4.18.4 或 4.18.4>
                        versionUrlCache[(module, ver)] = versionUrl;
                    }

                    // 2) 在该版本目录下拿“全部文件相对路径”清单（XML -R，UTF-8；你已实现）
                    if (!fileListCache.TryGetValue(versionUrl, out var relFiles))
                    {
                        relFiles = GetSvnFileListXmlRecursive(versionUrl, svnExe);
                        fileListCache[versionUrl] = relFiles;
                    }

                    // 3) 用“裸文件名”匹配（通常命中 1 个；若多个相同文件名，全部下载）
                    var matches = relFiles.Where(p => Path.GetFileName(p)
                                           .Equals(fileName, StringComparison.OrdinalIgnoreCase))
                                           .ToList();

                    if (matches.Count == 0)
                    {
                        SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] 版本中未找到同名文件：{fileName} @ {module} {ver}\n");
                        ng++;
                        continue; // ★短路
                    }

                    // 4) 逐个匹配项落地下载 —— 统一通过 TryFetchToFile
                    foreach (var rel in matches)
                    {
                        string url = versionUrl.TrimEnd('/') + "/" + rel.Replace("\\", "/");
                        string local = Path.Combine(outRoot, module, ver, rel.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(local) ?? outRoot);

                        bool fetched = TryFetchToFile(svnExe, url, local, exportTimeout, catTimeout);
                        if (!fetched)
                        {
                            SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] ダウンロード失敗(跳过): {url}\n");
                            ng++;                 // 计入失败
                            continue;             // ★ 失败短路，直接处理下一个文件
                        }

                        ok++;                     // 只有成功才 +1

                    }
                }
                catch (Exception ex)
                {
                    SafeLog("ErrorLog.txt",
                        $"[{DateTime.Now:HH:mm:ss}] 单项处理异常(已跳过): {lstMergeNeedsFile.Items[i]}\n{ex}\n");
                    continue; // ★关键：失败不停机，只跳过该文件
                }
            }

            MessageBox.Show($"下载完成：成功 {ok} 个，失败 {ng} 个。\n保存到：{outRoot}", "完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }



        private void cmdFilePrepare_Click(object sender, EventArgs e)
        {
            // 设置鼠标忙状态
            this.Cursor = Cursors.WaitCursor;
            cmdFilePrepare.Enabled = false;

            // 开始时清理旧的临时文件夹
            CleanupTempFolders();

            // 保存ディレクトリを外で定義（例外時でもアクセス可能にするため）
            string saveDir = "";

            // 処理開始ログ
            System.IO.File.WriteAllText("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] === カスタマイズファイル処理開始 ===\n");

            try
            {
                if (string.IsNullOrEmpty(GetSVNCodePath()))
                {
                    MessageBox.Show("SVNのパスが取得できません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string orgCode = txtOrgCode.Text.Trim();
                if (string.IsNullOrEmpty(orgCode))
                {
                    MessageBox.Show("機関コードが設定されていません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 准备保存目录 - 按机构创建子文件夹
                string orgName = txtOrgName.Text.Trim();
                saveDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{orgCode}_{orgName}_CustomizedFiles");

                // 如果已存在，先删除
                if (Directory.Exists(saveDir))
                {
                    Directory.Delete(saveDir, true);
                }
                Directory.CreateDirectory(saveDir);

                tslStatus.Text = "カスタマイズファイルを準備中...";

                // 未找到文件的记录リスト
                var notFoundFiles = new List<string>();
                int successCount = 0;

                // 迭代处理每个客户化文件
                for (int i = 0; i < lstCustomizedFile.Items.Count; i++)
                {
                    // 每个文件的处理都包装在try-catch中，确保单个文件错误不影响整体
                    try
                    {
                        if (i >= lstCustomizedFile.Items.Count) break; // 防护
                        string fileName = "";

                        try
                        {
                            fileName = lstCustomizedFile.Items[i].ToString();
                        }
                        catch (Exception itemEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"リストアイテム取得エラー: {itemEx.Message}");
                            notFoundFiles.Add($"リストアイテム{i} (取得エラー: {itemEx.Message})");
                            continue;
                        }

                        // 跳过错误信息
                        if (fileName.Contains("情報が見つかりません"))
                            continue;

                        tslStatus.Text = $"処理中: {fileName} ({i + 1}/{lstCustomizedFile.Items.Count})";
                        Application.DoEvents();

                        System.Diagnostics.Debug.WriteLine($"処理開始: {fileName} ({i + 1}/{lstCustomizedFile.Items.Count})");

                        // 从缓存中获取文件信息 - 只有缓存中存在的文件才继续处理
                        if (moduleInfoCache.TryGetValue(fileName, out var fileInfo))
                        {
                            tslStatus.Text = $"キャッシュヒット: {fileName} ({i + 1}/{lstCustomizedFile.Items.Count})";
                            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] キャッシュヒット: {fileName}\n");
                            Application.DoEvents();

                            // 查找SVN中匹配HASH的文件
                            string svnPath = "";
                            try
                            {
                                tslStatus.Text = $"SVNファイル検索開始: {fileName} ({i + 1}/{lstCustomizedFile.Items.Count})";
                                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVN検索開始: {fileName}\n");
                                Application.DoEvents();

                                svnPath = FindFileInSVNByHash(fileName, fileInfo.HashValue, orgCode);

                                tslStatus.Text = $"SVNファイル検索完了: {fileName} ({i + 1}/{lstCustomizedFile.Items.Count})";
                                // ★ 搜索结果为空就立刻跳过，别再进入下载/哈希等后续
                                if (string.IsNullOrWhiteSpace(svnPath))   // 如果在批量下载那段，这个变量名是 url
                                {
                                    SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] 検索結果为空，跳过\n");
                                    continue;
                                }

                                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVN検索完了: {fileName}, 結果={svnPath}\n");
                                Application.DoEvents();
                            }
                            catch (Exception findEx)
                            {
                                tslStatus.Text = $"SVN検索エラー: {fileName} ({i + 1}/{lstCustomizedFile.Items.Count})";
                                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVN検索エラー: {fileName}, {findEx.Message}\n");
                                System.Diagnostics.Debug.WriteLine($"ファイル検索エラー: {fileName}, {findEx.Message}");
                                notFoundFiles.Add($"{fileName} (検索エラー: {findEx.Message})");
                                continue;
                            }

                            if (!string.IsNullOrEmpty(svnPath))
                            {
                                // 构建正确的相对路径结构
                                string relativePath = "";
                                if (svnPath.Contains("/uhr/"))
                                {
                                    int uhrIndex = svnPath.IndexOf("/uhr/");
                                    relativePath = "uhr/" + svnPath.Substring(uhrIndex + 5); // uhr/jsp/pages/...
                                }
                                else if (svnPath.Contains("/ROOT/"))
                                {
                                    int rootIndex = svnPath.IndexOf("/ROOT/");
                                    relativePath = "ROOT/" + svnPath.Substring(rootIndex + 6); // ROOT/jsp/pages/...
                                }
                                else
                                {
                                    // 如果路径中没有明确的uhr或ROOT标记，直接使用文件名
                                    relativePath = fileName;
                                }

                                // 下载文件
                                string tempFile = "";
                                try
                                {
                                    tslStatus.Text = $"ダウンロード開始: {fileName}";
                                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ダウンロード開始: {fileName}\n");
                                    Application.DoEvents();

                                    tempFile = DownloadFileFromSVN(svnPath, fileName);

                                    // 检查下载是否真正成功
                                    if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile))
                                    {
                                        tslStatus.Text = $"ダウンロード成功: {fileName}";
                                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ダウンロード成功: {fileName}\n");
                                    }
                                    else
                                    {
                                        tslStatus.Text = $"ダウンロード失敗: {fileName}";
                                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ダウンロード失敗: {fileName}\n");
                                    }
                                    Application.DoEvents();
                                }
                                catch (Exception downloadEx)
                                {
                                    tslStatus.Text = $"ダウンロードエラー: {fileName}";
                                    SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] ダウンロードエラー: {fileName}, エラー: {downloadEx.Message}\n");
                                    notFoundFiles.Add($"{fileName} (ダウンロードエラー: {downloadEx.Message})");
                                    continue;
                                }
                                if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile))
                                {
                                    tslStatus.Text = $"HASH計算開始: {fileName}";
                                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] HASH計算開始: {fileName}\n");
                                    Application.DoEvents();

                                    // 在Windows中，前斜杠をバックスラッシュに変換
                                    relativePath = relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar);

                                    // relativePathは既に構築済み
                                    System.Diagnostics.Debug.WriteLine($"構築されたrelativePath: '{relativePath}'");

                                    // 创建完整的目标路径
                                    string targetFilePath = System.IO.Path.Combine(saveDir, relativePath);
                                    System.Diagnostics.Debug.WriteLine($"完全なtargetFilePath: '{targetFilePath}'");

                                    tslStatus.Text = $"HASH計算完了、ファイル保存開始: {fileName}";
                                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] HASH計算完了、ファイル保存開始: {fileName}\n");
                                    Application.DoEvents();
                                    string targetDir = System.IO.Path.GetDirectoryName(targetFilePath);

                                    if (!Directory.Exists(targetDir))
                                        Directory.CreateDirectory(targetDir);

                                    // 保存文件
                                    File.Copy(tempFile, targetFilePath, true);
                                    successCount++;

                                    tslStatus.Text = $"ファイル保存完了: {fileName} -> {relativePath} ({successCount}個目)";
                                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ファイル保存完了: {fileName} -> {relativePath} ({successCount}個目)\n");
                                    Application.DoEvents();

                                    // 清理临时文件
                                    try { File.Delete(tempFile); Directory.Delete(Path.GetDirectoryName(tempFile), true); } catch { }
                                }
                                else
                                {
                                    // SVNでHASHに一致するファイルが見つからない
                                    notFoundFiles.Add(fileName);
                                    tslStatus.Text = $"SVNファイル未発見: {fileName}";
                                    Application.DoEvents();
                                }
                            }
                        }
                        else
                        {
                            // キャッシュにファイル情報がない
                            notFoundFiles.Add(fileName);
                            tslStatus.Text = $"キャッシュに情報なし: {fileName}";
                            Application.DoEvents();
                        }
                    }
                    catch (Exception fileEx)
                    {
                        // 单个文件处理的异常捕获，确保不影响整个循环
                        System.Diagnostics.Debug.WriteLine($"ファイル処理エラー: インデックス{i}, エラー: {fileEx.Message}");
                        System.Diagnostics.Debug.WriteLine($"スタックトレース: {fileEx.StackTrace}");

                        // 尝试获取文件名，如果无法获取则使用索引
                        string errorFileName = "";
                        try
                        {
                            errorFileName = lstCustomizedFile.Items[i].ToString();
                        }
                        catch
                        {
                            errorFileName = $"インデックス{i}";
                        }

                        notFoundFiles.Add($"{errorFileName} (処理エラー: {fileEx.Message})");

                        tslStatus.Text = $"ファイル処理エラー: {errorFileName} ({i + 1}/{lstCustomizedFile.Items.Count})";
                        Application.DoEvents();

                        // 强制继续下一个文件
                        continue;
                    }
                }

                // 未找到ファイルのログを生成
                if (notFoundFiles.Count > 0 && !string.IsNullOrEmpty(saveDir))
                {
                    string logFilePath = System.IO.Path.Combine(saveDir, "NotFoundFiles.log");
                    using (var writer = new System.IO.StreamWriter(logFilePath, false, System.Text.Encoding.UTF8))
                    {
                        writer.WriteLine($"カスタマイズファイル処理ログ - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        writer.WriteLine($"総ファイル数: {lstCustomizedFile.Items.Count}");
                        writer.WriteLine($"成功: {successCount}");
                        writer.WriteLine($"見つからない: {notFoundFiles.Count}");
                        writer.WriteLine();
                        writer.WriteLine("見つからなかったファイル一覧:");

                        foreach (string fileName in notFoundFiles)
                        {
                            writer.WriteLine($"- {fileName}");
                        }
                    }
                }

                string resultMessage = $"カスタマイズファイルの準備が完了しました。\n";
                resultMessage += $"総ファイル数: {lstCustomizedFile.Items.Count}\n";
                resultMessage += $"成功: {successCount}\n";
                if (notFoundFiles.Count > 0)
                {
                    resultMessage += $"見つからない: {notFoundFiles.Count} (詳細はNotFoundFiles.logをご確認ください)";
                }

                string finalMessage = resultMessage;
                if (!string.IsNullOrEmpty(saveDir))
                {
                    finalMessage += $"\n保存先: {saveDir}";
                }

                tslStatus.Text = $"カスタマイズファイルの準備が完了しました。保存先: {saveDir}";
                MessageBox.Show(finalMessage, "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                tslStatus.Text = "カスタマイズファイル準備中にエラーが発生しました";
                MessageBox.Show($"カスタマイズファイル準備中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // 処理終了ログ
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] === カスタマイズファイル処理終了 ===\n");

                // 最终清理临时文件夹
                CleanupTempFolders();

                this.Cursor = Cursors.Default;
                cmdFilePrepare.Enabled = true;
            }
        }

        private void cmdMergeFilePrepare_Click(object sender, EventArgs e)
        {
            if (lstMergeNeedsFile.Items.Count == 0)
            {
                MessageBox.Show("没有需要下载的文件。");
                return;
            }
            DownloadAllFromMergeList();
        }


        private void cmdCodePath_Click(object sender, EventArgs e)
        {
            fldBrowser.SelectedPath = AppConfig.CODE_BASE_PATH;
        }


        /// <summary>
        /// プロジェクトディレクトリ下の svn.exe の存在をチェックします。
        /// </summary>
        /// <returns>svn.exe が存在すれば true、存在しなければ false</returns>
        private bool CheckSVNAvailable()
        {
            string svnPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
            if (!System.IO.File.Exists(svnPath))
            {
                MessageBox.Show(AppConfig.MSG_SVN_EXE_NOT_FOUND, AppConfig.TITLE_DEPENDENCY_MISSING, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }


        // 安全尝试：用 svn cat 直接算 hash（永不抛异常）
        private bool SafeTryCatHash(string fullSvnPath, out string hash)
        {
            hash = "";
            try
            {
                var svnExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
                // 并发排水以防 I/O 死锁
                var psi = new ProcessStartInfo
                {
                    FileName = svnExe,
                    Arguments = $"cat \"{fullSvnPath}\" --non-interactive --trust-server-cert",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    if (p == null) { SafeLog("ErrorLog.txt", $"[cat] Start失败: {fullSvnPath}\n"); return false; }

                    var msOut = new MemoryStream();
                    var msErr = new MemoryStream();
                    var tOut = Task.Run(() => { try { p.StandardOutput.BaseStream.CopyTo(msOut); } catch { } });
                    var tErr = Task.Run(() => { try { p.StandardError.BaseStream.CopyTo(msErr); } catch { } });

                    var start = DateTime.UtcNow;
                    const int timeoutMs = 120_000;
                    while (!p.WaitForExit(100))
                    {
                        if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                        {
                            try { p.Kill(); p.WaitForExit(3000); } catch { }
                            SafeLog("ErrorLog.txt", $"[cat] 超时: {fullSvnPath}\n");
                            return false;
                        }
                        Application.DoEvents();
                    }
                    try { Task.WaitAll(new[] { tOut, tErr }, 2000); } catch { }

                    var stderr = Encoding.UTF8.GetString(msErr.ToArray());
                    if (p.ExitCode != 0)
                    {
                        SafeLog("ErrorLog.txt", $"[cat] 失败({p.ExitCode}): {fullSvnPath}\nSTDERR: {Trim400(stderr)}\n");
                        return false;
                    }

                    var bytes = msOut.ToArray();
                    if (bytes.Length == 0) { hash = "empty_file_hash"; return true; }

                    var md5 = System.Security.Cryptography.MD5.Create();
                    var h = md5.ComputeHash(bytes);
                    hash = BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
                    return true;
                }
            }
            catch (Exception ex)
            {
                SafeLog("ErrorLog.txt", $"[cat] 例外: {fullSvnPath}\n{ex}\n");
                return false;
            }
        }

        // 安全尝试：export 到临时文件再算 hash（永不抛异常，负责清理临时文件）
        private bool SafeTryExportHash(string fullSvnPath, string fileName, out string hash)
        {
            hash = "";
            string tempFile = "";
            try
            {
                tempFile = DownloadFileFromSVN(fullSvnPath, fileName); // 同步下载（内部有超时/文本cat兜底）
                if (string.IsNullOrEmpty(tempFile) || !File.Exists(tempFile))
                {
                    SafeLog("ErrorLog.txt", $"[export] 未得到文件: {fullSvnPath}\n");
                    return false; // ★ 硬短路：不要再往下走
                }

                // 尺寸稳定 + 非零检查（避免半写/空文件）
                long a = new FileInfo(tempFile).Length;
                System.Threading.Thread.Sleep(120);
                long b = new FileInfo(tempFile).Length;
                int tries = 0;
                while (a != b && tries++ < 3)
                {
                    a = b;
                    System.Threading.Thread.Sleep(120);
                    b = new FileInfo(tempFile).Length;
                }
                if (b == 0)
                {
                    SafeLog("ErrorLog.txt", $"[export] 文件为0字节(跳过): {tempFile}\n");
                    return false;
                }

                // 计算 HASH：用共享读取 + 重试，避免被杀软/索引器短暂占用
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        using (var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var md5 = System.Security.Cryptography.MD5.Create())
                        {
                            var bytes = md5.ComputeHash(fs);
                            hash = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                            return true;
                        }
                    }
                    catch (IOException ioex)
                    {
                        if (i == 2)
                        {
                            SafeLog("ErrorLog.txt", $"[export] 读取文件计算哈希失败: {tempFile}\n{ioex}\n");
                            return false;
                        }
                        System.Threading.Thread.Sleep(150);
                    }
                }

                return false; // 理论到不了
            }
            catch (Exception ex)
            {
                SafeLog("ErrorLog.txt", $"[export] 例外: {fullSvnPath}\n{ex}\n");
                return false;
            }
            finally
            {
                try
                {
                    if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                        var dir = Path.GetDirectoryName(tempFile);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            // 只在目录已经空了的情况下删除，避免误删其他临时文件
                            if (Directory.Exists(dir) && Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 0)
                                Directory.Delete(dir, true);
                        }
                    }
                }
                catch { /* 清理失败忽略 */ }
            }
        }

        // 小工具：截断长 stderr
        private static string Trim400(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 400 ? s.Substring(0, 400) + "..." : s);


        // 任何日志都走它：就算路径/权限/占用有问题，绝不把主流程带崩
        private void SafeLog(string file, string text)
        {
            try { System.IO.File.AppendAllText(file, text); }
            catch { /* 日志失败不可影响主流程 */ }
        }


    }
}