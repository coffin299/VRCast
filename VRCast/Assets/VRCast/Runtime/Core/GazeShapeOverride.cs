using System;

namespace VRCast.Core
{
    /// <summary>
    /// アバターごとに覚える、目の動き（視線）に使う BlendShape の指定。
    /// enabled が false なら名前から自動で探す。上・下・左・右はアバターから見た方向（未指定の方向は自動のまま）。
    /// </summary>
    [Serializable]
    public class GazeShapeOverride
    {
        public bool enabled;
        public BlendShapeRef up = new BlendShapeRef();
        public BlendShapeRef down = new BlendShapeRef();
        public BlendShapeRef left = new BlendShapeRef();
        public BlendShapeRef right = new BlendShapeRef();

        /// <summary>
        /// どの方向も指定していなければ true。
        /// </summary>
        public bool IsEmpty => !up.IsSet && !down.IsSet && !left.IsSet && !right.IsSet;

        public GazeShapeOverride Clone()
        {
            return new GazeShapeOverride
            {
                enabled = enabled,
                up = up.Clone(),
                down = down.Clone(),
                left = left.Clone(),
                right = right.Clone(),
            };
        }

        /// <summary>
        /// 読み込んだ値の欠け（旧版の設定・手編集の null）を未指定で補う。
        /// </summary>
        public void Sanitize()
        {
            up = Normalize(up);
            down = Normalize(down);
            left = Normalize(left);
            right = Normalize(right);
        }

        private static BlendShapeRef Normalize(BlendShapeRef value)
        {
            value ??= new BlendShapeRef();
            value.path ??= string.Empty;
            value.blendShape ??= string.Empty;
            return value;
        }
    }
}
