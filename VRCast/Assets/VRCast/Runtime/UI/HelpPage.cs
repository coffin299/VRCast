using UnityEngine;

namespace VRCast.UI
{
    /// <summary>
    /// Web のヘルプページ（webpage ブランチを GitHub Pages で公開）を既定のブラウザで開く。
    /// 操作パネルの表示言語をページへ渡し、ページ側でも 5 言語を切り替えられる。
    /// </summary>
    public static class HelpPage
    {
        // 公開サイトのトップ（概要）とヘルプ
        public const string SiteUrl = "https://coffin299.github.io/VRCast/";
        public const string Url = SiteUrl + "help/";

        /// <summary>
        /// 表示言語付きのヘルプページの URL（?lang= の値はサイトの site.js と同じ）。
        /// </summary>
        public static string LocalizedUrl => Url + "?lang=" + Loc.T("en", "ja", "ko", "zh-Hans", "zh-Hant");

        /// <summary>
        /// ブラウザでヘルプページを開く。
        /// </summary>
        public static void Open()
        {
            Application.OpenURL(LocalizedUrl);
        }
    }
}
