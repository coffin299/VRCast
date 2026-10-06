using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// FX コントローラーと追加指定のクリップから「BlendShape だけを動かすクリップ」を表情プリセットとして抽出する。
    /// 0 秒時点の値を使い、重みがすべて 0 のクリップ（リセット用）は除外する。
    /// </summary>
    public static class ExpressionExtractor
    {
        // BlendShape カーブのプロパティ名接頭辞
        private const string BlendShapePrefix = "blendShape.";

        /// <summary>
        /// 抽出結果と、追加指定のクリップのうち表情として取り込めた数。
        /// </summary>
        public struct Result
        {
            public ExpressionSet Set;
            public int ExtraAdded;
        }

        /// <param name="controller">FX コントローラー（無ければ null）</param>
        /// <param name="extraClips">FX に無い表情として追加するクリップ（無ければ null）</param>
        public static Result Extract(AnimatorController controller, IReadOnlyList<AnimationClip> extraClips = null)
        {
            var presets = new List<ExpressionPreset>();
            var usedNames = new HashSet<string>();
            var usedClips = new HashSet<AnimationClip>();

            // コントローラー内の全クリップ（重複は Unity 側で除去済み）
            if (controller != null)
            {
                foreach (AnimationClip clip in controller.animationClips)
                {
                    // 同名クリップは最初の 1 つのみ採用し、上限で打ち切る
                    if (clip == null || !usedNames.Add(clip.name) || presets.Count >= ExpressionSet.MaxPresets)
                    {
                        continue;
                    }

                    // 追加指定に同じクリップがあっても二重に入れない
                    usedClips.Add(clip);
                    ExpressionPreset preset = TryCreatePreset(clip, clip.name);
                    if (preset != null)
                    {
                        presets.Add(preset);
                    }
                }
            }

            int extraAdded = 0;
            if (extraClips != null)
            {
                foreach (AnimationClip clip in extraClips)
                {
                    // FX と同じクリップ・指定の重複・上限超えは飛ばす
                    if (clip == null || !usedClips.Add(clip) || presets.Count >= ExpressionSet.MaxPresets)
                    {
                        continue;
                    }

                    // 別のクリップと名前が重なるときは番号を付けて区別する
                    ExpressionPreset preset = TryCreatePreset(clip, UniqueName(clip.name, usedNames));
                    if (preset != null)
                    {
                        presets.Add(preset);
                        extraAdded++;
                    }
                }
            }

            // 名前順で並べて UI 上で探しやすくする
            presets.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return new Result { Set = new ExpressionSet { presets = presets.ToArray() }, ExtraAdded = extraAdded };
        }

        private static string UniqueName(string name, HashSet<string> usedNames)
        {
            // 未使用ならそのまま、使用済みなら「名前 (2)」「名前 (3)」… の空いている番号
            string candidate = name;
            for (int i = 2; !usedNames.Add(candidate); i++)
            {
                candidate = $"{name} ({i})";
            }

            return candidate;
        }

        /// <summary>
        /// クリップを表情として取り込めるか（取り込めない場合はその理由）。
        /// </summary>
        public static ClipCheck Check(AnimationClip clip)
        {
            return Read(clip, null);
        }

        private static ExpressionPreset TryCreatePreset(AnimationClip clip, string name)
        {
            // 取り込めるクリップだけプリセットにする
            var values = new List<BlendShapeValue>();
            if (Read(clip, values) != ClipCheck.Expression)
            {
                return null;
            }

            return new ExpressionPreset { name = name, values = values.ToArray() };
        }

        private static ClipCheck Read(AnimationClip clip, List<BlendShapeValue> values)
        {
            // マテリアル差し替え等の参照カーブを含むクリップは表情ではない
            if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0)
            {
                return ClipCheck.HasOtherCurves;
            }

            // カーブが無い・多すぎるクリップは対象外
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings.Length == 0)
            {
                return ClipCheck.NoCurves;
            }

            if (bindings.Length > ExpressionSet.MaxValuesPerPreset)
            {
                return ClipCheck.TooManyCurves;
            }

            bool hasNonZero = false;
            foreach (EditorCurveBinding binding in bindings)
            {
                // BlendShape 以外のカーブが 1 つでもあれば対象外
                if (binding.type != typeof(SkinnedMeshRenderer)
                    || !binding.propertyName.StartsWith(BlendShapePrefix, StringComparison.Ordinal))
                {
                    return ClipCheck.HasOtherCurves;
                }

                // 0 秒時点の重みを 0〜100 に丸めて記録（判定だけのときは記録しない）
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                float weight = Mathf.Clamp(curve != null ? curve.Evaluate(0f) : 0f, 0f, 100f);
                hasNonZero |= weight > 0f;
                values?.Add(new BlendShapeValue
                {
                    path = binding.path,
                    blendShape = binding.propertyName.Substring(BlendShapePrefix.Length),
                    weight = weight,
                });
            }

            // すべて 0 のクリップはリセット用とみなして除外
            return hasNonZero ? ClipCheck.Expression : ClipCheck.AllZero;
        }
    }

    /// <summary>
    /// クリップを表情として取り込めるかの判定結果。
    /// </summary>
    public enum ClipCheck
    {
        // ブレンドシェイプだけを動かす表情クリップ
        Expression,

        // ブレンドシェイプ以外（小物の表示・マテリアルなど）も動かす
        HasOtherCurves,

        // 動かすものが無い
        NoCurves,

        // 動かすブレンドシェイプが上限より多い
        TooManyCurves,

        // すべて 0（表情を戻す用）
        AllZero,
    }
}
