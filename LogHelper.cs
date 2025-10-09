using System;
using System.IO;

namespace uhr.info.detector
{
    /// <summary>
    /// 日志记录辅助类
    /// 提供线程安全的日志记录功能，永不抛出异常
    /// </summary>
    public static class LogHelper
    {
        /// <summary>
        /// 文件准备日志名称
        /// </summary>
        public const string FilePrepareLogName = "FilePrepare.log";

        /// <summary>
        /// 安全日志记录方法（永不抛异常）
        /// 任何日志都走它：就算路径/权限/占用有问题，绝不把主流程带崩
        /// </summary>
        /// <param name="fileName">日志文件名</param>
        /// <param name="message">日志消息</param>
        public static void SafeLog(string fileName, string message)
        {
            try { File.AppendAllText(fileName, message); }
            catch { /* 吞掉所有异常，保证主流程不崩溃 */ }
        }

        /// <summary>
        /// 截断长字符串（用于日志）
        /// </summary>
        /// <param name="s">原始字符串</param>
        /// <param name="maxLength">最大长度（默认400）</param>
        /// <returns>截断后的字符串</returns>
        public static string TruncateString(string s, int maxLength = 400)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= maxLength ? s : s.Substring(0, maxLength) + "...";
        }
    }
}
