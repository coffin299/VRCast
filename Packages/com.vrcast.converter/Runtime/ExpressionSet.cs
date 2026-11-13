using System;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// metadata/expressions.json の内容。表情プリセット（BlendShape の組み合わせ）の一覧。
    /// </summary>
    [Serializable]
    public class ExpressionSet : IMetadata
    {
        // 信頼できない入力に対する上限
        public const int MaxPresets = 256;
        public const int MaxValuesPerPreset = 512;
        public const int MaxStringLength = 512;

        // 1 つのプリセットが覚えておく、停止時に姿勢を戻すボーンのパスの上限
        public const int MaxBonePaths = 256;

        public ExpressionPreset[] presets = Array.Empty<ExpressionPreset>();

        /// <summary>
        /// 内容を検証し、問題があればエラーメッセージを、無ければ null を返す。
        /// </summary>
        public string Validate()
        {
            // 配列欠落と件数上限
            if (presets == null || presets.Length > MaxPresets)
            {
                return $"presets must be 0-{MaxPresets} items.";
            }

            foreach (ExpressionPreset preset in presets)
            {
                // 各プリセットの名前と値の件数
                if (preset == null || !IsValidString(preset.name, false))
                {
                    return "Preset name is invalid.";
                }

                if (preset.values == null || preset.values.Length > MaxValuesPerPreset)
                {
                    return $"Preset '{preset.name}' has invalid values.";
                }

                foreach (BlendShapeValue value in preset.values)
                {
                    // パスは空（ルート）を許可、BlendShape 名は必須、重みは 0〜100
                    if (value == null || !IsValidString(value.path, true) || !IsValidString(value.blendShape, false)
                        || float.IsNaN(value.weight) || value.weight < 0f || value.weight > 100f)
                    {
                        return $"Preset '{preset.name}' has an invalid blend shape value.";
                    }
                }

                // ボーンクリップ名は空を許可（無いパッケージ・表情だけのプリセット）。パスは任意
                if (!IsValidString(preset.boneClip ?? string.Empty, true))
                {
                    return $"Preset '{preset.name}' has an invalid bone clip name.";
                }

                if (preset.bonePaths != null)
                {
                    if (preset.bonePaths.Length > MaxBonePaths)
                    {
                        return $"Preset '{preset.name}' has too many bone paths.";
                    }

                    foreach (string path in preset.bonePaths)
                    {
                        // ルート自身は動かさないので、空のパスは認めない
                        if (!IsValidString(path, false))
                        {
                            return $"Preset '{preset.name}' has an invalid bone path.";
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// metadata 内の文字列共通の検証（null 不可、空は指定時のみ可、長さ上限あり）。
        /// </summary>
        public static bool IsValidString(string value, bool allowEmpty)
        {
            // null 不可、空は指定時のみ可、長さ上限あり
            return value != null && (allowEmpty || value.Length > 0) && value.Length <= MaxStringLength;
        }
    }

    /// <summary>
    /// 1 つの表情（名前と BlendShape 値の組）。
    /// </summary>
    [Serializable]
    public class ExpressionPreset
    {
        public string name = string.Empty;
        public BlendShapeValue[] values = Array.Empty<BlendShapeValue>();

        // 表情以外も動かすクリップ等から BlendShape だけを取り出したもの。アプリでは設定で表示したときだけ一覧に出す
        // （この項目を知らない旧版のアプリは常に表示する）
        public bool hidden;

        // 追加指定のクリップから作った、人体以外のボーンの legacy クリップ名（空 = ボーンは動かさない）
        // 値の無いプリセットはこのクリップだけの切り替えになる
        public string boneClip = string.Empty;

        // boneClip が動かすボーンの、アバタールートからの相対パス。停止時に読み込み時の姿勢へ戻す
        public string[] bonePaths = Array.Empty<string>();
    }

    /// <summary>
    /// アバタールートからの相対パスの SkinnedMeshRenderer に対する BlendShape 重み（0〜100）。
    /// </summary>
    [Serializable]
    public class BlendShapeValue
    {
        public string path = string.Empty;
        public string blendShape = string.Empty;
        public float weight;
    }
}
