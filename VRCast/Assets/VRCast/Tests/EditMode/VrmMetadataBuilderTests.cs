using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Avatars;

namespace VRCast.Tests
{
    public class VrmMetadataBuilderTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            // テストで作った階層を片付ける
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private static List<VrmMorphBinding> Bindings(params (string path, string shape, float weight)[] values)
        {
            // (パス, BlendShape 名, 重み) の並びから表情の値を作る
            var result = new List<VrmMorphBinding>();
            foreach ((string path, string shape, float weight) in values)
            {
                result.Add(new VrmMorphBinding(path, shape, weight));
            }

            return result;
        }

        private static readonly List<VrmMorphBinding> None = new List<VrmMorphBinding>();

        [Test]
        public void BuildExpressions_ConvertsWeightsAndSkipsEmpty()
        {
            var expressions = new[]
            {
                new VrmExpression { name = "Happy", bindings = Bindings(("Face", "Fcl_ALL_Joy", 1f), ("Face", "Fcl_EYE_Joy", 0.5f)) },
                new VrmExpression { name = "MaterialOnly" },
                new VrmExpression { name = "Happy", bindings = Bindings(("Face", "Fcl_ALL_Fun", 2f)) },
            };

            ExpressionSet set = VrmMetadataBuilder.BuildExpressions(expressions);

            // 重みは 0〜100 へ（範囲外は丸める）、BlendShape の無い表情は除き、同名には番号を付ける
            Assert.That(set.presets, Has.Length.EqualTo(2));
            Assert.That(set.presets[0].name, Is.EqualTo("Happy"));
            Assert.That(set.presets[0].values[1].weight, Is.EqualTo(50f));
            Assert.That(set.presets[1].name, Is.EqualTo("Happy 2"));
            Assert.That(set.presets[1].values[0].weight, Is.EqualTo(100f));
            Assert.That(set.Validate(), Is.Null);
        }

        [Test]
        public void BuildEyelids_UsesMostCommonMeshAndWinks()
        {
            List<VrmMorphBinding> blink = Bindings(("Face", "Blink", 1f), ("Body", "Other", 1f), ("Face", "Blink2", 1f));

            EyelidData data = VrmMetadataBuilder.BuildEyelids(
                blink, Bindings(("Face", "Blink_L", 1f)), Bindings(("Face", "Blink_R", 1f)));

            // 最も多いメッシュの BlendShape だけを使い、左右の片目用をウインクにする
            Assert.That(data.meshPath, Is.EqualTo("Face"));
            Assert.That(data.blinkBlendShapes, Is.EqualTo(new[] { "Blink", "Blink2" }));
            Assert.That(data.winkLeftBlendShape, Is.EqualTo("Blink_L"));
            Assert.That(data.winkRightBlendShape, Is.EqualTo("Blink_R"));
        }

        [Test]
        public void BuildEyelids_OnlyOneEyeEach_ClosesBothForBlink()
        {
            EyelidData data = VrmMetadataBuilder.BuildEyelids(
                None, Bindings(("Face", "Blink_L", 1f)), Bindings(("Face", "Blink_R", 1f)));

            // 両目用が無ければ左右の片目用を同時に閉じる
            Assert.That(data.blinkBlendShapes, Is.EqualTo(new[] { "Blink_L", "Blink_R" }));
            Assert.That(data.winkLeftBlendShape, Is.EqualTo("Blink_L"));
        }

        [Test]
        public void BuildEyelids_NoBindings_ReturnsEmpty()
        {
            EyelidData data = VrmMetadataBuilder.BuildEyelids(None, None, None);

            // まばたき無し
            Assert.That(data.blinkBlendShapes, Is.Empty);
            Assert.That(data.winkLeftBlendShape, Is.Empty);
        }

        [Test]
        public void BuildLipSync_PlacesVowelsAtVisemeIndices()
        {
            LipSyncData data = VrmMetadataBuilder.BuildLipSync(
                Bindings(("Face", "A", 1f)), Bindings(("Face", "I", 1f)), Bindings(("Face", "U", 1f)),
                Bindings(("Face", "E", 1f)), Bindings(("Face", "O", 1f)));

            // あいうえおの位置にだけ名前が入る
            Assert.That(data.mode, Is.EqualTo(LipSyncData.ModeVisemeBlendShape));
            Assert.That(data.meshPath, Is.EqualTo("Face"));
            Assert.That(data.visemes, Has.Length.EqualTo(AvatarDescriptorData.VisemeCount));
            Assert.That(data.visemes[AvatarDescriptorData.VisemeAa], Is.EqualTo("A"));
            Assert.That(data.visemes[AvatarDescriptorData.VisemeI], Is.EqualTo("I"));
            Assert.That(data.visemes[AvatarDescriptorData.VisemeU], Is.EqualTo("U"));
            Assert.That(data.visemes[AvatarDescriptorData.VisemeE], Is.EqualTo("E"));
            Assert.That(data.visemes[AvatarDescriptorData.VisemeO], Is.EqualTo("O"));
            Assert.That(data.visemes[0], Is.Empty);
            Assert.That(new AvatarDescriptorData { lipSync = data }.Validate(), Is.Null);
        }

