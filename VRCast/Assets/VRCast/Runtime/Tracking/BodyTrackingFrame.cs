using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// 片腕分のトラッキング値。座標はすべてカメラ基準の Unity 座標系（x = 映像の右、y = 上、z = カメラから遠ざかる向き）。
    /// 位置の原点・単位は Provider 依存のため、Driver は点同士の差（方向）だけを使う。
    /// </summary>
    public struct ArmTrackingData
    {
        // 肩・肘・手首が映っていて腕の向きを使えるなら true
        public bool HasArm;

        // 肩が映っていて位置を使えるなら true（両肩がそろえば上半身の向きに使う。肘・手首が隠れていてもよい）
        public bool HasShoulder;
        public Vector3 Shoulder;
        public Vector3 Elbow;
        public Vector3 Wrist;

        // 手の 21 点（MediaPipe Hand Landmarker の番号順）。手が映っていなければ null
        public Vector3[] Hand;

        public bool HasHand => Hand != null;
    }

    /// <summary>
    /// Provider 共通の腕・手のトラッキング 1 フレーム分の値。左右は本人から見た左右。
    /// </summary>
    public struct BodyTrackingFrame
    {
        public ArmTrackingData Left;
        public ArmTrackingData Right;
    }
}
