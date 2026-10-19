using System;

namespace VRCast.Core
{
    /// <summary>
    /// 表情のキャリブレーションで記録した 1 つの顔（入力元・表情・ARKit 互換の値の平均）。
    /// 顔の画像ではなく、トラッカーが送る顔の動きの数値だけを持つ。
    /// </summary>
    [Serializable]
    public class ExpressionCalibrationSample
    {
        /// <summary>
        /// values の要素数（ArKitFace.BlendShapeNames と同じ並び・数）。
        /// </summary>
        public const int ValueCount = 52;

        // tongueOut 追加前の記録（末尾が無い 51 要素）
        private const int LegacyValueCount = 51;

        // 記録したときの顔の入力元（入力元ごとに値の出方が違うため、同じ入力元のときだけ使う）
        public TrackingSource source;

        // 表情（FaceExpression の値。0 = 真顔）
        public int expression;

        // ARKit 互換の値の平均（0〜1、左右は映像基準）
        public float[] values = Array.Empty<float>();

        /// <summary>
        /// 要素数が合い、すべて 0〜1 の有限な値なら true。
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (values == null || values.Length != ValueCount || expression < 0)
                {
                    return false;
                }

                foreach (float value in values)
                {
                    if (float.IsNaN(value) || value < 0f || value > 1f)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// 古い記録（51 種）なら末尾に tongueOut = 0 を足して現在の要素数にする。
        /// </summary>
        public void MigrateValues()
        {
            if (values == null || values.Length != LegacyValueCount)
            {
                return;
            }

            var padded = new float[ValueCount];
            Array.Copy(values, padded, LegacyValueCount);
            values = padded;
        }
    }
}
