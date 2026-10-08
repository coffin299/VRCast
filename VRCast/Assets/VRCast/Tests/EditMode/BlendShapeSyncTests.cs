using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRCast.Animations;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// BlendShapeSync（Modular Avatar の Blendshape Sync の再現）を検証する。
    /// </summary>
    public class BlendShapeSyncTests
    {
        private GameObject _root;
        private Mesh _faceMesh;
        private Mesh _chainMesh;
        private SkinnedMeshRenderer _face;
        private SkinnedMeshRenderer _chain;

        [SetUp]
        public void SetUp()
        {
            // 口の BlendShape を持つ "Body" と、口に追従する BlendShape を持つ "Chain" のアバターを用意
            _root = new GameObject("Avatar");
            _faceMesh = CreateMesh("vrc.v_aa");
            _chainMesh = CreateMesh("mouth");
            _face = CreateRenderer("Body", _faceMesh);
            _chain = CreateRenderer("Chain", _chainMesh);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_faceMesh);
            Object.DestroyImmediate(_chainMesh);
        }

        private static Mesh CreateMesh(string blendShape)
        {
            // BlendShape を 1 つ持つ三角形のメッシュ
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            mesh.AddBlendShapeFrame(blendShape, 100f, new Vector3[3], null, null);
            return mesh;
        }

        private SkinnedMeshRenderer CreateRenderer(string name, Mesh mesh)
        {
            // ルート直下にメッシュを置く
            var node = new GameObject(name);
            node.transform.SetParent(_root.transform);
            var renderer = node.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            return renderer;
        }

        private BlendShapeSync CreateSync(params BlendShapeSyncBinding[] bindings)
        {
            BlendShapeSync sync = _root.AddComponent<BlendShapeSync>();
            sync.Initialize(_root.transform, new BlendShapeSyncSet { bindings = bindings });
            return sync;
        }

        private static BlendShapeSyncBinding Binding(string sourcePath, string targetBlendShape)
        {
            return new BlendShapeSyncBinding
            {
                sourcePath = sourcePath, sourceBlendShape = "vrc.v_aa", targetPath = "Chain", targetBlendShape = targetBlendShape,
            };
        }

        [Test]
        public void Apply_CopiesSourceWeightToTarget()
        {
            // 口を開くと、チェーンの BlendShape も同じ値になること
            BlendShapeSync sync = CreateSync(Binding("Body", "mouth"));
            _face.SetBlendShapeWeight(0, 60f);
            sync.Apply();
            Assert.That(sync.Count, Is.EqualTo(1));
            Assert.That(_chain.GetBlendShapeWeight(0), Is.EqualTo(60f));

            // 閉じると戻ること
            _face.SetBlendShapeWeight(0, 0f);
            sync.Apply();
            Assert.That(_chain.GetBlendShapeWeight(0), Is.EqualTo(0f));
        }

        [Test]
        public void Initialize_SkipsMissingMeshesAndBlendShapes()
        {
            // 見つからないメッシュ・BlendShape の同期は無視されること
            BlendShapeSync sync = CreateSync(Binding("Missing", "mouth"), Binding("Body", "missing"));
            Assert.That(sync.Count, Is.EqualTo(0));
        }

        [Test]
        public void Apply_RespectsTargetLimit()
        {
            // 同期先に上限を付けていれば、その上限で切ること
            _root.AddComponent<BlendShapeLimiter>().Initialize(_root.transform, new List<BlendShapeLimit>
            {
                new BlendShapeLimit { path = "Chain", blendShape = "mouth", max = 40f },
            });
            BlendShapeSync sync = CreateSync(Binding("Body", "mouth"));
            _face.SetBlendShapeWeight(0, 90f);
            sync.Apply();
            Assert.That(_chain.GetBlendShapeWeight(0), Is.EqualTo(40f));
        }
    }
}
