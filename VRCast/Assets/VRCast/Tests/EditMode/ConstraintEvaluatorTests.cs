using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Dynamics;

namespace VRCast.Tests
{
    public class ConstraintEvaluatorTests
    {
        // 位置・角度比較の許容誤差
        private const float Tolerance = 1e-4f;

        private Transform _root;

        [SetUp]
        public void SetUp()
        {
            // 少しずらしたアバタールート（親空間への変換を確認するため）
            _root = new GameObject("Avatar").transform;
            _root.position = new Vector3(10f, 0f, 0f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root.gameObject);
        }

        private Transform AddChild(string name, Vector3 localPosition)
        {
            // ルート直下に子を作る
            var child = new GameObject(name).transform;
            child.SetParent(_root, false);
            child.localPosition = localPosition;
            return child;
        }

        private static ConstraintSourceData Source(string path, float weight = 1f)
        {
            return new ConstraintSourceData { path = path, weight = weight };
        }

        [Test]
        public void Position_AveragesSourcesByWeight()
        {
            Transform target = AddChild("Target", Vector3.zero);
            AddChild("A", new Vector3(0f, 0f, 0f));
            AddChild("B", new Vector3(4f, 0f, 0f));
            var data = new ConstraintData
            {
                type = ConstraintData.TypePosition,
                targetPath = "Target",
                sources = new[] { Source("A", 0.75f), Source("B", 0.25f) },
            };

            ConstraintEvaluator.Create(_root, data).Evaluate();

            // 重み 3:1 なので A 寄りの 1/4 地点
            Assert.That(Vector3.Distance(target.localPosition, new Vector3(1f, 0f, 0f)), Is.LessThan(Tolerance));
        }

        [Test]
        public void Position_UnaffectedAxisKeepsCurrentValue()
        {
            Transform target = AddChild("Target", new Vector3(0f, 5f, 0f));
            AddChild("A", new Vector3(1f, 2f, 3f));
            var data = new ConstraintData
            {
                type = ConstraintData.TypePosition,
                targetPath = "Target",
                positionAxes = 1 | 4,
                sources = new[] { Source("A") },
            };

            ConstraintEvaluator.Create(_root, data).Evaluate();

            // Y だけ元の値のまま
            Assert.That(Vector3.Distance(target.localPosition, new Vector3(1f, 5f, 3f)), Is.LessThan(Tolerance));
        }

        [Test]
        public void ZeroWeight_UsesRestValue()
        {
            Transform target = AddChild("Target", Vector3.zero);
            AddChild("A", new Vector3(1f, 2f, 3f));
            var data = new ConstraintData
            {
                type = ConstraintData.TypePosition,
                targetPath = "Target",
                weight = 0f,
                positionAtRest = new Vector3(0f, 1f, 0f),
                sources = new[] { Source("A") },
            };

            ConstraintEvaluator.Create(_root, data).Evaluate();

            Assert.That(Vector3.Distance(target.localPosition, new Vector3(0f, 1f, 0f)), Is.LessThan(Tolerance));
        }

        [Test]
        public void Inactive_LeavesTargetUntouched()
        {
            Transform target = AddChild("Target", new Vector3(0f, 7f, 0f));
            AddChild("A", new Vector3(1f, 2f, 3f));
            var data = new ConstraintData
            {
                type = ConstraintData.TypePosition,
                targetPath = "Target",
                active = false,
                sources = new[] { Source("A") },
            };

            ConstraintEvaluator.Create(_root, data).Evaluate();

            Assert.That(Vector3.Distance(target.localPosition, new Vector3(0f, 7f, 0f)), Is.LessThan(Tolerance));
        }

