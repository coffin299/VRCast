using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// FX コントローラーから「BlendShape だけを動かすクリップ」を表情プリセットとして抽出する。
    /// 0 秒時点の値を使い、重みがすべて 0 のクリップ（リセット用）は除外する。
    /// </summary>
    public static class ExpressionExtractor
    {
        // BlendShape カーブのプロパティ名接頭辞
        private const string BlendShapePrefix = "blendShape.";

        public static ExpressionSet Extract(AnimatorController controller)
        {
            var presets = new List<ExpressionPreset>();
            var usedNames = new HashSet<string>();

            // コントローラー内の全クリップ（重複は Unity 側で除去済み）
            foreach (AnimationClip clip in controller.animationClips)
            {
                // 同名クリップは最初の 1 つのみ採用し、上限で打ち切る
                if (clip == null || !usedNames.Add(clip.name) || presets.Count >= ExpressionSet.MaxPresets)
                {
                    continue;
                }

                ExpressionPreset preset = TryCreatePreset(clip);
                if (preset != null)
                {
                    presets.Add(preset);
                }
            }

            // 名前順で並べて UI 上で探しやすくする
            presets.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return new ExpressionSet { presets = presets.ToArray() };
        }

        private static ExpressionPreset TryCreatePreset(AnimationClip clip)
        {
            // マテリアル差し替え等の参照カーブを含むクリップは表情ではない
            if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0)
            {
                return null;
            }

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings.Length == 0 || bindings.Length > ExpressionSet.MaxValuesPerPreset)
            {
                return null;
            }

            var values = new List<BlendShapeValue>();
            bool hasNonZero = false;
            foreach (EditorCurveBinding binding in bindings)
            {
                // BlendShape 以外のカーブが 1 つでもあれば対象外
                if (binding.type != typeof(SkinnedMeshRenderer)
                    || !binding.propertyName.StartsWith(BlendShapePrefix, StringComparison.Ordinal))
                {
                    return null;
                }

                // 0 秒時点の重みを 0〜100 に丸めて記録
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                float weight = Mathf.Clamp(curve != null ? curve.Evaluate(0f) : 0f, 0f, 100f);
                hasNonZero |= weight > 0f;
                values.Add(new BlendShapeValue
                {
                    path = binding.path,
                    blendShape = binding.propertyName.Substring(BlendShapePrefix.Length),
                    weight = weight,
                });
            }

            // すべて 0 のクリップはリセット用とみなして除外
            if (!hasNonZero)
            {
                return null;
            }

            return new ExpressionPreset { name = clip.name, values = values.ToArray() };
        }
    }
}
