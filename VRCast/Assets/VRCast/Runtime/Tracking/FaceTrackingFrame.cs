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

        // 表情の強さ（0〜1）と、その有無（MediaPipe のみ。OpenSeeFace は常に無し）
        public ExpressionScores Expression;
        public bool HasExpression;
    }

    /// <summary>
    /// 表情ごとの強さ（0〜1）。Provider が顔の動きから合成する。
    /// </summary>
    public struct ExpressionScores
    {
        public float Smile;
        public float Surprise;
        public float Angry;
        public float Sad;

        /// <summary>
        /// 表情の種類で値を取り出す（ニュートラルは 0）。
        /// </summary>
        public float Get(FaceExpression expression)
        {
            // 種類ごとに対応する値を返す
            switch (expression)
            {
                case FaceExpression.Smile:
                    return Smile;
                case FaceExpression.Surprise:
                    return Surprise;
                case FaceExpression.Angry:
                    return Angry;
                case FaceExpression.Sad:
                    return Sad;
                default:
                    return 0f;
            }
        }
    }

    /// <summary>
    /// 検出する表情の種類（Neutral 以外は ExpressionScores の項目と対応）。
    /// </summary>
    public enum FaceExpression
    {
        Neutral = 0,
        Smile = 1,
        Surprise = 2,
        Angry = 3,
        Sad = 4,
    }
}
