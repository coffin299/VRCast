using UnityEngine;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// 操作パネルの英語 / 日本語 / 韓国語 / 中国語（簡体字・繁体字）の切り替え。
    /// 文言は使う場所に 5 言語の組で書く（T("Load", "読み込み", "불러오기", "加载", "載入")）。
    /// </summary>
    public static class Loc
    {
        /// <summary>
        /// 表示中の言語（Auto は解決済みの言語になる）。
        /// </summary>
        public static UiLanguage Current { get; private set; } = UiLanguage.English;

        /// <summary>
        /// 日本語で表示中なら true。
        /// </summary>
        public static bool IsJapanese => Current == UiLanguage.Japanese;

        /// <summary>
        /// 設定の言語（Auto は OS の言語）を反映する。パネルの描画前に毎回呼ぶ。
        /// </summary>
        public static void Apply(UiLanguage language)
        {
            // 明示指定はそのまま、Auto は OS の言語から決める
            Current = language != UiLanguage.Auto ? language : FromSystem(Application.systemLanguage);
        }

        /// <summary>
        /// 表示中の言語の文言を返す。
        /// </summary>
        public static string T(
            string english, string japanese, string korean, string chineseSimplified, string chineseTraditional)
        {
            // 言語ごとの文言（英語は既定）
            switch (Current)
            {
                case UiLanguage.Japanese:
                    return japanese;
                case UiLanguage.Korean:
                    return korean;
                case UiLanguage.ChineseSimplified:
                    return chineseSimplified;
                case UiLanguage.ChineseTraditional:
                    return chineseTraditional;
                default:
                    return english;
            }
        }

        private static UiLanguage FromSystem(SystemLanguage language)
        {
            // 日本語・韓国語・中国語以外の OS は英語（地域の分からない中国語は簡体字）
            switch (language)
            {
                case SystemLanguage.Japanese:
                    return UiLanguage.Japanese;
                case SystemLanguage.Korean:
                    return UiLanguage.Korean;
                case SystemLanguage.ChineseTraditional:
                    return UiLanguage.ChineseTraditional;
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                    return UiLanguage.ChineseSimplified;
                default:
                    return UiLanguage.English;
            }
        }
    }
}
