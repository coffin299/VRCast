using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// 全身モード（AppSettings.trackingPlantFeet）で、足を読込時（待機ポーズ適用後）の位置・向きに固定する。
    /// FaceTrackingDriver が腰を傾け・動かした後に、太もも・すね・足の 3 本で 2 関節の IK を解き、膝の曲げで吸収する。
    /// 足の位置はアバタールート基準で覚えるため、体の向き（Body yaw）を変えると一緒に回る。
    /// </summary>
    [DefaultExecutionOrder(-98)]
    public class FootPlanter : MonoBehaviour
    {
        // 脚を伸ばし切らない割合（伸び切ると膝の向きが定まらず震えるため、わずかに曲げを残す）
        private const float MaxReach = 0.999f;

        // 読込時の脚がほぼまっすぐとみなす、膝の曲げ方向の長さ（脚の長さに対する割合）
        private const float StraightLegRatio = 0.01f;

        private AppSettings _settings;
        private Leg _left;
        private Leg _right;

        // 前フレームに脚を動かしたか（OFF にしたとき一度だけ読込時の姿勢へ戻す）
        private bool _applied;

        /// <summary>
        /// 1 本の脚のボーンと、読込時の回転・足の位置と向き（アバタールート基準）。
        /// </summary>
        private sealed class Leg
        {
            public Transform Upper;
            public Transform Lower;
            public Transform Foot;
            public Quaternion UpperRest;
            public Quaternion LowerRest;
            public Quaternion FootRest;
            public Vector3 FootPosition;
            public Quaternion FootRotation;
        }

        public void Initialize(Animator animator, AppSettings settings)
        {
            _settings = settings;

            // Humanoid で脚のボーンがそろっているときだけ動かす
            if (animator == null || !animator.isHuman)
            {
                return;
            }

            _left = CreateLeg(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            _right = CreateLeg(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
        }

        private Leg CreateLeg(Animator animator, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones foot)
        {
            var leg = new Leg
            {
                Upper = animator.GetBoneTransform(upper),
                Lower = animator.GetBoneTransform(lower),
                Foot = animator.GetBoneTransform(foot),
            };
            if (leg.Upper == null || leg.Lower == null || leg.Foot == null)
            {
                return null;
            }

            // 読込時の回転と、固定する足の位置・向き（アバタールート基準）
            leg.UpperRest = leg.Upper.localRotation;
            leg.LowerRest = leg.Lower.localRotation;
            leg.FootRest = leg.Foot.localRotation;
            leg.FootPosition = transform.InverseTransformPoint(leg.Foot.position);
            leg.FootRotation = Quaternion.Inverse(transform.rotation) * leg.Foot.rotation;
            return leg;
        }

        private void LateUpdate()
        {
            // 未初期化・脚が無ければ何もしない
            if (_settings == null || (_left == null && _right == null))
            {
                return;
            }

            // OFF の間は触らない（ON から切り替えた直後だけ読込時の姿勢へ戻す）
            if (!_settings.trackingPlantFeet)
            {
                if (_applied)
                {
                    Restore(_left);
                    Restore(_right);
                    _applied = false;
                }

                return;
            }

            _applied = true;
            Plant(_left);
            Plant(_right);
        }

        private static void Restore(Leg leg)
        {
            // 脚が無い側は無視
            if (leg == null)
            {
                return;
            }

            leg.Upper.localRotation = leg.UpperRest;
            leg.Lower.localRotation = leg.LowerRest;
            leg.Foot.localRotation = leg.FootRest;
        }

        private void Plant(Leg leg)
        {
            // 脚が無い側は無視
            if (leg == null)
            {
                return;
            }

            // 前フレームの結果を残さないよう読込時の形へ戻してから、足を覚えた位置・向きへ合わせる
            Restore(leg);
            Vector3 target = transform.TransformPoint(leg.FootPosition);
            Solve(leg.Upper, leg.Lower, leg.Foot, target, transform.forward);
            leg.Foot.rotation = transform.rotation * leg.FootRotation;
        }

        /// <summary>
        /// 2 関節の IK。end（足）が target に届くよう upper（太もも）と lower（すね）を回す。
        /// 膝は今の曲げ方向へ曲げ、まっすぐなら bendHint（前方）へ曲げる。届かない距離は伸ばし切る手前まで。
        /// </summary>
        public static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 bendHint)
        {
            Vector3 a = upper.position;
            Vector3 b = lower.position;
            Vector3 c = end.position;

            // 骨の長さと、根元から目標までの距離（届く範囲に収める）
            float upperLength = Vector3.Distance(a, b);
            float lowerLength = Vector3.Distance(b, c);
            float reach = Mathf.Clamp(Vector3.Distance(a, target),
                Mathf.Abs(upperLength - lowerLength) + 1e-4f, (upperLength + lowerLength) * MaxReach);
            if (upperLength < 1e-5f || lowerLength < 1e-5f)
            {
                return;
            }

            // 曲げる平面の軸（今の膝の曲げ方向。脚がまっすぐなら bendHint の向きへ曲げる）
            Vector3 toEnd = c - a;
            Vector3 bend = Vector3.ProjectOnPlane(b - a, toEnd);
            if (bend.magnitude < (upperLength + lowerLength) * StraightLegRatio)
            {
                bend = Vector3.ProjectOnPlane(bendHint, toEnd);
            }

            Vector3 axis = Vector3.Cross(toEnd, bend);
            if (axis.sqrMagnitude < 1e-10f)
            {
                return;
            }

            axis.Normalize();

            // 今と目標の、根元の角度（根元→足 と 根元→膝）と膝の角度（膝→根元 と 膝→足）
            float rootNow = Angle(c - a, b - a);
            float kneeNow = Angle(a - b, c - b);
            float rootGoal = LawOfCosines(upperLength, reach, lowerLength);
            float kneeGoal = LawOfCosines(upperLength, lowerLength, reach);

            // 膝の角度を合わせて根元から足までの距離を reach にし、根元の角度で膝の位置を保つ
            upper.rotation = Quaternion.AngleAxis(rootGoal - rootNow, axis) * upper.rotation;
            lower.rotation = Quaternion.AngleAxis(kneeGoal - kneeNow, axis) * lower.rotation;

            // 最後に根元を回して足を目標の方向へ向ける
            upper.rotation = Quaternion.FromToRotation(end.position - a, target - a) * upper.rotation;
        }

        private static float Angle(Vector3 from, Vector3 to)
        {
            // 2 つの向きのなす角（度）
            return Vector3.Angle(from, to);
        }

        private static float LawOfCosines(float adjacentA, float adjacentB, float opposite)
        {
            // 辺 adjacentA・adjacentB に挟まれた角（度）。誤差で範囲外になった値は丸める
            float cos = (adjacentA * adjacentA + adjacentB * adjacentB - opposite * opposite) / (2f * adjacentA * adjacentB);
            return Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }
}
