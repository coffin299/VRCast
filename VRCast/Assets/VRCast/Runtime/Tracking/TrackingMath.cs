using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// パケット解析・Driver で共通の数値検査と座標変換。
    /// </summary>
    internal static class TrackingMath
    {
        /// <summary>
        /// すべて有限値（NaN・無限大を含まない）なら true。
        /// </summary>
        public static bool AllFinite(params float[] values)
        {
            foreach (float value in values)
            {
                // 1 つでも非有限なら不正
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// カメラ基準のベクトルをアバタールート基準へ変換する。
        /// 通常はカメラの方を向いたアバター（180° 回転）、鏡像は左右反転のみ。
        /// </summary>
        public static Vector3 ToAvatar(Vector3 cameraVector, bool mirror)
        {
            return mirror
                ? new Vector3(cameraVector.x, cameraVector.y, -cameraVector.z)
                : new Vector3(-cameraVector.x, cameraVector.y, -cameraVector.z);
        }

        /// <summary>
        /// 回転を左右反転する（X 軸まわりはそのまま、Y・Z 軸まわりを逆向き）。
        /// </summary>
        public static Quaternion MirrorRotation(Quaternion rotation)
        {
            return new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w);
        }
    }
}
