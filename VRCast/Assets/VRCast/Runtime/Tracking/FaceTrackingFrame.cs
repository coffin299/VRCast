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

        // ARKit 互換の BlendShape の値（MediaPipePacket.BlendShapeNames 順、0〜1、左右は映像基準）。
        // パーフェクトシンク用。MediaPipe のみで、無ければ null（書き換えない）
        public float[] BlendShapes;
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
        public float Wink;
        public float Squint;
        public float Pout;

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
                case FaceExpression.Wink:
                    return Wink;
                case FaceExpression.Squint:
                    return Squint;
                case FaceExpression.Pout:
                    return Pout;
                default:
                    return 0f;
            }
        }
    }

    /// <summary>
    /// 検出する表情の種類（Neutral 以外は ExpressionScores の項目と対応。設定・判定で数値を使うため並びを変えない）。
    /// </summary>
    public enum FaceExpression
    {
        Neutral = 0,
        Smile = 1,
        Surprise = 2,
        Angry = 3,
        Sad = 4,
        Wink = 5,
        Squint = 6,
        Pout = 7,
    }

    /// <summary>
    /// ニュートラル以外の表情の範囲と、表情の組を表すビット列。
    /// </summary>
    public static class FaceExpressions
    {
        public const FaceExpression First = FaceExpression.Smile;
        public const FaceExpression Last = FaceExpression.Pout;

        /// <summary>
        /// すべての表情を含むビット列。
        /// </summary>
        public const int All = ~0;

        /// <summary>
        /// ビット列に表情が含まれていれば true。
        /// </summary>
        public static bool Contains(int mask, FaceExpression expression)
        {
            return (mask & Bit(expression)) != 0;
        }

        /// <summary>
        /// 表情 1 つ分のビット。
        /// </summary>
        public static int Bit(FaceExpression expression)
        {
            return 1 << (int)expression;
        }
    }
}
