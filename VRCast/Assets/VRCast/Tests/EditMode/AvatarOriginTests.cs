using NUnit.Framework;
using UnityEngine;
using VRCast.Avatars;

namespace VRCast.Tests
{
    /// <summary>
    /// 原点からずれたアバターを、読み込み時に足元がルートへ来るよう動かす処理を検証する。
    /// </summary>
    public class AvatarOriginTests
    {
        [Test]
        public void TryAlign_ShiftsChildrenSoFeetSitOnRoot()
        {
            // 体がルートの斜め上にあり、小物は体の横についている
            var root = new GameObject("Avatar");
            Transform armature = Child(root.transform, "Armature", new Vector3(2f, 1f, -1f));
            Transform hips = Child(armature, "Hips", new Vector3(0f, 1f, 0f));
            Transform foot = Child(hips, "Foot", new Vector3(0.1f, -1f, 0f));
            Transform prop = Child(root.transform, "Prop", Vector3.zero);
            prop.position = armature.position + new Vector3(0.5f, 0f, 0f);
            Vector3 propFromArmature = prop.position - armature.position;

            Assert.That(AvatarOrigin.TryAlign(root.transform, hips, foot, null, out Vector3 moved), Is.True);

            // 腰の真下の足元がルートに合い、小物は体との位置関係を保つ
            Vector3 anchor = new Vector3(hips.position.x, foot.position.y, hips.position.z);
            Assert.That(Vector3.Distance(anchor, root.transform.position), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(prop.position - armature.position, propFromArmature), Is.LessThan(1e-4f));
            Assert.That(moved.sqrMagnitude, Is.GreaterThan(0.1f));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void TryAlign_LeavesACenteredAvatar()
        {
            // 足元がルートにあり、腰がその真上（通常の立ち）
            var root = new GameObject("Avatar");
            Transform hips = Child(root.transform, "Hips", new Vector3(0f, 1f, 0f));
            Transform foot = Child(hips, "Foot", new Vector3(0.02f, -1f, 0f));
            Vector3 hipsBefore = hips.position;

            Assert.That(AvatarOrigin.TryAlign(root.transform, hips, foot, null, out _), Is.False);
            Assert.That(Vector3.Distance(hips.position, hipsBefore), Is.LessThan(1e-4f));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void TryAlign_TurnsASidewaysAvatarToFaceForward()
        {
            // 立っているが、正面がルートの右を向いている（腰の真上に頭、両腕は前後）
            var root = new GameObject("Avatar");
            Transform hips = Child(root.transform, "Hips", new Vector3(0f, 1f, 0f));
            Transform foot = Child(hips, "Foot", new Vector3(0f, -1f, 0f));
            Transform leftArm = Child(hips, "LeftArm", new Vector3(0f, 0f, 0.4f));
            Transform rightArm = Child(hips, "RightArm", new Vector3(0f, 0f, -0.4f));

            Assert.That(AvatarOrigin.TryAlign(
                root.transform, hips, foot, null, leftArm, rightArm, out _), Is.True);

            // 腰は足元の真上、正面（左手から右手の右×上）はルートの前
            Vector3 up = (hips.position - foot.position).normalized;
            Vector3 forward = Vector3.Cross(rightArm.position - leftArm.position, up).normalized;
            Assert.That(Vector3.Dot(up, Vector3.up), Is.GreaterThan(0.99f));
            Assert.That(Vector3.Dot(forward, Vector3.forward), Is.GreaterThan(0.99f));
            Assert.That(foot.position.magnitude, Is.LessThan(1e-3f));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void TryAlign_StandsUpAnAvatarLyingOnItsBack()
        {
            // 仰向け（足元から腰が奥、両腕は左右、胸が上）
            var root = new GameObject("Avatar");
            Transform armature = Child(root.transform, "Armature", Vector3.zero);
            armature.rotation = Quaternion.Euler(90f, 0f, 0f);
            Transform hips = Child(armature, "Hips", new Vector3(0f, 1f, 0f));
            Transform foot = Child(armature, "Foot", Vector3.zero);
            Transform leftArm = Child(armature, "LeftArm", new Vector3(-0.4f, 1f, 0f));
            Transform rightArm = Child(armature, "RightArm", new Vector3(0.4f, 1f, 0f));

            Assert.That(AvatarOrigin.TryAlign(
                root.transform, hips, foot, null, leftArm, rightArm, out _), Is.True);

            Vector3 up = (hips.position - foot.position).normalized;
            Assert.That(Vector3.Dot(up, Vector3.up), Is.GreaterThan(0.99f));
            Assert.That(Mathf.Abs(foot.position.y), Is.LessThan(1e-3f));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void TryAlign_LeavesASlightTurn()
        {
            // 15° だけ横を向いている立ちは、モデルの向きとして残す
            var root = new GameObject("Avatar");
            Transform hips = Child(root.transform, "Hips", new Vector3(0f, 1f, 0f));
            hips.rotation = Quaternion.Euler(0f, 15f, 0f);
            Transform foot = Child(hips, "Foot", new Vector3(0f, -1f, 0f));
            Transform leftArm = Child(hips, "LeftArm", new Vector3(-0.4f, 0f, 0f));
            Transform rightArm = Child(hips, "RightArm", new Vector3(0.4f, 0f, 0f));
            Vector3 hipsBefore = hips.position;

            Assert.That(AvatarOrigin.TryAlign(
                root.transform, hips, foot, null, leftArm, rightArm, out _), Is.False);
            Assert.That(Vector3.Distance(hips.position, hipsBefore), Is.LessThan(1e-4f));
            Object.DestroyImmediate(root);
        }

        private static Transform Child(Transform parent, string name, Vector3 localPosition)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            return child.transform;
        }
    }
}
