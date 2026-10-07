using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// iFacialMocap のテキスト形式の読み取り（値の範囲・左右の入れ替え・頭の向き・不正なデータ）を検証する。
    /// </summary>
    public class IFacialMocapPacketTests
    {
        [Test]
        public void TryParse_ShapesAndHead_FillsFrame()
        {
            // 0〜100 の値が 0〜1 になり、表情・パーフェクトシンク用の値がそろうこと
            Assert.That(IFacialMocapPacket.TryParse(
                "jawOpen-100|mouthSmile_L-50|hapihapi-0|=head#0,0,0,1.5,2.5,3.5|rightEye#1,2,3|leftEye#1,2,3|",
                out FaceTrackingFrame frame), Is.True);

            Assert.That(frame.MouthOpen, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(frame.BlendShapes, Is.Not.Null);
            Assert.That(frame.HasExpression, Is.True);
            Assert.That(frame.BlendShapeRange.BlinkClosed, Is.EqualTo(ArKitRange.IPhone.BlinkClosed));
            Assert.That(Quaternion.Angle(frame.HeadRotation, Quaternion.identity), Is.LessThan(0.01f));
        }

        [Test]
        public void TryParse_SwapsPersonSidesToVideoSides()
        {
            // iFacialMocap は ARKit そのまま（本人基準）: 本人の左目を閉じ、本人の左の口角を上げる
            IFacialMocapPacket.TryParse("eyeBlink_L-100|mouthSmile_L-60|", out FaceTrackingFrame frame);

            // 共通の映像基準（本人の左 = 映像の右）に並ぶこと
            Assert.That(frame.BlendShapes[ArKitFace.EyeBlinkRight], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(frame.BlendShapes[ArKitFace.IndexOf("mouthSmileRight")], Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(frame.BlendShapes[ArKitFace.IndexOf("mouthSmileLeft")], Is.EqualTo(0f).Within(1e-5f));
            Assert.That(frame.EyeOpenLeft, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void TryParse_V2Separator_IsAccepted()
        {
            // 新しい形式の "名前&値"（小数）も読めること
            Assert.That(IFacialMocapPacket.TryParse("jawOpen&100.0|", out FaceTrackingFrame frame), Is.True);
            Assert.That(frame.MouthOpen, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void TryParse_HeadDegrees_ConvertsToUnity()
        {
            // ARKit（右手系）の回転角を、MediaPipe と同じく Y・Z 軸まわりを逆向きにして Unity へ変換すること
            IFacialMocapPacket.TryParse("jawOpen-0|=head#10,20,5,0,0,0|", out FaceTrackingFrame frame);
            Assert.That(Quaternion.Angle(frame.HeadRotation, Quaternion.Euler(10f, -20f, -5f)), Is.LessThan(0.01f));
        }

        [Test]
        public void TryParse_HeadPitch_MatchesMediaPipeDirection()
        {
            // 上下だけの回転は MediaPipe の変換（四元数の x を保つ）と同じ向きになること（上を向いたら上）
            Quaternion arKit = Quaternion.Euler(15f, 0f, 0f);
            var mediaPipe = new Quaternion(arKit.x, -arKit.y, -arKit.z, arKit.w);
            IFacialMocapPacket.TryParse("jawOpen-0|=head#15,0,0,0,0,0|", out FaceTrackingFrame frame);
            Assert.That(Quaternion.Angle(frame.HeadRotation, mediaPipe), Is.LessThan(0.01f));
        }

        [Test]
        public void TryParse_NotIFacialMocap_ReturnsFalse()
        {
            // ARKit 名の値が無いテキスト・壊れた頭の値だけのものは不正として扱うこと
            Assert.That(IFacialMocapPacket.TryParse("{\"v\":1}", out _), Is.False);
            Assert.That(IFacialMocapPacket.TryParse("hapihapi-10|=head#a,b,c|", out _), Is.False);
            Assert.That(IFacialMocapPacket.TryParse(string.Empty, out _), Is.False);
        }
    }
}
