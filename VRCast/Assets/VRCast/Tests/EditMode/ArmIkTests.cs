using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// 腕の位置合わせ（顔基準の割合・顔の前への押し出し・2 関節の肘の位置）を検証する。
    /// </summary>
    public class ArmIkTests
    {
        [Test]
        public void FaceWeight_IsOneNearFaceAndZeroFarAway()
        {
            // 顔のすぐ前は顔基準、遠くは肩基準、その間はなめらかに変わること
            Assert.That(ArmIk.FaceWeight(0.05f), Is.EqualTo(1f));
            Assert.That(ArmIk.FaceWeight(0.5f), Is.EqualTo(0f));
            float middle = ArmIk.FaceWeight((ArmIk.FaceNearDistance + ArmIk.FaceFarDistance) * 0.5f);
            Assert.That(middle, Is.GreaterThan(0f).And.LessThan(1f));
        }

        [Test]
        public void KeepInFrontOfFace_PushesOnlyHandsInFront()
        {
            // 顔の正面で奥行きが足りない手は、顔の手前まで出すこと
            Vector3 front = ArmIk.KeepInFrontOfFace(new Vector3(0f, -0.02f, 0.01f));
            Assert.That(front.z, Is.EqualTo(ArmIk.FaceClearance).Within(1e-5f));

            // 十分手前の手と、頭の横（耳）の手はそのままにすること
            Assert.That(ArmIk.KeepInFrontOfFace(new Vector3(0f, 0f, 0.2f)).z, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(ArmIk.KeepInFrontOfFace(new Vector3(0.2f, 0f, -0.05f)).z, Is.EqualTo(-0.05f).Within(1e-5f));
        }

        [Test]
        public void KeepHandInFrontOfFace_PushesFingersOverTheFace()
        {
            // 手首は顎の下（顔の正面の円の外）で、指先だけが顔に重なって奥にある
            var wrist = new Vector3(0.05f, -0.2f, 0.04f);
            var points = new Vector3[2];
            points[1] = new Vector3(0.02f, 0.02f, -0.03f) - wrist;

            // 指先が顔の手前まで出る分だけ、手首を前へ動かすこと（横・縦は変えない）
            Vector3 pushed = ArmIk.KeepHandInFrontOfFace(wrist, points, 0);
            Assert.That(pushed.z, Is.EqualTo(0.13f).Within(1e-4f));
            Assert.That(pushed.x, Is.EqualTo(wrist.x).Within(1e-5f));
            Assert.That(pushed.y, Is.EqualTo(wrist.y).Within(1e-5f));

            // 頭の横の手と、顔より明らかに奥の指、すでに手前の指は動かさないこと
            var beside = new Vector3(0.2f, 0f, -0.05f);
            Assert.That(ArmIk.KeepHandInFrontOfFace(beside, new[] { Vector3.zero, new Vector3(0.02f, 0f, 0f) }, 0).z,
                Is.EqualTo(beside.z).Within(1e-5f));
            var deepFinger = new Vector3(0.02f, 0.02f, -0.25f) - wrist;
            Assert.That(ArmIk.KeepHandInFrontOfFace(wrist, new[] { Vector3.zero, deepFinger }, 0).z,
                Is.EqualTo(wrist.z).Within(1e-5f));
            var forwardFinger = new Vector3(0.02f, 0.02f, 0.2f) - wrist;
            Assert.That(ArmIk.KeepHandInFrontOfFace(wrist, new[] { Vector3.zero, forwardFinger }, 0).z,
                Is.EqualTo(wrist.z).Within(1e-5f));

            // 手の点が無ければ、手首だけの補正と同じであること
            var front = new Vector3(0f, -0.02f, 0.01f);
            Assert.That(ArmIk.KeepHandInFrontOfFace(front, null, 0).z, Is.EqualTo(ArmIk.FaceClearance).Within(1e-5f));
        }

        [Test]
        public void SolveElbow_ReachableTarget_KeepsBoneLengthsAndBendSide()
        {
            // 長さ 0.3 + 0.25 の腕で、前方 0.4 の目標へ、下に肘を曲げる
            Vector3 shoulder = Vector3.zero;
            var target = new Vector3(0f, 0f, 0.4f);
            Vector3 elbow = ArmIk.SolveElbow(shoulder, 0.3f, 0.25f, target, Vector3.down, Vector3.forward,
                out bool reachable);

            // 届き、上腕・前腕の長さが保たれ、肘は下側にあること
            Assert.That(reachable, Is.True);
            Assert.That(Vector3.Distance(shoulder, elbow), Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(Vector3.Distance(elbow, target), Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(elbow.y, Is.LessThan(0f));
        }

        [Test]
        public void SolveElbow_TooFarTarget_StretchesTowardTarget()
        {
            // 腕の長さより遠い目標
            Vector3 elbow = ArmIk.SolveElbow(Vector3.zero, 0.3f, 0.25f, new Vector3(1f, 0f, 0f), Vector3.down,
                Vector3.right, out bool reachable);

            // 届かず、肘はほぼまっすぐ目標の方向にあること
            Assert.That(reachable, Is.False);
            Assert.That(Vector3.Angle(elbow, Vector3.right), Is.LessThan(5f));
        }

        [Test]
        public void BendDirection_IsPerpendicularToShoulderWristLine()
        {
            // 肩→手首の線に垂直な成分だけが残ること
            Vector3 bend = ArmIk.BendDirection(new Vector3(0.1f, -0.2f, 0f), new Vector3(0.4f, 0f, 0f));
            Assert.That(bend.x, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(bend.y, Is.EqualTo(-0.2f).Within(1e-5f));
        }
    }
}
