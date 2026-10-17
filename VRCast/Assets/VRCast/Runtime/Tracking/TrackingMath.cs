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
        /// 目標へ寄せる 1 フレームの割合（0〜1）。目標との差 error が小さい間は slowRate（1 秒あたり）で細かい揺れを抑え、
        /// fullError 以上なら fastRate で追う（速く動いたときに遅れない）。
        /// </summary>
        public static float FollowBlend(float error, float fullError, float slowRate, float fastRate, float deltaTime)
        {
            float t = fullError > 0f ? Mathf.Clamp01(error / fullError) : 1f;
            float rate = Mathf.Lerp(slowRate, fastRate, t);
            return 1f - Mathf.Exp(-rate * Mathf.Max(0f, deltaTime));
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
        /// 傾き補正を掛けてからアバタールート基準へ変換する（補正が identity なら ToAvatar と同じ）。
        /// </summary>
        public static Vector3 ToAvatar(Vector3 cameraVector, bool mirror, Quaternion tiltCorrection)
        {
            return ToAvatar(tiltCorrection * cameraVector, mirror);
        }

        /// <summary>
        /// 傾き補正後の頭の向き（補正が identity ならそのまま）。
        /// </summary>
        public static Quaternion CorrectHeadRotation(Quaternion headRotation, Quaternion tiltCorrection)
        {
            return tiltCorrection * headRotation;
        }

        /// <summary>
        /// 回転を左右反転する（X 軸まわりはそのまま、Y・Z 軸まわりを逆向き）。
        /// </summary>
        public static Quaternion MirrorRotation(Quaternion rotation)
        {
            return new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w);
        }

        /// <summary>
        /// 目の閉じ具合（0 = 開き、1 = 閉じ）のうち offset 以下を開いているとみなし、残りを 0〜1 へ広げ直す
        /// （普段から目が細く映る人でも半目にせず、閉じ切ったまばたきは閉じたままにする）。
        /// </summary>
        public static float OpenEyes(float closed, float offset)
        {
            return Mathf.InverseLerp(offset, 1f, closed);
        }
    }
}
