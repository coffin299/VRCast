using UnityEngine;
using VRCast.Platform;

namespace VRCast.UI
{
    /// <summary>
    /// 新しいバージョンの入手先（GitHub / BOOTH）を開くボタン。更新の通知と Settings で共有する。
    /// </summary>
    public static class UpdateDownloadButtons
    {
        public static void Draw(UpdateChecker updates)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Download from GitHub", "GitHub からダウンロード", "GitHub에서 다운로드",
                    "从 GitHub 下载", "從 GitHub 下載"), GuiControls.Shrinkable))
            {
                updates.OpenGitHub();
            }

            if (GUILayout.Button(Loc.T("Download from BOOTH", "BOOTH からダウンロード", "BOOTH에서 다운로드",
                    "从 BOOTH 下载", "從 BOOTH 下載"), GuiControls.Shrinkable))
            {
                updates.OpenBooth();
            }

            GUILayout.EndHorizontal();
        }
    }
}
