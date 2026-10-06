using UnityEngine;

namespace VRCast.Animations
{
    /// <summary>
    /// 1 つの BlendShape に「元の値（表情等）と上乗せ値の大きい方」を書き込む。
    /// 自分が最後に書いた値から変わっていれば他の処理（表情切替等）が書き換えたとみなし、それを新しい元の値にする。
    /// まばたき・リップシンク（将来はトラッキング）が表情を壊さずに重ねるために使う。
    /// </summary>
    public sealed class BlendShapeOverlay
    {
        private readonly SkinnedMeshRenderer _renderer;
        private readonly int _index;
        private float _base;
        private float _lastWritten = float.NaN;

        private BlendShapeOverlay(SkinnedMeshRenderer renderer, int index)
        {
            _renderer = renderer;
            _index = index;
        }

        /// <summary>
        /// root からの相対パスと BlendShape 名で対象を探す。空パスはルート自身。
        /// </summary>
        public static bool TryFind(Transform root, string path, string blendShape,
            out SkinnedMeshRenderer renderer, out int index)
        {
            renderer = null;
            index = -1;

            // 名前が無ければ対象なし
            if (string.IsNullOrEmpty(blendShape))
            {
                return false;
            }

            // パスから SkinnedMeshRenderer を取得
            Transform node = string.IsNullOrEmpty(path) ? root : root.Find(path);
            renderer = node != null ? node.GetComponent<SkinnedMeshRenderer>() : null;
            if (renderer == null || renderer.sharedMesh == null)
            {
                return false;
            }

            // BlendShape 名からインデックスを取得
            index = renderer.sharedMesh.GetBlendShapeIndex(blendShape);
            return index >= 0;
        }

        /// <summary>
        /// 対象を探して上乗せ用オブジェクトを作る。見つからなければ null。
        /// </summary>
        public static BlendShapeOverlay Create(Transform root, string path, string blendShape)
        {
            return TryFind(root, path, blendShape, out SkinnedMeshRenderer renderer, out int index)
                ? new BlendShapeOverlay(renderer, index)
                : null;
        }

        /// <summary>
        /// 見つけ済みの BlendShape の上乗せ用オブジェクトを作る。
        /// </summary>
        public static BlendShapeOverlay Create(SkinnedMeshRenderer renderer, int index)
        {
            return new BlendShapeOverlay(renderer, index);
        }

        /// <summary>
        /// 上乗せ値（0〜100）を書き込む。0 なら元の値に戻る。
        /// </summary>
        public void Write(float overlay)
        {
            // 破棄済みなら何もしない
            if (_renderer == null)
            {
                return;
            }

            // 他の処理が書き換えていれば、その値を元の値として採用
            float current = _renderer.GetBlendShapeWeight(_index);
            if (!Mathf.Approximately(current, _lastWritten))
            {
                _base = current;
            }

            // 元の値と上乗せ値の大きい方を書き込む
            float weight = Mathf.Max(_base, Mathf.Clamp(overlay, 0f, 100f));
            _renderer.SetBlendShapeWeight(_index, weight);
            _lastWritten = weight;
        }
    }
}
