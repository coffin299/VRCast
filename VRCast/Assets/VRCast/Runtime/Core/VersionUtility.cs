using System;
using System.Globalization;

namespace VRCast.Core
{
    /// <summary>
    /// "1.2.0" 形式（先頭の "v" は許容、欠けた桁は 0）のバージョン番号を比較する。
    /// </summary>
    public static class VersionUtility
    {
        // 比較する桁数（メジャー・マイナー・パッチ）
        private const int PartCount = 3;

        /// <summary>
        /// candidate が current より新しければ true。どちらかが読めなければ false（誤って通知しないため）。
        /// </summary>
        public static bool IsNewer(string candidate, string current)
        {
            // 読めない番号は比較しない
            if (!TryParse(candidate, out int[] newer) || !TryParse(current, out int[] older))
            {
                return false;
            }

            // 上の桁から順に比べ、最初に違う桁で決める
            for (int i = 0; i < PartCount; i++)
            {
                if (newer[i] != older[i])
                {
                    return newer[i] > older[i];
                }
            }

            return false;
        }

        /// <summary>
        /// バージョン番号を [メジャー, マイナー, パッチ] に分解する。数字以外・負数・4 桁以上なら false。
        /// </summary>
        public static bool TryParse(string text, out int[] parts)
        {
            parts = new int[PartCount];

            // 空は不正
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            // タグ名の "v1.2.0" も受け付ける
            string trimmed = text.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(1);
            }

            // 桁数が多すぎるものは別の形式とみなす
            string[] tokens = trimmed.Split('.');
            if (tokens.Length > PartCount)
            {
                return false;
            }

            for (int i = 0; i < tokens.Length; i++)
            {
                // 各桁は 0 以上の整数のみ
                if (!int.TryParse(tokens[i], NumberStyles.None, CultureInfo.InvariantCulture, out parts[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
