using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// 表情ツール（FaceEmo）・FX コントローラー・追加指定のクリップから「BlendShape だけを動かすクリップ」を表情プリセットとして抽出する。
    /// 0 秒時点の値を使う。FX の表情でないクリップ（小物の切り替え等を含む・重みがすべて 0）は BlendShape 部分だけを非表示として取り込む。
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
            public int NamedAdded;

            // FX の表情でないクリップから BlendShape だけを非表示として取り込んだ数
            public int HiddenAdded;

            // 表情ツールの表情のうち取り込まなかったもの（「メニュー上の場所: 理由」。原因を調べるためにログへ出す）
            public List<string> NamedSkipped;
        }

        /// <summary>
        /// 表情ツール（FaceEmo 等）で名前が付いている表情クリップ。
        /// </summary>
        public struct NamedClip
        {
            public AnimationClip Clip;
            public string Name;

            // メニュー上の場所（例: 「Folder / Mode / Left」。ログ用）
            public string Source;
        }

        /// <param name="controller">FX コントローラー（無ければ null）</param>
        /// <param name="extraClips">FX に無い表情として追加するクリップ（無ければ null）</param>
        /// <param name="namedClips">
        /// 表情ツールの表情（無ければ null）。FX より優先し、BlendShape 以外のカーブは無視して取り込む
        /// </param>
        public static Result Extract(AnimatorController controller, IReadOnlyList<AnimationClip> extraClips = null,
            IReadOnlyList<NamedClip> namedClips = null)
        {
            var presets = new List<ExpressionPreset>();
            var usedNames = new HashSet<string>();
            var usedClips = new HashSet<AnimationClip>();
            // 表情ツールのクリップ名（NDMF が FX 用に複製した同名クリップを二重に入れないため）
            var namedSources = new HashSet<string>();

            int namedAdded = 0;
            var namedSkipped = new List<string>();
            // 表情ツールで取り込んだ「クリップと名前」の組（同じクリップでも名前が違うモードは別の表情として出す）
            var usedNamedClips = new HashSet<(AnimationClip, string)>();
            if (namedClips != null)
            {
                foreach (NamedClip named in namedClips)
                {
                    // 同じクリップで同じ名前・上限超えは飛ばす（理由はログ用に控える）
                    if (named.Clip == null)
                    {
                        continue;
                    }

                    // 名前が無ければクリップ名を使う
                    string name = string.IsNullOrWhiteSpace(named.Name) ? named.Clip.name : named.Name.Trim();
                    if (!usedNamedClips.Add((named.Clip, name)))
                    {
                        namedSkipped.Add($"{named.Source}: same clip and name as another expression ({named.Clip.name})");
                        continue;
                    }

                    // FX・追加指定から同じクリップを二重に入れないよう控える
                    usedClips.Add(named.Clip);

                    if (presets.Count >= ExpressionSet.MaxPresets)
                    {
                        namedSkipped.Add($"{named.Source}: over {ExpressionSet.MaxPresets} expressions");
                        continue;
                    }

                    namedSources.Add(named.Clip.name);
                    // 表情ツールのモードは BlendShape が 1 つでもあれば取り込む（値がすべて 0・多すぎる場合も見送らない）
                    ExpressionPreset preset = CreateFromBlendShapes(named.Clip, false);
                    if (preset != null)
                    {
                        // 名前が重なれば番号を付ける
                        preset.name = UniqueName(name, usedNames);
                        presets.Add(preset);
                        namedAdded++;
                    }
                    else
                    {
                        namedSkipped.Add($"{named.Source}: no blend shapes ({named.Clip.name})");
                    }
                }
            }

            // コントローラー内の全クリップ（重複は Unity 側で除去済み）。表情でないものは後で非表示として取り込む
            var hiddenClips = new List<AnimationClip>();
            if (controller != null)
            {
                foreach (AnimationClip clip in controller.animationClips)
                {
                    // 表情ツール分と同名クリップは最初の 1 つのみ採用し、上限で打ち切る
                    if (clip == null || usedClips.Contains(clip) || namedSources.Contains(clip.name)
                        || !usedNames.Add(clip.name) || presets.Count >= ExpressionSet.MaxPresets)
                    {
                        continue;
                    }

                    // 追加指定に同じクリップがあっても二重に入れない
                    usedClips.Add(clip);
                    ExpressionPreset preset = TryCreatePreset(clip, clip.name, false);
                    if (preset != null)
                    {
                        presets.Add(preset);
                    }
                    else
                    {
                        hiddenClips.Add(clip);
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
                    ExpressionPreset preset = TryCreatePreset(clip, UniqueName(clip.name, usedNames), false);
                    if (preset != null)
                    {
                        presets.Add(preset);
                        extraAdded++;
                    }
                }
            }

            // FX の表情でないクリップ（小物の切り替え等を含む・値がすべて 0）は BlendShape 部分だけを非表示として取り込む
            // （上限に達したときに表情・追加指定が押し出されないよう最後に回す。名前は FX の読み込みで予約済み）
            int hiddenAdded = 0;
            foreach (AnimationClip clip in hiddenClips)
            {
                if (presets.Count >= ExpressionSet.MaxPresets)
                {
                    break;
                }

                ExpressionPreset preset = CreateFromBlendShapes(clip, true);
                if (preset != null)
                {
                    preset.name = clip.name;
                    presets.Add(preset);
                    hiddenAdded++;
                }
            }

            // 表示するものを先に名前順で並べ、非表示は後ろに置く（キーワードでの自動の割り当てで表示中の表情を優先するため）
            presets.Sort((a, b) => a.hidden != b.hidden
                ? a.hidden.CompareTo(b.hidden)
                : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return new Result
            {
                Set = new ExpressionSet { presets = presets.ToArray() },
                ExtraAdded = extraAdded,
                NamedAdded = namedAdded,
                NamedSkipped = namedSkipped,
                HiddenAdded = hiddenAdded,
            };
        }

        private static ExpressionPreset CreateFromBlendShapes(AnimationClip clip, bool hidden)
        {
            // BlendShape のカーブだけを読み、上限を超える分は切り捨てる（BlendShape が 1 つも無ければ null）
            var values = new List<BlendShapeValue>();
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (values.Count >= ExpressionSet.MaxValuesPerPreset)
                {
                    break;
                }

                if (IsBlendShape(binding))
                {
                    values.Add(ToValue(clip, binding));
                }
            }

            return values.Count > 0
                ? new ExpressionPreset { values = values.ToArray(), hidden = hidden }
                : null;
        }

        private static BlendShapeValue ToValue(AnimationClip clip, EditorCurveBinding binding)
        {
            // 0 秒時点の重みを 0〜100 に丸める
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            return new BlendShapeValue
            {
                path = binding.path,
                blendShape = binding.propertyName.Substring(BlendShapePrefix.Length),
                weight = Mathf.Clamp(curve != null ? curve.Evaluate(0f) : 0f, 0f, 100f),
            };
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
            return Read(clip, null, false);
        }

        private static ExpressionPreset TryCreatePreset(AnimationClip clip, string name, bool blendShapesOnly)
        {
            return TryCreatePreset(clip, name, blendShapesOnly, out _);
        }

        private static ExpressionPreset TryCreatePreset(AnimationClip clip, string name, bool blendShapesOnly,
            out ClipCheck check)
        {
            // 取り込めるクリップだけプリセットにする（取り込めなければ理由を返す）
            var values = new List<BlendShapeValue>();
            check = Read(clip, values, blendShapesOnly);
            if (check != ClipCheck.Expression)
            {
                return null;
            }

            return new ExpressionPreset { name = name, values = values.ToArray() };
        }

        /// <param name="blendShapesOnly">true なら BlendShape 以外のカーブを無視する（表情ツールの小物の表示等）</param>
        private static ClipCheck Read(AnimationClip clip, List<BlendShapeValue> values, bool blendShapesOnly)
        {
            // マテリアル差し替え等の参照カーブを含むクリップは表情ではない
            if (!blendShapesOnly && AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0)
            {
                return ClipCheck.HasOtherCurves;
            }

            // カーブが無い・多すぎるクリップは対象外
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            if (blendShapesOnly)
            {
                bindings = Array.FindAll(bindings, IsBlendShape);
            }

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
                if (!IsBlendShape(binding))
                {
                    return ClipCheck.HasOtherCurves;
                }

                // 0 秒時点の重みを記録（判定だけのときは記録しない）
                BlendShapeValue value = ToValue(clip, binding);
                hasNonZero |= value.weight > 0f;
                values?.Add(value);
            }

            // すべて 0 のクリップはリセット用とみなして除外
            return hasNonZero ? ClipCheck.Expression : ClipCheck.AllZero;
        }

        private static bool IsBlendShape(EditorCurveBinding binding)
        {
            // SkinnedMeshRenderer の blendShape.* カーブだけが表情
            return binding.type == typeof(SkinnedMeshRenderer)
                && binding.propertyName.StartsWith(BlendShapePrefix, StringComparison.Ordinal);
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
