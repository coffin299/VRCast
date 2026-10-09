using UnityEngine;
using VRCast.Platform;

namespace VRCast.UI
{
    /// <summary>
    /// 新しいバージョンの入手（自動更新の「今すぐ更新」と進み具合、入手先の GitHub / BOOTH を開くボタン）。
    /// 更新の通知と Settings で共有する。
    /// </summary>
    public static class UpdateDownloadButtons
    {
        // バイト数を MB で表示するときの割る数
        private const float BytesPerMegabyte = 1024f * 1024f;

        public static void Draw(UpdateChecker updates)
        {
            UpdateInstaller installer = updates.Installer;

            // 更新の途中は進み具合だけを出す（入手先のボタンは出さない）
            if (installer.IsBusy)
            {
                DrawProgress(installer);
                return;
            }

            DrawInstall(installer);
            DrawManual(updates);
        }

        /// <summary>
        /// 自動更新に失敗したときの理由（表示言語の文言 + 英語の原因）。
        /// </summary>
        public static string DescribeFailure(UpdateInstaller.Failure failure, string detail)
        {
            string reason;
            switch (failure)
            {
                case UpdateInstaller.Failure.Download:
                    reason = Loc.T("Could not download the update", "更新をダウンロードできませんでした",
                        "업데이트를 다운로드하지 못했습니다", "无法下载更新", "無法下載更新");
                    break;
                case UpdateInstaller.Failure.Corrupted:
                    reason = Loc.T("The downloaded file was damaged", "ダウンロードしたファイルが壊れていました",
                        "다운로드한 파일이 손상되었습니다", "下载的文件已损坏", "下載的檔案已損毀");
                    break;
                case UpdateInstaller.Failure.Extract:
                    reason = Loc.T("Could not extract the update", "更新を展開できませんでした",
                        "업데이트를 압축 해제하지 못했습니다", "无法解压更新", "無法解壓縮更新");
                    break;
                case UpdateInstaller.Failure.Launch:
                    reason = Loc.T("Could not start the updater", "更新プログラムを起動できませんでした",
                        "업데이트 프로그램을 시작하지 못했습니다", "无法启动更新程序", "無法啟動更新程式");
                    break;
                default:
                    reason = Loc.T("The update failed", "更新に失敗しました", "업데이트에 실패했습니다", "更新失败", "更新失敗");
                    break;
            }

            return string.IsNullOrEmpty(detail) ? reason : $"{reason} ({detail})";
        }

        private static void DrawProgress(UpdateInstaller installer)
        {
            switch (installer.State)
            {
                case UpdateInstaller.InstallState.Downloading:
                    // 進み具合（% と MB）と中止のボタン
                    string amount = $"{installer.Progress * 100f:F0}% ({installer.DownloadedBytes / BytesPerMegabyte:F0} / "
                        + $"{installer.TotalBytes / BytesPerMegabyte:F0} MB)";
                    GuiControls.Hint(Loc.T("Downloading", "ダウンロード中", "다운로드 중", "正在下载", "正在下載") + $"... {amount}");
                    if (GUILayout.Button(Loc.T("Cancel", "キャンセル", "취소", "取消", "取消"), GuiControls.Shrinkable))
                    {
                        installer.Cancel();
                    }

                    break;
                case UpdateInstaller.InstallState.Preparing:
                    GuiControls.Hint(Loc.T("Checking and extracting the update...", "更新を確認・展開しています...",
                        "업데이트를 확인하고 압축을 푸는 중...", "正在检查并解压更新...", "正在檢查並解壓縮更新..."));
                    break;
                default:
                    GuiControls.Hint(Loc.T("Closing VRCast to install the update...", "更新のため VRCast を終了しています...",
                        "업데이트를 위해 VRCast를 종료하는 중...", "正在关闭 VRCast 以安装更新...", "正在關閉 VRCast 以安裝更新..."));
                    break;
            }
        }

        private static void DrawInstall(UpdateInstaller installer)
        {
            // 直前の失敗（もう一度押せる。手動の入手先も下に出る）
            if (installer.State == UpdateInstaller.InstallState.Failed)
            {
                GuiControls.Warning(DescribeFailure(installer.LastFailure, installer.FailureDetail));
            }

            switch (installer.GetAvailability())
            {
                case UpdateInstaller.Availability.Available:
                    // 押してほしい操作なのでアクセント色で目立たせる（入手先のボタンは通常の色のまま）
                    UiTheme theme = UiTheme.Current;
                    // 自動で起動し直すことは下の説明に書き、ボタンの文字は短くする（長いと幅が足りず文字が切れる）
                    if (GUILayout.Button(Loc.T("Update now", "今すぐ更新", "지금 업데이트", "立即更新", "立即更新"),
                            theme != null ? theme.AccentButton : GUI.skin.button, GuiControls.Shrinkable))
                    {
                        installer.Install();
                    }

                    GuiControls.Hint(Loc.T(
                        "Downloads from GitHub, closes VRCast, replaces the files and starts it again. Settings are kept",
                        "GitHub からダウンロードし、VRCast をいったん終了してファイルを入れ替えてから起動し直します。設定はそのまま残ります",
                        "GitHub에서 다운로드하고 VRCast를 종료한 뒤 파일을 교체하고 다시 시작합니다. 설정은 그대로 유지됩니다",
                        "从 GitHub 下载，关闭 VRCast 并替换文件后重新启动。设置会保留",
                        "從 GitHub 下載，關閉 VRCast 並替換檔案後重新啟動。設定會保留"));
                    break;
                case UpdateInstaller.Availability.NotWritable:
                    GuiControls.Hint(Loc.T(
                        "Automatic update is not available because VRCast is in a folder that needs administrator rights "
                        + "(such as Program Files). Download it from below",
                        "VRCast が管理者権限の必要なフォルダ（Program Files など）にあるため自動更新できません。下のボタンから入手してください",
                        "VRCast가 관리자 권한이 필요한 폴더(Program Files 등)에 있어 자동 업데이트할 수 없습니다. 아래 버튼에서 받으세요",
                        "VRCast 位于需要管理员权限的文件夹（如 Program Files）中，无法自动更新。请通过下方按钮获取",
                        "VRCast 位於需要系統管理員權限的資料夾（如 Program Files）中，無法自動更新。請透過下方按鈕取得"));
                    break;
            }
        }

        private static void DrawManual(UpdateChecker updates)
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
