using UnityEngine;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// 操作パネルの日本語 / 英語の切り替え。文言は使う場所に英語・日本語の組で書く（T("Load", "読み込み")）。
    /// </summary>
    public static class Loc
    {
        /// <summary>
        /// 日本語で表示中なら true。
        /// </summary>
        public static bool IsJapanese { get; private set; }

        /// <summary>
        /// 設定の言語（Auto は OS の言語）を反映する。パネルの描画前に毎回呼ぶ。
        /// </summary>
        public static void Apply(UiLanguage language)
        {
            IsJapanese = language == UiLanguage.Japanese
                || (language == UiLanguage.Auto && Application.systemLanguage == SystemLanguage.Japanese);
        }

        /// <summary>
        /// 表示中の言語の文言を返す。
        /// </summary>
        public static string T(string english, string japanese)
        {
            return IsJapanese ? japanese : english;
        }
    }
}
