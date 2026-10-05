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

        [TestCase("STATS: camera 30.0 fps", LogLevel.Debug)]
        [TestCase("INFO: camera 0 opened", LogLevel.Info)]
        [TestCase("WARN: camera read failed", LogLevel.Warning)]
        [TestCase("ERROR: Failed to open camera 0", LogLevel.Error)]
        [TestCase("Traceback (most recent call last):", LogLevel.Error)]
        [TestCase("W0000 00:00:1700000000.000000 1234 inference.cc:12] slow", LogLevel.Warning)]
        [TestCase("E0000 00:00:1700000000.000000 1234 gl_context.cc:34] failed", LogLevel.Error)]
        [TestCase("I0000 00:00:1700000000.000000 1234 delegate.cc:56] Created", LogLevel.Info)]
        [TestCase("Tracking camera 0 -> 127.0.0.1:11573", LogLevel.Info)]
        public void ClassifyOutput_UsesLinePrefix(string line, LogLevel expected)
        {
            // 同梱トラッカーの接頭辞・glog の重要度・Python の例外から重要度を決めること
            Assert.That(TrackerProcess.ClassifyOutput(line), Is.EqualTo(expected));
        }

        [TestCase("VRCast Camera", true)]
        [TestCase("OBS Virtual Camera", true)]
        [TestCase("Integrated IR Camera", true)]
        [TestCase("Integrated Camera", false)]
        [TestCase("Logitech BRIO", false)]
        [TestCase("", false)]
        public void IsLikelyUnusableCamera_DetectsVirtualAndInfrared(string name, bool expected)
        {
            // 仮想カメラ（VRCast 自身・OBS）と赤外線カメラだけを該当とすること
            Assert.That(TrackerProcess.IsLikelyUnusableCamera(name), Is.EqualTo(expected));
        }

        [Test]
        public void ChooseDefaultCamera_PrefersRealCamera()
        {
            // 先頭が仮想カメラでも実カメラを選び、仮想カメラしか無ければその先頭を選ぶこと
            Assert.That(TrackerProcess.ChooseDefaultCamera(new[] { "VRCast Camera", "", "USB Camera" }),
                Is.EqualTo("USB Camera"));
            Assert.That(TrackerProcess.ChooseDefaultCamera(new[] { "", "OBS Virtual Camera" }),
                Is.EqualTo("OBS Virtual Camera"));
            Assert.That(TrackerProcess.ChooseDefaultCamera(new string[0]), Is.Empty);
        }

        [Test]
        public void DescribeExitCode_ExplainsKnownCodes()
        {
            // MediaPipe 版の終了コードと Windows の異常終了コードを説明し、不明なものは unknown
            StringAssert.Contains("camera stopped",
                TrackerProcess.DescribeExitCode(TrackingSource.MediaPipe, 2));
            StringAssert.Contains("DLL",
                TrackerProcess.DescribeExitCode(TrackingSource.OpenSeeFace, unchecked((int)0xC0000135)));
            Assert.That(TrackerProcess.DescribeExitCode(TrackingSource.OpenSeeFace, 2), Is.EqualTo("unknown"));
        }
    }
}
