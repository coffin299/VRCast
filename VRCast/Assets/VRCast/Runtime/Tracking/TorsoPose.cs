using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// 両肩の位置から上半身の向き（ひねり = 縦軸まわり、傾き = 前後軸まわり）を求める。
    /// 角度はアバタールート基準の度で、正面を向いて両肩が水平なら 0。
    /// </summary>
    public static class TorsoPose
    {
        /// <summary>
        /// 肩幅がこれ未満（真横を向いた・誤検出）なら向きを求めない（m。MediaPipe の world 座標の単位）。
        /// </summary>
        public const float MinShoulderWidth = 0.1f;

        /// <summary>
        /// 両肩が映っていれば、ひねり（x、正 = アバターの右肩が後ろへ）と傾き（y、正 = アバターの右肩が上がる）を返す。
        /// mirror が true なら本人の右肩をアバターの左肩として扱う（腕と同じ対応）。
        /// </summary>
        public static bool TryGetAngles(BodyTrackingFrame body, bool mirror, out Vector2 angles)
        {
            angles = Vector2.zero;

            // 両肩が映っていなければ求めない
            if (!body.Left.HasShoulder || !body.Right.HasShoulder)
            {
                return false;
            }

            // アバターの左肩 → 右肩の向き（鏡像では本人の右肩がアバターの左肩）をアバタールート基準へ
            Vector3 avatarRight = mirror ? body.Left.Shoulder : body.Right.Shoulder;
            Vector3 avatarLeft = mirror ? body.Right.Shoulder : body.Left.Shoulder;
            Vector3 line = TrackingMath.ToAvatar(avatarRight - avatarLeft, mirror);

            // 肩幅が狭すぎれば向きが定まらない
            if (line.magnitude < MinShoulderWidth)
            {
                return false;
            }

            // 縦軸まわりの回転で +x が (cos, 0, -sin) になる角度と、前後軸まわりの回転で (cos, sin, 0) になる角度
            float horizontal = new Vector2(line.x, line.z).magnitude;
            angles = new Vector2(
                Mathf.Atan2(-line.z, line.x) * Mathf.Rad2Deg,
                Mathf.Atan2(line.y, horizontal) * Mathf.Rad2Deg);
            return true;
        }

        /// <summary>
        /// ひねり・傾き（度）からアバタールート基準の回転を作る。
        /// </summary>
        public static Quaternion ToRotation(Vector2 angles)
        {
            return Quaternion.Euler(0f, angles.x, angles.y);
        }
    }
}
