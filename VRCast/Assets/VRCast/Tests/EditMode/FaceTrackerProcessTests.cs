using NUnit.Framework;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// facetracker のカメラ一覧出力の解析を検証する。
    /// </summary>
    public class FaceTrackerProcessTests
    {
        [Test]
        public void ParseCameraList_ReadsNamesInIndexOrder()
        {
            // 見出し行・CRLF・前後空白を含む出力
            const string output = "Available cameras:\r\n0: Integrated Camera\r\n  1:  OBS Virtual Camera \r\n";

            // 番号順に名前だけが取り出されること
            Assert.That(FaceTrackerProcess.ParseCameraList(output),
                Is.EqualTo(new[] { "Integrated Camera", "OBS Virtual Camera" }));
        }

        [Test]
        public void ParseCameraList_FillsMissingIndices()
        {
            // 番号が飛んでいても位置 = カメラ番号を保つこと
            Assert.That(FaceTrackerProcess.ParseCameraList("2: USB Camera"),
                Is.EqualTo(new[] { string.Empty, string.Empty, "USB Camera" }));
        }

        [Test]
        public void ParseCameraList_EmptyOrUnrelated_ReturnsEmpty()
        {
            // 空出力・一覧以外の出力は 0 件
            Assert.That(FaceTrackerProcess.ParseCameraList(null), Is.Empty);
            Assert.That(FaceTrackerProcess.ParseCameraList("Error: no camera"), Is.Empty);
        }
    }
}
