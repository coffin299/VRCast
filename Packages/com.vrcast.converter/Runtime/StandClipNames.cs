using System;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// VRChat の立ち・待機アニメを見分ける名前。書き出しとアプリで同じ判定にする。
    /// </summary>
    public static class StandClipNames
    {
        /// <summary>
        /// 立ち・待機らしい名前なら true（大文字小文字は区別しない）。
        /// </summary>
        public static bool LooksLikeStand(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (name.IndexOf("立ち", StringComparison.Ordinal) >= 0
                || name.IndexOf("待機", StringComparison.Ordinal) >= 0
                || name.IndexOf("リラックス", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            string lower = name.ToLowerInvariant();
            return lower.Contains("stand") || lower.Contains("idle") || lower.Contains("wait")
                || lower.Contains("afk") || lower.Contains("relax");
        }
    }
}
