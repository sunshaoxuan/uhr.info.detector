using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace uhr.info.detector
{
    static class Program
    {
        /// <summary>
        /// アプリケーションのメイン エントリ ポイントです。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.ThreadException += (s, e) =>
            {
                System.IO.File.AppendAllText("ErrorLog.txt",
                    $"[{DateTime.Now:HH:mm:ss}] UI线程未处理异常: {e.Exception}\n");
                MessageBox.Show("捕获到未处理异常，已记录并继续。", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                System.IO.File.AppendAllText("ErrorLog.txt",
                    $"[{DateTime.Now:HH:mm:ss}] 非UI线程未处理异常: {ex}\n");
                // 不再 rethrow，防止进程退出
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new frmMain());
        }

    }
}
