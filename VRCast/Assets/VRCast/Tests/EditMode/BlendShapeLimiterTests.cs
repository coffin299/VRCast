using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRCast.Animations;
using VRCast.Core;

namespace VRCast.Tests
{
    public class BlendShapeLimiterTests
    {
        private GameObject _root;
        private Mesh _mesh;
        private SkinnedMeshRenderer _face;

        [SetUp]
        public void SetUp()
        {
            // "Body" に Blink / Smile を持つアバターを用意
            _root = new GameObject("Avatar");
            var body = new GameObject("Body");
            body.transform.SetParent(_root.transform);
            _face = body.AddComponent<SkinnedMeshRenderer>();
            _mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            var deltas = new Vector3[3];
            _mesh.AddBlendShapeFrame("Blink", 100f, deltas, null, null);
            _mesh.AddBlendShapeFrame("Smile", 100f, deltas, null, null);
            _face.sharedMesh = _mesh;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_mesh);
        }

        private BlendShapeLimiter CreateLimiter(List<BlendShapeLimit> limits)
        {
            BlendShapeLimiter limiter = _root.AddComponent<BlendShapeLimiter>();
            limiter.Initialize(_root.transform, limits);
            return limiter;
        }

        [Test]
        public void Initialize_ListsShapesAndAppliesSavedLimits()
        {
            // 全 BlendShape を列挙し、記録済みの上限を当てること（見つからない名前は無視）
            BlendShapeLimiter limiter = CreateLimiter(new List<BlendShapeLimit>
            {
                new BlendShapeLimit { path = "Body", blendShape = "Blink", max = 70f },
                new BlendShapeLimit { path = "Body", blendShape = "Missing", max = 10f },
            });
            Assert.That(limiter.Shapes.Count, Is.EqualTo(2));
            Assert.That(limiter.LimitedCount, Is.EqualTo(1));
            Assert.That(limiter.Shapes[0].Max, Is.EqualTo(70f));
        }

        [Test]
        public void Overlay_MarksShapeAsFace()
        {
            // 初期化直後はどれも顔ではなく、上乗せを作った BlendShape だけが顔になること
            BlendShapeLimiter limiter = CreateLimiter(new List<BlendShapeLimit>());
            Assert.That(limiter.FaceCount, Is.EqualTo(0));
            BlendShapeOverlay.Create(_face, 0);
            BlendShapeOverlay.Create(_face, 0);
            Assert.That(limiter.FaceCount, Is.EqualTo(1));
            Assert.That(limiter.Shapes[0].IsFace, Is.True);
            Assert.That(limiter.Shapes[1].IsFace, Is.False);
        }

        [Test]
        public void Limit_ClampsOnlyLimitedShapes()
        {
            // 上限付きの Blink だけ切り、Smile はそのまま返すこと
            BlendShapeLimiter limiter = CreateLimiter(new List<BlendShapeLimit>());
            limiter.SetMax(limiter.Shapes[0], 60f);
            Assert.That(BlendShapeLimiter.Limit(_face, 0, 100f), Is.EqualTo(60f));
            Assert.That(BlendShapeLimiter.Limit(_face, 1, 100f), Is.EqualTo(100f));
        }

        [Test]
        public void Overlay_WritesWithinLimit()
        {
            // まばたき等の上乗せも上限で切られること
            BlendShapeLimiter limiter = CreateLimiter(new List<BlendShapeLimit>());
            limiter.SetMax(limiter.Shapes[0], 80f);
            BlendShapeOverlay overlay = BlendShapeOverlay.Create(_face, 0);
            overlay.Write(100f);
            Assert.That(_face.GetBlendShapeWeight(0), Is.EqualTo(80f).Within(0.01f));

            // 上限を外すと次の書き込みから 100 まで動くこと
            limiter.SetMax(limiter.Shapes[0], 100f);
            overlay.Write(100f);
            Assert.That(_face.GetBlendShapeWeight(0), Is.EqualTo(100f).Within(0.01f));
        }

        [Test]
        public void Exempt_SkipsLimitButNotLimitAlways()
        {
            // 上限をかけない間は Limit がそのまま返し、LimitAlways は切ること。戻せば Limit も切ること
            BlendShapeLimiter limiter = CreateLimiter(new List<BlendShapeLimit>());
            limiter.SetMax(limiter.Shapes[1], 40f);
            BlendShapeLimiter.SetExempt(_face, 1, true);
            Assert.That(BlendShapeLimiter.Limit(_face, 1, 100f), Is.EqualTo(100f));
            Assert.That(BlendShapeLimiter.LimitAlways(_face, 1, 100f), Is.EqualTo(40f));

            BlendShapeLimiter.SetExempt(_face, 1, false);
            Assert.That(BlendShapeLimiter.Limit(_face, 1, 100f), Is.EqualTo(40f));
        }

        [Test]
        public void Overlay_KeepsExemptBaseButLimitsOverlay()
        {
            // 上限をかけない表情の値は残し、上乗せ分だけを上限で切ること
            BlendShapeLimiter limiter = CreateLimiter(new List<BlendShapeLimit>());
            limiter.SetMax(limiter.Shapes[0], 50f);
            BlendShapeLimiter.SetExempt(_face, 0, true);
            BlendShapeOverlay overlay = BlendShapeOverlay.Create(_face, 0);

            _face.SetBlendShapeWeight(0, 90f);
            overlay.Write(0f);
            Assert.That(_face.GetBlendShapeWeight(0), Is.EqualTo(90f).Within(0.01f));

            _face.SetBlendShapeWeight(0, 0f);
            overlay.Write(100f);
            Assert.That(_face.GetBlendShapeWeight(0), Is.EqualTo(50f).Within(0.01f));
        }

        [Test]
        public void ExportAndClearAll_TrackLimitedShapes()
        {
            // 上限付きだけを書き出し、すべて解除で空になること
            BlendShapeLimiter limiter = CreateLimiter(new List<BlendShapeLimit>());
            limiter.SetMax(limiter.Shapes[1], 50f);
            List<BlendShapeLimit> exported = limiter.Export();
            Assert.That(exported.Count, Is.EqualTo(1));
            Assert.That(exported[0].path, Is.EqualTo("Body"));
            Assert.That(exported[0].blendShape, Is.EqualTo("Smile"));
            Assert.That(exported[0].max, Is.EqualTo(50f));

            limiter.ClearAll();
            Assert.That(limiter.LimitedCount, Is.EqualTo(0));
            Assert.That(limiter.Export(), Is.Empty);
        }
    }
}
