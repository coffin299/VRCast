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
    /// Provider 共通の腕・手・脚のトラッキング 1 フレーム分の値。左右は本人から見た左右。
    /// </summary>
    public struct BodyTrackingFrame
    {
        public ArmTrackingData Left;
        public ArmTrackingData Right;

        // 脚（腰・膝・足首・足先・かかと。古いトラッカーは HasLeg が false。かかとの無い送信は HasHeel が false）
        public LegTrackingData LeftLeg;
        public LegTrackingData RightLeg;

        // 体の推定での顔の位置（両目の中点、映っていなければ鼻。腕と同じ座標系）。古いトラッカー・顔が映っていなければ false
        public bool HasFace;
        public Vector3 Face;

        // 受信するたびに増える番号（新しい値が届いたかの判定用）
        public int Sequence;

        // 映像上の腰の位置（部屋を歩き回る。古いトラッカーは false）
        public bool HasRoot;
        public float RootImageX;
        public float RootImageWidth;
        public float RootWorldWidth;
    }

    /// <summary>
    /// 片脚分のトラッキング値。座標は腕と同じカメラ基準の Unity 座標系。
    /// 位置の原点は腰の中心なので、Driver は点同士の差（方向）だけを使う。
    /// </summary>
    public struct LegTrackingData
    {
        // 腰・膝・足首が映っていて脚の曲げを使えるなら true
        public bool HasLeg;

        // 足先が映っているなら true
        public bool HasFoot;

        // かかとが映っているなら true（古いトラッカーは false）
        public bool HasHeel;

        public Vector3 Hip;
        public Vector3 Knee;
        public Vector3 Ankle;
        public Vector3 Foot;
        public Vector3 Heel;
    }
}
