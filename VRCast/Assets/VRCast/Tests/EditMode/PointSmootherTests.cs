using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// トラッカーの値へなめらかに寄せる PointSmoother を検証する。
    /// </summary>
    public class PointSmootherTests
    {
        private const float SmoothTime = 0.05f;
        private const float Frame = 1f / 60f;

        [Test]
        public void FirstPush_SnapsToValue()
        {
            var smoother = new PointSmoother(1, SmoothTime);
            smoother.Push(new[] { new Vector3(1f, 2f, 3f) }, 0f);
            smoother.Update(0f);

            Assert.That(smoother.HasValue, Is.True);
            Assert.That(smoother.Current[0], Is.EqualTo(new Vector3(1f, 2f, 3f)));
        }

        [Test]
        public void MovesTowardTarget_WithoutStopping()
        {
            // 次の値が届かない間も毎フレーム少しずつ近づき続ける（途中で止まらない）
            var smoother = new PointSmoother(1, SmoothTime);
            smoother.Push(new[] { Vector3.zero }, 0f);
            smoother.Push(new[] { Vector3.right }, 0f);

            float previous = 0f;
            for (int i = 1; i <= 6; i++)
            {
                smoother.Update(i * Frame);
                float x = smoother.Current[0].x;
                Assert.That(x, Is.GreaterThan(previous));
                Assert.That(x, Is.LessThanOrEqualTo(1f));
                previous = x;
            }
        }

        [Test]
        public void ReachesTarget_AfterEnoughTime()
        {
            var smoother = new PointSmoother(1, SmoothTime);
            smoother.Push(new[] { Vector3.zero }, 0f);
            smoother.Push(new[] { Vector3.right }, 0f);
            for (int i = 1; i <= 60; i++)
            {
                smoother.Update(i * Frame);
            }

            Assert.That(smoother.Current[0].x, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void Reset_SnapsNextValue()
        {
            var smoother = new PointSmoother(1, SmoothTime);
            smoother.Push(new[] { Vector3.zero }, 0f);
            smoother.Reset();

            Assert.That(smoother.HasValue, Is.False);

            // 見失った後の最初の値は寄せずに合わせる
            smoother.Push(new[] { Vector3.up }, 1f);
            smoother.Update(1f);
            Assert.That(smoother.Current[0], Is.EqualTo(Vector3.up));
        }
    }
}
