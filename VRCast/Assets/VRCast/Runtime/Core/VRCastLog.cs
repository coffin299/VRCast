using UnityEngine;

namespace VRCast.Core
{
    /// <summary>
    /// Debug.Log をカテゴリ付きで薄くラップするログ出力。
    /// 出力先は Unity 標準（Editor Console / Player.log）のまま。
    /// Detail は「詳細ログ」が ON のときだけデバッグログタブに残す（Player.log には書かない）。
    /// </summary>
    public static class VRCastLog
    {
        // すべてのログ行に付与する接頭辞
        private const string Prefix = "[VRCast]";

        /// <summary>
        /// 詳細ログを記録するか。頻繁に呼ぶ箇所は、文字列を組み立てる前にこれで確認する。
        /// </summary>
        public static bool DetailEnabled => LogBuffer.DetailEnabled;

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

        public static void Detail(string category, string message)
        {
            // 詳細ログはディスクに書かずアプリ内にだけ残す（OFF なら LogBuffer 側で捨てる）
            LogBuffer.Add(LogLevel.Debug, category, message);
        }

        private static string Format(string category, string message)
        {
            // "[VRCast][Category] message" 形式に整形
            return $"{Prefix}[{category}] {message}";
        }
    }
}
