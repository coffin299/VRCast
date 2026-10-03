using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// Provider 共通のフェイストラッキング 1 フレーム分の値。
    /// HeadRotation は Unity 座標系（カメラ基準、正面の基準は Provider 依存）で、Driver がキャリブレーションで正面を決める。
    /// </summary>
    public struct FaceTrackingFrame
    {
        public Quaternion HeadRotation;

        // 目の開き（0 = 閉じ、1 = 開き）。本人から見た左右
        public float EyeOpenLeft;
        public float EyeOpenRight;

        // 口の開き（0 = 閉じ、1 = 最大）
        public float MouthOpen;
    }
}
