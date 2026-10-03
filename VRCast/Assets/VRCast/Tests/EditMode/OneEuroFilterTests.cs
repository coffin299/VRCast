using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.Tests
{
    public class OneEuroFilterTests
    {
        // 60fps 相当の時間刻み
        private const float DeltaTime = 1f / 60f;

        [Test]
        public void Filter_FirstSample_PassesThrough()
        {
            var filter = new OneEuroFilter(1f, 0f);

            Assert.That(filter.Filter(new Vector3(1f, 2f, 3f), DeltaTime), Is.EqualTo(new Vector3(1f, 2f, 3f)));
        }

        [Test]
        public void Filter_ConstantInput_ConvergesToInput()
        {
            // 0 から始めて一定値を 2 秒間入れると、ほぼその値になること
            var filter = new OneEuroFilter(1f, 0f);
            filter.Reset(Vector3.zero);
            Vector3 value = Vector3.zero;
            for (int i = 0; i < 120; i++)
            {
                value = filter.Filter(Vector3.one, DeltaTime);
            }

            Assert.That(Vector3.Distance(value, Vector3.one), Is.LessThan(1e-3f));
        }

        [Test]
        public void Filter_SmallJitter_IsAttenuated()
        {
            // ±1cm で振動する入力は、振幅が半分以下に抑えられること
            var filter = new OneEuroFilter(1.5f, 0.5f);
            filter.Reset(Vector3.zero);
            float maxAbs = 0f;
            for (int i = 0; i < 120; i++)
            {
                float sample = i % 2 == 0 ? 0.01f : -0.01f;
                maxAbs = Mathf.Max(maxAbs, Mathf.Abs(filter.Filter(new Vector3(sample, 0f, 0f), DeltaTime).x));
            }

            Assert.That(maxAbs, Is.LessThan(0.005f));
        }

        [Test]
        public void Filter_ZeroDeltaTime_KeepsValue()
        {
            var filter = new OneEuroFilter(1f, 0f);
            filter.Reset(Vector3.one);

            Assert.That(filter.Filter(Vector3.zero, 0f), Is.EqualTo(Vector3.one));
        }
    }
}
