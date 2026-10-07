using NUnit.Framework;
using UnityEngine;
using VRCast.Animations;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// 待機モーションの角度計算と設定値の補正を検証する。
    /// </summary>
    public class IdleMotionTests
    {
        [Test]
        public void BreathCurve_StartsEmptyPeaksAtInhaleEndAndRepeats()
        {
            // 吐き切りから始まり、吸い終わりで最大、1 周で戻ること
            Assert.That(IdleMotion.BreathCurve(0f), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(IdleMotion.BreathCurve(IdleMotion.InhaleRatio), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(IdleMotion.BreathCurve(1f), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(IdleMotion.BreathCurve(2.25f), Is.EqualTo(IdleMotion.BreathCurve(0.25f)).Within(1e-5f));
        }

        [Test]
        public void Curves_StayInRange()
        {
            // 呼吸は 0〜1、揺れは -1〜1 に収まること
            for (float t = 0f; t < 60f; t += 0.05f)
            {
                float breath = IdleMotion.BreathCurve(t / IdleMotion.BreathPeriod);
                float sway = IdleMotion.SwayCurve(t);
                Assert.That(breath, Is.InRange(0f, 1f));
                Assert.That(sway, Is.InRange(-1f, 1f));
            }
        }

        [Test]
        public void Evaluate_ZeroStrength_IsStill()
        {
            // 強さ 0 なら、どの時刻でも全ての角度が 0 であること
            for (float t = 0f; t < 30f; t += 0.37f)
            {
                IdleMotionPose pose = IdleMotion.Evaluate(t, 0f, 0f, 0f);
                Assert.AreEqual(0f, pose.ChestPitch);
                Assert.AreEqual(0f, pose.ShoulderRaise);
                Assert.AreEqual(0f, pose.TorsoRoll);
                Assert.AreEqual(0f, pose.TorsoYaw);
                Assert.AreEqual(Vector3.zero, pose.Head);
            }
        }

        [Test]
        public void Evaluate_StandardStrength_StaysWithinMaxAngles()
        {
            // 強さ 1 なら、各角度が上限を超えないこと
            for (float t = 0f; t < 120f; t += 0.1f)
            {
                IdleMotionPose pose = IdleMotion.Evaluate(t, 1f, 1f, 1f);
                Assert.That(pose.ChestPitch, Is.InRange(0f, IdleMotion.MaxChestPitch));
                Assert.That(pose.ShoulderRaise, Is.InRange(0f, IdleMotion.MaxShoulderRaise));
                Assert.That(Mathf.Abs(pose.TorsoRoll), Is.LessThanOrEqualTo(IdleMotion.MaxTorsoRoll + 1e-5f));
                Assert.That(Mathf.Abs(pose.TorsoYaw), Is.LessThanOrEqualTo(IdleMotion.MaxTorsoYaw + 1e-5f));
                Assert.That(Mathf.Abs(pose.Head.x), Is.LessThanOrEqualTo(IdleMotion.MaxHead.x + 1e-5f));
                Assert.That(Mathf.Abs(pose.Head.y), Is.LessThanOrEqualTo(IdleMotion.MaxHead.y + 1e-5f));
                Assert.That(Mathf.Abs(pose.Head.z), Is.LessThanOrEqualTo(IdleMotion.MaxHead.z + 1e-5f));
            }
        }

        [Test]
        public void Evaluate_ActuallyMoves()
        {
            // 強さ 1 で時刻を進めると、呼吸・揺れ・頭のいずれも変化すること
            IdleMotionPose a = IdleMotion.Evaluate(0.5f, 1f, 1f, 1f);
            IdleMotionPose b = IdleMotion.Evaluate(2.5f, 1f, 1f, 1f);
            Assert.AreNotEqual(a.ChestPitch, b.ChestPitch);
            Assert.AreNotEqual(a.TorsoRoll, b.TorsoRoll);
            Assert.AreNotEqual(a.Head, b.Head);
        }

        [Test]
        public void Sanitize_ClampsIdleMotionSettings()
        {
            // 範囲外の強さ・速さ（手編集など）が補正されること
            var settings = new AppSettings
            {
                idleBreathing = -1f,
                idleSway = 10f,
                idleHeadMotion = float.MaxValue,
                idleMotionSpeed = 0f,
            };
            settings.Sanitize();
            Assert.AreEqual(0f, settings.idleBreathing);
            Assert.AreEqual(AppSettings.MaxIdleMotionStrength, settings.idleSway);
            Assert.AreEqual(AppSettings.MaxIdleMotionStrength, settings.idleHeadMotion);
            Assert.AreEqual(AppSettings.MinIdleMotionSpeed, settings.idleMotionSpeed);
        }
    }
}
