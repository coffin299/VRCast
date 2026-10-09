using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// 全身モードの脚の IK（太もも・すね・足の 2 関節）を検証する。
    /// </summary>
    public class FootPlanterTests
    {
        private GameObject _root;
        private Transform _upper;
        private Transform _lower;
        private Transform _foot;

        [SetUp]
        public void SetUp()
        {
            // まっすぐ下へ伸びた長さ 0.4 + 0.4 の脚（根元は高さ 1）
            _root = new GameObject("Leg");
            _upper = new GameObject("Upper").transform;
            _upper.SetParent(_root.transform, false);
            _upper.localPosition = new Vector3(0f, 1f, 0f);
            _lower = new GameObject("Lower").transform;
            _lower.SetParent(_upper, false);
            _lower.localPosition = new Vector3(0f, -0.4f, 0f);
            _foot = new GameObject("Foot").transform;
            _foot.SetParent(_lower, false);
            _foot.localPosition = new Vector3(0f, -0.4f, 0f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void Solve_HipsLowered_KeepsFootAndBendsKneeForward()
        {
            // 腰を 0.1 下げても足は床（高さ 0.2）の元の位置に残ること
            var target = new Vector3(0f, 0.2f, 0f);
            _upper.position += Vector3.down * 0.1f;
            FootPlanter.Solve(_upper, _lower, _foot, target, Vector3.forward);

            Assert.That(Vector3.Distance(_foot.position, target), Is.LessThan(1e-3f));

            // 膝は前（bendHint の向き）へ曲がること
            Assert.That(_lower.position.z, Is.GreaterThan(0.05f));

            // 骨の長さは変わらないこと
            Assert.That(Vector3.Distance(_upper.position, _lower.position), Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(Vector3.Distance(_lower.position, _foot.position), Is.EqualTo(0.4f).Within(1e-4f));
        }

        [Test]
        public void Solve_HipsShifted_ReachesTarget()
        {
            // 腰が横・後ろへずれても、届く範囲なら足は元の位置に合うこと
            var target = new Vector3(0f, 0.2f, 0f);
            _upper.position += new Vector3(0.1f, -0.05f, -0.1f);
            FootPlanter.Solve(_upper, _lower, _foot, target, Vector3.forward);

            Assert.That(Vector3.Distance(_foot.position, target), Is.LessThan(1e-3f));
        }

        [Test]
        public void Solve_OutOfReach_StretchesTowardTarget()
        {
            // 届かない距離では伸ばし切る手前まで、目標の方向へ向くこと
            var target = new Vector3(0f, -1f, 0f);
            FootPlanter.Solve(_upper, _lower, _foot, target, Vector3.forward);

            Vector3 toFoot = _foot.position - _upper.position;
            Assert.That(Vector3.Angle(toFoot, target - _upper.position), Is.LessThan(0.5f));
            Assert.That(toFoot.magnitude, Is.LessThanOrEqualTo(0.8f));
        }
    }
}
