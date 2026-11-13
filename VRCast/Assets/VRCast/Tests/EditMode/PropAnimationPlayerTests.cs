using NUnit.Framework;
using UnityEngine;
using VRCast.Animations;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// PropAnimationPlayer（小物のアニメーションの自動再生）を検証する。
    /// </summary>
    public class PropAnimationPlayerTests
    {
        private GameObject _root;
        private Transform _prop;
        private Animation _animation;

        [SetUp]
        public void SetUp()
        {
            // 人体用の Animator（コントローラー無し）と、FX の動きを入れる Animation を持つルート
            _root = new GameObject("Avatar");
            _root.AddComponent<Animator>();
            _animation = _root.AddComponent<Animation>();
            _animation.playAutomatically = false;
            _prop = new GameObject("Prop").transform;
            _prop.SetParent(_root.transform, false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        private AnimationClip AddClip(string name, float from, float to)
        {
            // 小物の x を 1 秒で from → to へ動かす legacy クリップ
            var clip = new AnimationClip { legacy = true, name = name, wrapMode = WrapMode.Loop };
            clip.SetCurve("Prop", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, from, 1f, to));
            _animation.AddClip(clip, name);
            return clip;
        }

        private PropAnimationPlayer CreatePlayer(bool enabled)
        {
            var player = _root.AddComponent<PropAnimationPlayer>();
            player.Initialize(_root.transform, new AppSettings { propAnimationsEnabled = enabled });
            return player;
        }

        private void SampleAt(float time)
        {
            foreach (AnimationState state in _animation)
            {
                state.time = time;
            }

            _animation.Sample();
        }

        [Test]
        public void FxMotions_PlayTogetherWithLaterLayerWinning()
        {
            AddClip(AvatarPackageLayout.FxMotionClipPrefix + "000_Low", 0f, 1f);
            AddClip(AvatarPackageLayout.FxMotionClipPrefix + "001_High", 2f, 4f);
            PropAnimationPlayer player = CreatePlayer(true);

            // FX のレイヤー順に後のものが優先され、ルートの Animator があっても動くこと
            Assert.That(player.FxMotionCount, Is.EqualTo(2));
            SampleAt(0.5f);
            Assert.That(_prop.localPosition.x, Is.EqualTo(3f).Within(0.01f));
        }

        [Test]
        public void OtherRootClips_AreNotPlayed()
        {
            // 接頭辞の無いクリップと、追加のボーンは FX の動きとして扱わないこと
            AddClip("UserClip", 0f, 1f);
            AddClip(AvatarPackageLayout.ExtraBoneClipPrefix + "000_Tail", 0f, 1f);
            PropAnimationPlayer player = CreatePlayer(true);
            Assert.That(player.FxMotionCount, Is.EqualTo(0));
            Assert.That(player.IsAvailable, Is.False);
        }

        [Test]
        public void Disabled_ReturnsToStartAndStops()
        {
            AddClip(AvatarPackageLayout.FxMotionClipPrefix + "000_Move", 1f, 2f);
            _prop.localPosition = new Vector3(5f, 0f, 0f);
            CreatePlayer(false);

            // OFF なら最初の姿勢に戻し、その後は時間を進めても動かないこと
            Assert.That(_prop.localPosition.x, Is.EqualTo(1f).Within(0.01f));
            SampleAt(0.5f);
            Assert.That(_prop.localPosition.x, Is.EqualTo(1f).Within(0.01f));
        }

        [Test]
        public void PropAnimator_WithoutController_IsIgnored()
        {
            // コントローラーの無い小物の Animator は再生対象にならないこと
            _prop.gameObject.AddComponent<Animator>();
            Assert.That(CreatePlayer(true).AnimatorCount, Is.EqualTo(0));
        }
    }
}
