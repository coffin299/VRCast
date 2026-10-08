using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// Modular Avatar の Blendshape Sync の再現。同期先の BlendShape を毎フレーム同期元の値に合わせる
    /// （口に付いたチェーン・ピアスなど、顔とは別のメッシュを口パク・まばたき・トラッキング・表情に追従させる）。
    /// まばたき・口パク・表情などの書き込みの後、上限（BlendShapeLimiter）で切る前に動かす。
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder)]
    public sealed class BlendShapeSync : MonoBehaviour
    {
        // 顔の BlendShape を書き込む処理（既定の順序・トラッキング）より後、BlendShapeLimiter（10000）より前
        private const int ExecutionOrder = 9000;

        // ログのカテゴリ名
        private const string LogCategory = "BlendShapeSync";

        private struct Link
        {
            public SkinnedMeshRenderer Source;
            public int SourceIndex;
            public SkinnedMeshRenderer Target;
            public int TargetIndex;
        }

        private readonly List<Link> _links = new List<Link>();

        /// <summary>
        /// 解決できた同期の数。
        /// </summary>
        public int Count => _links.Count;

        /// <summary>
        /// 同期の一覧をアバター内のメッシュへ解決する（見つからないメッシュ・BlendShape は無視）。
        /// </summary>
        public void Initialize(Transform root, BlendShapeSyncSet set)
        {
            int unresolved = 0;
            foreach (BlendShapeSyncBinding binding in set.bindings)
            {
                // 同期元・同期先の両方が見つかったものだけ使う
                if (BlendShapeOverlay.TryFind(root, binding.sourcePath, binding.sourceBlendShape,
                        out SkinnedMeshRenderer source, out int sourceIndex)
                    && BlendShapeOverlay.TryFind(root, binding.targetPath, binding.targetBlendShape,
                        out SkinnedMeshRenderer target, out int targetIndex))
                {
                    _links.Add(new Link
                    {
                        Source = source, SourceIndex = sourceIndex, Target = target, TargetIndex = targetIndex,
                    });
                }
                else
                {
                    unresolved++;
                }
            }

            // 解決できなかった数（メッシュの統合等で消えたもの）を調査用に残す
            if (_links.Count > 0 || unresolved > 0)
            {
                VRCastLog.Info(LogCategory, $"Synced {_links.Count} blend shapes (skipped {unresolved}).");
            }
        }

        /// <summary>
        /// 同期先を同期元の値に合わせる（毎フレームの LateUpdate から呼ぶ。テストからも呼べる）。
        /// </summary>
        public void Apply()
        {
            foreach (Link link in _links)
            {
                // 破棄済みのメッシュは飛ばす
                if (link.Source == null || link.Target == null)
                {
                    continue;
                }

                // 同期先の上限を通し、変わったときだけ書く（毎フレームのメッシュの更新を避ける）
                float weight = BlendShapeLimiter.Limit(link.Target, link.TargetIndex,
                    link.Source.GetBlendShapeWeight(link.SourceIndex));
                if (!Mathf.Approximately(link.Target.GetBlendShapeWeight(link.TargetIndex), weight))
                {
                    link.Target.SetBlendShapeWeight(link.TargetIndex, weight);
                }
            }
        }

        private void LateUpdate()
        {
            Apply();
        }
    }
}
