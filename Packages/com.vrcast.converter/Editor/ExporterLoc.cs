using UnityEditor;
using UnityEngine;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// エクスポーターの英語 / 日本語 / 韓国語 / 中国語（簡体字・繁体字）の切り替え。
    /// アプリ側の Loc と同じく、文言は使う場所に 5 言語の組で書く（T("Export", "書き出し", "내보내기", "导出", "匯出")）。
    /// 変換パッケージはアプリのアセンブリを参照できないため、Editor 用に別に持つ。
    /// </summary>
    public static class ExporterLoc
    {
        /// <summary>
        /// 表示言語の選択肢（EditorPrefs に数値で保存するため並び順を変えない）。
        /// </summary>
        public enum Language
        {
            // Unity Editor が動いている OS の言語に合わせる
            Auto = 0,
            English = 1,
            Japanese = 2,
            Korean = 3,
            ChineseSimplified = 4,
            ChineseTraditional = 5,
        }

        // 選んだ言語を保持する EditorPrefs キー
        private const string LanguageKey = "VRCast.Converter.Language";

        // 言語の選択肢の表示名（Language の並び。言語名はその言語で書く。先頭の Auto は表示言語で書く）
        private static readonly string[] LanguageNames =
        {
            string.Empty, "English", "日本語", "한국어", "简体中文", "繁體中文",
        };

        /// <summary>
        /// 設定の言語（Auto のまま）。
        /// </summary>
        public static Language Selected
        {
            get => (Language)EditorPrefs.GetInt(LanguageKey, (int)Language.Auto);
            set => EditorPrefs.SetInt(LanguageKey, (int)value);
        }

        /// <summary>
        /// 表示中の言語（Auto は OS の言語に解決済み）。
        /// </summary>
        public static Language Current
        {
            get
            {
                // 明示指定はそのまま、Auto は OS の言語から決める
                Language selected = Selected;
                return selected != Language.Auto ? selected : FromSystem(Application.systemLanguage);
            }
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
                case Language.Japanese:
                    return japanese;
                case Language.Korean:
                    return korean;
                case Language.ChineseSimplified:
                    return chineseSimplified;
                case Language.ChineseTraditional:
                    return chineseTraditional;
                default:
                    return english;
            }
        }

        /// <summary>
        /// 言語の選択肢の表示名を Language の並びで返す（先頭は表示言語で書いた Auto）。
        /// </summary>
        public static string[] LanguageLabels()
        {
            // 共有の配列を書き換えないよう複製してから Auto を入れる
            var labels = (string[])LanguageNames.Clone();
            labels[(int)Language.Auto] = T("Auto", "自動", "자동", "自动", "自動");
            return labels;
        }

        private static Language FromSystem(SystemLanguage language)
        {
            // 日本語・韓国語・中国語以外の OS は英語（地域の分からない中国語は簡体字）
            switch (language)
            {
                case SystemLanguage.Japanese:
                    return Language.Japanese;
                case SystemLanguage.Korean:
                    return Language.Korean;
                case SystemLanguage.ChineseTraditional:
                    return Language.ChineseTraditional;
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                    return Language.ChineseSimplified;
                default:
                    return Language.English;
            }
        }
    }
}
