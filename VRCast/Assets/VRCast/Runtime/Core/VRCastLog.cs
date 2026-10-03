using UnityEngine;

namespace VRCast.Core
{
    /// <summary>
    /// Debug.Log をカテゴリ付きで薄くラップするログ出力。
    /// 出力先は Unity 標準（Editor Console / Player.log）のまま。
    /// </summary>
    public static class VRCastLog
    {
        // すべてのログ行に付与する接頭辞
        private const string Prefix = "[VRCast]";

        public static void Info(string category, string message)
        {
            // 通常ログとして出力
            Debug.Log(Format(category, message));
        }

        public static void Warning(string category, string message)
        {
            // 警告ログとして出力
            Debug.LogWarning(Format(category, message));
        }

        public static void Error(string category, string message)
        {
            // エラーログとして出力
            Debug.LogError(Format(category, message));
        }

        private static string Format(string category, string message)
        {
            // "[VRCast][Category] message" 形式に整形
            return $"{Prefix}[{category}] {message}";
        }
    }
}