        [Test]
        public void Parent_FollowsSourceWithOffset()
        {
            Transform target = AddChild("Target", Vector3.zero);
            Transform source = AddChild("Hand", new Vector3(1f, 0f, 0f));
            source.localRotation = Quaternion.Euler(0f, 90f, 0f);
            var data = new ConstraintData
            {
                type = ConstraintData.TypeParent,
                targetPath = "Target",
                sources = new[] { new ConstraintSourceData { path = "Hand", positionOffset = Vector3.forward } },
            };

            ConstraintEvaluator.Create(_root, data).Evaluate();

            // ソースのローカル前方（Y 90° 回転でワールド +X）に 1 進んだ位置・同じ向き
            Assert.That(Vector3.Distance(target.position, source.TransformPoint(Vector3.forward)), Is.LessThan(Tolerance));
            Assert.That(Quaternion.Angle(target.rotation, source.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void Aim_PointsAimAxisAtSource()
        {
            Transform target = AddChild("Target", Vector3.zero);
            AddChild("Goal", new Vector3(3f, 0f, 5f));
            var data = new ConstraintData
            {
                type = ConstraintData.TypeAim,
                targetPath = "Target",
                aimAxis = Vector3.right,
                sources = new[] { Source("Goal") },
            };

            ConstraintEvaluator.Create(_root, data).Evaluate();

            // ローカル +X がゴールを向き、上はシーンの上に近い
            Vector3 expected = new Vector3(3f, 0f, 5f).normalized;
            Assert.That(Vector3.Angle(target.rotation * Vector3.right, expected), Is.LessThan(0.01f));
            Assert.That(Vector3.Dot(target.rotation * Vector3.up, Vector3.up), Is.GreaterThan(0.99f));
        }

        [Test]
        public void LookAt_PointsForwardAtSource()
        {
            Transform target = AddChild("Target", Vector3.zero);
            AddChild("Goal", new Vector3(-2f, 1f, 0f));
            var data = new ConstraintData
            {
                type = ConstraintData.TypeLookAt,
                targetPath = "Target",
                sources = new[] { Source("Goal") },
            };

            ConstraintEvaluator.Create(_root, data).Evaluate();

            Assert.That(Vector3.Angle(target.forward, new Vector3(-2f, 1f, 0f)), Is.LessThan(0.01f));
        }

        [Test]
        public void Create_MissingTargetOrRoot_ReturnsNull()
        {
            // 存在しないターゲット・ルート自身は評価対象外
            Assert.That(ConstraintEvaluator.Create(_root, new ConstraintData { targetPath = "Nothing" }), Is.Null);
            Assert.That(ConstraintEvaluator.Create(_root, new ConstraintData { targetPath = string.Empty }), Is.Null);
        }

        [Test]
        public void SortByDependency_EvaluatesReferencedConstraintFirst()
        {
            // B は A のターゲットをソースにしているため、A が先
            AddChild("TargetA", Vector3.zero);
            AddChild("TargetB", Vector3.zero);
            AddChild("Source", Vector3.one);
            ConstraintEvaluator b = ConstraintEvaluator.Create(_root, new ConstraintData
            {
                targetPath = "TargetB",
                sources = new[] { Source("TargetA") },
            });
            ConstraintEvaluator a = ConstraintEvaluator.Create(_root, new ConstraintData
            {
                targetPath = "TargetA",
                sources = new[] { Source("Source") },
            });

            List<ConstraintEvaluator> sorted = ConstraintSolver.SortByDependency(new List<ConstraintEvaluator> { b, a });

            Assert.That(sorted, Is.EqualTo(new[] { a, b }));
        }

        [Test]
        public void Validate_RejectsInvalidValues()
        {
            // 種類・重み・軸・回転の不正値はいずれも弾かれること
            Assert.That(new ConstraintData { type = "twist" }.Validate(), Is.Not.Null);
            Assert.That(new ConstraintData { weight = 2f }.Validate(), Is.Not.Null);
            Assert.That(new ConstraintData { positionAxes = 8 }.Validate(), Is.Not.Null);
            Assert.That(new ConstraintData { aimAxis = Vector3.zero }.Validate(), Is.Not.Null);
            Assert.That(new ConstraintData { rotationOffset = new Quaternion(0f, 0f, 0f, 0f) }.Validate(), Is.Not.Null);
            Assert.That(new ConstraintData { worldUpType = "sky" }.Validate(), Is.Not.Null);
            Assert.That(new ConstraintData().Validate(), Is.Null);
        }
    }
}
