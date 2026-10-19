using System.Collections.Generic;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// 表情のキャリブレーション: 本人が登録した真顔と各表情の ARKit 互換の値から、表情の強さ（0〜1）を求める。
    /// 強さは「真顔 → 登録した表情」の向きへ今の顔がどれだけ進んだか（登録した顔で 1）で、
    /// その向きから外れている分（別の表情・話している口など）だけ弱める。
    /// 登録が無い表情（と真顔が未登録の入力元）は、組み込みの判定の値をそのまま使う。
    /// </summary>
    public static class ExpressionCalibration
    {
        // 真顔と表情の差がこれ未満（値の差の二乗和）なら、区別できないとみなして使わない
        public const float MinSeparation = 0.01f;

        // 比べる値の位置（視線の eyeLook* は、よそ見で表情が変わらないよう除く）
        private static readonly int[] Features = BuildFeatures();

        // 左右を入れ替えた値の作業用（ウインクを反対の目でしても同じ表情とみなすため）
        private static readonly float[] Swapped = new float[ArKitFace.BlendShapeNames.Length];

        /// <summary>
        /// 入力元 source の、表情 expression（Neutral = 真顔）の記録（無ければ null）。
        /// </summary>
        public static ExpressionCalibrationSample Find(
            IReadOnlyList<ExpressionCalibrationSample> samples, TrackingSource source, FaceExpression expression)
        {
            if (samples == null)
            {
                return null;
            }

            for (int i = 0; i < samples.Count; i++)
            {
                ExpressionCalibrationSample sample = samples[i];
                if (sample != null && sample.source == source && sample.expression == (int)expression && sample.IsValid)
                {
                    return sample;
                }
            }

            return null;
        }

        /// <summary>
        /// 記録を追加する（同じ入力元・表情の記録は置き換える）。
        /// </summary>
        public static void Set(List<ExpressionCalibrationSample> samples, TrackingSource source, FaceExpression expression,
            float[] values)
        {
            Remove(samples, source, expression);
            samples.Add(new ExpressionCalibrationSample
            {
                source = source,
                expression = (int)expression,
                values = (float[])values.Clone(),
            });
        }

        /// <summary>
        /// 記録を消す（真顔を消しても表情の記録は残すが、真顔を登録し直すまで使わない）。
        /// </summary>
        public static void Remove(List<ExpressionCalibrationSample> samples, TrackingSource source,
            FaceExpression expression)
        {
            samples.RemoveAll(sample => sample == null
                || (sample.source == source && sample.expression == (int)expression));
        }

        /// <summary>
        /// 入力元 source の記録をすべて消す。
        /// </summary>
        public static void RemoveAll(List<ExpressionCalibrationSample> samples, TrackingSource source)
        {
            samples.RemoveAll(sample => sample == null || sample.source == source);
        }

        /// <summary>
        /// 真顔と表情の値が、判定に使えるだけ違うなら true。
        /// </summary>
        public static bool IsDistinct(float[] neutral, float[] target)
        {
            float distance = 0f;
            foreach (int f in Features)
            {
                float d = target[f] - neutral[f];
                distance += d * d;
            }

            return distance >= MinSeparation;
        }

        /// <summary>
        /// 登録のある表情の強さを、記録との比較で求めた値に置き換えて返す（真顔が未登録・値が無いならそのまま）。
        /// </summary>
        public static ExpressionScores Apply(ExpressionScores scores, float[] current,
            IReadOnlyList<ExpressionCalibrationSample> samples, TrackingSource source)
        {
            // 値の無い入力元・真顔の記録が無い入力元は組み込みの判定のまま
            if (current == null || current.Length != ArKitFace.BlendShapeNames.Length)
            {
                return scores;
            }

            ExpressionCalibrationSample neutral = Find(samples, source, FaceExpression.Neutral);
            if (neutral == null)
            {
                return scores;
            }

            for (var expression = FaceExpressions.First; expression <= FaceExpressions.Last; expression++)
            {
                ExpressionCalibrationSample target = Find(samples, source, expression);
                if (target == null)
                {
                    continue;
                }

                // ウインクは反対の目でも同じ表情とみなし、左右を入れ替えた顔とも比べて強い方を使う
                float score = Score(current, neutral.values, target.values);
                if (expression == FaceExpression.Wink)
                {
                    score = Mathf.Max(score, Score(Swap(current), neutral.values, target.values));
                }

                scores.Set(expression, score);
            }

            return scores;
        }

        /// <summary>
        /// 今の顔 current が、真顔 neutral から表情 target へ向かってどれだけ進んでいるか（0〜1）。
        /// 向きから外れている分（外れ具合 = 外れた距離 / 表情までの距離）で 1 / (1 + 外れ具合²) 倍に弱める。
        /// </summary>
        public static float Score(float[] current, float[] neutral, float[] target)
        {
            // 表情の向き（真顔からの差）への射影
            float along = 0f;
            float length = 0f;
            foreach (int f in Features)
            {
                float d = target[f] - neutral[f];
                along += (current[f] - neutral[f]) * d;
                length += d * d;
            }

            // 真顔と区別できない記録は使わない
            if (length < MinSeparation)
            {
                return 0f;
            }

            float progress = Mathf.Clamp01(along / length);

            // 進んだ位置から今の顔までの距離（向きから外れた分）
            float off = 0f;
            foreach (int f in Features)
            {
                float r = current[f] - neutral[f] - progress * (target[f] - neutral[f]);
                off += r * r;
            }

            float ratio = off / length;
            return progress / (1f + ratio);
        }

        private static float[] Swap(float[] values)
        {
            // 左右の付く名前を相手の位置へ（左右の無い名前はそのまま）
            for (int i = 0; i < values.Length; i++)
            {
                Swapped[PerfectSyncBlendShapes.SourceIndex(i, false)] = values[i];
            }

            return Swapped;
        }

        private static int[] BuildFeatures()
        {
            // 視線（eyeLook*）と舌出しは、表情の判定を揺らさないよう除く
            var features = new List<int>();
            string[] names = ArKitFace.BlendShapeNames;
            for (int i = 0; i < names.Length; i++)
            {
                if (!names[i].StartsWith("eyeLook", System.StringComparison.Ordinal)
                    && names[i] != "tongueOut")
                {
                    features.Add(i);
                }
            }

            return features.ToArray();
        }
    }
}
