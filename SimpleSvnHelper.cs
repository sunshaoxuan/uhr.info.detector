using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace uhr.info.detector
{
    /// <summary>
    /// シンプルなSVNヘルパークラス
    /// 複雑な非同期処理やタイムアウトロジックを避け、直接的な方法でSVN操作を処理
    /// </summary>
    public class SimpleSvnHelper
    {
        public readonly string svnExePath;
        private readonly Action<string> logAction;

        public SimpleSvnHelper(string svnExePath, Action<string> logAction = null)
        {
            this.svnExePath = svnExePath;
            this.logAction = logAction ?? (s => { }); // 默认空日志
        }

        /// <summary>
        /// SVNからファイルをローカルにダウンロード
        /// HTTP直接ダウンロードを使用し、頻繁なProcess作成を避ける
        /// </summary>
        /// <param name="svnUrl">SVNファイルのURL</param>
        /// <param name="fileName">ファイル名</param>
        /// <returns>ダウンロードされたファイルのローカルパス（失敗時はnull）</returns>
        public string DownloadFile(string svnUrl, string fileName)
        {
            try
            {
                // 创建临时目录
                string tempDir = Path.Combine(Path.GetTempPath(), "uhr_simple_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(tempDir);
                string localFile = Path.Combine(tempDir, fileName);

                logAction($"[SimpleSvn] 开始HTTP下载: {fileName}");

                // 使用WebClient直接HTTP下载，避免Process
                using (WebClient client = new WebClient())
                {
                    // SVN通常支持HTTP访问
                    client.Encoding = Encoding.UTF8;

                    // 添加超时和重试机制
                    for (int retry = 0; retry < 3; retry++)
                    {
                        try
                        {
                            client.DownloadFile(svnUrl, localFile);

                            if (File.Exists(localFile) && new FileInfo(localFile).Length > 0)
                            {
                                long size = new FileInfo(localFile).Length;
                                logAction($"[SimpleSvn] HTTP下载成功: {fileName}, size={size}");
                                return localFile;
                            }
                        }
                        catch (WebException wex)
                        {
                            logAction($"[SimpleSvn] HTTP下载失败(尝试{retry + 1}/3): {wex.Message}");
                            if (retry == 2) throw;
                            System.Threading.Thread.Sleep(500);
                        }
                    }
                }

                logAction($"[SimpleSvn] HTTP下载失败，文件不存在或为空");
                return null;
            }
            catch (Exception ex)
            {
                logAction($"[SimpleSvn] HTTP下载异常: {ex.GetType().Name} - {ex.Message}");
                // HTTP下载失败，回退到SVN命令行方式
                return DownloadFileBySvnCommand(svnUrl, fileName);
            }
        }

        /// <summary>
        /// 使用SVN命令行下载（备用方案）
        /// </summary>
        private string DownloadFileBySvnCommand(string svnUrl, string fileName)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "uhr_simple_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(tempDir);
                string localFile = Path.Combine(tempDir, fileName);

                logAction($"[SimpleSvn] 回退SVN命令下载: {fileName}");

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = svnExePath,
                    Arguments = $"export \"{svnUrl}\" \"{localFile}\" --force --non-interactive --trust-server-cert",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using (Process p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        logAction($"[SimpleSvn] SVNプロセス起動失敗");
                        return null;
                    }

                    p.WaitForExit(30000); // 30秒超时

                    if (!p.HasExited)
                    {
                        logAction($"[SimpleSvn] SVNタイムアウト");
                        p.Kill();
                        return null;
                    }

                    if (p.ExitCode != 0)
                    {
                        logAction($"[SimpleSvn] SVN下载失败: ExitCode={p.ExitCode}");
                        return null;
                    }
                }

                System.Threading.Thread.Sleep(200); // 等待文件写入

                if (!File.Exists(localFile))
                {
                    logAction($"[SimpleSvn] SVN文件不存在: {localFile}");
                    return null;
                }

                long size = new FileInfo(localFile).Length;
                logAction($"[SimpleSvn] SVN下载成功: {fileName}, size={size}");
                return localFile;
            }
            catch (Exception ex)
            {
                logAction($"[SimpleSvn] SVN下载异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 计算文件MD5哈希 - 最简单的方式，增加更多保护
        /// </summary>
        public string CalculateHash(string filePath)
        {
            FileStream stream = null;
            MD5 md5 = null;
            try
            {
                logAction($"[SimpleSvn] ハッシュ計算開始: {filePath}");

                if (string.IsNullOrEmpty(filePath))
                {
                    logAction($"[SimpleSvn] ファイルパスが空");
                    return null;
                }

                if (!File.Exists(filePath))
                {
                    logAction($"[SimpleSvn] ファイルが存在しません: {filePath}");
                    return null;
                }

                // 等待文件可访问
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        break;
                    }
                    catch (IOException)
                    {
                        if (i == 4) throw;
                        System.Threading.Thread.Sleep(100);
                    }
                }

                if (stream == null)
                {
                    logAction($"[SimpleSvn] ファイルを開けません: {filePath}");
                    return null;
                }

                logAction($"[SimpleSvn] ファイルオープン成功, サイズ: {stream.Length}");

                md5 = MD5.Create();
                byte[] hashBytes = md5.ComputeHash(stream);
                string hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                logAction($"[SimpleSvn] ハッシュ計算成功: {hash}");
                return hash;
            }
            catch (Exception ex)
            {
                logAction($"[SimpleSvn] 哈希计算失败: {ex.GetType().Name} - {ex.Message}");
                logAction($"[SimpleSvn] StackTrace: {ex.StackTrace}");
                return null;
            }
            finally
            {
                if (stream != null)
                {
                    try { stream.Close(); stream.Dispose(); } catch { }
                }
                if (md5 != null)
                {
                    try { md5.Dispose(); } catch { }
                }
            }
        }

        /// <summary>
        /// 从SVN获取文件并计算哈希 - 一步到位
        /// </summary>
        public string DownloadAndGetHash(string svnUrl, string fileName)
        {
            string tempFile = DownloadFile(svnUrl, fileName);
            if (string.IsNullOrEmpty(tempFile))
                return null;

            try
            {
                string hash = CalculateHash(tempFile);
                return hash;
            }
            finally
            {
                // 清理临时文件
                try
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                    string dir = Path.GetDirectoryName(tempFile);
                    if (Directory.Exists(dir) && Directory.GetFiles(dir).Length == 0)
                        Directory.Delete(dir);
                }
                catch { }
            }
        }

        /// <summary>
        /// SVN命令执行结果
        /// </summary>
        private class SvnResult
        {
            public int ExitCode { get; set; }
            public string Stdout { get; set; }
            public string Stderr { get; set; }
            public bool TimedOut { get; set; }
        }

        /// <summary>
        /// 执行SVN命令并返回结果
        /// </summary>
        private SvnResult RunSvnCommand(string args, int timeoutMs, Encoding forcedEncoding = null)
        {
            var encoding = forcedEncoding ?? Encoding.GetEncoding(932); // Shift_JIS as default
            var psi = new ProcessStartInfo
            {
                FileName = svnExePath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding,
            };

            using (var p = Process.Start(psi))
            {
                if (p == null)
                {
                    return new SvnResult { ExitCode = -1, Stderr = "Process.Start returned null" };
                }

                var msOut = new System.IO.MemoryStream();
                var msErr = new System.IO.MemoryStream();
                var tOut = System.Threading.Tasks.Task.Run(() =>
                {
                    try { p.StandardOutput.BaseStream.CopyTo(msOut); } 
                    catch (Exception ex) { logAction?.Invoke($"[SimpleSvn] STDOUT読み取り失敗: {ex.Message}"); }
                });
                var tErr = System.Threading.Tasks.Task.Run(() =>
                {
                    try { p.StandardError.BaseStream.CopyTo(msErr); } 
                    catch (Exception ex) { logAction?.Invoke($"[SimpleSvn] STDERR読み取り失敗: {ex.Message}"); }
                });

                var start = DateTime.UtcNow;
                bool timedOut = false;

                // 使用轮询方式等待，避免无限等待导致的闪退
                while (!p.WaitForExit(100))
                {
                    if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                    {
                        timedOut = true;
                        try
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                        }
                        catch { }
                        break;
                    }
                }

                try
                {
                    System.Threading.Tasks.Task.WaitAll(new[] { tOut, tErr }, 2000);
                }
                catch { }

                return new SvnResult
                {
                    ExitCode = timedOut ? -1 : p.ExitCode,
                    Stdout = encoding.GetString(msOut.ToArray()),
                    Stderr = encoding.GetString(msErr.ToArray()),
                    TimedOut = timedOut
                };
            }
        }

        /// <summary>
        /// 从SVN下载单个文件（按需下载优化版本）
        /// </summary>
        public bool DownloadSingleFile(string svnFileUrl, string localFilePath, int timeoutMs = 30000)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localFilePath) ?? ".");

                logAction($"[SimpleSvn] 按需下载開始: {Path.GetFileName(localFilePath)}");

                // 尝试两次export
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    var res = RunSvnCommand(
                        $"export \"{svnFileUrl}\" \"{localFilePath}\" --force --quiet --non-interactive --trust-server-cert",
                        timeoutMs);

                    if (!res.TimedOut && res.ExitCode == 0)
                    {
                        // 等待文件写入完成
                        for (int i = 0; i < 25 && !File.Exists(localFilePath); i++)
                        {
                            System.Threading.Thread.Sleep(20);
                        }

                        if (File.Exists(localFilePath))
                        {
                            // 等待文件大小稳定
                            long size1 = new FileInfo(localFilePath).Length;
                            System.Threading.Thread.Sleep(200);
                            long size2 = new FileInfo(localFilePath).Length;

                            int stabilityChecks = 0;
                            while (size1 != size2 && stabilityChecks++ < 3)
                            {
                                size1 = size2;
                                System.Threading.Thread.Sleep(200);
                                size2 = new FileInfo(localFilePath).Length;
                            }

                            logAction($"[SimpleSvn] ダウンロード成功: {Path.GetFileName(localFilePath)}, size={size2}");
                            return true;
                        }
                    }

                    logAction($"[SimpleSvn] ダウンロード失敗({attempt}/2): ExitCode={res.ExitCode}, Timeout={res.TimedOut}");

                    if (!string.IsNullOrEmpty(res.Stderr))
                    {
                        logAction($"[SimpleSvn] STDERR: {res.Stderr}");
                    }
                }

                logAction($"[SimpleSvn] ダウンロード最終失敗: {Path.GetFileName(localFilePath)}");
                return false;
            }
            catch (Exception ex)
            {
                logAction($"[SimpleSvn] ダウンロード例外: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 批量下载SVN文件（按需下载，只下载指定的文件）
        /// </summary>
        /// <param name="svnBaseUrl">SVN基础URL</param>
        /// <param name="fileList">需要下载的文件列表（文件名）</param>
        /// <param name="targetDir">目标目录</param>
        /// <param name="onProgress">进度回调</param>
        /// <returns>成功下载的文件列表</returns>
        public System.Collections.Generic.List<string> DownloadFilesBatch(
            string svnBaseUrl,
            System.Collections.Generic.List<string> fileList,
            string targetDir,
            Action<int, int, string> onProgress = null)
        {
            var successFiles = new System.Collections.Generic.List<string>();

            logAction($"[SimpleSvn] バッチダウンロード開始: {fileList.Count}個のファイル");

            for (int i = 0; i < fileList.Count; i++)
            {
                string fileName = fileList[i];
                onProgress?.Invoke(i + 1, fileList.Count, fileName);

                try
                {
                    // 构造SVN文件URL（假设文件在SVN仓库的某个位置）
                    // 这里需要根据实际SVN结构调整
                    string svnFileUrl = $"{svnBaseUrl.TrimEnd('/')}/{fileName}";
                    string localPath = Path.Combine(targetDir, fileName);

                    if (DownloadSingleFile(svnFileUrl, localPath))
                    {
                        successFiles.Add(localPath);
                    }
                }
                catch (Exception ex)
                {
                    logAction($"[SimpleSvn] ファイル処理例外: {fileName}, {ex.Message}");
                }
            }

            logAction($"[SimpleSvn] バッチダウンロード完了: {successFiles.Count}/{fileList.Count}個成功");
            return successFiles;
        }

        /// <summary>
        /// 批量导出整个SVN目录（全量下载，保底方案）
        /// </summary>
        public bool ExportEntireRepository(string svnUrl, string targetDir, int timeoutMs = 600000)
        {
            try
            {
                Directory.CreateDirectory(targetDir);

                logAction($"[SimpleSvn] SVN全体エクスポート開始: {svnUrl}");

                var psi = new ProcessStartInfo
                {
                    FileName = svnExePath,
                    Arguments = $"export \"{svnUrl}\" \"{targetDir}\" --force --non-interactive --trust-server-cert",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using (var p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        logAction($"[SimpleSvn] プロセス起動失敗");
                        return false;
                    }

                    // 使用带超时参数的WaitForExit，避免闪退问题
                    bool exited = p.WaitForExit(timeoutMs);

                    if (!exited)
                    {
                        logAction($"[SimpleSvn] エクスポートタイムアウト");
                        try { p.Kill(); } catch { }
                        return false;
                    }

                    int exitCode = p.ExitCode;
                    logAction($"[SimpleSvn] エクスポート完了: ExitCode={exitCode}");

                    return exitCode == 0 && Directory.Exists(targetDir);
                }
            }
            catch (Exception ex)
            {
                logAction($"[SimpleSvn] エクスポート例外: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取SVN目录下的子目录列表
        /// </summary>
        public System.Collections.Generic.List<string> GetSvnSubDirectories(string svnUrl, int timeoutMs = 30000)
        {
            try
            {
                var res = RunSvnCommand($"list --xml \"{svnUrl}\"", timeoutMs, Encoding.UTF8);
                if (res.TimedOut || res.ExitCode != 0)
                    throw new Exception(res.Stderr ?? "SVN timeout");

                var xdoc = System.Xml.Linq.XDocument.Parse(res.Stdout);
                return xdoc.Descendants("entry")
                           .Where(e => (string)e.Attribute("kind") == "dir")
                           .Select(e => (string)e.Element("name"))
                           .ToList();
            }
            catch (Exception ex)
            {
                throw new Exception("SVN subdirectory get failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 递归列出某个URL下的所有文件（不含目录），返回相对路径
        /// </summary>
        public System.Collections.Generic.List<string> GetSvnFileListRecursive(string svnUrl, int timeoutMs = 120000)
        {
            var res = RunSvnCommand($"list --xml -R \"{svnUrl}\"", timeoutMs, Encoding.UTF8);
            if (res.TimedOut) throw new Exception("SVN列表命令超时");
            if (res.ExitCode != 0) throw new Exception($"SVN列表命令失敗(ExitCode={res.ExitCode}): {res.Stderr}");
            if (string.IsNullOrWhiteSpace(res.Stdout)) return new System.Collections.Generic.List<string>();

            var xdoc = System.Xml.Linq.XDocument.Parse(res.Stdout);
            return xdoc.Descendants("entry")
                       .Where(e => (string)e.Attribute("kind") == "file")
                       .Select(e => (string)e.Element("name"))
                       .Where(n => !string.IsNullOrWhiteSpace(n))
                       .ToList();
        }

        /// <summary>
        /// 清理所有临时文件夹
        /// </summary>
        public static void CleanupTempFolders()
        {
            try
            {
                string tempPath = Path.GetTempPath();

                // 清理uhr_simple_*文件夹（单个文件下载）
                var simpleDirs = Directory.GetDirectories(tempPath, "uhr_simple_*");
                foreach (var dir in simpleDirs)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch { }
                }

                // 清理uhr_batch_*文件夹（批量SVN导出 - CustomizedFiles）
                var batchDirs = Directory.GetDirectories(tempPath, "uhr_batch_*");
                foreach (var dir in batchDirs)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch { }
                }

                // 清理uhr_merge_*文件夹（批量SVN导出 - MergeFiles）
                var mergeDirs = Directory.GetDirectories(tempPath, "uhr_merge_*");
                foreach (var dir in mergeDirs)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch { }
                }

                // 清理uhr_ondemand_*文件夹（按需下载 - On-demand Download）
                var ondemandDirs = Directory.GetDirectories(tempPath, "uhr_ondemand_*");
                foreach (var dir in ondemandDirs)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch { }
                }

                // 清理uhr_merge_ondemand_*文件夹（マージファイル按需下载 - Merge On-demand Download）
                var mergeOndemandDirs = Directory.GetDirectories(tempPath, "uhr_merge_ondemand_*");
                foreach (var dir in mergeOndemandDirs)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