        [Test]
        public void BuildLipSync_NoBindings_ReturnsNone()
        {
            LipSyncData data = VrmMetadataBuilder.BuildLipSync(None, None, None, None, None);

            // 口の形が無ければリップシンク無し
            Assert.That(data.mode, Is.EqualTo(LipSyncData.ModeNone));
        }

        private Transform Child(Transform parent, string name, Vector3 localPosition)
        {
            // 子の Transform を作る
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            return child;
        }

        [Test]
        public void BuildPhysBones_FollowsJointsAndIgnoresOtherChildren()
        {
            _root = new GameObject("Root");
            Transform hair = Child(_root.transform, "Hair", Vector3.up);
            Transform middle = Child(hair, "Middle", Vector3.down * 0.1f);
            Child(hair, "Accessory", Vector3.right * 0.1f);
            Transform tip = Child(middle, "Tip", Vector3.down * 0.1f);
            Child(tip, "Extra", Vector3.down * 0.1f);

            var spring = new VrmSpring
            {
                joints =
                {
                    new VrmSpringJoint { transform = hair, stiffness = 1f, dragForce = 0.4f, radius = 0.02f },
                    new VrmSpringJoint { transform = middle, stiffness = 1f, dragForce = 0.4f, radius = 0.04f },
                    new VrmSpringJoint { transform = tip },
                },
                colliders = { 1 },
            };
            var colliders = new List<VrmCollider>
            {
                // 長さ 0 のカプセル（球になる）と、上下に伸びるカプセル
                new VrmCollider { transform = _root.transform, shape = VrmColliderShape.Capsule, radius = 0.05f },
                new VrmCollider
                {
                    transform = hair, shape = VrmColliderShape.Capsule, radius = 0.05f,
                    offset = Vector3.zero, tail = Vector3.up * 0.2f,
                },
            };

            PhysBoneSet set = VrmMetadataBuilder.BuildPhysBones(_root.transform, new[] { spring }, colliders);

            // 関節の並びだけをたどり、それ以外の子（途中の小物・末端の先）は除外する
            Assert.That(set.bones, Has.Length.EqualTo(1));
            PhysBoneData bone = set.bones[0];
            Assert.That(bone.rootPath, Is.EqualTo("Hair"));
            Assert.That(bone.ignorePaths, Is.EquivalentTo(new[] { "Hair/Accessory", "Hair/Middle/Tip/Extra" }));

            // stiffness 1 → pull 0.5、dragForce 0.4 → spring 0、関節ごとに違う半径はカーブにする
            Assert.That(bone.pull, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(bone.spring, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(bone.radius, Is.EqualTo(0.04f).Within(1e-5f));
            Assert.That(bone.radiusCurve, Is.EqualTo(new[] { 0.5f, 0.5f, 1f }).Within(1e-5f));

            // カプセルは両端の中点・軸・半球込みの高さへ。番号は変換後の位置を指す
            Assert.That(set.colliders, Has.Length.EqualTo(2));
            Assert.That(set.colliders[0].shape, Is.EqualTo(PhysBoneColliderData.ShapeSphere));
            PhysBoneColliderData capsule = set.colliders[1];
            Assert.That(capsule.shape, Is.EqualTo(PhysBoneColliderData.ShapeCapsule));
            Assert.That(capsule.position.y, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(capsule.height, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(bone.colliders, Is.EqualTo(new[] { 1 }));
            Assert.That(set.Validate(), Is.Null);
        }

        [Test]
        public void BuildPhysBones_BrokenChain_StopsAtGap()
        {
            _root = new GameObject("Root");
            Transform hair = Child(_root.transform, "Hair", Vector3.up);
            Transform tip = Child(hair, "Tip", Vector3.down * 0.1f);
            Transform other = Child(_root.transform, "Other", Vector3.zero);

            var spring = new VrmSpring
            {
                joints =
                {
                    new VrmSpringJoint { transform = hair },
                    new VrmSpringJoint { transform = tip },
                    new VrmSpringJoint { transform = other },
                },
            };

            PhysBoneSet set = VrmMetadataBuilder.BuildPhysBones(_root.transform, new[] { spring }, new List<VrmCollider>());

            // 親子でつながらない関節の手前までをチェーンにする
            Assert.That(set.bones, Has.Length.EqualTo(1));
            Assert.That(set.bones[0].rootPath, Is.EqualTo("Hair"));
            Assert.That(set.bones[0].ignorePaths, Is.Empty);
        }

        [Test]
        public void BuildPhysBones_SingleJoint_IsSkipped()
        {
            _root = new GameObject("Root");
            Transform hair = Child(_root.transform, "Hair", Vector3.up);
            var spring = new VrmSpring { joints = { new VrmSpringJoint { transform = hair } } };

            PhysBoneSet set = VrmMetadataBuilder.BuildPhysBones(_root.transform, new[] { spring }, new List<VrmCollider>());

            // 揺れる区間が無いチェーンは作らない
            Assert.That(set.bones, Is.Empty);
        }
    }
}
