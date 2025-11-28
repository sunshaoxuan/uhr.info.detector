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

        #region Process待機と安定化の補助メソッド
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
                    catch (Exception ex) { LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] プロセス強制終了失敗: {ex.Message}\n"); }
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
        /// <param name="delayMs">チェック間隔（ミリ秒）</param>
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

        // エンコーディング定義は EncodingHelper クラスに移動済み

        // DecodeSvnText メソッドは EncodingHelper.DecodeSvnText に移動済み

        // テキストファイル拡張子（失敗時はsvn catにフォールバック）
        private static bool IsTextLikeByExt(string pathOrName)
        {
            var ext = Path.GetExtension(pathOrName)?.ToLowerInvariant();
            return ext == ".htm" || ext == ".html" || ext == ".jsp" || ext == ".js" ||
                   ext == ".css" || ext == ".xml" || ext == ".json" || ext == ".txt" ||
                   ext == ".md" || ext == ".dicon";
        }

        // 安全な取得：まずexport（タイムアウトとリトライ付き）、失敗してテキストならcat、すべて失敗でfalseを返す
        private bool TryFetchToFile(string svnExe, string fullSvnPath, string localPath, int exportTimeoutMs, int catTimeoutMs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? AppDomain.CurrentDomain.BaseDirectory);

            // export を2回試行
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
                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] export OK: {fullSvnPath} -> {localPath} size={finalSize}\n");
                        return true;
                    }
                }

                LogHelper.SafeLog("ErrorLog.txt",
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
                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] cat OK: {fullSvnPath} -> {localPath} len={resCat.Stdout.Length}\n");
                    return true;
                }
                LogHelper.SafeLog("ErrorLog.txt",
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
                // 強制エンコーディングが指定されている場合、それを使用、そうでなければ既存のエンコーディング戦略を維持
                StandardOutputEncoding = forcedEncoding ?? EncodingHelper.SvnConsoleEncoding,
                StandardErrorEncoding = forcedEncoding ?? EncodingHelper.SvnConsoleEncoding,
            };

            using (var p = Process.Start(psi))
            {
                if (p == null) return new SvnResult { ExitCode = -1, Stderr = "Process.Start returned null" };

                var msOut = new MemoryStream();
                var msErr = new MemoryStream();
                var tOut = Task.Run(() => { 
                    try { p.StandardOutput.BaseStream.CopyTo(msOut); } 
                    catch (Exception ex) { LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] STDOUT読み取り失敗: {ex.Message}\n"); } 
                });
                var tErr = Task.Run(() => { 
                    try { p.StandardError.BaseStream.CopyTo(msErr); } 
                    catch (Exception ex) { LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] STDERR読み取り失敗: {ex.Message}\n"); } 
                });

                bool timedOut = !WaitProcessWithTimeout(p, timeoutMs);
                try { Task.WaitAll(new[] { tOut, tErr }, 2000); } 
                catch (Exception ex) { LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] Task.WaitAll失敗: {ex.Message}\n"); }

                // psiで指定されたエンコーディングを使用してデコード
                var enc = forcedEncoding ?? EncodingHelper.SvnConsoleEncoding;
                return new SvnResult
                {
                    ExitCode = timedOut ? -1 : p.ExitCode,
                    Stdout = enc.GetString(msOut.ToArray()),
                    Stderr = enc.GetString(msErr.ToArray()),
                    TimedOut = timedOut
                };
            }
        }


        // 全機関リストをキャッシュ
        private List<string> orgListCache = new List<string>();
        private string lastOrgFilterText = string.Empty; // 前回のフィルタテキストを保存（不要な刷新を防ぐ）
        private bool isUpdatingOrgList = false; // 機関リスト更新中フラグ（不要な刷新を防ぐ）
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
            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォーム閉じる Reason={e.CloseReason}, TaskRunning={_isTaskRunning}\n");

            if (_isTaskRunning && e.CloseReason == CloseReason.UserClosing)
            {
                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] タスク実行中のため閉じるをキャンセル\n");
                e.Cancel = true;
                MessageBox.Show("処理中です。完了するまでお待ちください。", "処理中", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private async void frmMain_Load(object sender, EventArgs e)
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
            isUpdatingOrgList = true; // 更新フラグを設定
            try
            {
                lstOrgs.Items.Clear();
                lstOrgs.Items.AddRange(orgList.ToArray());
            }
            finally
            {
                isUpdatingOrgList = false;
            }

            // 5. ターゲットバージョンリストの初期化
            // データベースから共通のMODULE_INFO表から最高バージョンを読み込む
            try
            {
                await InitTargetVersionListFromDatabase();
                Debug.WriteLine($"[Init] バージョンリスト初期化完了");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"バージョンリスト初期化中にエラーが発生しました:\n\n{ex.Message}\n\nスタックトレース:\n{ex.StackTrace}",
                    "初期化エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                InitEmptyTargetVersionList();
            }

            // 6. コントロールのロック等を保持（元コード続行）
            txtOrgCode.ReadOnly = true; txtOrgCode.BackColor = Color.White;
            txtOrgName.ReadOnly = true; txtOrgName.BackColor = Color.White;
            txtFWVersion.ReadOnly = true; txtFWVersion.BackColor = Color.White;
            txtSalaryVersion.ReadOnly = true; txtSalaryVersion.BackColor = Color.White;
            txtCoreVersion.ReadOnly = true; txtCoreVersion.BackColor = Color.White;
            txtYearAdjustVersion.ReadOnly = true; txtYearAdjustVersion.BackColor = Color.White;
            cmdShowReport.Enabled = false;
            txtLastUpdatedInfo.Enabled = true;
            txtLastUpdatedInfo.ReadOnly = true; 
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
                    dirs.Sort(VersionCompareHelper.CompareVersionStringSmartAsc); // 数字桁数優先ソート
                    // ComboBox 表示は降順（新→旧）
                    var displayDirs = dirs.OrderByDescending(x => x, Comparer<string>.Create(VersionCompareHelper.CompareVersionStringSmartAsc)).ToList();
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
                    dirs.Sort(VersionCompareHelper.CompareVersionStringSmartAsc);
                    // ComboBox 表示は降順（新→旧）
                    var displayDirs = dirs.OrderByDescending(x => x, Comparer<string>.Create(VersionCompareHelper.CompareVersionStringSmartAsc)).ToList();
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
                    dirs.Sort(VersionCompareHelper.CompareVersionStringSmartAsc);
                    // ComboBox 表示は降順（新→旧）
                    var displayDirs = dirs.OrderByDescending(x => x, Comparer<string>.Create(VersionCompareHelper.CompareVersionStringSmartAsc)).ToList();
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

        /// <summary>
        /// データベースからターゲットバージョンリストを初期化（新しい実装）
        /// MODULE_INFO表のFUNCTIONVERフィールドから最大バージョンを取得
        /// </summary>
        private async Task InitTargetVersionListFromDatabase()
        {
            Debug.WriteLine($"========== InitTargetVersionListFromDatabase 開始 ==========");

            // 1. cboFWTargetVersion を固定値に設定：4.18.4、変更不可
            cboFWTargetVersion.Items.Clear();
            cboFWTargetVersion.Items.Add(AppConfig.FIXED_FW_TARGET_VERSION);
            cboFWTargetVersion.SelectedIndex = AppConfig.DEFAULT_SELECTED_INDEX;
            cboFWTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;

            // 2. データベースから共通機能（Core）バージョンリストを取得
            Debug.WriteLine($"[InitDB] Core バージョン取得開始");
            try
            {
                var coreVersions = await GetAllVersionsFromDatabase("Core_V");
                Debug.WriteLine($"[InitDB] Core バージョン取得結果: {coreVersions.Count}個");
                cboCoreTargetVersion.Items.Clear();
                if (coreVersions.Count > 0)
                {
                    cboCoreTargetVersion.Items.AddRange(coreVersions.ToArray());
                    cboCoreTargetVersion.SelectedIndex = AppConfig.DEFAULT_SELECTED_INDEX; // 最大バージョン（最初の要素）を選択
                    Debug.WriteLine($"[InitDB] Core バージョンリストを設定しました（{coreVersions.Count}個、デフォルト: {coreVersions[0]}）");
                }
                else
                {
                    Debug.WriteLine($"[InitDB] Core バージョンが空でした");
                }
                cboCoreTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InitDB] Core バージョン取得エラー: {ex.Message}");
                Debug.WriteLine($"[InitDB] スタックトレース: {ex.StackTrace}");
                cboCoreTargetVersion.Items.Clear();
                cboCoreTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            }

            // 3. データベースから給与明細（Salary）バージョンリストを取得
            try
            {
                Debug.WriteLine($"[InitDB] Salary バージョン取得開始");
                var salaryVersions = await GetAllVersionsFromDatabase("Salary_V");
                Debug.WriteLine($"[InitDB] Salary バージョン取得結果: {salaryVersions.Count}個");
                cboSalaryTargetVersion.Items.Clear();
                if (salaryVersions.Count > 0)
                {
                    cboSalaryTargetVersion.Items.AddRange(salaryVersions.ToArray());
                    cboSalaryTargetVersion.SelectedIndex = AppConfig.DEFAULT_SELECTED_INDEX; // 最大バージョン（最初の要素）を選択
                    Debug.WriteLine($"[InitDB] Salary ComboBoxに設定: {salaryVersions.Count}個（デフォルト: {salaryVersions[0]}）");
                }
                else
                {
                    Debug.WriteLine($"[InitDB] Salary バージョンが空のため、ComboBoxに何も追加しません");
                }
                cboSalaryTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InitDB] Salary バージョン取得エラー: {ex.Message}");
                Debug.WriteLine($"[InitDB] スタックトレース: {ex.StackTrace}");
                cboSalaryTargetVersion.Items.Clear();
                cboSalaryTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            }

            // 4. データベースから年末調整（Nencho）バージョンリストを取得
            try
            {
                Debug.WriteLine($"[InitDB] Nencho バージョン取得開始");
                var nenchoVersions = await GetAllVersionsFromDatabase("Nencho_V");
                Debug.WriteLine($"[InitDB] Nencho バージョン取得結果: {nenchoVersions.Count}個");
                cboYearAdjustTargetVersion.Items.Clear();
                if (nenchoVersions.Count > 0)
                {
                    cboYearAdjustTargetVersion.Items.AddRange(nenchoVersions.ToArray());
                    cboYearAdjustTargetVersion.SelectedIndex = AppConfig.DEFAULT_SELECTED_INDEX; // 最大バージョン（最初の要素）を選択
                    Debug.WriteLine($"[InitDB] Nencho ComboBoxに設定: {nenchoVersions.Count}個（デフォルト: {nenchoVersions[0]}）");
                }
                else
                {
                    Debug.WriteLine($"[InitDB] Nencho バージョンが空のため、ComboBoxに何も追加しません");
                }
                cboYearAdjustTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InitDB] Nencho バージョン取得エラー: {ex.Message}");
                Debug.WriteLine($"[InitDB] スタックトレース: {ex.StackTrace}");
                cboYearAdjustTargetVersion.Items.Clear();
                cboYearAdjustTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            }

            // 5. 諸手当（Shoteate）バージョンリストを取得
            try
            {
                Debug.WriteLine($"[InitDB] Shoteate バージョン取得開始");
                var shoteateVersions = await GetAllVersionsFromDatabase("Shoteate_V");
                Debug.WriteLine($"[InitDB] Shoteate バージョン取得結果: {shoteateVersions.Count}個");
                cboShoteateTargetVersion.Items.Clear();
                if (shoteateVersions.Count > 0)
                {
                    cboShoteateTargetVersion.Items.AddRange(shoteateVersions.ToArray());
                    cboShoteateTargetVersion.SelectedIndex = AppConfig.DEFAULT_SELECTED_INDEX; // 最大バージョン（最初の要素）を選択
                    Debug.WriteLine($"[InitDB] Shoteate ComboBoxに設定: {shoteateVersions.Count}個（デフォルト: {shoteateVersions[0]}）");
                }
                else
                {
                    Debug.WriteLine($"[InitDB] Shoteate バージョンが空のため、ComboBoxに何も追加しません");
                }
                cboShoteateTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InitDB] Shoteate バージョン取得エラー: {ex.Message}");
                Debug.WriteLine($"[InitDB] スタックトレース: {ex.StackTrace}");
                cboShoteateTargetVersion.Items.Clear();
                cboShoteateTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            }
        }

        /// <summary>
        /// ターゲットバージョンリストを空で初期化
        /// </summary>
        private void InitEmptyTargetVersionList()
        {
            // フレームワークバージョンは固定値
            cboFWTargetVersion.Items.Clear();
            cboFWTargetVersion.Items.Add(AppConfig.FIXED_FW_TARGET_VERSION);
            cboFWTargetVersion.SelectedIndex = AppConfig.DEFAULT_SELECTED_INDEX;
            cboFWTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;

            // 他のComboBoxは空のまま
            cboCoreTargetVersion.Items.Clear();
            cboCoreTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            cboSalaryTargetVersion.Items.Clear();
            cboSalaryTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            cboYearAdjustTargetVersion.Items.Clear();
            cboYearAdjustTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            cboShoteateTargetVersion.Items.Clear();
            cboShoteateTargetVersion.DropDownStyle = ComboBoxStyle.DropDownList;
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
                // -R で再帰、--xml は UTF-8 固定整形
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


        // 汎用バージョン比較・ファイル衝突チェックメソッド
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

            tslStatus.Text = $"{moduleName}バージョン比較: {currentVersion} → {targetVersion}";

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

                // バージョン名と実際のフォルダ名のマッピングを作成
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
                allVersions.Sort(VersionCompareHelper.CompareVersionStringSmartAsc);
                System.Diagnostics.Debug.WriteLine($"{moduleName}全バージョン: {string.Join(", ", allVersions)}");

                // スマートバージョンマッチング
                int idxCur = VersionCompareHelper.FindSmartVersionIndex(allVersions, currentVersion, moduleName);
                int idxTarget = VersionCompareHelper.FindSmartVersionIndex(allVersions, targetVersion, moduleName);
                System.Diagnostics.Debug.WriteLine($"{moduleName}現在バージョンインデックス: {idxCur}, 目標バージョンインデックス: {idxTarget}");

                if (idxCur >= 0 && idxTarget >= 0 && idxCur != idxTarget)
                {
                    List<string> needVers;
                    if (idxCur < idxTarget)
                    {
                        // アップグレード: (cur, target]
                        needVers = allVersions.GetRange(idxCur + 1, idxTarget - idxCur);
                    }
                    else
                    {
                        // ダウングレード: (target, cur]
                        needVers = allVersions.GetRange(idxTarget + 1, idxCur - idxTarget);
                    }

                                  System.Diagnostics.Debug.WriteLine($"{moduleName}確認が必要なバージョン: {string.Join(", ", needVers)}");

                    foreach (var ver in needVers)
                    {
                        token.ThrowIfCancellationRequested();
                        tslStatus.Text = $"{moduleName} {ver} のファイルを確認中...";

                        // 実際のフォルダ名を使用してパスを構成
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
                            System.Diagnostics.Debug.WriteLine($"{moduleName}フォルダ名のマッピングが見つかりません: {ver}");
                        }
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"{moduleName}ルートディレクトリが見つかりませんでした。キーワード: {folderKeyword}");
            }
        }

        // FindSmartVersionIndex メソッドは VersionCompareHelper.FindSmartVersionIndex に移動済み（直接呼び出し）

        // 年末調整用のバージョンマッチングは VersionCompareHelper.FindSmartVersionIndex を直接使用

        // CompareVersionStringSmartAsc メソッドは VersionCompareHelper.CompareVersionStringSmartAsc に移動済み（直接呼び出し）

        private void txtOrgFilter_TextChanged(object sender, EventArgs e)
        {
            // 既に更新中の場合は何もしない（再帰呼び出しを防ぐ）
            if (isUpdatingOrgList)
            {
                return;
            }
            
            string filterText = txtOrgFilter.Text.Trim();
            
            // テキストが実際に変更されていない場合は何もしない（不要な刷新を防ぐ）
            if (filterText == lastOrgFilterText)
            {
                return;
            }
            
            // フィルタテキストボックスにフォーカスがない場合は更新しない（他のコントロール操作による誤動作を防ぐ）
            if (!txtOrgFilter.Focused)
            {
                // フォーカスがない場合は、lastOrgFilterTextを更新しない（次回フォーカスが戻った時に正しく動作するため）
                return;
            }
            
            // 現在のアクティブコントロールがフィルタテキストボックスでない場合も更新しない
            // また、アクティブコントロールが目標バージョンComboBoxのいずれかでもない場合も更新しない
            var activeControl = this.ActiveControl;
            if (activeControl != txtOrgFilter)
            {
                // 目標バージョンComboBoxのいずれかがアクティブな場合は更新しない
                if (activeControl == cboFWTargetVersion ||
                    activeControl == cboCoreTargetVersion ||
                    activeControl == cboSalaryTargetVersion ||
                    activeControl == cboYearAdjustTargetVersion ||
                    activeControl == cboShoteateTargetVersion)
                {
                    return;
                }
                
                // その他のコントロールがアクティブな場合も更新しない
                if (activeControl != null)
                {
                    return;
                }
            }
            
            // 更新フラグを設定
            isUpdatingOrgList = true;
            try
            {
                lastOrgFilterText = filterText;
                
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
            finally
            {
                isUpdatingOrgList = false;
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
                Application.DoEvents(); // UI更新

                token.ThrowIfCancellationRequested();

                // 5. 次にカスタマイズファイルリストを取得（非同期処理：GetSvnFileListXmlRecursive）
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
                    // SVN最後更新情報を取得して表示
                    await GatherSvnLastUpdatedInfoAsync();

                    SwitchFilePrepareButton(true, 1); // カスタマイズファイル準備ボタンを表示
                    tslStatus.Text = $"カスタマイズファイルが{lstCustomizedFile.Items.Count}個見つかりました。";

                    // 6. 最後にファイル比較を実行（非同期処理）
                    await CompareFiles(customizedFiles, token);

                    if (lstMergeNeedsFile.Items.Count > 0)
                    {
                        SwitchFilePrepareButton(true, 2); // マージファイル準備ボタンを表示
                        tslStatus.Text = $"カスタマイズファイルと競合するファイルが{lstMergeNeedsFile.Items.Count}個見つかりました。";
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
                tslStatus.Text = $"カスタマイズファイル: {result.Count}個が見つかりました。";
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

        /// <summary>
        /// データベースからFUNCTIONVERの一覧を取得し、指定されたプレフィックスの最大バージョンを返す
        /// 目標バージョン用：MODULE_INFOテーブル（組織コードなし）から読み込む
        /// </summary>
        /// <param name="prefix">バージョンのプレフィックス（例: "Core_V", "Salary_V", "Nencho_V", "Shoteate_V"）</param>
        /// <returns>最大のバージョン番号、エラーの場合は空文字列</returns>
        /// <summary>
        /// データベースから指定されたプレフィックスに一致するすべてのバージョンリストを取得
        /// </summary>
        /// <param name="prefix">バージョンプレフィックス（例: "Core_V", "Salary_V"）</param>
        /// <returns>バージョンリスト（降順ソート済み）</returns>
        private async Task<List<string>> GetAllVersionsFromDatabase(string prefix)
        {
            Debug.WriteLine($"---------- GetAllVersionsFromDatabase 開始 ----------");
            Debug.WriteLine($"[GetAllVer] prefix='{prefix}'");

            if (string.IsNullOrEmpty(prefix))
            {
                Debug.WriteLine($"[GetAllVer] パラメータが空のため終了");
                return new List<string>();
            }

            string tableName = "MODULE_INFO";  // 目標バージョンは組織に依存しない共通テーブル
            string connStr = AppConfig.ORACLE_CONNECTION_STRING;
            string sql = $"SELECT DISTINCT FUNCTIONVER FROM {tableName} ORDER BY FUNCTIONVER";

            Debug.WriteLine($"[GetAllVer] テーブル名: {tableName}");
            Debug.WriteLine($"[GetAllVer] SQL: {sql}");

            try
            {
                var functionVersions = new List<string>();

                Debug.WriteLine($"[GetAllVer] データベース接続開始");
                using (var conn = new OracleConnection(connStr))
                {
                    await conn.OpenAsync();
                    Debug.WriteLine($"[GetAllVer] データベース接続成功");

                    using (var cmd = new OracleCommand(sql, conn))
                    {
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string functionVer = reader["FUNCTIONVER"]?.ToString() ?? "";
                                if (!string.IsNullOrEmpty(functionVer))
                                {
                                    functionVersions.Add(functionVer);
                                    Debug.WriteLine($"[DB] FUNCTIONVER: {functionVer}");
                                }
                            }
                        }
                    }
                }

                Debug.WriteLine($"[DB] 取得したFUNCTIONVER総数: {functionVersions.Count}");

                // プレフィックスに一致するバージョンをフィルタリング
                var matchedVersions = functionVersions
                    .Where(v => v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Debug.WriteLine($"[DB] {prefix}に一致するバージョン数: {matchedVersions.Count}");

                // バージョン番号を抽出してソート用のキーを作成
                var versionPairs = matchedVersions
                    .Select(v => new { FullVersion = v, VersionNumber = VersionCompareHelper.ExtractVersionFromTail(v) })
                    .Where(vp => !string.IsNullOrEmpty(vp.VersionNumber))
                    .OrderByDescending(vp => vp.VersionNumber, new VersionComparer())
                    .Select(vp => vp.VersionNumber)  // バージョン番号部分のみを返す
                    .Distinct()  // 重複を除去
                    .ToList();

                Debug.WriteLine($"[DB] {prefix}のバージョンリスト（降順）: {string.Join(", ", versionPairs)}");

                return versionPairs;
            }
            catch (OracleException ex)
            {
                Debug.WriteLine($"[GetAllVer] Oracle例外発生: {ex.Message}");

                // テーブルが存在しない場合、大文字のテーブル名で再試行
                if (ex.Message.Contains(AppConfig.ORACLE_ERROR_TABLE_NOT_EXISTS) ||
                    ex.Message.Contains(AppConfig.ORACLE_ERROR_OBJECT_NOT_EXISTS))
                {
                    Debug.WriteLine($"[GetAllVer] テーブルが見つかりません。大文字で再試行します");
                    tableName = tableName.ToUpper();
                    sql = $"SELECT DISTINCT FUNCTIONVER FROM {tableName} ORDER BY FUNCTIONVER";
                    Debug.WriteLine($"[GetAllVer] 新しいSQL: {sql}");

                    try
                    {
                        var functionVersions = new List<string>();

                        using (var conn = new OracleConnection(connStr))
                        {
                            await conn.OpenAsync();
                            Debug.WriteLine($"[GetAllVer] 再試行: データベース接続成功");
                            using (var cmd = new OracleCommand(sql, conn))
                            {
                                using (var reader = await cmd.ExecuteReaderAsync())
                                {
                                    while (await reader.ReadAsync())
                                    {
                                        string functionVer = reader["FUNCTIONVER"]?.ToString() ?? "";
                                        if (!string.IsNullOrEmpty(functionVer))
                                        {
                                            functionVersions.Add(functionVer);
                                        }
                                    }
                                }
                            }
                        }

                        Debug.WriteLine($"[GetAllVer] 再試行で取得したバージョン数: {functionVersions.Count}");
                        var matchedVersions = functionVersions
                            .Where(v => v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        var versionPairs = matchedVersions
                            .Select(v => new { FullVersion = v, VersionNumber = VersionCompareHelper.ExtractVersionFromTail(v) })
                            .Where(vp => !string.IsNullOrEmpty(vp.VersionNumber))
                            .OrderByDescending(vp => vp.VersionNumber, new VersionComparer())
                            .Select(vp => vp.VersionNumber)  // バージョン番号部分のみを返す
                            .Distinct()  // 重複を除去
                            .ToList();
                        Debug.WriteLine($"[GetAllVer] 再試行成功。{prefix}のバージョン数: {versionPairs.Count}");
                        return versionPairs;
                    }
                    catch (Exception retryEx)
                    {
                        Debug.WriteLine($"[GetAllVer] 再試行も失敗 ({prefix}): {retryEx.Message}");
                        return new List<string>();
                    }
                }
                else
                {
                    Debug.WriteLine($"GetAllVersionsFromDatabase エラー ({prefix}): {ex.Message}");
                    return new List<string>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetAllVersionsFromDatabase 予期しないエラー ({prefix}): {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// データベースから指定されたプレフィックスの最大バージョンを取得（後方互換性のため保持）
        /// </summary>
        private async Task<string> GetMaxVersionFromDatabase(string prefix)
        {
            var allVersions = await GetAllVersionsFromDatabase(prefix);
            if (allVersions.Count > 0)
            {
                return allVersions[0]; // 降順ソート済みなので最初が最大バージョン
            }
            return string.Empty;
        }

        // MD5 HASH値を計算（改良版、より多くの保護機能を追加）
        private string CalculateFileHash(string filePath)
        {
            try
            {
                // ファイルの存在とアクセス可能性を厳密にチェック
                if (string.IsNullOrEmpty(filePath))
                {
                    System.Diagnostics.Debug.WriteLine("CalculateFileHash: ファイルパスが空");
                    return "";
                }

                if (!File.Exists(filePath))
                {
                    System.Diagnostics.Debug.WriteLine($"CalculateFileHash: ファイル不存在: {filePath}");
                    return "";
                }

                // ファイルサイズをチェックし、過大なファイルの処理を避ける
                FileInfo fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 50 * 1024 * 1024) // 50MB制限
                {
                    System.Diagnostics.Debug.WriteLine($"CalculateFileHash: ファイル過大({fileInfo.Length / 1024 / 1024}MB): {filePath}");
                    return "";
                }

                // 極小のファイルの場合、空ファイルの可能性もある
                if (fileInfo.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"CalculateFileHash: ファイルが空: {filePath}");
                    return "empty_file_hash";
                }

                System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 計算開始: {filePath} (サイズ: {fileInfo.Length} bytes)");

                // ファイルの解放を待機し、他のプロセスによる占有を防ぐ
                int retryCount = 0;
                while (retryCount < 5)
                {
                    try
                    {
                        // 厳密な方法でファイル読み取りを試行
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
                            System.Diagnostics.Debug.WriteLine($"CalculateFileHash: HASH計算成功: {hash}");
                            return hash;
                        }
                    }
                    catch (IOException ioEx) when (retryCount < 4)
                    {
                        // ファイルが占有されている場合、待機後にリトライ
                        retryCount++;
                        System.Diagnostics.Debug.WriteLine($"CalculateFileHash: ファイルが占有中、再試行{retryCount}/5: {ioEx.Message}");
                        System.Threading.Thread.Sleep(500); // 500ms待機
                    }
                    catch (UnauthorizedAccessException authEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"CalculateFileHash: アクセス権限拒否: {authEx.Message}");
                        return "";
                    }
                    catch (Exception otherEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"CalculateFileHash: その他のエラー: {otherEx.Message}");
                        break;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"CalculateFileHash: リトライ回数が尽きました、HASH計算失敗");
                return "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CalculateFileHash: 全体例外: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"CalculateFileHash: スタックトレース: {ex.StackTrace}");
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

            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] export開姁E {fileName}\n  URL={fullSvnPath}\n  OUT={targetPath}\n");

            if (!File.Exists(svnExe))
            {
                LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVN実行ファイル不存在: {svnExe}\n");
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
                StandardOutputEncoding = EncodingHelper.SvnConsoleEncoding,   // ★新規追加：CP932でデコード
                StandardErrorEncoding = EncodingHelper.SvnConsoleEncoding    // ★新規追加
            };

            Process proc = null;
            try
            {
                proc = System.Diagnostics.Process.Start(psi);
                if (proc == null)
                {
                    LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] プロセス起動失敗\n");
                    return "";
                }

                var msOut = new MemoryStream();
                var msErr = new MemoryStream();
                var tOut = Task.Run(() => { 
                    try { proc.StandardOutput.BaseStream.CopyTo(msOut); } 
                    catch (Exception ex) { LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] STDOUT読み取り失敗: {ex.Message}\n"); } 
                });
                var tErr = Task.Run(() => { 
                    try { proc.StandardError.BaseStream.CopyTo(msErr); } 
                    catch (Exception ex) { LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] STDERR読み取り失敗: {ex.Message}\n"); } 
                });

                const int timeoutMs = 120_000;
                if (!WaitProcessWithTimeout(proc, timeoutMs))
                {
                    // タイムアウト時の処琁E
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
                                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] cat OK: {fileName} -> {targetPath}\n");
                                return targetPath; // ★成功：catの結果をダウンロード成功として扱う
                            }

                            // cat失敗も記録
                            LogHelper.SafeLog("ErrorLog.txt",
                                $"[{DateTime.Now:HH:mm:ss}] cat NG Exit={catRes.ExitCode} Timeout={catRes.TimedOut}\nURL={fullSvnPath}\nSTDERR:\n{catRes.Stderr}\n");
                        }

                        LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] svn export タイムアウト: {fullSvnPath}\n");
                        return "";
                    }


                // … stdoutTask / stderrTask の後処理終了
                try { tOut.Wait(2000); } 
                catch (Exception ex) { LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] tOut.Wait失敗: {ex.Message}\n"); }
                try { tErr.Wait(2000); } 
                catch (Exception ex) { LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] tErr.Wait失敗: {ex.Message}\n"); }

                byte[] rawOut = msOut.ToArray();
                byte[] rawErr = msErr.ToArray();

                string stdoutStr = EncodingHelper.DecodeSvnText(rawOut);
                string stderrStr = EncodingHelper.DecodeSvnText(rawErr);

                if (proc.ExitCode != 0)
                {
                    LogHelper.SafeLog("ErrorLog.txt",
                        $"[{DateTime.Now:HH:mm:ss}] export失敗(ExitCode={proc.ExitCode}) URL={fullSvnPath}\n" +
                        $"STDERR:\n{(stderrStr.Length > 400 ? stderrStr.Substring(0, 400) + "..." : stderrStr)}\n");
                    return "";
                }

                for (int i = 0; i < 25 && !File.Exists(targetPath); i++) System.Threading.Thread.Sleep(20);
                if (!File.Exists(targetPath))
                {
                    LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] export終了後も出力ファイル不存在: {targetPath}\n");
                    return "";
                }

                long finalSize = WaitFileStable(targetPath);
                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] export成功: {fileName} size={finalSize}\n");
                return targetPath;
            }
            catch (Exception ex)
            {
                LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] DownloadFileFromSVN 例外: {ex.Message}\nStackTrace: {ex.StackTrace}\n");
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
                catch { /* 削除失敗は無視 */ }
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
                if (res.TimedOut) throw new Exception("SVNリストコマンドタイムアウト");
                if (res.ExitCode != 0) throw new Exception($"SVNリストコマンド失敗(ExitCode={res.ExitCode}): {res.Stderr}");
                if (string.IsNullOrWhiteSpace(res.Stdout)) throw new Exception($"SVNが何も結果を返しませんでした: {baseSvnUrl}");

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
                    throw new Exception($"機関コード'{orgCode}'に対応するSVNフォルダが見つかりません。利用可能フォルダ: {string.Join(", ", names)}");

                System.Diagnostics.Debug.WriteLine($"見つけた機関フォルダ: {orgFolder}");
                return orgFolder; // →引き続き返す
            }
            catch (Exception ex)
            {
                throw new Exception($"機関SVNフォルダの検索失敗: {ex.Message}");
            }
        }



        // SVNキャッシュ関連フィールド
        private Dictionary<string, List<string>> svnFileListCache = new Dictionary<string, List<string>>();

        // SVN URLパスを取得（動的に正しいフォルダーを検索）
        private string GetSvnUrl()
        {
            string orgCode = txtOrgCode.Text.Trim();
            string orgFolder = FindOrgSvnFolder(orgCode);
            System.Diagnostics.Debug.WriteLine($"見つけたorgFolderの実際の値: '{orgFolder}'");

            string baseOrgUrl = $"{AppConfig.SVN_CUSTOMIZED_PATH}{orgFolder}";
            System.Diagnostics.Debug.WriteLine($"基機関URL: '{baseOrgUrl}'");

            // 機構フォルダ以下の構造を確認し、有効なカスタマイズフォルダを自動検出
            string svnPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
            var validFolders = DetectValidCustomizationFolders(baseOrgUrl, svnPath);
            System.Diagnostics.Debug.WriteLine($"検出された有効なフォルダ: {string.Join(", ", validFolders)}");

            if (validFolders.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"有効なフォルダが見つからない、基本ディレクトリを使用: {baseOrgUrl}");
                return baseOrgUrl;
            }
            else if (validFolders.Count == 1)
            {
                string selectedFolder = $"{baseOrgUrl}/{validFolders[0]}";
                System.Diagnostics.Debug.WriteLine($"自動選択: {selectedFolder}");
                return selectedFolder;
            }
            else
            {
                // 複数見つかった場合、ユーザーに選択させる
                string selectedFolder = ShowFolderSelectionDialog(validFolders);
                if (!string.IsNullOrEmpty(selectedFolder))
                {
                    string fullPath = $"{baseOrgUrl}/{selectedFolder}";
                    System.Diagnostics.Debug.WriteLine($"ユーザー選択: {fullPath}");
                    return fullPath;
                }
                else
                {
                    // キャンセルされた場合は最初のフォルダを使用
                    string fallbackPath = $"{baseOrgUrl}/{validFolders[0]}";
                    System.Diagnostics.Debug.WriteLine($"キャンセル、最初のフォルダを使用: {fallbackPath}");
                    return fallbackPath;
                }
            }
        }

        /// <summary>
        /// 有効なカスタマイズフォルダを自動検出する
        /// 条件: uhr, WEB-INF, jsp サブディレクトリと index.html ファイルが存在すること
        /// 除外: backup フォルダは検出対象外
        /// </summary>
        private List<string> DetectValidCustomizationFolders(string baseOrgUrl, string svnExePath)
        {
            var validFolders = new List<string>();
            var subdirs = GetSvnSubDirectories(baseOrgUrl, svnExePath);

            foreach (var subdir in subdirs)
            {
                // backup フォルダは除外
                if (subdir.Equals("backup", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"backupフォルダをスキップ: {subdir}");
                    continue;
                }

                if (IsValidCustomizationFolder(baseOrgUrl, subdir, svnExePath))
                {
                    validFolders.Add(subdir);
                    System.Diagnostics.Debug.WriteLine($"有効なフォルダを検出: {subdir}");
                }
            }

            return validFolders;
        }

        /// <summary>
        /// フォルダが有効なカスタマイズフォルダかどうかを判定
        /// </summary>
        private bool IsValidCustomizationFolder(string baseOrgUrl, string folderName, string svnExePath)
        {
            try
            {
                string folderUrl = $"{baseOrgUrl}/{folderName}";
                var res = RunSvn(svnExePath, $"list --xml \"{folderUrl}\"", AppConfig.SVN_COMMAND_TIMEOUT_MS, Encoding.UTF8);

                if (res.TimedOut || res.ExitCode != 0)
                {
                    return false;
                }

                var xdoc = System.Xml.Linq.XDocument.Parse(res.Stdout);
                var entries = xdoc.Descendants("entry").ToList();

                var folders = entries
                    .Where(e => (string)e.Attribute("kind") == "dir")
                    .Select(e => (string)e.Element("name"))
                    .ToList();

                var files = entries
                    .Where(e => (string)e.Attribute("kind") == "file")
                    .Select(e => (string)e.Element("name"))
                    .ToList();

                // 必須条件: uhr, WEB-INF, jsp フォルダと index.html ファイル
                bool hasUhr = folders.Any(f => f.Equals("uhr", StringComparison.OrdinalIgnoreCase));
                bool hasWebInf = folders.Any(f => f.Equals("WEB-INF", StringComparison.OrdinalIgnoreCase));
                bool hasJsp = folders.Any(f => f.Equals("jsp", StringComparison.OrdinalIgnoreCase));
                bool hasIndexHtml = files.Any(f => f.Equals("index.html", StringComparison.OrdinalIgnoreCase));

                bool isValid = hasUhr && hasWebInf && hasJsp && hasIndexHtml;

                if (!isValid)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"フォルダ '{folderName}' は無効: uhr={hasUhr}, WEB-INF={hasWebInf}, jsp={hasJsp}, index.html={hasIndexHtml}");
                }

                return isValid;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"フォルダ '{folderName}' のチェック中にエラー: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ユーザーに複数のフォルダから選択させるダイアログを表示
        /// </summary>
        private string ShowFolderSelectionDialog(List<string> folders)
        {
            using (var form = new Form())
            {
                form.Text = "カスタマイズフォルダの選択";
                form.Width = 400;
                form.Height = 300;
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                var label = new Label
                {
                    Text = "複数の有効なフォルダが見つかりました。\n使用するフォルダを選択してください:",
                    Dock = DockStyle.Top,
                    Height = 50,
                    Padding = new Padding(10),
                    AutoSize = false
                };

                var listBox = new ListBox
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(10)
                };

                foreach (var folder in folders)
                {
                    listBox.Items.Add(folder);
                }

                if (listBox.Items.Count > 0)
                {
                    listBox.SelectedIndex = 0;
                }

                var buttonPanel = new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 50
                };

                var okButton = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Width = 80,
                    Location = new System.Drawing.Point(220, 10)
                };

                var cancelButton = new Button
                {
                    Text = "キャンセル",
                    DialogResult = DialogResult.Cancel,
                    Width = 80,
                    Location = new System.Drawing.Point(310, 10)
                };

                buttonPanel.Controls.Add(okButton);
                buttonPanel.Controls.Add(cancelButton);

                form.Controls.Add(listBox);
                form.Controls.Add(label);
                form.Controls.Add(buttonPanel);

                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                if (form.ShowDialog() == DialogResult.OK && listBox.SelectedItem != null)
                {
                    return listBox.SelectedItem.ToString();
                }

                return null;
            }
        }

        // SVN中のすべてのファイルリストを取得
        private List<string> GetAllFilesFromSvn(string svnUrl, string svnPath)
        {
            try
            {
                var res = RunSvn(svnPath, $"list --xml -R \"{svnUrl}\"",
                                 AppConfig.SVN_COMMAND_TIMEOUT_MS * 5, Encoding.UTF8);
                if (res.TimedOut) throw new Exception("SVNリストコマンドタイムアウト");
                if (res.ExitCode != 0) throw new Exception($"SVNリストコマンド失敗(ExitCode={res.ExitCode}): {res.Stderr}");
                if (string.IsNullOrWhiteSpace(res.Stdout))
                    throw new Exception($"SVNコマンドが何も結果を返しませんでした、パスが正しいかチェックしてください: {svnUrl}");

                var xdoc = System.Xml.Linq.XDocument.Parse(res.Stdout);
                return xdoc.Descendants("entry")
                           .Where(e => (string)e.Attribute("kind") == "file")
                           .Select(e => (string)e.Element("name"))
                           .Where(n => !string.IsNullOrWhiteSpace(n))
                           .ToList();
            }
            catch (Exception ex)
            {
                LogHelper.SafeLog("DebugLog.txt", $"SVNファイルリスト取得失敗: {ex.Message}\n");
                return new List<string>();
            }
        }

        // HASHと一致するファイルのSVN中の完全パスを検索
        private string FindFileInSVNByHash(string fileName, string targetHash, string orgCode)
        {
            try
            {
                string svnPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);

                // 先にSVN可用性をチェック
                if (string.IsNullOrEmpty(GetSVNCodePath()))
                {
                    MessageBox.Show("SVNのパスが取得できません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return "";
                }

                // SVN URLを構築
                string svnBaseUrl = GetSvnUrl();
                System.Diagnostics.Debug.WriteLine($"SVN URL: {svnBaseUrl}");

                // ファイルリストマッピングをキャッシュ
                string cacheKey = svnBaseUrl;

                if (!svnFileListCache.TryGetValue(cacheKey, out List<string> allFiles))
                {
                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] 初回SVN全ファイルリスト取得中: {fileName}\n");

                    allFiles = GetAllFilesFromSvn(svnBaseUrl, svnPath);
                    svnFileListCache[cacheKey] = allFiles;

                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVNファイルキャッシュ完了: {allFiles.Count}個のファイルを発見\n");
                }
                else
                {
                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVNキャッシュヒット: {fileName} ({allFiles.Count}個のファイル)\n");
                }

                // 同名のすべてのファイルを検索
                var candidates = allFiles.Where(file => System.IO.Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase)).ToList();

                // デバッグ情報：SVNファイル一覧の内容を確認
                System.Diagnostics.Debug.WriteLine($"SVNファイル一覧の最初の10個のファイル:");
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

                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] HASH比較: {fileName} ({candidates.Count}個の候補)\n");

                // 各候補ファイルのHASHをチェック
                for (int idx = 0; idx < candidates.Count; idx++)
                {
                    try
                    {
                        var candidate = candidates[idx];

                        // URL構築時はスラッシュとエンコーディングに注意
                        string fullSvnPath = svnBaseUrl.TrimEnd('/') + "/" + candidate;

                        if (string.IsNullOrWhiteSpace(fullSvnPath))
                        {
                            LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] 検索結果が空、スキップ\n");
                            continue;
                        }

                        // 詳細なURL構築情報 - 特にhituyoushorui.htm用
                        if (fileName.ToLower().Contains("hituyoushorui"))
                        {
                            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now}] *** 特殊ファイル *** URL構成: svnBaseUrl='{svnBaseUrl}', candidate='{candidate}', fullSvnPath='{fullSvnPath}'\n");
                            System.Diagnostics.Debug.WriteLine($"*** 特殊ファイル hituyoushorui *** URL: {fullSvnPath}");
                        }
                        else
                        {
                            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now}] URL構成: svnBaseUrl='{svnBaseUrl}', candidate='{candidate}', fullSvnPath='{fullSvnPath}'\n");
                        }
                        System.Diagnostics.Debug.WriteLine($"完全SVNパスをダウンロード: '{fullSvnPath}'");
                        System.Diagnostics.Debug.WriteLine($"候補ファイル名: '{candidate}'");

                        // URL構築完了でfullSvnPathを得た後、以下のセクションで元のtempFileロジックを置き換え
                        string fileHash = "";
                        bool ok = false;

                        // テキスト/スクリプト類は優先的にcatを使用
                        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
                        bool preferCat = ext == ".js" || ext == ".css" || ext == ".htm" || ext == ".html" ||
                                         ext == ".json" || ext == ".xml" || ext == ".txt" || ext == ".md";

                        if (preferCat)
                        {
                            ok = SafeTryCatHash(fullSvnPath, out fileHash);
                            if (!ok) LogHelper.SafeLog("DebugLog.txt", $"[hash] cat失敗→exportに戻す: {fullSvnPath}\n");
                        }

                        // export + hashに戻す
                        if (!ok)
                        {
                            ok = SafeTryExportHash(fullSvnPath, fileName, out fileHash);
                        }

                        // 失敗したらスキップ、以降の操作は継続しない
                        if (!ok || string.IsNullOrEmpty(fileHash))
                        {
                            LogHelper.SafeLog("ErrorLog.txt", $"[hash] hash計算失敗、スキップ: {fullSvnPath}\n");
                            continue;
                        }

                        // ヒット時は即座に返す
                        if (!string.IsNullOrEmpty(targetHash) &&
                            string.Equals(fileHash, targetHash, StringComparison.OrdinalIgnoreCase))
                        {
                            LogHelper.SafeLog("DebugLog.txt", $"[match] ヒット: {fileName} == {fullSvnPath}\n");
                            return fullSvnPath;
                        }

                    }
                    catch (Exception ex)
                    {
                        LogHelper.SafeLog("ErrorLog.txt", $"[candidate] 未知の例外、既にスキップ済み: {candidates[idx]}\n{ex}\n");
                        continue;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"一致するHASHのファイル'{fileName}'が見つかりません、目標HASH: {targetHash}");
                return "";
            }
            catch (Exception ex)
            {
                LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] FindFileInSVNByHash 例外: {fileName}, {ex.Message}\n");
                return "";
            }
        }

        private bool TryParseMergeListItem(string item, out string ver, out string module, out string file)
        {
            ver = module = file = "";
            if (string.IsNullOrWhiteSpace(item)) return false;
            // バージョン（連続非空白）+ スペース + モジュール名（Core|Salary|YearAdjust）+ スペース + ファイル名（残り全部）
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

            // 选择该模块的关键字与子路征E
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
                    throw new Exception($"未知のモジュール: {moduleName}");
            }

            // 1) リリース成果物の根下で該当モジュールの根目録を見つける（キーワードを含むディレクトリ名）
            var rootDirectories = GetSvnSubDirectories(svnBasePath, svnExe);  // 既に XML + UTF-8 解析済み
            var moduleRoot = rootDirectories.FirstOrDefault(d => d.Contains(folderKeyword));
            if (string.IsNullOrEmpty(moduleRoot))
                throw new Exception($"{moduleName} の根目録が見つかりません（キーワード: {folderKeyword}）");

            // 2) モジュールのバージョン親ディレクトリを構成
            string moduleFullPath = $"{svnBasePath}/{moduleRoot}{modulePath}";

            // 3) モジュール下のすべての「バージョンディレクトリ」をリストアップし、表示バージョン → 実ディレクトリ名のマッピングを構築
            var versionDirs = GetSvnSubDirectories(moduleFullPath, svnExe); // 純粋なディレクトリ名リスト
            if (versionDirs.Count == 0)
                throw new Exception($"{moduleName} にバージョンディレクトリがありません");

            string prefix = AppConfig.VERSION_PREFIX;   // プロジェクトで既定済み
            var versionToFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folderName in versionDirs)
            {
                string displayName = (!string.IsNullOrEmpty(prefix) && folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                     ? folderName.Substring(prefix.Length)
                                     : folderName;
                // 後で上書きしても前でも関係なし、名称は一般的に唯一
                versionToFolder[displayName] = folderName;
            }

            if (!versionToFolder.TryGetValue(version, out string folder))
                throw new Exception($"{moduleName} バージョン '{version}' に対応するディレクトリが見つかりません（利用可能: {string.Join(", ", versionToFolder.Keys)}）");

            // 4) 最終的なバージョンディレクトリURLを返す
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
                tslStatus.Text = mergeFiles.Any() ? $"マージが必要なファイル: {mergeFiles.Count}個" : "マージが必要なファイルはありません。";
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

                tslStatus.Text = $"バージョン情報 {versionCount}個を取得しました。";

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

        // 補助メソッド：INSERT 文からCS_CPROPERTYVALUE フィールドの値を抽出、フィールド順序可変、フィールド名大文字小文字無視
        private string ExtractValue(string insertLine)
        {
            // 1. フィールド名部分・フィールド値部分を抽出
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
            // 1. フィールド名部分・フィールド値部分を抽出
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

            // 年末調整バージョンのチェック：部分一致を含む、実際のバージョン番号は長い文字列（例："2.11.4+reigetsu2.11.4+shoumeisho2.10.2"）
            if (!string.IsNullOrEmpty(txtYearAdjustVersion.Text) &&
                !string.IsNullOrEmpty(cboYearAdjustTargetVersion.Text) &&
                txtYearAdjustVersion.Text.Contains(cboYearAdjustTargetVersion.Text))
                txtYearAdjustVersion.BackColor = Color.LightGreen;
            else
                txtYearAdjustVersion.BackColor = Color.White;
        }

        private void cmdShowReport_Click(object sender, EventArgs e)
        {
            // 変数を収雁E
            string orgName = txtOrgName.Text.Trim();
            string fwVersion = txtFWVersion.Text.Trim();
            string coreVersion = txtCoreVersion.Text.Trim();
            string salaryVersion = txtSalaryVersion.Text.Trim();
            string nenchoVersion = txtYearAdjustVersion.Text.Trim();

            // 修正：カスタマイズファイル数量判定ロジック
            int customizeFileCount = 0;
            if (!string.IsNullOrEmpty(lblCustomizedFileCount.Text) && lblCustomizedFileCount.Text != "")
            {
                if (int.TryParse(lblCustomizedFileCount.Text.Replace("(", "").Replace(")", ""), out int cnt))
                {
                    customizeFileCount = cnt;
                }
            }
            // lblCustomizedFileCountが空の場合、カスタマイズファイルがないことを示し、countは0とする

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
            //          XXXX は顧客コードです。名称が必ずしも正確とは限らないため、コードであいまい一致します。
            //          末尾が「_HRAP」のフォルダを特定してください。
            //          その配下の「uhr」フォルダが当該機関のモジュール位置です。
            // SVNのサーバ接続確認
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

        // 一時フォルダを削除
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
                        System.Diagnostics.Debug.WriteLine($"作成された一時フォルダー: {tempFolder}");
                    }
                    catch
                    {
                        // 一時フォルダーの削除に失敗した場合は無視
                    }
                }
            }
            catch
            {
                // 全体のクリーンアップが失敗した場合は無視
            }
        }

        // lstMergeNeedsFile のすべての項目をダウンロード（選択項目ではなく）——実際の処理時は TryFetchToFile のみを使用
        private void DownloadAllFromMergeList()
        {
            if (lstMergeNeedsFile.Items.Count == 0)
            {
                MessageBox.Show("列表が空です。ダウンロード可能なファイルがありません。");
                return;
            }

            string svnExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
            string outRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "UHR_MERGE_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(outRoot);

            int exportTimeout = AppConfig.SVN_COMMAND_TIMEOUT_MS * 3; // 大きいファイルには長めに
            int catTimeout = AppConfig.SVN_COMMAND_TIMEOUT_MS;

            int ok = 0, ng = 0;

            // リモート負荷を減らすため、バージョンディレクトリURLをキャッシュ → ファイル削除
            // ValueTupleはデフォルトのEqualityComparerを使用する必要があり、StringComparerは直接使用できない
            var versionUrlCache = new Dictionary<(string module, string ver), string>();
            var fileListCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lstMergeNeedsFile.Items.Count; i++)
            {
                try
                {
                    Application.DoEvents();

                    // リスト項目フォーマット: "{verPadded} {modulePadded} {file}"
                    if (!TryParseMergeListItem(lstMergeNeedsFile.Items[i].ToString(), out var ver, out var module, out var fileName))
                    {
                        LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] リスト項目の解析失敗、スキップ: {lstMergeNeedsFile.Items[i]}\n");
                        ng++;
                        continue;
                    }


                    // 1) 「モジュール・バージョン」のリモートバージョンディレクトリURLを特定（既実装の補助関数）
                    if (!versionUrlCache.TryGetValue((module, ver), out var versionUrl))
                    {
                        versionUrl = ResolveModuleVersionUrl(module, ver); // e.g. .../<モジュール>/<v4.18.4 または 4.18.4>
                        versionUrlCache[(module, ver)] = versionUrl;
                    }

                    // 2) 該当バージョンディレクトリ下の「全ファイルの相対パス」リストを取得（XML -R、UTF-8、既実装済み）
                    if (!fileListCache.TryGetValue(versionUrl, out var relFiles))
                    {
                        relFiles = GetSvnFileListXmlRecursive(versionUrl, svnExe);
                        fileListCache[versionUrl] = relFiles;
                    }

                    // 3) 「ファイル名のみ」でマッチング（通常1個ヒット、複数同名ファイルがあれば全部ダウンロード）
                    var matches = relFiles.Where(p => Path.GetFileName(p)
                                           .Equals(fileName, StringComparison.OrdinalIgnoreCase))
                                           .ToList();

                    if (matches.Count == 0)
                    {
                        LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] バージョン中に同名ファイルが見つかりません: {fileName} @ {module} {ver}\n");
                        ng++;
                        continue; // ☁E��路
                    }

                    // 4) 逐个匹配项落地下载 — E统一通迁ETryFetchToFile
                    foreach (var rel in matches)
                    {
                        string url = versionUrl.TrimEnd('/') + "/" + rel.Replace("\\", "/");
                        string local = Path.Combine(outRoot, module, ver, rel.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(local) ?? outRoot);

                        bool fetched = TryFetchToFile(svnExe, url, local, exportTimeout, catTimeout);
                        if (!fetched)
                        {
                            LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] ダウンロード失敗、スキップ: {url}\n");
                            ng++;                 // 失敗数にカウント
                            continue;             // ★失敗はショートカット、次のファイルへ直接進む
                        }

                        ok++;                     // 成功した場合のみ +1

                    }
                }
                catch (Exception ex)
                {
                    LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] 単項処理例外(既にスキップ): {lstMergeNeedsFile.Items[i]}\n{ex}\n");
                    continue; // ★重要：失敗しても停止せず、ファイルのみスキップ
                }
            }

            MessageBox.Show($"ダウンロード完了：成功{ok}個、失敗{ng}個\n保存先：{outRoot}", "完了",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }



        private void cmdFilePrepare_Click(object sender, EventArgs e)
        {
            // 基本検証
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

            // 禁用按钮,显示忙状态E
            cmdFilePrepare.Enabled = false;
            this.Cursor = Cursors.WaitCursor;
            _isTaskRunning = true;

            SimpleSvnHelper.CleanupTempFolders();
            System.IO.File.WriteAllText("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] === カスタマイズファイル処理開始（フォアグラウンドスレッド版）===\n");

            // フォアグラウンドスレッドを使用（Task.Run（バックグラウンドスレッド）ではなく）、アプリケーションの意図しない終了を防止
            var thread = new System.Threading.Thread(() =>
            {
                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォアグラウンドスレッド開始、ID={System.Threading.Thread.CurrentThread.ManagedThreadId}, IsBackground={System.Threading.Thread.CurrentThread.IsBackground}\n");

                (int, int, string) result = (0, 0, "");
                Exception threadException = null;

                try
                {
                    result = ProcessFilesInBackground(svnExe, orgCode, orgName, svnBaseUrl);
                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessFilesInBackground完了、結果を取得\n");
                }
                catch (Exception ex)
                {
                    threadException = ex;
                    LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] スレッド例外: {ex.Message}\n{ex.StackTrace}\n");
                }

                // 检查窗体是否还存在
                if (this.IsDisposed || !this.IsHandleCreated)
                {
                    LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォームが既に破棄されています\n");
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
                            LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] エラーをUI表示\n");
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
                                    $"処理完了しました。\n成功: {successCount}個\n失敗: {notFoundCount}個\n保存先: {saveDir}",
                                    "完了",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information);

                                try
                                {
                                    System.Diagnostics.Process.Start("explorer.exe", saveDir);
                                }
                                catch (Exception ex)
                                {
                                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] エクスプローラー起動失敗: {ex.Message}\n");
                                }
                            }
                            else
                            {
                                MessageBox.Show("ファイルが見つかりませんでした。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }

                            tslStatus.Text = $"完了: {successCount}個成功、{notFoundCount}個失敗";
                        }

                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] UI更新完了\n");
                    }));
                }
                catch (Exception invokeEx)
                {
                    LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] Invoke例外: {invokeEx.Message}\n{invokeEx.StackTrace}\n");
                }

                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォアグラウンドスレッド終了\n");
            });

            thread.IsBackground = false;  // フォアグラウンドスレッドに設定、スレッド実行中のアプリケーション終了を防止
            thread.Start();

            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォアグラウンドスレッド起動完了\n");
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
                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ステータス更新失敗: {ex.Message}\n"); 
            }
        }

        static string PSEscape(string s) => "'" + s.Replace("'", "''") + "'";  // 単一引用符をエスケープ

        /// <summary>
        /// .classファイルに対応する.javaソースファイルをダウンロード
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
                // .classファイルパスを.javaファイルパスに変換
                // 例: WEB-INF/classes/com/example/Foo.class -> WEB-INF/classes/com/example/Foo.java
                string javaFilePath = classFilePath.Substring(0, classFilePath.Length - 6) + ".java";
                string javaFileName = Path.GetFileName(javaFilePath);

                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] .classファイル検出、対応する.javaファイルを検索: {javaFileName}\n");

                // SVNファイルリスト中で対応する.javaファイルを検索
                var javaFile = allSvnFiles.FirstOrDefault(f => f.Equals(javaFilePath, StringComparison.OrdinalIgnoreCase));

                if (javaFile != null)
                {
                    string svnFileUrl = $"{svnBaseUrl.TrimEnd('/')}/{javaFile}";
                    string localFilePath = Path.Combine(workTempDir, javaFile.Replace('/', Path.DirectorySeparatorChar));

                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] .javaファイルダウンロード試衁E {javaFile}\n");

                    if (svnHelper.DownloadSingleFile(svnFileUrl, localFilePath))
                    {
                        // 目標ディレクトリにコピー、相対パスを保持
                        string targetPath = Path.Combine(saveDir, javaFile.Replace('/', Path.DirectorySeparatorChar));
                        string targetDir = Path.GetDirectoryName(targetPath);

                        if (!Directory.Exists(targetDir))
                            Directory.CreateDirectory(targetDir);

                        File.Copy(localFilePath, targetPath, true);
                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] .javaファイル保存成功: {javaFileName}\n");
                    }
                    else
                    {
                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] .javaファイルダウンロード失敗: {javaFile}\n");
                    }
                }
                else
                {
                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] 対応する.javaファイルがSVNに見つかりません: {javaFilePath}\n");
                }
            }
            catch (Exception ex)
            {
                LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] .javaファイルダウンロード例外: {ex.Message}\n");
            }
        }

        /// <summary>
        /// 汎用オンデマンドダウンロード方法 - ファイル名のリストに基づいてSVNからオンデマンドダウンロードしハッシュ検証
        /// </summary>
        /// <param name="downloadAllMatches">true=ハッシュが一致するすべてのファイルをダウンロード（merge用）、false=最初の一致のみダウンロード（customized用）</param>
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

            // 第1段階：SVNファイルリストを取得（軽量級操作）
            List<string> allSvnFiles;
            if (!svnFileListCache.TryGetValue(svnBaseUrl, out allSvnFiles))
            {
                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVNファイルリスト取得中...\n");
                allSvnFiles = GetAllFilesFromSvn(svnBaseUrl, svnExe);
                svnFileListCache[svnBaseUrl] = allSvnFiles;
                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVNファイル数: {allSvnFiles.Count}\n");
            }
            else
            {
                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVNファイルリストキャッシュ使用: {allSvnFiles.Count}個\n");
            }

            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] 処理対象ファイル数: {fileNames.Count}\n");

            // 第2段階：オンデマンドダウンロードとハッシュ検証
            for (int i = 0; i < fileNames.Count; i++)
            {
                try
                {
                    string fileName = fileNames[i];
                    if (fileName.Contains("惁E��が見つかりません"))
                        continue;

                    UpdateStatus($"ダウンロード中: {fileName} ({i + 1}/{fileNames.Count})");

                    if (!moduleInfoCache.TryGetValue(fileName, out var fileInfo))
                    {
                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] キャッシュなし: {fileName}\n");
                        notFoundFiles.Add($"{fileName} (キャッシュなし)");
                        continue;
                    }

                    // SVNファイルリスト中で同名ファイルを検索
                    var matchingFiles = allSvnFiles.Where(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase)).ToList();

                    if (matchingFiles.Count == 0)
                    {
                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVNで見つかりません: {fileName}\n");
                        notFoundFiles.Add($"{fileName} (SVNで見つかりません)");
                        continue;
                    }

                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] {fileName}の候裁E {matchingFiles.Count}個\n");

                    bool matched = false;
                    int matchCount = 0;
                    foreach (var svnFilePath in matchingFiles)
                    {
                        // ファイルを一時ディレクトリにダウンロード
                        string svnFileUrl = $"{svnBaseUrl.TrimEnd('/')}/{svnFilePath}";
                        string localFilePath = Path.Combine(workTempDir, svnFilePath.Replace('/', Path.DirectorySeparatorChar));

                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ダウンロード試行: {svnFilePath}\n");

                        if (!svnHelper.DownloadSingleFile(svnFileUrl, localFilePath))
                        {
                            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ダウンロード失敗: {svnFilePath}\n");
                            continue;
                        }

                        // ハッシュを検証
                        string actualHash = svnHelper.CalculateHash(localFilePath);

                        if (string.IsNullOrEmpty(actualHash))
                        {
                            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ハッシュ計算失敗: {localFilePath}\n");
                            continue;
                        }

                        if (actualHash.Equals(fileInfo.HashValue, StringComparison.OrdinalIgnoreCase))
                        {
                            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ハッシュ一致: {fileName}\n");

                            // 目標ディレクトリにコピー、相対パスを保持
                            string targetPath = Path.Combine(saveDir, svnFilePath.Replace('/', Path.DirectorySeparatorChar));
                            string targetDir = Path.GetDirectoryName(targetPath);

                            if (!Directory.Exists(targetDir))
                                Directory.CreateDirectory(targetDir);

                            File.Copy(localFilePath, targetPath, true);
                            successCount++;
                            matched = true;
                            matchCount++;

                            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] 保存成功: {fileName} ({successCount}個目)\n");

                            // .classファイルの場合、対応する.javaファイルのダウンロードを試みる
                            if (fileName.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
                            {
                                DownloadCorrespondingJavaFile(svnFilePath, svnBaseUrl, allSvnFiles, workTempDir, saveDir, svnHelper);
                            }

                            // すべてのマッチをダウンロードする必要がない場合、最初の1個を見つけたら終了
                            if (!downloadAllMatches)
                                break;
                        }
                        else
                        {
                            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ハッシュ不一致: expected={fileInfo.HashValue}, actual={actualHash}\n");
                        }
                    }

                    if (!matched)
                    {
                        notFoundFiles.Add($"{fileName} (ハッシュ不一致)");
                    }
                    else if (downloadAllMatches && matchCount > 1)
                    {
                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] {fileName}: {matchCount}個のマッチファイルを保存\n");
                    }
                }
                catch (Exception fileEx)
                {
                    string errorFileName = fileNames[i];
                    LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] ファイル処理例外: {errorFileName}\n{fileEx.Message}\n{fileEx.StackTrace}\n");
                    notFoundFiles.Add($"{errorFileName} (処理例外)");
                }
            }

            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ダウンロード完了: {successCount}個成功\n");
            return (successCount, notFoundFiles);
        }

        private (int successCount, int notFoundCount, string saveDir) ProcessFilesInBackground(string svnExe, string orgCode, string orgName, string svnBaseUrl)
        {
            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessFilesInBackground開始 - スレッドID: {System.Threading.Thread.CurrentThread.ManagedThreadId}\n");

            string workTempDir = null;

            try
            {
                string saveDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{orgCode}_{orgName}_CustomizedFiles");

            if (Directory.Exists(saveDir))
            {
                Directory.Delete(saveDir, true);
            }
            Directory.CreateDirectory(saveDir);

            var svnHelper = new SimpleSvnHelper(svnExe, msg => LogHelper.SafeLog("DebugLog.txt", msg + "\n"));

            UpdateStatus("SVNファイルリスト取得中...");

            workTempDir = Path.Combine(Path.GetTempPath(), "uhr_ondemand_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
            Directory.CreateDirectory(workTempDir);
            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] 作業用一時フォルダ: {workTempDir}\n");

            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] === オンデマンドダウンロードモード：必要なファイルのみダウンロード ===\n");
            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVN URL: {svnBaseUrl}\n");

            // ダウンロードが必要なファイルリストを取得
            List<string> fileNames = new List<string>();
            this.Invoke(new Action(() =>
            {
                foreach (var item in lstCustomizedFile.Items)
                {
                    fileNames.Add(item.ToString());
                }
            }));

            // 汎用ダウンロード方法を呼び出し
            var (successCount, notFoundFiles) = DownloadFilesOnDemand(fileNames, svnBaseUrl, svnExe, workTempDir, saveDir, svnHelper);

            // 一時フォルダの削除は既にfinallyブロックに移動済み、成功・失敗に関わらず削除を保証

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

                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessFilesInBackground正常終了\n");
                return (successCount, notFoundFiles.Count, saveDir);
            }
            catch (Exception ex)
            {
                LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessFilesInBackground例外: {ex.Message}\n{ex.StackTrace}\n");
                throw;
            }
            finally
            {
                // 成功・失敗に関わらず、一時フォルダを削除
                if (!string.IsNullOrEmpty(workTempDir))
                {
                    try
                    {
                        if (Directory.Exists(workTempDir))
                        {
                            Directory.Delete(workTempDir, true);
                            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] [Finally] 作業用フォルダ削除: {workTempDir}\n");
                        }
                    }
                    catch (Exception cleanEx)
                    {
                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] [Finally] 作業用フォルダ削除失敗: {cleanEx.Message}\n");
                    }
                }
            }
        }

        /// <summary>
        /// マージファイルのバックグラウンド処理（フォアグラウンドスレッド版）
        /// </summary>
        private (int successCount, int notFoundCount, string saveDir) ProcessMergeFilesInBackground(string svnExe, string orgCode, string orgName, string svnBaseUrl)
        {
            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessMergeFilesInBackground開始 - スレッドID: {System.Threading.Thread.CurrentThread.ManagedThreadId}\n");

            string workTempDir = null;

            try
            {
                string saveDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{orgCode}_{orgName}_MergeFiles");

                if (Directory.Exists(saveDir))
                {
                    Directory.Delete(saveDir, true);
                }
                Directory.CreateDirectory(saveDir);

                var svnHelper = new SimpleSvnHelper(svnExe, msg => LogHelper.SafeLog("DebugLog.txt", msg + "\n"));

                UpdateStatus("SVNファイルリスト取得中...");

                workTempDir = Path.Combine(Path.GetTempPath(), "uhr_merge_ondemand_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                Directory.CreateDirectory(workTempDir);
                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] 作業用一時フォルダ: {workTempDir}\n");

                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] === オンデマンドダウンロードモード（マージファイル）：必要なファイルのみダウンロード ===\n");
                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] SVN URL: {svnBaseUrl}\n");

                // lstMergeNeedsFileからファイルリストを取得、UIスレッドで読み取る必要あり
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

                // ファイル名のリストを抽出
                List<string> fileNames = mergeFileList.Select(x => x.fileName).ToList();

                // 汎用ダウンロード方法を呼び出し（merge モード：ハッシュが一致するすべてのファイルをダウンロード）
                var (successCount, notFoundFiles) = DownloadFilesOnDemand(fileNames, svnBaseUrl, svnExe, workTempDir, saveDir, svnHelper, downloadAllMatches: true);

                // 一時フォルダの削除は既にfinallyブロックに移動済み、成功・失敗に関わらず削除を保証

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

                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessMergeFilesInBackground正常終了\n");
                return (successCount, notFoundFiles.Count, saveDir);
            }
            catch (Exception ex)
            {
                LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessMergeFilesInBackground例外: {ex.Message}\n{ex.StackTrace}\n");
                throw;
            }
            finally
            {
                // 成功・失敗に関わらず、一時フォルダを削除
                if (!string.IsNullOrEmpty(workTempDir))
                {
                    try
                    {
                        if (Directory.Exists(workTempDir))
                        {
                            Directory.Delete(workTempDir, true);
                            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] [Finally] 作業用フォルダ削除: {workTempDir}\n");
                        }
                    }
                    catch (Exception cleanEx)
                    {
                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] [Finally] 作業用フォルダ削除失敗: {cleanEx.Message}\n");
                    }
                }
            }
        }

        /// <summary>
        /// シンプルなファイル検索方法 - 最初に一致したファイルURLを返す、ハッシュ検証はせず、ダウンロード後に検証する
        /// </summary>
        private string FindFileInSVNByHashSimple(string svnBaseUrl, string fileName, string targetHash, SimpleSvnHelper svnHelper)
        {
            try
            {
                // ファイルリストを取得
                if (!svnFileListCache.TryGetValue(svnBaseUrl, out List<string> allFiles))
                {
                    allFiles = GetAllFilesFromSvn(svnBaseUrl, svnHelper.svnExePath);
                    svnFileListCache[svnBaseUrl] = allFiles;
                }

                // 查找同名斁E�� - 返回第一个匹配的
                var candidate = allFiles.FirstOrDefault(file => Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase));

                if (candidate == null)
                    return null;

                string fullUrl = svnBaseUrl.TrimEnd('/') + "/" + candidate;
                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] 候補発要E {fileName} -> {fullUrl}\n");
                return fullUrl;
            }
            catch (Exception ex)
            {
                LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] FindFileInSVNByHashSimple エラー: {fileName}\n{ex.Message}\n{ex.StackTrace}\n");
                return null;
            }
        }

        /// <summary>
        /// SVNパスから相対パスを抽出
        /// </summary>
        /// <summary>
        /// SVNパスからuhrまたはROOT以降の相対パスを抽出、uhrまたはROOT自体は含まない
        /// 例: "v1.2.3/uhr/WEB-INF/classes/Foo.class" -> "WEB-INF/classes/Foo.class"
        /// </summary>
        private string ExtractRelativePathFromUhrOrRoot(string svnPath)
        {
            if (svnPath.Contains("/uhr/"))
            {
                int idx = svnPath.IndexOf("/uhr/");
                return svnPath.Substring(idx + 5); // /uhr/ 之后皁E��刁E
            }
            else if (svnPath.Contains("/ROOT/"))
            {
                int idx = svnPath.IndexOf("/ROOT/");
                return svnPath.Substring(idx + 6); // /ROOT/ 之后皁E��刁E
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

            // 基本検証
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

            // 禁用按钮,显示忙状态E
            cmdMergeFilePrepare.Enabled = false;
            this.Cursor = Cursors.WaitCursor;
            _isTaskRunning = true;

            SimpleSvnHelper.CleanupTempFolders();
            System.IO.File.WriteAllText("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] === マージファイル処理開始（フォアグラウンドスレッド版）===\n");

            // フォアグラウンドスレッドを使用（Task.Run（バックグラウンドスレッド）ではなく）、アプリケーションの意図しない終了を防止
            var thread = new System.Threading.Thread(() =>
            {
                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォアグラウンドスレッド開始、ID={System.Threading.Thread.CurrentThread.ManagedThreadId}, IsBackground={System.Threading.Thread.CurrentThread.IsBackground}\n");

                (int, int, string) result = (0, 0, "");
                Exception threadException = null;

                try
                {
                    result = ProcessMergeFilesInBackground(svnExe, orgCode, orgName, svnBaseUrl);
                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] ProcessMergeFilesInBackground完了、結果を取得\n");
                }
                catch (Exception ex)
                {
                    threadException = ex;
                    LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] スレッド例外: {ex.Message}\n{ex.StackTrace}\n");
                }

                // 检查窗体是否还存在
                if (this.IsDisposed || !this.IsHandleCreated)
                {
                    LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォームが既に破棄されています\n");
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
                            LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] エラーをUI表示\n");
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
                                    $"処理完了しました。\n成功: {successCount}個\n失敗: {notFoundCount}個\n保存先: {saveDir}",
                                    "完了",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information);

                                try
                                {
                                    System.Diagnostics.Process.Start("explorer.exe", saveDir);
                                }
                                catch (Exception ex)
                                {
                                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] エクスプローラー起動失敗: {ex.Message}\n");
                                }
                            }
                            else
                            {
                                MessageBox.Show("ファイルが見つかりませんでした。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }

                            tslStatus.Text = $"完了: {successCount}個成功、{notFoundCount}個失敗";
                        }

                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] UI更新完了\n");
                    }));
                }
                catch (Exception invokeEx)
                {
                    LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] Invoke例外: {invokeEx.Message}\n{invokeEx.StackTrace}\n");
                }

                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォアグラウンドスレッド終了\n");
            });

            thread.IsBackground = false;  // フォアグラウンドスレッドに設定、スレッド実行中のアプリケーション終了を防止
            thread.Start();

            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] フォアグラウンドスレッド起動完了\n");
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


        // 安全な試行：svn catを使って直接hash計算、例外は投げない
        private bool SafeTryCatHash(string fullSvnPath, out string hash)
        {
            hash = "";
            var catStopwatch = Stopwatch.StartNew();
            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [CatHash] start: {fullSvnPath}");
            try
            {
                var svnExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);
                // 并发排水以防 I/O 死锁E
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
                        LogHelper.SafeLog("ErrorLog.txt", $"[cat] Start失敗: {fullSvnPath}\n");
                        catStopwatch.Stop();
                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [CatHash] fail(start): {fullSvnPath}, elapsed={catStopwatch.ElapsedMilliseconds}ms\n");
                        return false;
                    }

                    var msOut = new MemoryStream();
                    var msErr = new MemoryStream();
                    var tOut = Task.Run(() => { 
                        try { p.StandardOutput.BaseStream.CopyTo(msOut); } 
                        catch (Exception ex) { LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] STDOUT読み取り失敗: {ex.Message}\n"); } 
                    });
                    var tErr = Task.Run(() => { 
                        try { p.StandardError.BaseStream.CopyTo(msErr); } 
                        catch (Exception ex) { LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] STDERR読み取り失敗: {ex.Message}\n"); } 
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
                                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] cat OK: {fullSvnPath}\n");
                                    return true; // ★成功：catの結果をダウンロード成功として扱う
                                }

                                // cat失敗も記録
                                LogHelper.SafeLog("ErrorLog.txt",
                                    $"[{DateTime.Now:HH:mm:ss}] cat NG Exit={catRes.ExitCode} Timeout={catRes.TimedOut}\nURL={fullSvnPath}\nSTDERR:\n{catRes.Stderr}\n");
                            }

                            LogHelper.SafeLog("ErrorLog.txt", $"[cat] 趁E��: {fullSvnPath}\n");
                            catStopwatch.Stop();
                            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [CatHash] timeout: {fullSvnPath}, elapsed={catStopwatch.ElapsedMilliseconds}ms\n");
                            return false;
                        }
                        Application.DoEvents();
                    }
                    try { Task.WaitAll(new[] { tOut, tErr }, 2000); } catch { }

                    var stderr = Encoding.UTF8.GetString(msErr.ToArray());
                    if (p.ExitCode != 0)
                    {
                        LogHelper.SafeLog("ErrorLog.txt", $"[cat] 失敗({p.ExitCode}): {fullSvnPath}\nSTDERR: {LogHelper.TruncateString(stderr)}\n");
                        catStopwatch.Stop();
                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [CatHash] fail(exit={p.ExitCode}): {fullSvnPath}, elapsed={catStopwatch.ElapsedMilliseconds}ms\n");
                        return false;
                    }

                    var bytes = msOut.ToArray();
                    if (bytes.Length == 0)
                    {
                        hash = "empty_file_hash";
                        catStopwatch.Stop();
                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [CatHash] success(empty): {fullSvnPath}, elapsed={catStopwatch.ElapsedMilliseconds}ms\n");
                        return true;
                    }

                    var md5 = System.Security.Cryptography.MD5.Create();
                    var h = md5.ComputeHash(bytes);
                    hash = BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
                    catStopwatch.Stop();
                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [CatHash] success: {fullSvnPath}, bytes={bytes.Length}, hash={hash}, elapsed={catStopwatch.ElapsedMilliseconds}ms\n");
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
                    catch { /* 削除失敗は無視 */ }
                }
            }
            catch (Exception ex)
            {
                LogHelper.SafeLog("ErrorLog.txt", $"[cat] 例外: {fullSvnPath}\n{ex}\n");
                catStopwatch.Stop();
                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [CatHash] exception: {fullSvnPath}, elapsed={catStopwatch.ElapsedMilliseconds}ms, detail={ex.Message}");
                return false;
            }
        }

        // 安全な試行：exportで一時ファイルに保存してhash計算、例外は投げず、一時ファイルの削除も担当
        private bool SafeTryExportHash(string fullSvnPath, string fileName, out string hash)
        {
            hash = "";
            var exportStopwatch = Stopwatch.StartNew();
            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] start: {fullSvnPath}, file={fileName}");
            string tempFile = "";
            try
            {
                tempFile = DownloadFileFromSVN(fullSvnPath, fileName); // 同期ダウンロード、内部にタイムアウト/ファイルcat予備機能あり
                if (string.IsNullOrEmpty(tempFile) || !File.Exists(tempFile))
                {
                    LogHelper.SafeLog("ErrorLog.txt", $"[export] ファイルを得られませんでした: {fullSvnPath}\n");
                    exportStopwatch.Stop();
                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] fail(no_file): {fullSvnPath}, elapsed={exportStopwatch.ElapsedMilliseconds}ms");
                    return false; // ★強制ショートカット、以降の処理は不要
                }

                // サイズ安定化 + 非ゼロチェック、半端な空ファイルを避ける
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
                    LogHelper.SafeLog("ErrorLog.txt", $"[export] 斁E��为0字节(跳迁E: {tempFile}\n");
                    exportStopwatch.Stop();
                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] fail(empty_file): {fullSvnPath}, elapsed={exportStopwatch.ElapsedMilliseconds}ms");
                    return false;
                }

                // HASH計算：共有読み取り + リトライを使用、アンチウイルス/インデクサの一時的な占有を避ける
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
                            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] success: {fullSvnPath}, file={fileName}, size={fs.Length}, hash={hash}, elapsed={exportStopwatch.ElapsedMilliseconds}ms\n");
                            return true;
                        }
                    }
                    catch (IOException ioex)
                    {
                        if (i == 2)
                        {
                            LogHelper.SafeLog("ErrorLog.txt", $"[export] 读取文件计算哈希失败: {tempFile}\n{ioex}\n");
                            exportStopwatch.Stop();
                            LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] fail(read): {fullSvnPath}, elapsed={exportStopwatch.ElapsedMilliseconds}ms, detail={ioex.Message}\n");
                            return false;
                        }
                        System.Threading.Thread.Sleep(150);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.SafeLog("ErrorLog.txt", $"[export] 计算哈希时发生异常: {tempFile}\n{ex}\n");
                        exportStopwatch.Stop();
                        LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] exception(hash_calc): {fullSvnPath}, elapsed={exportStopwatch.ElapsedMilliseconds}ms, detail={ex.Message}\n");
                        return false;
                    }
                }

                exportStopwatch.Stop();
                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] fail(unexpected): {fullSvnPath}, elapsed={exportStopwatch.ElapsedMilliseconds}ms");
                return false; // 想定外の状況
            }
            catch (Exception ex)
            {
                LogHelper.SafeLog("ErrorLog.txt", $"[export] 例外: {fullSvnPath}\n{ex}\n");
                exportStopwatch.Stop();
                LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss.fff}] [ExportHash] exception: {fullSvnPath}, elapsed={exportStopwatch.ElapsedMilliseconds}ms, detail={ex.Message}");
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
                            // 只在目录已经空亁E��惁E�E下删除�E�避免误删其他临时斁E��
                            if (Directory.Exists(dir) && Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 0)
                                Directory.Delete(dir, true);
                        }
                    }
                }
                catch { /* 削除失敗は無視 */ }
            }
        }

        // 小ツール：長いstderrを切り詰める
        private static string Trim400(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 400 ? s.Substring(0, 400) + "..." : s);


        // 安全なログ記録：パス/権限/占有に問題があっても、メインフローをクラッシュさせない
        private static void SafeLog(string file, string text)
        {
            try { System.IO.File.AppendAllText(file, text); }
            catch { /* ログ失敗はメインフローに影響させない */ }
        }



        /// <summary>
        /// SVN最終更新情報を非同期で取得
        /// svn log --xml --limit 1 を使用して最新のコミット情報（日時、著者、メッセージ）を取得
        /// </summary>

        private async Task GatherSvnLastUpdatedInfoAsync()
        {
            tslStatus.Text = "SVN最終更新情報を取得中...";
            Application.UseWaitCursor = true;
            this.Cursor = Cursors.WaitCursor;

            try
            {
                string orgCode = txtOrgCode.Text.Trim();
                string orgName = txtOrgName.Text.Trim();

                if (string.IsNullOrEmpty(orgCode))
                {
                    txtLastUpdatedInfo.Text = "(機関が未選択)";
                    return;
                }

                string orgFolder;
                try
                {
                    // FindOrgSvnFolder は例外を投げる可能性がある
                    orgFolder = FindOrgSvnFolder(orgCode);
                }
                catch (Exception ex)
                {
                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] FindOrgSvnFolder 失敗: {ex.Message}\n");
                    txtLastUpdatedInfo.Text = "(SVNフォルダ検索失敗)";
                    return;
                }

                string baseOrgUrl = $"{AppConfig.SVN_CUSTOMIZED_PATH}{orgFolder}";
                string svnExe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfig.SVN_EXE_RELATIVE_PATH);

                // svn log --xml を使用して最新のコミット情報を取得（--limit 1 で最新のみ）
                var res = await Task.Run(() => RunSvn(svnExe, $"log --xml --limit 1 \"{baseOrgUrl}\"", AppConfig.SVN_COMMAND_TIMEOUT_MS * 5, Encoding.UTF8));

                if (res == null || res.TimedOut || res.ExitCode != 0 || string.IsNullOrWhiteSpace(res.Stdout))
                {
                    LogHelper.SafeLog("DebugLog.txt", $"[{DateTime.Now:HH:mm:ss}] GatherSvnLastUpdatedInfoAsync: svn log 取得失敗 Exit={res?.ExitCode} TimedOut={res?.TimedOut}\n");
                    txtLastUpdatedInfo.Text = "(SVN情報取得失敗)";
                    return;
                }

                try
                {
                    var xdoc = System.Xml.Linq.XDocument.Parse(res.Stdout);

                    // logentry 要素を収集（svn log --xml の場合）
                    var logEntries = xdoc.Descendants("logentry")
                                      .Select(entry => new
                                      {
                                          Revision = (string)entry.Attribute("revision") ?? "",
                                          Author = (string)entry.Element("author") ?? "",
                                          DateStr = (string)entry.Element("date") ?? "",
                                          Message = (string)entry.Element("msg") ?? ""
                                      })
                                      .Where(x => !string.IsNullOrEmpty(x.DateStr))
                                      .ToList();

                    if (logEntries.Count == 0)
                    {
                        txtLastUpdatedInfo.Text = "(更新情報なし)";
                        return;
                    }

                    // --limit 1 を指定しているので、最初（最新）の1件のみ取得される
                    var latest = logEntries.First();
                    
                    if (DateTimeOffset.TryParse(latest.DateStr, out var latestDt))
                    {
                        // ローカル時間に変換して表示
                        var local = latestDt.ToLocalTime();
                        string msg = string.IsNullOrWhiteSpace(latest.Message) ? "(メッセージなし)" : latest.Message;
                        
                        // メッセージが長すぎる場合は切り詰める
                        if (msg.Length > 50)
                            msg = msg.Substring(0, 47) + "...";
                        
                        txtLastUpdatedInfo.Text = $"{local:yyyy/MM/dd HH:mm:ss} by {latest.Author}: {msg}";
                    }
                    else
                    {
                        // 日付解析失敗時は生の文字列を使用
                        string msg = string.IsNullOrWhiteSpace(latest.Message) ? "" : $": {latest.Message}";
                        if (msg.Length > 50)
                            msg = msg.Substring(0, 47) + "...";
                        txtLastUpdatedInfo.Text = $"{latest.DateStr} by {latest.Author}{msg}";
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] GatherSvnLastUpdatedInfoAsync XML解析失敗: {ex.Message}\n{ex.StackTrace}\n");
                    txtLastUpdatedInfo.Text = "(更新情報解析失敗)";
                }
            }
            catch (Exception ex)
            {
                LogHelper.SafeLog("ErrorLog.txt", $"[{DateTime.Now:HH:mm:ss}] GatherSvnLastUpdatedInfoAsync 例外: {ex.Message}\n{ex.StackTrace}\n");
                txtLastUpdatedInfo.Text = $"(取得失敗: {ex.Message})";
            }
            finally
            {
                Application.UseWaitCursor = false;
                this.Cursor = Cursors.Default;
                tslStatus.Text = "";
            }
        }

        /// <summary>
        /// バージョン文字列の比較を行うIComparer実装
        /// </summary>
        private class VersionComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                return VersionCompareHelper.CompareVersionStringSmartAsc(x, y);
            }
        }
    }
}
