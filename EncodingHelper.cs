using System;
using System.Text;

namespace uhr.info.detector
{
    /// <summary>
    /// 编码处理辅助类
    /// 处理各种字符编码转换，特别是SVN输出的编码自动检测
    /// </summary>
    public static class EncodingHelper
    {
        /// <summary>
        /// SVN控制台默认编码 (Shift_JIS / CP932)
        /// </summary>
        public static readonly Encoding SvnConsoleEncoding = Encoding.GetEncoding(932);

        /// <summary>
        /// Shift_JIS 编码 (CP932)
        /// </summary>
        public static readonly Encoding EncSjis = Encoding.GetEncoding(932);

        /// <summary>
        /// UTF-8 编码（无BOM，严格模式）
        /// </summary>
        public static readonly Encoding EncUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <summary>
        /// SVNからのテキスト出力を自動判別してデコード
        /// UTF-8（BOM付きまたは無損失デコード）を優先し、失敗した場合はCP932（Shift_JIS）にフォールバック
        /// </summary>
        /// <param name="bytes">デコードするバイト配列</param>
        /// <returns>デコードされた文字列</returns>
        public static string DecodeSvnText(byte[] bytes)
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
                if (!s.Contains("\uFFFD")) return s;
            }
            catch { /* UTF-8ではない */ }

            // 3) CP932にフォールバック
            return EncSjis.GetString(bytes);
        }
    }
}
