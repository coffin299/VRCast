using UnityEngine;

namespace VRCast.Avatars
{
    /// <summary>
    /// 読み込んだアバターの体がルートから大きくずれているとき、子をまとめて動かして足元と向きをルートに合わせる。
    /// 上下は足元から腰、前後は両腕の線で決める。少しのずれはモデルの誤差として残す。
    /// </summary>
    public static class AvatarOrigin
    {
        // 直す位置のずれの下限（脚の長さに対する割合と、それ未満にはしないメートル）
        private const float OffsetRatio = 0.15f;
        private const float MinOffset = 0.05f;

        // これ未満の傾き・向きのずれは直さない（度）
        private const float MinAngle = 25f;

        /// <summary>
        /// Humanoid の足元と向きがルートから離れていれば、子をまとめて合わせる。合わせたら true。
        /// </summary>
        public static bool TryAlign(Animator animator, out Vector3 moved)
        {
            if (animator == null || !animator.isHuman)
            {
                moved = Vector3.zero;
                return false;
            }

            return TryAlign(
                animator.transform,
                animator.GetBoneTransform(HumanBodyBones.Hips),
                animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                animator.GetBoneTransform(HumanBodyBones.RightFoot),
                animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
                animator.GetBoneTransform(HumanBodyBones.RightUpperArm),
                out moved);
        }

        /// <summary>
        /// 腰の真下の足元と、体の上下・前後がルートから離れていれば、ルートの子をまとめて合わせる。合わせたら true。
        /// 腕が無いときは上下だけ合わせ、前後はそのままにする。
        /// </summary>
        public static bool TryAlign(
            Transform root, Transform hips, Transform leftFoot, Transform rightFoot, out Vector3 moved)
        {
            return TryAlign(root, hips, leftFoot, rightFoot, null, null, out moved);
        }

        /// <summary>
        /// 腰の真下の足元と、体の上下・前後がルートから離れていれば、ルートの子をまとめて合わせる。合わせたら true。
        /// </summary>
        public static bool TryAlign(
            Transform root, Transform hips, Transform leftFoot, Transform rightFoot,
            Transform leftArm, Transform rightArm, out Vector3 moved)
        {
            moved = Vector3.zero;

            // 腰がルートの外（またはルート自身）なら、子を動かしても体は揃わない
            if (root == null || hips == null || hips == root || !hips.IsChildOf(root))
            {
                return false;
            }

            // 先に向きを起こす（寝たまま位置だけ合わせると、横倒しのまま足元が原点に来る）
            bool rotated = TryUpright(root, hips, leftFoot, rightFoot, leftArm, rightArm);
            bool shifted = TryShift(root, hips, leftFoot, rightFoot, out moved);
            return rotated || shifted;
        }

        private static bool TryUpright(
            Transform root, Transform hips, Transform leftFoot, Transform rightFoot, Transform leftArm, Transform rightArm)
        {
            // 体の上は足元から腰。足が無ければ合わせない（どちらが上か決められない）
            if (!TryBodyUp(root, hips, leftFoot, rightFoot, out Vector3 up))
            {
                return false;
            }

            Quaternion from;
            if (TryBodyForward(root, leftArm, rightArm, up, out Vector3 forward))
            {
                from = Quaternion.LookRotation(forward, up);
            }
            else if (Vector3.Angle(up, root.up) >= MinAngle)
            {
                // 腕が無いときは上下だけ。前後はルートの正面のうち、体の上に垂直な成分を残す
                Vector3 flatForward = Vector3.ProjectOnPlane(root.forward, up);
                if (flatForward.sqrMagnitude < 1e-8f)
                {
                    return false;
                }

                from = Quaternion.LookRotation(flatForward, up);
            }
            else
            {
                return false;
            }

            // 少しの傾き・斜め向きは残す
            if (Quaternion.Angle(from, root.rotation) < MinAngle)
            {
                return false;
            }

            // 体の上と前を、ルートの上と前へ。子はルートの位置を中心にまとめて回す
            Quaternion delta = root.rotation * Quaternion.Inverse(from);
            Vector3 pivot = root.position;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                child.position = pivot + delta * (child.position - pivot);
                child.rotation = delta * child.rotation;
            }

            return true;
        }

        private static bool TryShift(Transform root, Transform hips, Transform leftFoot, Transform rightFoot, out Vector3 moved)
        {
            moved = Vector3.zero;

            // 足元は両足の低い方（ルートの上方向）。足のボーンが無いときは高さを変えない
            bool hasFoot = false;
            float feetHeight = 0f;
            if (IsUnder(root, leftFoot))
            {
                feetHeight = root.InverseTransformPoint(leftFoot.position).y;
                hasFoot = true;
            }

            if (IsUnder(root, rightFoot))
            {
                float height = root.InverseTransformPoint(rightFoot.position).y;
                feetHeight = hasFoot ? Mathf.Min(feetHeight, height) : height;
                hasFoot = true;
            }

            Vector3 hipsLocal = root.InverseTransformPoint(hips.position);

            // 足が無いときは腰の高さを保ったまま、水平だけ合わせる
            float anchorHeight = hasFoot ? feetHeight : hipsLocal.y;
            var anchor = new Vector3(hipsLocal.x, anchorHeight, hipsLocal.z);
            Vector3 delta = root.position - root.TransformPoint(anchor);
            if (!hasFoot)
            {
                delta = Vector3.ProjectOnPlane(delta, root.up);
            }

            // 脚の長さの 15%（最低 5cm）未満は直さない
            float leg = hasFoot ? Mathf.Abs(hipsLocal.y - feetHeight) : 1f;
            if (delta.magnitude < Mathf.Max(MinOffset, leg * OffsetRatio))
            {
                return false;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                root.GetChild(i).position += delta;
            }

            moved = delta;
            return true;
        }

        private static bool TryBodyUp(
            Transform root, Transform hips, Transform leftFoot, Transform rightFoot, out Vector3 up)
        {
            // 両足の中点から腰。片方しか無ければその足から
            Vector3 feet = Vector3.zero;
            int count = 0;
            if (IsUnder(root, leftFoot))
            {
                feet += leftFoot.position;
                count++;
            }

            if (IsUnder(root, rightFoot))
            {
                feet += rightFoot.position;
                count++;
            }

            if (count == 0)
            {
                up = Vector3.zero;
                return false;
            }

            up = hips.position - feet / count;
            return up.sqrMagnitude > 1e-8f;
        }

        private static bool TryBodyForward(Transform root, Transform leftArm, Transform rightArm, Vector3 up, out Vector3 forward)
        {
            forward = Vector3.zero;
            if (!IsUnder(root, leftArm) || !IsUnder(root, rightArm))
            {
                return false;
            }

            // 左手から右手を、体の上に垂直な成分だけ残す（気を付けでも左右が残る）
            Vector3 right = Vector3.ProjectOnPlane(rightArm.position - leftArm.position, up);
            if (right.sqrMagnitude < 1e-8f)
            {
                return false;
            }

            // 右 × 上 = 前（ルートと同じ、右が +x で上が +y なら前は +z）
            forward = Vector3.Cross(right, up);
            return forward.sqrMagnitude > 1e-8f;
        }

        private static bool IsUnder(Transform root, Transform foot)
        {
            return foot != null && foot != root && foot.IsChildOf(root);
        }
    }
}
