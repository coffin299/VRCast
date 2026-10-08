using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// 両肩の位置からの上半身の向き（ひねり・傾き）の計算を検証する。
    /// 座標はカメラ基準（x = 映像の右、y = 上、z = カメラから遠ざかる向き）で、本人はカメラの方を向いている。
    /// </summary>
    public class TorsoPoseTests
    {
        // 本人の右肩は映像の左、左肩は映像の右
        private static readonly Vector3 RightShoulder = new Vector3(-0.2f, 0f, 0f);
        private static readonly Vector3 LeftShoulder = new Vector3(0.2f, 0f, 0f);

        [Test]
        public void TryGetAngles_FacingCamera_IsZero()
        {
            // 正面を向いて両肩が水平なら 0（鏡像の有無に関係なく）
            foreach (bool mirror in new[] { false, true })
            {
                Assert.That(TorsoPose.TryGetAngles(Body(RightShoulder, LeftShoulder), mirror, out Vector2 angles), Is.True);
                Assert.That(angles.x, Is.EqualTo(0f).Within(1e-3f));
                Assert.That(angles.y, Is.EqualTo(0f).Within(1e-3f));
            }
        }

        [Test]
        public void TryGetAngles_RightShoulderBack_TwistsBySide()
        {
            // 本人の右肩が奥・左肩が手前（右へひねる）
            BodyTrackingFrame body = Body(RightShoulder + new Vector3(0f, 0f, 0.1f), LeftShoulder - new Vector3(0f, 0f, 0.1f));
            float expected = Mathf.Atan2(0.2f, 0.4f) * Mathf.Rad2Deg;

            // 通常はアバターの右肩が後ろへ（正）、鏡像ではアバターの左肩が後ろへ（負）
            Assert.That(TorsoPose.TryGetAngles(body, false, out Vector2 normal), Is.True);
            Assert.That(normal.x, Is.EqualTo(expected).Within(1e-3f));
            Assert.That(TorsoPose.TryGetAngles(body, true, out Vector2 mirrored), Is.True);
            Assert.That(mirrored.x, Is.EqualTo(-expected).Within(1e-3f));
        }

        [Test]
        public void TryGetAngles_RightShoulderUp_TiltsBySide()
        {
            // 本人の右肩が上がっている
            BodyTrackingFrame body = Body(RightShoulder + new Vector3(0f, 0.1f, 0f), LeftShoulder);
            float expected = Mathf.Atan2(0.1f, 0.4f) * Mathf.Rad2Deg;

            // 通常はアバターの右肩が上がり（正）、鏡像ではアバターの左肩が上がる（負）
            Assert.That(TorsoPose.TryGetAngles(body, false, out Vector2 normal), Is.True);
            Assert.That(normal.y, Is.EqualTo(expected).Within(1e-3f));
            Assert.That(TorsoPose.TryGetAngles(body, true, out Vector2 mirrored), Is.True);
            Assert.That(mirrored.y, Is.EqualTo(-expected).Within(1e-3f));
        }

        [Test]
        public void TryGetAngles_MissingShoulderOrNarrow_ReturnsFalse()
        {
            // 片方の肩が映っていない
            BodyTrackingFrame missing = Body(RightShoulder, LeftShoulder);
            missing.Left.HasShoulder = false;
            Assert.That(TorsoPose.TryGetAngles(missing, false, out _), Is.False);

            // 肩幅が狭すぎる（真横を向いた）
            BodyTrackingFrame narrow = Body(new Vector3(-0.02f, 0f, 0f), new Vector3(0.02f, 0f, 0f));
            Assert.That(TorsoPose.TryGetAngles(narrow, false, out _), Is.False);
        }

        [Test]
        public void ToRotation_TurnsRightAxisAlongShoulderLine()
        {
            // ひねりと傾きを両方含む肩の線
            BodyTrackingFrame body = Body(new Vector3(-0.2f, 0.05f, 0.1f), new Vector3(0.2f, -0.05f, -0.1f));
            Assert.That(TorsoPose.TryGetAngles(body, false, out Vector2 angles), Is.True);

            // 回転後のアバターの右向きが、アバターの左肩 → 右肩の向き（通常は x を反転）と一致すること
            Vector3 line = new Vector3(0.4f, 0.1f, -0.2f).normalized;
            Vector3 rotated = TorsoPose.ToRotation(angles) * Vector3.right;
            Assert.That(Vector3.Distance(rotated, line), Is.LessThan(1e-4f));
        }

        private static BodyTrackingFrame Body(Vector3 right, Vector3 left)
        {
            // 両肩だけが映っているフレーム
            return new BodyTrackingFrame
            {
                Left = new ArmTrackingData { HasShoulder = true, Shoulder = left },
                Right = new ArmTrackingData { HasShoulder = true, Shoulder = right },
            };
        }
    }
}
