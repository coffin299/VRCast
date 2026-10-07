using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.Editor.Build
{
    /// <summary>
    /// Windows ビルドの後、プロジェクト直下の Trackers にあるトラッカー一式をビルドの StreamingAssets へコピーする。
    /// メニュー・Build Settings・-executeMethod のどの経路でビルドしても同梱されるようにビルド後処理で行う。
    /// </summary>
    internal sealed class BundledTrackerCopier : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            // Windows スタンドアロン以外は対象外
            BuildTarget target = report.summary.platform;
            if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneWindows)
            {
                return;
            }

            // 出力先: (exe のあるフォルダ)/(exe 名)_Data/StreamingAssets
            string outputPath = report.summary.outputPath;
            string dataFolder = Path.Combine(Path.GetDirectoryName(outputPath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(outputPath) + "_Data");
            string streamingAssets = Path.Combine(dataFolder, "StreamingAssets");

            // 入力元ごとに、置かれているものだけをコピーする
            foreach (TrackingSource source in (TrackingSource[])System.Enum.GetValues(typeof(TrackingSource)))
            {
                // 外部アプリから受信する入力元には同梱版が無い（MediaPipe のフォルダを二重にコピーしない）
                if (!TrackingSourceInfo.UsesBundledTracker(source))
                {
                    continue;
                }

                string folder = TrackerProcess.FolderOf(source);
                string from = Path.Combine(TrackerProcess.BundledRoot, folder);
                if (!Directory.Exists(from))
                {
                    continue;
                }

                // 前回ビルドの残りと混ざらないよう消してからコピー
                string to = Path.Combine(streamingAssets, folder);
                if (Directory.Exists(to))
                {
                    Directory.Delete(to, true);
                }

                CopyDirectory(from, to);
                Debug.Log($"[VRCast][Build] Copied {folder} to {to}");
            }
        }

        private static void CopyDirectory(string from, string to)
        {
            // フォルダ構成を先に作り、ファイルを相対パスのまま上書きコピー
            foreach (string directory in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
            }

            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
            }
        }
    }
}
