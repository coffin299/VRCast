using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// トラッカーの値の間を補間する PointInterpolator を検証する。
    /// </summary>
    public class PointInterpolatorTests
    {
        private const float Interval = 1f / 30f;

        [Test]
        public void FirstPush_SnapsToValue()
        {
            var interpolator = new PointInterpolator(1);
            interpolator.Push(new[] { new Vector3(1f, 2f, 3f) }, 0f);
            interpolator.Update(0f);

            Assert.That(interpolator.HasValue, Is.True);
            Assert.That(interpolator.Current[0], Is.EqualTo(new Vector3(1f, 2f, 3f)));
        }

        [Test]
        public void RegularPackets_MoveHalfwayAtHalfInterval()
        {
            // 一定間隔で届けば、間隔の半分の時点で中間に来る
            var interpolator = new PointInterpolator(1);
            interpolator.Push(new[] { Vector3.zero }, 0f);
            interpolator.Push(new[] { Vector3.right }, Interval);

            interpolator.Update(Interval * 1.5f);
            Assert.That(interpolator.Current[0].x, Is.EqualTo(0.5f).Within(1e-4f));

            // 最新の値に着いたら止まる（行き過ぎない）
            interpolator.Update(Interval * 5f);
            Assert.That(interpolator.Current[0].x, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void EarlyPacket_StartsFromShownPosition()
        {
            // 途中で次が届いても、表示位置は飛ばない
            var interpolator = new PointInterpolator(1);
            interpolator.Push(new[] { Vector3.zero }, 0f);
            interpolator.Push(new[] { Vector3.right }, Interval);
            interpolator.Update(Interval * 1.5f);
            Vector3 shown = interpolator.Current[0];

            interpolator.Push(new[] { Vector3.right * 2f }, Interval * 1.5f);
            interpolator.Update(Interval * 1.5f);

            Assert.That(interpolator.Current[0].x, Is.EqualTo(shown.x).Within(1e-4f));
        }

        [Test]
        public void Reset_SnapsNextValue()
        {
            var interpolator = new PointInterpolator(1);
            interpolator.Push(new[] { Vector3.zero }, 0f);
            interpolator.Reset();

            Assert.That(interpolator.HasValue, Is.False);

            // 見失った後の最初の値は補間せずに合わせる
            interpolator.Push(new[] { Vector3.up }, 1f);
            interpolator.Update(1f);
            Assert.That(interpolator.Current[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Interval_IsClampedForLongGaps()
        {
            // 途切れた後の長い間隔で、動きが極端に遅くならない
            var interpolator = new PointInterpolator(1);
            interpolator.Push(new[] { Vector3.zero }, 0f);
            interpolator.Push(new[] { Vector3.right }, 10f);

            Assert.That(interpolator.Interval, Is.LessThanOrEqualTo(0.2f));
        }
    }
}
