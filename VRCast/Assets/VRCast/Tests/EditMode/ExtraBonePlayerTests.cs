using NUnit.Framework;
using UnityEngine;
using VRCast.Animations;
using VRCast.AvatarFormat;

namespace VRCast.Tests
{
    /// <summary>
    /// ExtraBonePlayer（追加のボーンクリップの選択再生）を検証する。
    /// </summary>
    public class ExtraBonePlayerTests
    {
        private GameObject _root;
        private Transform _tail;
        private Animation _animation;
        private Mesh _mesh;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Avatar");
            _animation = _root.AddComponent<Animation>();
            _animation.playAutomatically = false;
            _tail = new GameObject("Tail").transform;
            _tail.SetParent(_root.transform, false);

            // 表情に結び付くプリセットを一覧へ残すための BlendShape
            _mesh = new Mesh();
            _mesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right };
            _mesh.triangles = new[] { 0, 1, 2 };
            _mesh.AddBlendShapeFrame("smile", 100f, new Vector3[3], null, null);
            GameObject body = new GameObject("Body");
            body.transform.SetParent(_root.transform, false);
            body.AddComponent<SkinnedMeshRenderer>().sharedMesh = _mesh;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_mesh);
        }

        private AnimationClip AddClip(string name, float from, float to)
        {
            var clip = new AnimationClip { legacy = true, name = name, wrapMode = WrapMode.Loop };
            clip.SetCurve("Tail", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, from, 1f, to));
            _animation.AddClip(clip, name);
            return clip;
        }

        private ExtraBonePlayer Create(ExpressionSet set)
        {
            var expressions = _root.AddComponent<ExpressionController>();
            expressions.Initialize(_root.transform, set);
            var player = _root.AddComponent<ExtraBonePlayer>();
            player.Initialize(_root.transform, set, expressions);
            expressions.BindBones(player);
            return player;
        }

        [Test]
        public void BoneOnly_PlaysAndRestoresRestPose()
        {
            string clip = AvatarPackageLayout.ExtraBoneClipPrefix + "000_Tail";
            AddClip(clip, 0f, 4f);
            _tail.localPosition = new Vector3(1f, 0f, 0f);
            ExtraBonePlayer player = Create(new ExpressionSet
            {
                presets = new[]
                {
                    new ExpressionPreset
                    {
                        name = "Tail",
                        values = new BlendShapeValue[0],
                        boneClip = clip,
                        bonePaths = new[] { "Tail" },
                    },
                },
            });

            // ブレンドシェイプが無いので切り替えに出て、選ぶと動き、外すと読み込み時の姿勢へ戻る
            Assert.That(player.Count, Is.EqualTo(1));
            player.ToggleManual(0);
            Assert.That(player.IsPlaying, Is.True);
            Sample(clip, 1f);
            Assert.That(_tail.localPosition.x, Is.EqualTo(4f).Within(0.01f));
            player.ToggleManual(0);
            Assert.That(player.IsPlaying, Is.False);
            Assert.That(_tail.localPosition.x, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void ExpressionClip_TakesPriorityAndIsNotListed()
        {
            string ears = AvatarPackageLayout.ExtraBoneClipPrefix + "000_Ears";
            string tail = AvatarPackageLayout.ExtraBoneClipPrefix + "001_Tail";
            AddClip(ears, 2f, 2f);
            AddClip(tail, 5f, 5f);
            var set = new ExpressionSet
            {
                presets = new[]
                {
                    new ExpressionPreset
                    {
                        name = "Smile",
                        values = new[] { new BlendShapeValue { path = "Body", blendShape = "smile", weight = 100f } },
                        boneClip = ears,
                        bonePaths = new[] { "Tail" },
                    },
                    new ExpressionPreset
                    {
                        name = "Tail",
                        values = new BlendShapeValue[0],
                        boneClip = tail,
                        bonePaths = new[] { "Tail" },
                    },
                },
            };
            var expressions = _root.AddComponent<ExpressionController>();
            expressions.Initialize(_root.transform, set);
            var player = _root.AddComponent<ExtraBonePlayer>();
            player.Initialize(_root.transform, set, expressions);
            expressions.BindBones(player);

            // 表情に結び付いたクリップは切り替えに出さない
            Assert.That(player.Count, Is.EqualTo(1));
            Assert.That(player.NameAt(0), Is.EqualTo("Tail"));
            player.ToggleManual(0);
            expressions.Select(0, false);
            Sample(ears, 0f);
            Assert.That(_tail.localPosition.x, Is.EqualTo(2f).Within(0.01f));

            // 表情を外すと、選んでいたボーンだけのクリップに戻る
            expressions.ResetToNeutral();
            Sample(tail, 0f);
            Assert.That(_tail.localPosition.x, Is.EqualTo(5f).Within(0.01f));
        }

        [Test]
        public void OtherClips_AreIgnored()
        {
            AddClip(AvatarPackageLayout.FxMotionClipPrefix + "000_Prop", 0f, 1f);
            ExtraBonePlayer player = Create(new ExpressionSet());
            Assert.That(player.Count, Is.EqualTo(0));
            Assert.That(player.IsPlaying, Is.False);
        }

        private void Sample(string clip, float time)
        {
            _animation[clip].time = time;
            _animation.Sample();
        }
    }
}
