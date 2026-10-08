using System;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// metadata/blendshape_sync.json の内容。Modular Avatar の Blendshape Sync（別メッシュの BlendShape を
    /// 同期元に合わせる設定）の一覧。MA はアニメーションにしか同期を書き足さないため、
    /// まばたき・口パク・トラッキングのように直接書き込む値も Runtime が毎フレーム写して同期する。
    /// </summary>
    [Serializable]
    public class BlendShapeSyncSet : IMetadata
    {
        // 信頼できない入力に対する上限
        public const int MaxBindings = 4096;

        public BlendShapeSyncBinding[] bindings = Array.Empty<BlendShapeSyncBinding>();

        /// <summary>
        /// 内容を検証し、問題があればエラーメッセージを、無ければ null を返す。
        /// </summary>
        public string Validate()
        {
            // 配列欠落と件数上限
            if (bindings == null || bindings.Length > MaxBindings)
            {
                return $"bindings must be 0-{MaxBindings} items.";
            }

            foreach (BlendShapeSyncBinding binding in bindings)
            {
                // パスは空（ルート）を許可、BlendShape 名は必須
                if (binding == null
                    || !ExpressionSet.IsValidString(binding.sourcePath, true)
                    || !ExpressionSet.IsValidString(binding.sourceBlendShape, false)
                    || !ExpressionSet.IsValidString(binding.targetPath, true)
                    || !ExpressionSet.IsValidString(binding.targetBlendShape, false))
                {
                    return "A blend shape sync binding is invalid.";
                }
            }

            return null;
        }
    }

    /// <summary>
    /// 1 つの同期（同期元のメッシュ・BlendShape → 同期先のメッシュ・BlendShape）。パスはアバタールートからの相対パス。
    /// </summary>
    [Serializable]
    public class BlendShapeSyncBinding
    {
        public string sourcePath = string.Empty;
        public string sourceBlendShape = string.Empty;
        public string targetPath = string.Empty;
        public string targetBlendShape = string.Empty;
    }
}
