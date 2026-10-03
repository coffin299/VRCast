using System.IO;
using NUnit.Framework;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// トラッカーのカメラ一覧出力の解析と、同梱版の探索を検証する。
    /// </summary>
    public class TrackerProcessTests
    {
        [Test]
        public void ParseCameraList_ReadsNamesInIndexOrder()
        {
            // 見出し行・CRLF・前後空白を含む出力
            const string output = "Available cameras:\r\n0: Integrated Camera\r\n  1:  OBS Virtual Camera \r\n";

            // 番号順に名前だけが取り出されること
            Assert.That(TrackerProcess.ParseCameraList(output),
                Is.EqualTo(new[] { "Integrated Camera", "OBS Virtual Camera" }));
        }

        [Test]
        public void ParseCameraList_FillsMissingIndices()
        {
            // 番号が飛んでいても位置 = カメラ番号を保つこと
            Assert.That(TrackerProcess.ParseCameraList("2: USB Camera"),
                Is.EqualTo(new[] { string.Empty, string.Empty, "USB Camera" }));
        }

        [Test]
        public void ParseCameraList_EmptyOrUnrelated_ReturnsEmpty()
        {
            // 空出力・一覧以外の出力は 0 件
            Assert.That(TrackerProcess.ParseCameraList(null), Is.Empty);
            Assert.That(TrackerProcess.ParseCameraList("Error: no camera"), Is.Empty);
        }

        [Test]
        public void FindBundled_SearchesOnlyTheSourceFolder()
        {
            // MediaPipe 版だけを展開階層付きで置いた一時 StreamingAssets
            string root = Path.Combine(Path.GetTempPath(), "VRCastTests_" + System.Guid.NewGuid().ToString("N"));
            string folder = Path.Combine(root, TrackerProcess.FolderOf(TrackingSource.MediaPipe), "vrcast_tracker");
            Directory.CreateDirectory(folder);
            string executable = Path.Combine(folder, TrackerProcess.ExecutableOf(TrackingSource.MediaPipe));
            File.WriteAllText(executable, string.Empty);

            try
            {
                // MediaPipe は見つかり、OpenSeeFace は見つからないこと
                Assert.That(TrackerProcess.FindBundled(root, TrackingSource.MediaPipe), Is.EqualTo(executable));
                Assert.That(TrackerProcess.FindBundled(root, TrackingSource.OpenSeeFace), Is.Null);
            }
            finally
            {
                // 一時フォルダを後始末
                Directory.Delete(root, true);
            }
        }
    }
}
