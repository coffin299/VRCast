using System;

namespace VRCast.Core
{
    /// <summary>
    /// アバターごとに覚える 1 つの BlendShape の上限（まばたきで目が消える等、100 まで動かすと破綻する形の対策）。
    /// path はアバターのルートからの相対パス（空ならルート自身）。
    /// </summary>
    [Serializable]
    public class BlendShapeLimit
    {
        // 上限の範囲（100 = 制限なし）
        public const float MinWeight = 0f;
        public const float MaxWeight = 100f;

        public string path = string.Empty;
        public string blendShape = string.Empty;
        public float max = MaxWeight;

        /// <summary>
        /// 保存できる値なら true（名前があり、上限が有限値）。
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(blendShape) && !float.IsNaN(max) && !float.IsInfinity(max);
    }
}
