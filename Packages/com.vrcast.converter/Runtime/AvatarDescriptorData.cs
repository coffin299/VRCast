using System;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// metadata/descriptor.json の内容。VRCAvatarDescriptor のうち Runtime で使う設定（リップシンク・まぶた）。
    /// </summary>
    [Serializable]
    public class AvatarDescriptorData : IMetadata
    {
        // VRChat の Viseme 数（sil, PP, FF, TH, DD, kk, CH, SS, nn, RR, aa, E, I, O, U）
        public const int VisemeCount = 15;

        // 母音の Viseme（aa = あ、E = え、I = い、O = お、U = う）のインデックス。aa は口を開く代表としても使う
        public const int VisemeAa = 10;
        public const int VisemeE = 11;
        public const int VisemeI = 12;
        public const int VisemeO = 13;
        public const int VisemeU = 14;

        public LipSyncData lipSync = new LipSyncData();
        public EyelidData eyelids = new EyelidData();

        public string Validate()
        {
            // 各ブロックの欠落
            if (lipSync == null || eyelids == null)
            {
                return "lipSync and eyelids are required.";
            }

            // モードは既知の値のみ
            if (lipSync.mode != LipSyncData.ModeNone && lipSync.mode != LipSyncData.ModeVisemeBlendShape
                && lipSync.mode != LipSyncData.ModeJawFlapBlendShape)
            {
                return $"Unknown lipSync.mode '{lipSync.mode}'.";
            }

            // パスと BlendShape 名は空可・長さ上限あり
            if (!ExpressionSet.IsValidString(lipSync.meshPath, true)
                || !ExpressionSet.IsValidString(lipSync.mouthOpenBlendShape, true)
                || !ExpressionSet.IsValidString(eyelids.meshPath, true)
                || !ExpressionSet.IsValidString(eyelids.winkLeftBlendShape, true)
                || !ExpressionSet.IsValidString(eyelids.winkRightBlendShape, true))
            {
                return "Descriptor contains an invalid string.";
            }

            // まばたき BlendShape は上限件数まで、名前は必須
            if (eyelids.blinkBlendShapes == null || eyelids.blinkBlendShapes.Length > EyelidData.MaxBlinkBlendShapes)
            {
                return $"eyelids.blinkBlendShapes must have 0-{EyelidData.MaxBlinkBlendShapes} items.";
            }

            foreach (string shape in eyelids.blinkBlendShapes)
            {
                if (!ExpressionSet.IsValidString(shape, false))
                {
                    return "eyelids.blinkBlendShapes contains an invalid name.";
                }
            }

            // Viseme 名は 0 件または 15 件
            if (lipSync.visemes == null || (lipSync.visemes.Length != 0 && lipSync.visemes.Length != VisemeCount))
            {
                return $"lipSync.visemes must have 0 or {VisemeCount} items.";
            }

            foreach (string viseme in lipSync.visemes)
            {
                if (!ExpressionSet.IsValidString(viseme, true))
                {
                    return "lipSync.visemes contains an invalid name.";
                }
            }

            return null;
        }
    }

    /// <summary>
    /// リップシンク設定。mode に応じて visemes または mouthOpenBlendShape を使う。
    /// </summary>
    [Serializable]
    public class LipSyncData
    {
        // 対応するモード（ジョーボーン等は未対応のため none として書き出す）
        public const string ModeNone = "none";
        public const string ModeVisemeBlendShape = "visemeBlendShape";
        public const string ModeJawFlapBlendShape = "jawFlapBlendShape";

        public string mode = ModeNone;
        public string meshPath = string.Empty;
        public string[] visemes = Array.Empty<string>();
        public string mouthOpenBlendShape = string.Empty;
    }

    /// <summary>
    /// まぶた設定。blinkBlendShapes（左右別の場合は複数）が空ならまばたき不可。
    /// winkLeft / winkRightBlendShape は片目だけ閉じる BlendShape（アバターから見た左右、両方揃った場合のみ。空ならウインク不可）。
    /// blinkBlendShapes と同名でもよい（左右別のまばたき BlendShape をウインクにも使う場合）。
    /// </summary>
    [Serializable]
    public class EyelidData
    {
        // 1 つのメッシュでまばたきに使う BlendShape 数の上限
        public const int MaxBlinkBlendShapes = 4;

        public string meshPath = string.Empty;
        public string[] blinkBlendShapes = Array.Empty<string>();
        public string winkLeftBlendShape = string.Empty;
        public string winkRightBlendShape = string.Empty;
    }
}
