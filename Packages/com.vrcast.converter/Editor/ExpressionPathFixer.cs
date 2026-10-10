using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// 表情の BlendShape のパスを、改変（NDMF・MA・メッシュ統合等）を適用した後のアバターに合わせて直す。
    /// FaceEmo 等は改変前のアバターから読むため、メッシュの場所が変わるとアプリで見つからず表情一覧に出ない。
    /// </summary>
    public static class ExpressionPathFixer
    {
        /// <summary>
        /// 直した値の数と、アプリで 1 つも見つからず表情一覧に出ない表情の名前。
        /// </summary>
        public struct Result
        {
            public int FixedValues;
            public List<string> Unresolved;
        }

        public static Result Fix(ExpressionSet expressions, Transform root)
        {
            var result = new Result { Unresolved = new List<string>() };
            // 非アクティブを含む全メッシュ（BlendShape 名から探し直す候補）
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            foreach (ExpressionPreset preset in expressions.presets)
            {
                bool resolved = false;
                foreach (BlendShapeValue value in preset.values)
                {
                    // そのままのパスで見つかれば直さない
                    Transform node = string.IsNullOrEmpty(value.path) ? root : root.Find(value.path);
                    if (Has(node, value.blendShape))
                    {
                        resolved = true;
                        continue;
                    }

                    // 見つからなければ同じ BlendShape を持つメッシュへ付け替える（決められなければそのまま）
                    SkinnedMeshRenderer target = FindMoved(renderers, value);
                    if (target != null)
                    {
                        value.path = ReflectionUtility.PathOf(target.transform, root);
                        result.FixedValues++;
                        resolved = true;
                    }
                }

                // 1 つも見つからない表情はアプリの一覧に出ないため、書き出しの結果で知らせる
                if (!resolved)
                {
                    result.Unresolved.Add(preset.name);
                }
            }

            return result;
        }

        private static SkinnedMeshRenderer FindMoved(SkinnedMeshRenderer[] renderers, BlendShapeValue value)
        {
            // 元のパスの末尾（オブジェクト名）が同じものを優先し、無ければ BlendShape を持つものが 1 つだけのときに選ぶ
            string name = value.path.Substring(value.path.LastIndexOf('/') + 1);
            SkinnedMeshRenderer sameName = null;
            SkinnedMeshRenderer only = null;
            int sameNameCount = 0;
            int count = 0;
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                if (!Has(renderer.transform, value.blendShape))
                {
                    continue;
                }

                count++;
                only = renderer;
                if (renderer.name == name)
                {
                    sameNameCount++;
                    sameName = renderer;
                }
            }

            // 候補が複数あって決められないときは取り違えを避けて直さない
            if (sameNameCount == 1)
            {
                return sameName;
            }

            return count == 1 ? only : null;
        }

        private static bool Has(Transform node, string blendShape)
        {
            // メッシュがあり、その BlendShape を持つか
            var renderer = node != null ? node.GetComponent<SkinnedMeshRenderer>() : null;
            return renderer != null && renderer.sharedMesh != null
                && !string.IsNullOrEmpty(blendShape)
                && renderer.sharedMesh.GetBlendShapeIndex(blendShape) >= 0;
        }
    }
}
