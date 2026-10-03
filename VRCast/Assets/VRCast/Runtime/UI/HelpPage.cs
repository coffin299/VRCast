using System;
using System.IO;
using UnityEngine;

namespace VRCast.UI
{
    /// <summary>
    /// 同梱のヘルプページ（StreamingAssets/Help/index.html）を既定のブラウザで開く。
    /// ページ側で日本語 / 英語を切り替える（ブラウザの言語に合わせ、手動切替も可）。
    /// </summary>
    public static class HelpPage
    {
        // 同梱フォルダ名（StreamingAssets 内）とファイル名
        private const string FolderName = "Help";
        private const string FileName = "index.html";

        /// <summary>
        /// ヘルプページのフルパス。
        /// </summary>
        public static string FilePath => Path.Combine(Application.streamingAssetsPath, FolderName, FileName);

        /// <summary>
        /// ヘルプページが同梱されていれば true。
        /// </summary>
        public static bool Exists => File.Exists(FilePath);

        /// <summary>
        /// ブラウザでヘルプページを開く。無ければ false。
        /// </summary>
        public static bool Open()
        {
            // 同梱されていなければ開かない
            if (!Exists)
            {
                return false;
            }

            // ローカルファイルを file:// の URL として開く
            Application.OpenURL(new Uri(FilePath).AbsoluteUri);
            return true;
        }
    }
}
