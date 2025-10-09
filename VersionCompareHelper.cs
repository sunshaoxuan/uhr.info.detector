using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace uhr.info.detector
{
    /// <summary>
    /// 版本比较辅助类
    /// 处理版本号比较和匹配逻辑
    /// </summary>
    public static class VersionCompareHelper
    {
        /// <summary>
        /// バージョン文字列の昇順比較（例: 2.9.1 < 2.10.4 < 2.11.3）
        /// 数字部分は整数として比較し、非数字部分は文字列として比較
        /// </summary>
        /// <param name="a">バージョン文字列A</param>
        /// <param name="b">バージョン文字列B</param>
        /// <returns>負数(a&lt;b)、0(a==b)、正数(a&gt;b)</returns>
        public static int CompareVersionStringSmartAsc(string a, string b)
        {
            var pa = a.Split('.');
            var pb = b.Split('.');
            int len = Math.Max(pa.Length, pb.Length);

            for (int i = 0; i < len; i++)
            {
                string sa = (i < pa.Length) ? pa[i] : "0";
                string sb = (i < pb.Length) ? pb[i] : "0";

                // 数値として解析可能であれば数値比較
                bool isNumA = int.TryParse(sa, out int na);
                bool isNumB = int.TryParse(sb, out int nb);

                if (isNumA && isNumB)
                {
                    if (na != nb) return na.CompareTo(nb);
                }
                else
                {
                    // 文字列比較
                    int cmp = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp;
                }
            }
            return 0;
        }

        /// <summary>
        /// 汎用スマートバージョンマッチング
        /// 1. 完全一致を試す
        /// 2. ベースバージョン（+記号より前の部分）でマッチング
        /// 3. 部分一致（最も近いバージョンを見つける）
        /// </summary>
        /// <param name="availableVersions">利用可能なバージョンリスト</param>
        /// <param name="targetVersion">目標バージョン</param>
        /// <param name="moduleName">モジュール名（デバッグ用）</param>
        /// <returns>マッチしたインデックス、見つからない場合は-1</returns>
        public static int FindSmartVersionIndex(List<string> availableVersions, string targetVersion, string moduleName)
        {
            if (string.IsNullOrEmpty(targetVersion))
                return -1;

            // 1. 完全一致を試す
            int exactMatch = availableVersions.FindIndex(v => v.Equals(targetVersion, StringComparison.OrdinalIgnoreCase));
            if (exactMatch >= 0)
            {
                Debug.WriteLine($"{moduleName}完全一致: {targetVersion} -> {availableVersions[exactMatch]}");
                return exactMatch;
            }

            // 2. ベースバージョン（+記号より前の部分）でマッチング
            string baseVersion = targetVersion.Split('+')[0].Trim();
            int baseMatch = availableVersions.FindIndex(v => v.StartsWith(baseVersion, StringComparison.OrdinalIgnoreCase));
            if (baseMatch >= 0)
            {
                Debug.WriteLine($"{moduleName}ベースバージョン一致: {targetVersion} -> {availableVersions[baseMatch]} (ベース: {baseVersion})");
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
                Debug.WriteLine($"{moduleName}最適マッチ: {targetVersion} -> {availableVersions[bestMatch]} (スコア: {bestScore})");
                return bestMatch;
            }

            Debug.WriteLine($"{moduleName}マッチなし: {targetVersion}");
            return -1;
        }
    }
}
