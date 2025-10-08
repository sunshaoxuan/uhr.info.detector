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

        #region Process超时等待辅助方法
        /// <summary>
        /// Processの完了を待機し、タイムアウトとUIの定期更新をサポート
        /// </summary>
        /// <param name="process">待機するProcessオブジェクト</param>
        /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
        /// <returns>trueは正常完了、falseはタイムアウト</returns>
        private bool WaitProcessWithTimeout(Process process, int timeoutMs)
        {
            var start = DateTime.UtcNow;
            while (!process.WaitForExit(100))
            {
                if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                {
                    try { process.Kill(); process.WaitForExit(3000); } 
                    catch (Exception ex) { SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] プロセス強制終了失敗: {ex.Message}\n"); }
                    return false;
                }
                Application.DoEvents();
            }
            return true;
        }

        /// <summary>
        /// ファイルサイズの安定を待機（ファイル書き込み完了を確保）
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="maxRetries">最大リトライ回数</param>
        /// <param name="delayMs">各チェック間隔（ミリ秒）</param>
        /// <returns>ファイルの最終サイズ</returns>
        private long WaitFileStable(string filePath, int maxRetries = 3, int delayMs = 200)
        {
            long size1 = new FileInfo(filePath).Length;
            System.Threading.Thread.Sleep(delayMs);
            long size2 = new FileInfo(filePath).Length;
            
            int tries = 0;
            while (size1 != size2 && tries++ < maxRetries)
            {
                size1 = size2;
                System.Threading.Thread.Sleep(delayMs);
                size2 = new FileInfo(filePath).Length;
            }
            
            return size2;
        }
        #endregion

        /// <summary>
        /// SVNコマンド実行結果を格納するクラス
        /// </summary>
        private sealed class SvnResult
        {
            /// <summary>プロセスの終了コード</summary>
            public int ExitCode { get; set; }
            /// <summary>標準出力の内容</summary>
            public string Stdout { get; set; }
            /// <summary>標準エラー出力の内容</summary>
            public string Stderr { get; set; }
            /// <summary>タイムアウトが発生したかどうか</summary>
            public bool TimedOut { get; set; }
        }

        private static readonly Encoding SvnConsoleEncoding = Encoding.GetEncoding(932); // Shift_JIS (CP932)
                                                                                         // よく使用される2つのエンコーディング
        private static readonly Encoding EncSjis = Encoding.GetEncoding(932);              // CP932 / Shift_JIS
        private static readonly Encoding EncUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private const string FilePrepareLogName = "FilePrepare.log";

        /// <summary>
        /// SVNからのテキスト出力を自動判別してデコード
        /// UTF-8（BOM付きまたは無損失デコード）を優先し、失敗した場合はCP932（Shift_JIS）にフォールバック
        /// </summary>
        /// <param name="bytes">デコードするバイト配列</param>
        /// <returns>デコードされた文字列</returns>
        private static string DecodeSvnText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            // 1) BOM 直判 UTF-8
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return EncUtf8.GetString(bytes, 3, bytes.Length - 3);

            // 2) UTF-8無損失デコードを試行（throwOnInvalidBytes=trueは無効なシーケンスで例外を投げる）
            try
            {
                var s = EncUtf8.GetString(bytes);
                // 一部のフォールバック実装がU+FFFDを挿入することを避ける（念のため再チェック）
                if (!s.Contains('\uFFFD')) return s;
            }
            catch { /* UTF-8ではない */ }

            // 3) CP932にフォールバック
            return EncSjis.GetString(bytes);
        }

        // テキストファイル拡張子（失敗時はsvn catにフォールバック）
        private static bool IsTextLikeByExt(string pathOrName)
        {
            var ext = Path.GetExtension(pathOrName)?.ToLowerInvariant();
            return ext == ".htm" || ext == ".html" || ext == ".jsp" || ext == ".js" ||
                   ext == ".css" || ext == ".xml" || ext == ".json" || ext == ".txt" ||
                   ext == ".md" || ext == ".dicon";
        }

        // 安全な取得：まずexport（タイムアウトとリトライ付き）、失敗してテキストならcat；すべて失敗でfalseを返す
        private bool TryFetchToFile(string svnExe, string fullSvnPath, string localPath, int exportTimeoutMs, int catTimeoutMs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? AppDomain.CurrentDomain.BaseDirectory);

            // exportを2回試行
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
                        long finalSize = WaitFileStable(localPath);
                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] export OK: {fullSvnPath} -> {localPath} size={finalSize}\n");
                        return true;
                    }
                }

                SafeLog("ErrorLog.txt",
                    $"[{DateTime.Now:HH:mm:ss}] export NG({attempt}/2) Exit={res.ExitCode} Timeout={res.TimedOut}\nURL={fullSvnPath}\nSTDERR:\n{res.Stderr}\n");
                Application.DoEvents();
            }

            // テキストファイルのフォールバック cat
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

        /// <summary>
        /// SVNコマンドを実行して結果を取得
        /// </summary>
        /// <param name="exe">SVN実行ファイルのパス</param>
        /// <param name="args">コマンドライン引数</param>
        /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
        /// <param name="forcedEncoding">強制的に使用するエンコーディング（--xml出力時はUTF-8を指定）</param>
        /// <returns>SVNコマンドの実行結果</returns>
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
                // 強制エンコーディングが指定されている場合はそれを使用、そうでなければ既存のエンコーディング戦略を維持
                StandardOutputEncoding = forcedEncoding ?? SvnConsoleEncoding,
                StandardErrorEncoding = forcedEncoding ?? SvnConsoleEncoding,
            };

            using (var p = Process.Start(psi))
            {
                if (p == null) return new SvnResult { ExitCode = -1, Stderr = "Process.Start returned null" };

                var msOut = new MemoryStream();
                var msErr = new MemoryStream();
                var tOut = Task.Run(() => { 
                    try { p.StandardOutput.BaseStream.CopyTo(msOut); } 
                    catch (Exception ex) { SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] STDOUT読み取り失敗: {ex.Message}\n"); } 
                });
                var tErr = Task.Run(() => { 
                    try { p.StandardError.BaseStream.CopyTo(msErr); } 
                    catch (Exception ex) { SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] STDERR読み取り失敗: {ex.Message}\n"); } 
                });

                bool timedOut = !WaitProcessWithTimeout(p, timeoutMs);
                try { Task.WaitAll(new[] { tOut, tErr }, 2000); } 
                catch (Exception ex) { SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] Task.WaitAll失敗: {ex.Message}\n"); }

                // psiで指定されたエンコーディングを使用してデコード
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

        /// <summary>
        /// MODULE_INFO表のファイル情報を格納するクラス
        /// </summary>
        public class ModuleFileInfo
        {
            /// <summary>ファイル名</summary>
            public string Filename { get; set; }
            /// <summary>ファイルのMD5ハッシュ値</summary>
            public string HashValue { get; set; }
            /// <summary>機関コード</summary>
            public string KikanCode { get; set; }
            /// <summary>機関名</summary>
            public string KikanName { get; set; }
            /// <summary>機能バージョン</summary>
            public string FunctionVersion { get; set; }
            /// <summary>カスタマイズフラグ（1=カスタマイズ済み、0=標準）</summary>
            public int CustomizedFlag { get; set; }
        }
        public frmMain()
        {
            InitializeComponent();
            this.FormClosing += FrmMain_FormClosing;
        }

        private volatile bool _isTaskRunning = false;

        private void FrmMain_FormClosing(object sender, System.Windows.Forms.FormClosingEventArgs e)
        {
            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォーム閉じる: Reason={e.CloseReason}, TaskRunning={_isTaskRunning}\n");

            if (_isTaskRunning && e.CloseReason == CloseReason.UserClosing)
            {
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] タスク実行中のため閉じるをキャンセル\n");
                e.Cancel = true;
                MessageBox.Show("処理中です。完了するまでお待ちください。", "処理中", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
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

        // 再帰的に指定されたURL下のすべての「ファイル」（ディレクトリを除く）をリストアップし、相対パス（サブディレクトリを含む）を返す
        private List<string> GetSvnFileListXmlRecursive(string svnUrl, string svnExePath, int timeoutMs = 120000)
        {
            var res = RunSvn(svnExePath, $"list --xml -R \"{svnUrl}\"", timeoutMs, Encoding.UTF8);
            if (res.TimedOut) throw new Exception("SVNリストコマンドタイムアウト");
            if (res.ExitCode != 0) throw new Exception($"SVNリストコマンド失敗(ExitCode={res.ExitCode}): {res.Stderr}");
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
        [Obsolete("このメソッドは現在使用されておらず、将来のバージョンで削除される可能性があります")]
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

            // キャッシュにデータが既に存在するかチェック、なければデータベースから完全情報を読み取り
            if (!await ReadModuleInfoFromDatabase(orgCode))
                return result;

            tslStatus.Text = "カスタマイズファイルを読み込み中...";
            Application.UseWaitCursor = true;
            lstCustomizedFile.Items.Clear();

            // キャッシュからカスタマイズファイルを抽出して表示
            result = moduleInfoCache.Values
                .Where(info => info.CustomizedFlag == 1)
                .OrderBy(info => info.Filename)
                .Select(info => info.Filename)
                .ToList();

            // インターフェース表示
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

        // データベースからMODULE_INFO表の完全データを読み取り、キャッシュする
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
                            moduleInfoCache.Clear(); // 前のキャッシュをクリア
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
                    // テーブルが存在しない場合、別の形式を試す
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
                                    moduleInfoCache.Clear(); // 前のキャッシュをクリア
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

                // ファイルサイズをチェックし、過大なファイルの処理を避ける
                FileInfo fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 50 * 1024 * 1024) // 50MB制限
                {
                    System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 文件过大({fileInfo.Length / 1024 / 1024}MB): {filePath}");
                    return "";
                }

                // 極小のファイルの場合、空ファイルの可能性もある
                if (fileInfo.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 文件为空: {filePath}");
                    return "empty_file_hash";
                }

                System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 开始计算 {filePath} (大小: {fileInfo.Length} bytes)");

                // ファイルの解放を待機し、他のプロセスによる占有を防ぐ
                int retryCount = 0;
                while (retryCount < 5)
                {
                    try
                    {
                        // 複数の方法でファイル読み取りを試行
                        byte[] fileBytes = null;

                        // 方法1: 直接バイト読み取り（最も安全な方法）
                        try
                        {
                            fileBytes = File.ReadAllBytes(filePath);
                            System.Diagnostics.Debug.WriteLine($"CalculateFileHash: バイト読み込み成功: {fileBytes.Length} bytes");
                        }
                        catch (Exception readEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"CalculateFileHash: バイト読み込み失敗: {readEx.Message}");

                            // 方法2: Stream読み取りを試行
                            using (var stream = File.OpenRead(filePath))
                            {
                                using (var ms = new MemoryStream())
                                {
                                    stream.CopyTo(ms);
                                    fileBytes = ms.ToArray();
                                }
                            }
                        }

                        // ファイルバイト内容のHASHを計算
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
                        // ファイルが占有されている場合、待機後にリトライ
                        retryCount++;
                        System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 文件被占用，重试 {retryCount}/5: {ioEx.Message}");
                        System.Threading.Thread.Sleep(500); // 500ms待機
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
                // MessageBoxを表示せず、デバッグ情報を記録し、ブロックを避ける
                return "";
            }
        }

        // SVNからファイルを一時ディレクトリにダウンロード（同期 + 並行STDOUT/STDERR排水 + 引用符 + タイムアウト + --quiet）
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
                StandardOutputEncoding = SvnConsoleEncoding,   // ★ 新規追加：CP932でデコード
                StandardErrorEncoding = SvnConsoleEncoding    // ★ 新規追加
            };

            Process proc = null;
            try
            {
                proc = System.Diagnostics.Process.Start(psi);
                if (proc == null)
                {
                    SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] プロセス起動失敗\n");
                    return "";
                }

                var msOut = new MemoryStream();
                var msErr = new MemoryStream();
                var tOut = Task.Run(() => { 
                    try { proc.StandardOutput.BaseStream.CopyTo(msOut); } 
                    catch (Exception ex) { SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] STDOUT読み取り失敗: {ex.Message}\n"); } 
                });
                var tErr = Task.Run(() => { 
                    try { proc.StandardError.BaseStream.CopyTo(msErr); } 
                    catch (Exception ex) { SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] STDERR読み取り失敗: {ex.Message}\n"); } 
                });

                const int timeoutMs = 120_000;
                if (!WaitProcessWithTimeout(proc, timeoutMs))
                {
                    // タイムアウト時の処理
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
                                return targetPath; // ★ 成功：catの結果をダウンロード成功として扱う
                            }

                            // cat失敗も記録
                            SafeLog("ErrorLog.txt",
                                $"[{DateTime.Now:HH:mm:ss}] cat NG Exit={catRes.ExitCode} Timeout={catRes.TimedOut}\nURL={fullSvnPath}\nSTDERR:\n{catRes.Stderr}\n");
                        }

                        SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] svn export タイムアウト: {fullSvnPath}\n");
                        return "";
                    }


                // … stdoutTask / stderrTask の後処理の後：
                try { tOut.Wait(2000); } 
                catch (Exception ex) { SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] tOut.Wait失敗: {ex.Message}\n"); }
                try { tErr.Wait(2000); } 
                catch (Exception ex) { SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] tErr.Wait失敗: {ex.Message}\n"); }

                byte[] rawOut = msOut.ToArray();
                byte[] rawErr = msErr.ToArray();

                string stdoutStr = DecodeSvnText(rawOut);
                string stderrStr = DecodeSvnText(rawErr);

                if (proc.ExitCode != 0)
                {
                    SafeLog("ErrorLog.txt",
                        $"[{DateTime.Now:HH:mm:ss}] export失敗(ExitCode={proc.ExitCode}) URL={fullSvnPath}\n" +
                        $"STDERR:\n{(stderrStr.Length > 400 ? stderrStr.Substring(0, 400) + "..." : stderrStr)}\n");
                    return "";
                }

                for (int i = 0; i < 25 && !File.Exists(targetPath); i++) System.Threading.Thread.Sleep(20);
                if (!File.Exists(targetPath))
                {
                    SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] export終了だが出力ファイル不存在: {targetPath}\n");
                    return "";
                }

                long finalSize = WaitFileStable(targetPath);
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] export成功: {fileName} size={finalSize}\n");
                return targetPath;
            }
            catch (Exception ex)
            {
                SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] DownloadFileFromSVN 例外: {ex.Message}\nStackTrace: {ex.StackTrace}\n");
                return "";
            }
            finally
            {
                try
                {
                    if (proc != null && !proc.HasExited)
                    {
                        proc.Kill();
                        proc.WaitForExit(1000);
                    }
                    proc?.Dispose();
                }
                catch { /* 清理失败忽略 */ }
            }
        }


        // 機関に対応するSVNフォルダを検索
        private string FindOrgSvnFolder(string orgCode)
        {
            try
            {
                string svnPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
                string baseSvnUrl = AppConfig.SVN_CUSTOMIZED_PATH;

                // --xmlで固定UTF-8出力；RunSvnで強制UTF-8デコード
                var res = RunSvn(svnPath, $"list --xml \"{baseSvnUrl}\"", AppConfig.SVN_COMMAND_TIMEOUT_MS * 5, Encoding.UTF8);
                if (res.TimedOut) throw new Exception("SVN列表命令超时");
                if (res.ExitCode != 0) throw new Exception($"SVN列表命令失败(ExitCode={res.ExitCode}): {res.Stderr}");
                if (string.IsNullOrWhiteSpace(res.Stdout)) throw new Exception($"SVN未返回任何结果: {baseSvnUrl}");

                // XML解析：<entry kind="dir"><name>xxxx</name></entry>
                var xdoc = System.Xml.Linq.XDocument.Parse(res.Stdout);
                var names = xdoc.Descendants("entry")
                                .Where(e => (string)e.Attribute("kind") == "dir")
                                .Select(e => (string)e.Element("name"))
                                .Where(s => !string.IsNullOrEmpty(s))
                                .ToList();

                // 優先順位：*_HRAP、次に*_HRDB
                string orgFolder =
                    names.FirstOrDefault(n => n.StartsWith($"{orgCode}_", StringComparison.OrdinalIgnoreCase) && n.EndsWith("_HRAP")) ??
                    names.FirstOrDefault(n => n.StartsWith($"{orgCode}_", StringComparison.OrdinalIgnoreCase) && n.EndsWith("_HRDB"));

                if (string.IsNullOrEmpty(orgFolder))
                    throw new Exception($"未找到机构代码 '{orgCode}' 对应的SVN文件夹。可用文件夹: {string.Join(", ", names)}");

                System.Diagnostics.Debug.WriteLine($"找到的机构文件夹: {orgFolder}");
                return orgFolder; // ← 引き続き返す
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
                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] 初回SVN全ファイルリスト取得中: {fileName}\n");

                    allFiles = GetAllFilesFromSvn(svnBaseUrl, svnPath);
                    svnFileListCache[cacheKey] = allFiles;

                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVNファイルキャッシュ完了: {allFiles.Count}個のファイルを発見\n");
                }
                else
                {
                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVNキャッシュ命中: {fileName} ({allFiles.Count}個のファイル)\n");
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

                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] HASH比較中: {fileName} ({candidates.Count}個の候補)\n");

                // 检查每个候选文件的HASH
                for (int idx = 0; idx < candidates.Count; idx++)
                {
                    try
                    {
                        var candidate = candidates[idx];

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

            // 年末調整バージョンのチェック（包含匹配，因为实际版本号是长串如"2.11.4+reigetsu2.11.4+shoumeisho2.10.2"）
            if (!string.IsNullOrEmpty(txtYearAdjustVersion.Text) &&
                !string.IsNullOrEmpty(cboYearAdjustTargetVersion.Text) &&
                txtYearAdjustVersion.Text.Contains(cboYearAdjustTargetVersion.Text))
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
            // ValueTuple需要使用默认的EqualityComparer，不能直接用StringComparer
            var versionUrlCache = new Dictionary<(string module, string ver), string>();
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
                    SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] 单项处理异常(已跳过): {lstMergeNeedsFile.Items[i]}\n{ex}\n");
                    continue; // ★关键：失败不停机，只跳过该文件
                }
            }

            MessageBox.Show($"下载完成：成功 {ok} 个，失败 {ng} 个。\n保存到：{outRoot}", "完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }



        private void cmdFilePrepare_Click(object sender, EventArgs e)
        {
            // 基本验证
            string svnExe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
            if (!System.IO.File.Exists(svnExe))
            {
                MessageBox.Show("svn.exeが見つかりません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string orgCode = txtOrgCode.Text.Trim();
            if (string.IsNullOrEmpty(orgCode))
            {
                MessageBox.Show("機関コードが設定されていません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string orgName = txtOrgName.Text.Trim();
            string svnBaseUrl = GetSvnUrl();
            if (string.IsNullOrEmpty(svnBaseUrl))
            {
                MessageBox.Show("SVN URLが取得できません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 禁用按钮,显示忙状态
            cmdFilePrepare.Enabled = false;
            this.Cursor = Cursors.WaitCursor;
            _isTaskRunning = true;

            SimpleSvnHelper.CleanupTempFolders();
            System.IO.File.WriteAllText("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] === カスタマイズファイル処理開始（フォアグラウンドスレッド版） ===\n");

            // 使用前台线程而不是Task.Run(后台线程),防止应用程序意外退出
            var thread = new System.Threading.Thread(() =>
            {
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォアグラウンドスレッド開始、ID={System.Threading.Thread.CurrentThread.ManagedThreadId}, IsBackground={System.Threading.Thread.CurrentThread.IsBackground}\n");

                (int, int, string) result = (0, 0, "");
                Exception threadException = null;

                try
                {
                    result = ProcessFilesInBackground(svnExe, orgCode, orgName, svnBaseUrl);
                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessFilesInBackground完了、結果を取得\n");
                }
                catch (Exception ex)
                {
                    threadException = ex;
                    SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] スレッド例外: {ex.Message}\n{ex.StackTrace}\n");
                }

                // 检查窗体是否还存在
                if (this.IsDisposed || !this.IsHandleCreated)
                {
                    SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォームが既に破棄されています\n");
                    return;
                }

                try
                {
                    this.Invoke(new Action(() =>
                    {
                        _isTaskRunning = false;
                        this.Cursor = Cursors.Default;
                        cmdFilePrepare.Enabled = true;

                        if (threadException != null)
                        {
                            SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] エラーをUI表示\n");
                            MessageBox.Show($"処理中にエラーが発生しました。\n{threadException.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            tslStatus.Text = "エラーが発生しました";
                        }
                        else
                        {
                            int successCount = result.Item1;
                            int notFoundCount = result.Item2;
                            string saveDir = result.Item3;

                            if (successCount > 0)
                            {
                                MessageBox.Show(
                                    $"処理が完了しました。\n成功: {successCount}個\n失敗: {notFoundCount}個\n保存先: {saveDir}",
                                    "完了",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information);

                                try
                                {
                                    System.Diagnostics.Process.Start("explorer.exe", saveDir);
                                }
                                catch (Exception ex)
                                {
                                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] エクスプローラー起動失敗: {ex.Message}\n");
                                }
                            }
                            else
                            {
                                MessageBox.Show("ファイルが見つかりませんでした。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }

                            tslStatus.Text = $"完了: {successCount}個成功, {notFoundCount}個失敗";
                        }

                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] UI更新完了\n");
                    }));
                }
                catch (Exception invokeEx)
                {
                    SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] Invoke例外: {invokeEx.Message}\n{invokeEx.StackTrace}\n");
                }

                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォアグラウンドスレッド終了\n");
            });

            thread.IsBackground = false;  // 设为前台线程，防止应用程序在线程运行时退出
            thread.Start();

            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォアグラウンドスレッド起動完了\n");
        }

        private void UpdateStatus(string message)
        {
            try
            {
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() => tslStatus.Text = message));
                }
                else
                {
                    tslStatus.Text = message;
                }
            }
            catch (Exception ex) 
            { 
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ステータス更新失敗: {ex.Message}\n"); 
            }
        }

        static string PSEscape(string s) => "'" + s.Replace("'", "''") + "'";  // 单引号转义

        /// <summary>
        /// 为.class文件下载对应的.java源文件
        /// </summary>
        private void DownloadCorrespondingJavaFile(
            string classFilePath,
            string svnBaseUrl,
            List<string> allSvnFiles,
            string workTempDir,
            string saveDir,
            SimpleSvnHelper svnHelper)
        {
            try
            {
                // 将.class文件路径转换为.java文件路径
                // 例如: WEB-INF/classes/com/example/Foo.class -> WEB-INF/classes/com/example/Foo.java
                string javaFilePath = classFilePath.Substring(0, classFilePath.Length - 6) + ".java";
                string javaFileName = Path.GetFileName(javaFilePath);

                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] .classファイル検出、対応する.javaファイルを検索: {javaFileName}\n");

                // 在SVN文件列表中查找对应的.java文件
                var javaFile = allSvnFiles.FirstOrDefault(f => f.Equals(javaFilePath, StringComparison.OrdinalIgnoreCase));

                if (javaFile != null)
                {
                    string svnFileUrl = $"{svnBaseUrl.TrimEnd('/')}/{javaFile}";
                    string localFilePath = Path.Combine(workTempDir, javaFile.Replace('/', Path.DirectorySeparatorChar));

                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] .javaファイルダウンロード試行: {javaFile}\n");

                    if (svnHelper.DownloadSingleFile(svnFileUrl, localFilePath))
                    {
                        // 复制到目标目录，保留相对路径
                        string targetPath = Path.Combine(saveDir, javaFile.Replace('/', Path.DirectorySeparatorChar));
                        string targetDir = Path.GetDirectoryName(targetPath);

                        if (!Directory.Exists(targetDir))
                            Directory.CreateDirectory(targetDir);

                        File.Copy(localFilePath, targetPath, true);
                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] .javaファイル保存成功: {javaFileName}\n");
                    }
                    else
                    {
                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] .javaファイルダウンロード失敗: {javaFile}\n");
                    }
                }
                else
                {
                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] 対応する.javaファイルがSVNに見つかりません: {javaFilePath}\n");
                }
            }
            catch (Exception ex)
            {
                SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] .javaファイルダウンロード例外: {ex.Message}\n");
            }
        }

        /// <summary>
        /// 通用的按需下载方法 - 根据文件名列表从SVN按需下载并验证哈希
        /// </summary>
        /// <param name="downloadAllMatches">true=下载所有哈希匹配的文件（用于merge），false=只下载第一个匹配（用于customized）</param>
        private (int successCount, List<string> notFoundFiles) DownloadFilesOnDemand(
            List<string> fileNames,
            string svnBaseUrl,
            string svnExe,
            string workTempDir,
            string saveDir,
            SimpleSvnHelper svnHelper,
            bool downloadAllMatches = false)
        {
            var notFoundFiles = new List<string>();
            int successCount = 0;

            // 第1段階：获取SVN文件列表（轻量级操作）
            List<string> allSvnFiles;
            if (!svnFileListCache.TryGetValue(svnBaseUrl, out allSvnFiles))
            {
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVNファイルリスト取得中...\n");
                allSvnFiles = GetAllFilesFromSvn(svnBaseUrl, svnExe);
                svnFileListCache[svnBaseUrl] = allSvnFiles;
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVNファイル数: {allSvnFiles.Count}\n");
            }
            else
            {
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVNファイルリストキャッシュ使用: {allSvnFiles.Count}個\n");
            }

            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] 処理対象ファイル数: {fileNames.Count}\n");

            // 第2段階：按需下载并验证哈希
            for (int i = 0; i < fileNames.Count; i++)
            {
                try
                {
                    string fileName = fileNames[i];
                    if (fileName.Contains("情報が見つかりません"))
                        continue;

                    UpdateStatus($"ダウンロード中: {fileName} ({i + 1}/{fileNames.Count})");

                    if (!moduleInfoCache.TryGetValue(fileName, out var fileInfo))
                    {
                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] キャッシュなし: {fileName}\n");
                        notFoundFiles.Add($"{fileName} (キャッシュなし)");
                        continue;
                    }

                    // 在SVN文件列表中查找同名文件
                    var matchingFiles = allSvnFiles.Where(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase)).ToList();

                    if (matchingFiles.Count == 0)
                    {
                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVNで見つかりません: {fileName}\n");
                        notFoundFiles.Add($"{fileName} (SVNで見つかりません)");
                        continue;
                    }

                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] {fileName}の候補: {matchingFiles.Count}個\n");

                    bool matched = false;
                    int matchCount = 0;
                    foreach (var svnFilePath in matchingFiles)
                    {
                        // 下载文件到临时目录
                        string svnFileUrl = $"{svnBaseUrl.TrimEnd('/')}/{svnFilePath}";
                        string localFilePath = Path.Combine(workTempDir, svnFilePath.Replace('/', Path.DirectorySeparatorChar));

                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ダウンロード試行: {svnFilePath}\n");

                        if (!svnHelper.DownloadSingleFile(svnFileUrl, localFilePath))
                        {
                            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ダウンロード失敗: {svnFilePath}\n");
                            continue;
                        }

                        // 验证哈希
                        string actualHash = svnHelper.CalculateHash(localFilePath);

                        if (string.IsNullOrEmpty(actualHash))
                        {
                            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ハッシュ計算失敗: {localFilePath}\n");
                            continue;
                        }

                        if (actualHash.Equals(fileInfo.HashValue, StringComparison.OrdinalIgnoreCase))
                        {
                            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ハッシュ一致: {fileName}\n");

                            // 复制到目标目录，保留相对路径
                            string targetPath = Path.Combine(saveDir, svnFilePath.Replace('/', Path.DirectorySeparatorChar));
                            string targetDir = Path.GetDirectoryName(targetPath);

                            if (!Directory.Exists(targetDir))
                                Directory.CreateDirectory(targetDir);

                            File.Copy(localFilePath, targetPath, true);
                            successCount++;
                            matched = true;
                            matchCount++;

                            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] 保存成功: {fileName} ({successCount}個目)\n");

                            // 如果是.class文件，尝试下载对应的.java文件
                            if (fileName.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
                            {
                                DownloadCorrespondingJavaFile(svnFilePath, svnBaseUrl, allSvnFiles, workTempDir, saveDir, svnHelper);
                            }

                            // 如果不需要下载所有匹配，找到第一个就退出
                            if (!downloadAllMatches)
                                break;
                        }
                        else
                        {
                            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ハッシュ不一致: expected={fileInfo.HashValue}, actual={actualHash}\n");
                        }
                    }

                    if (!matched)
                    {
                        notFoundFiles.Add($"{fileName} (ハッシュ不一致)");
                    }
                    else if (downloadAllMatches && matchCount > 1)
                    {
                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] {fileName}: {matchCount}個のマッチファイルを保存\n");
                    }
                }
                catch (Exception fileEx)
                {
                    string errorFileName = fileNames[i];
                    SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] ファイル処理例外: {errorFileName}\n{fileEx.Message}\n{fileEx.StackTrace}\n");
                    notFoundFiles.Add($"{errorFileName} (処理例外)");
                }
            }

            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ダウンロード完了: {successCount}個成功\n");
            return (successCount, notFoundFiles);
        }

        private (int successCount, int notFoundCount, string saveDir) ProcessFilesInBackground(string svnExe, string orgCode, string orgName, string svnBaseUrl)
        {
            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessFilesInBackground開始 - スレッドID: {System.Threading.Thread.CurrentThread.ManagedThreadId}\n");

            string workTempDir = null;

            try
            {
                string saveDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{orgCode}_{orgName}_CustomizedFiles");

            if (Directory.Exists(saveDir))
            {
                Directory.Delete(saveDir, true);
            }
            Directory.CreateDirectory(saveDir);

            var svnHelper = new SimpleSvnHelper(svnExe, msg => SafeLog("DebugLog.txt", msg + "\n"));

            UpdateStatus("SVNファイルリスト取得中...");

            workTempDir = Path.Combine(Path.GetTempPath(), "uhr_ondemand_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
            Directory.CreateDirectory(workTempDir);
            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] 作業用一時フォルダ: {workTempDir}\n");

            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] === 按需下载模式：仅下载需要的文件 ===\n");
            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVN URL: {svnBaseUrl}\n");

            // 获取需要下载的文件列表
            List<string> fileNames = new List<string>();
            this.Invoke(new Action(() =>
            {
                foreach (var item in lstCustomizedFile.Items)
                {
                    fileNames.Add(item.ToString());
                }
            }));

            // 调用通用下载方法
            var (successCount, notFoundFiles) = DownloadFilesOnDemand(fileNames, svnBaseUrl, svnExe, workTempDir, saveDir, svnHelper);

            // 临时文件夹清理已移至finally块，确保无论成功失败都会清理

            if (notFoundFiles.Count > 0 && !string.IsNullOrEmpty(saveDir))
            {
                string logFilePath = System.IO.Path.Combine(saveDir, "NotFoundFiles.log");
                using (var writer = new System.IO.StreamWriter(logFilePath, false, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine($"カスタマイズファイル処理ログ - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine($"総ファイル数: {fileNames.Count}");
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

                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessFilesInBackground正常終了\n");
                return (successCount, notFoundFiles.Count, saveDir);
            }
            catch (Exception ex)
            {
                SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessFilesInBackground例外: {ex.Message}\n{ex.StackTrace}\n");
                throw;
            }
            finally
            {
                // 无论成功还是失败，都清理临时文件夹
                if (!string.IsNullOrEmpty(workTempDir))
                {
                    try
                    {
                        if (Directory.Exists(workTempDir))
                        {
                            Directory.Delete(workTempDir, true);
                            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] [Finally] 作業用フォルダ削除: {workTempDir}\n");
                        }
                    }
                    catch (Exception cleanEx)
                    {
                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] [Finally] 作業用フォルダ削除失敗: {cleanEx.Message}\n");
                    }
                }
            }
        }

        /// <summary>
        /// マージファイルのバックグラウンド処理（フォアグラウンドスレッド版）
        /// </summary>
        private (int successCount, int notFoundCount, string saveDir) ProcessMergeFilesInBackground(string svnExe, string orgCode, string orgName, string svnBaseUrl)
        {
            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessMergeFilesInBackground開始 - スレッドID: {System.Threading.Thread.CurrentThread.ManagedThreadId}\n");

            string workTempDir = null;

            try
            {
                string saveDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{orgCode}_{orgName}_MergeFiles");

                if (Directory.Exists(saveDir))
                {
                    Directory.Delete(saveDir, true);
                }
                Directory.CreateDirectory(saveDir);

                var svnHelper = new SimpleSvnHelper(svnExe, msg => SafeLog("DebugLog.txt", msg + "\n"));

                UpdateStatus("SVNファイルリスト取得中...");

                workTempDir = Path.Combine(Path.GetTempPath(), "uhr_merge_ondemand_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                Directory.CreateDirectory(workTempDir);
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] 作業用一時フォルダ: {workTempDir}\n");

                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] === 按需下载模式（マージファイル）：仅下载需要的文件 ===\n");
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVN URL: {svnBaseUrl}\n");

                // 从lstMergeNeedsFile获取文件列表（需要在UI线程中读取）
                List<(string ver, string module, string fileName)> mergeFileList = new List<(string, string, string)>();
                this.Invoke(new Action(() =>
                {
                    foreach (var item in lstMergeNeedsFile.Items)
                    {
                        if (TryParseMergeListItem(item.ToString(), out var ver, out var module, out var fileName))
                        {
                            mergeFileList.Add((ver, module, fileName));
                        }
                    }
                }));

                // 提取文件名列表
                List<string> fileNames = mergeFileList.Select(x => x.fileName).ToList();

                // 调用通用下载方法（merge模式：下载所有哈希匹配的文件）
                var (successCount, notFoundFiles) = DownloadFilesOnDemand(fileNames, svnBaseUrl, svnExe, workTempDir, saveDir, svnHelper, downloadAllMatches: true);

                // 临时文件夹清理已移至finally块，确保无论成功失败都会清理

                if (notFoundFiles.Count > 0 && !string.IsNullOrEmpty(saveDir))
                {
                    string logFilePath = System.IO.Path.Combine(saveDir, "NotFoundFiles.log");
                    using (var writer = new System.IO.StreamWriter(logFilePath, false, System.Text.Encoding.UTF8))
                    {
                        writer.WriteLine($"マージファイル処理ログ - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        writer.WriteLine($"総ファイル数: {mergeFileList.Count}");
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

                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessMergeFilesInBackground正常終了\n");
                return (successCount, notFoundFiles.Count, saveDir);
            }
            catch (Exception ex)
            {
                SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessMergeFilesInBackground例外: {ex.Message}\n{ex.StackTrace}\n");
                throw;
            }
            finally
            {
                // 无论成功还是失败，都清理临时文件夹
                if (!string.IsNullOrEmpty(workTempDir))
                {
                    try
                    {
                        if (Directory.Exists(workTempDir))
                        {
                            Directory.Delete(workTempDir, true);
                            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] [Finally] 作業用フォルダ削除: {workTempDir}\n");
                        }
                    }
                    catch (Exception cleanEx)
                    {
                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] [Finally] 作業用フォルダ削除失敗: {cleanEx.Message}\n");
                    }
                }
            }
        }

        /// <summary>
        /// 简单的文件查找方法 - 返回第一个匹配的文件URL（不验证哈希，留到下载后验证）
        /// </summary>
        private string FindFileInSVNByHashSimple(string svnBaseUrl, string fileName, string targetHash, SimpleSvnHelper svnHelper)
        {
            try
            {
                // 获取文件列表
                if (!svnFileListCache.TryGetValue(svnBaseUrl, out List<string> allFiles))
                {
                    allFiles = GetAllFilesFromSvn(svnBaseUrl, svnHelper.svnExePath);
                    svnFileListCache[svnBaseUrl] = allFiles;
                }

                // 查找同名文件 - 返回第一个匹配的
                var candidate = allFiles.FirstOrDefault(file => Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase));

                if (candidate == null)
                    return null;

                string fullUrl = svnBaseUrl.TrimEnd('/') + "/" + candidate;
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] 候補発見: {fileName} -> {fullUrl}\n");
                return fullUrl;
            }
            catch (Exception ex)
            {
                SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] FindFileInSVNByHashSimple エラー: {fileName}\n{ex.Message}\n{ex.StackTrace}\n");
                return null;
            }
        }

        /// <summary>
        /// 从SVN路径中提取相对路径
        /// </summary>
        /// <summary>
        /// 从SVN路径中提取uhr或ROOT之后的相对路径（不包括uhr或ROOT本身）
        /// 例如: "v1.2.3/uhr/WEB-INF/classes/Foo.class" -> "WEB-INF/classes/Foo.class"
        /// </summary>
        private string ExtractRelativePathFromUhrOrRoot(string svnPath)
        {
            if (svnPath.Contains("/uhr/"))
            {
                int idx = svnPath.IndexOf("/uhr/");
                return svnPath.Substring(idx + 5); // /uhr/ 之后的部分
            }
            else if (svnPath.Contains("/ROOT/"))
            {
                int idx = svnPath.IndexOf("/ROOT/");
                return svnPath.Substring(idx + 6); // /ROOT/ 之后的部分
            }
            return null; // 既不包含uhr也不包含ROOT
        }

        private string ExtractRelativePath(string svnPath, string fileName)
        {
            if (svnPath.Contains("/uhr/"))
            {
                int idx = svnPath.IndexOf("/uhr/");
                return "uhr/" + svnPath.Substring(idx + 5);
            }
            else if (svnPath.Contains("/ROOT/"))
            {
                int idx = svnPath.IndexOf("/ROOT/");
                return "ROOT/" + svnPath.Substring(idx + 6);
            }
            else
            {
                return fileName;
            }
        }

        private void cmdMergeFilePrepare_Click(object sender, EventArgs e)
        {
            if (lstMergeNeedsFile.Items.Count == 0)
            {
                MessageBox.Show("マージが必要なファイルがありません。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 基本验证
            string svnExe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
            if (!System.IO.File.Exists(svnExe))
            {
                MessageBox.Show(AppConfig.MSG_SVN_EXE_NOT_FOUND, AppConfig.TITLE_DEPENDENCY_MISSING, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string orgCode = txtOrgCode.Text.Trim();
            string orgName = txtOrgName.Text.Trim();
            string svnBaseUrl = GetSvnUrl();

            if (string.IsNullOrEmpty(orgCode))
            {
                MessageBox.Show("機構コードと機構名を入力してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrEmpty(svnBaseUrl))
            {
                MessageBox.Show("SVN URLが取得できません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 禁用按钮,显示忙状态
            cmdMergeFilePrepare.Enabled = false;
            this.Cursor = Cursors.WaitCursor;
            _isTaskRunning = true;

            SimpleSvnHelper.CleanupTempFolders();
            System.IO.File.WriteAllText("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] === マージファイル処理開始（フォアグラウンドスレッド版） ===\n");

            // 使用前台线程而不是Task.Run(后台线程),防止应用程序意外退出
            var thread = new System.Threading.Thread(() =>
            {
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォアグラウンドスレッド開始、ID={System.Threading.Thread.CurrentThread.ManagedThreadId}, IsBackground={System.Threading.Thread.CurrentThread.IsBackground}\n");

                (int, int, string) result = (0, 0, "");
                Exception threadException = null;

                try
                {
                    result = ProcessMergeFilesInBackground(svnExe, orgCode, orgName, svnBaseUrl);
                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessMergeFilesInBackground完了、結果を取得\n");
                }
                catch (Exception ex)
                {
                    threadException = ex;
                    SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] スレッド例外: {ex.Message}\n{ex.StackTrace}\n");
                }

                // 检查窗体是否还存在
                if (this.IsDisposed || !this.IsHandleCreated)
                {
                    SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォームが既に破棄されています\n");
                    return;
                }

                try
                {
                    this.Invoke(new Action(() =>
                    {
                        _isTaskRunning = false;
                        this.Cursor = Cursors.Default;
                        cmdMergeFilePrepare.Enabled = true;

                        if (threadException != null)
                        {
                            SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] エラーをUI表示\n");
                            MessageBox.Show($"処理中にエラーが発生しました。\n{threadException.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            tslStatus.Text = "エラーが発生しました";
                        }
                        else
                        {
                            int successCount = result.Item1;
                            int notFoundCount = result.Item2;
                            string saveDir = result.Item3;

                            if (successCount > 0)
                            {
                                MessageBox.Show(
                                    $"処理が完了しました。\n成功: {successCount}個\n失敗: {notFoundCount}個\n保存先: {saveDir}",
                                    "完了",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information);

                                try
                                {
                                    System.Diagnostics.Process.Start("explorer.exe", saveDir);
                                }
                                catch (Exception ex)
                                {
                                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] エクスプローラー起動失敗: {ex.Message}\n");
                                }
                            }
                            else
                            {
                                MessageBox.Show("ファイルが見つかりませんでした。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }

                            tslStatus.Text = $"完了: {successCount}個成功, {notFoundCount}個失敗";
                        }

                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] UI更新完了\n");
                    }));
                }
                catch (Exception invokeEx)
                {
                    SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] Invoke例外: {invokeEx.Message}\n{invokeEx.StackTrace}\n");
                }

                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォアグラウンドスレッド終了\n");
            });

            thread.IsBackground = false;  // 设为前台线程，防止应用程序在线程运行时退出
            thread.Start();

            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォアグラウンドスレッド起動完了\n");
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
            var catStopwatch = Stopwatch.StartNew();
            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [CatHash] start: {fullSvnPath}");
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

                Process p = null;
                try
                {
                    p = Process.Start(psi);
                    if (p == null)
                    {
                        SafeLog("ErrorLog.txt", $"[cat] Start失败: {fullSvnPath}\n");
                        catStopwatch.Stop();
                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [CatHash] fail(start): {fullSvnPath}, elapsed={catStopwatch.ElapsedMilliseconds}ms\n");
                        return false;
                    }

                    var msOut = new MemoryStream();
                    var msErr = new MemoryStream();
                    var tOut = Task.Run(() => { 
                        try { p.StandardOutput.BaseStream.CopyTo(msOut); } 
                        catch (Exception ex) { SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] STDOUT読み取り失敗: {ex.Message}\n"); } 
                    });
                    var tErr = Task.Run(() => { 
                        try { p.StandardError.BaseStream.CopyTo(msErr); } 
                        catch (Exception ex) { SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] STDERR読み取り失敗: {ex.Message}\n"); } 
                    });

                    var start = DateTime.UtcNow;
                    const int timeoutMs = 120_000;
                    while (!p.WaitForExit(100))
                    {
                        if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                        {
                            try { p.Kill(); p.WaitForExit(3000); } catch { }

                            // タイムアウト時の処理
                            if (IsTextLikeByExt(fullSvnPath))
                            {
                                string svnExeLocal = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
                                var catRes = RunSvn(svnExeLocal,
                                                    $"cat \"{fullSvnPath}\" --non-interactive --trust-server-cert",
                                                    AppConfig.SVN_COMMAND_TIMEOUT_MS);
                                if (!catRes.TimedOut && catRes.ExitCode == 0 && !string.IsNullOrEmpty(catRes.Stdout))
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(fullSvnPath) ?? Path.GetTempPath());
                                    File.WriteAllText(fullSvnPath, catRes.Stdout, Encoding.UTF8);
                                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] cat OK: {fullSvnPath}\n");
                                    return true; // ★ 成功：把 cat 的结果当作下载成功
                                }

                                // cat失敗も記録
                                SafeLog("ErrorLog.txt",
                                    $"[{DateTime.Now:HH:mm:ss}] cat NG Exit={catRes.ExitCode} Timeout={catRes.TimedOut}\nURL={fullSvnPath}\nSTDERR:\n{catRes.Stderr}\n");
                            }

                            SafeLog("ErrorLog.txt", $"[cat] 超时: {fullSvnPath}\n");
                            catStopwatch.Stop();
                            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [CatHash] timeout: {fullSvnPath}, elapsed={catStopwatch.ElapsedMilliseconds}ms\n");
                            return false;
                        }
                        Application.DoEvents();
                    }
                    try { Task.WaitAll(new[] { tOut, tErr }, 2000); } catch { }

                    var stderr = Encoding.UTF8.GetString(msErr.ToArray());
                    if (p.ExitCode != 0)
                    {
                        SafeLog("ErrorLog.txt", $"[cat] 失败({p.ExitCode}): {fullSvnPath}\nSTDERR: {Trim400(stderr)}\n");
                        catStopwatch.Stop();
                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [CatHash] fail(exit={p.ExitCode}): {fullSvnPath}, elapsed={catStopwatch.ElapsedMilliseconds}ms\n");
                        return false;
                    }

                    var bytes = msOut.ToArray();
                    if (bytes.Length == 0)
                    {
                        hash = "empty_file_hash";
                        catStopwatch.Stop();
                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [CatHash] success(empty): {fullSvnPath}, elapsed={catStopwatch.ElapsedMilliseconds}ms\n");
                        return true;
                    }

                    var md5 = System.Security.Cryptography.MD5.Create();
                    var h = md5.ComputeHash(bytes);
                    hash = BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
                    catStopwatch.Stop();
                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [CatHash] success: {fullSvnPath}, bytes={bytes.Length}, hash={hash}, elapsed={catStopwatch.ElapsedMilliseconds}ms\n");
                    return true;
                }
                finally
                {
                    try
                    {
                        if (p != null && !p.HasExited)
                        {
                            p.Kill();
                            p.WaitForExit(1000);
                        }
                        p?.Dispose();
                    }
                    catch { /* 清理失败忽略 */ }
                }
            }
            catch (Exception ex)
            {
                SafeLog("ErrorLog.txt", $"[cat] 例外: {fullSvnPath}\n{ex}\n");
                catStopwatch.Stop();
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [CatHash] exception: {fullSvnPath}, elapsed={catStopwatch.ElapsedMilliseconds}ms, detail={ex.Message}");
                return false;
            }
        }

        // 安全尝试：export 到临时文件再算 hash（永不抛异常，负责清理临时文件）
        private bool SafeTryExportHash(string fullSvnPath, string fileName, out string hash)
        {
            hash = "";
            var exportStopwatch = Stopwatch.StartNew();
            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] start: {fullSvnPath}, file={fileName}");
            string tempFile = "";
            try
            {
                tempFile = DownloadFileFromSVN(fullSvnPath, fileName); // 同步下载（内部有超时/文件cat兜底）
                if (string.IsNullOrEmpty(tempFile) || !File.Exists(tempFile))
                {
                    SafeLog("ErrorLog.txt", $"[export] 未得到文件: {fullSvnPath}\n");
                    exportStopwatch.Stop();
                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] fail(no_file): {fullSvnPath}, elapsed={exportStopwatch.ElapsedMilliseconds}ms");
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
                    exportStopwatch.Stop();
                    SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] fail(empty_file): {fullSvnPath}, elapsed={exportStopwatch.ElapsedMilliseconds}ms");
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
                            var hashBytes = md5.ComputeHash(fs);
                            hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                            exportStopwatch.Stop();
                            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] success: {fullSvnPath}, file={fileName}, size={fs.Length}, hash={hash}, elapsed={exportStopwatch.ElapsedMilliseconds}ms\n");
                            return true;
                        }
                    }
                    catch (IOException ioex)
                    {
                        if (i == 2)
                        {
                            SafeLog("ErrorLog.txt", $"[export] 读取文件计算哈希失败: {tempFile}\n{ioex}\n");
                            exportStopwatch.Stop();
                            SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] fail(read): {fullSvnPath}, elapsed={exportStopwatch.ElapsedMilliseconds}ms, detail={ioex.Message}\n");
                            return false;
                        }
                        System.Threading.Thread.Sleep(150);
                    }
                    catch (Exception ex)
                    {
                        SafeLog("ErrorLog.txt", $"[export] 计算哈希时发生异常: {tempFile}\n{ex}\n");
                        exportStopwatch.Stop();
                        SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] exception(hash_calc): {fullSvnPath}, elapsed={exportStopwatch.ElapsedMilliseconds}ms, detail={ex.Message}\n");
                        return false;
                    }
                }

                exportStopwatch.Stop();
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] fail(unexpected): {fullSvnPath}, elapsed={exportStopwatch.ElapsedMilliseconds}ms");
                return false; // 理论到不了
            }
            catch (Exception ex)
            {
                SafeLog("ErrorLog.txt", $"[export] 例外: {fullSvnPath}\n{ex}\n");
                exportStopwatch.Stop();
                SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] exception: {fullSvnPath}, elapsed={exportStopwatch.ElapsedMilliseconds}ms, detail={ex.Message}");
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
        private static void SafeLog(string file, string text)
        {
            try { System.IO.File.AppendAllText(file, text); }
            catch { /* 日志失败不可影响主流程 */ }
        }


    }
}