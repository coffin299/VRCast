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

        // 頭の位置（Unity 座標系、カメラ基準。単位は Provider 依存のため Driver は正面位置からの差分だけを使う）
        public Vector3 HeadPosition;

        // 目の開き（0 = 閉じ、1 = 開き）。本人から見た左右
        public float EyeOpenLeft;
        public float EyeOpenRight;

        // 口の開き（0 = 閉じ、1 = 最大）
        public float MouthOpen;

        // 視線（顔に対する目の向き。x = 左右、y = 上下の角度（度）、正面の基準は Provider 依存）と、その有無
        public Vector2 Gaze;
        public bool HasGaze;
    }
}
