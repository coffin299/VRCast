using System;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// metadata/perfect_sync.json の内容。アプリのパーフェクトシンク化で決めた設定
    /// （顔メッシュ・打った目印・ARKit 名ごとの元と強さ・既存 BlendShape の割り当て）。
    /// 生成した形状そのものは perfect_sync_shapes.bin（PerfectSyncShapeData）に入る。
    /// 目印は保存済みのファイルを開いて編集をやり直すために持つ。
    /// </summary>
    [Serializable]
    public class PerfectSyncSet : IMetadata
    {
        // この形式の版（項目を足したときに増やす）
        public const int CurrentVersion = 1;

        // 信頼できない入力に対する上限
        public const int MaxLandmarks = 128;
        public const int MaxLandmarkId = 255;
        public const int MaxShapes = 64;
        public const int MaxBindings = 512;
        public const float MaxStrength = 2f;

        // 目印の細かさ（0 = 最小・1 = 標準・2 = 詳細）
        public const int MaxDetail = 2;

        // アバターに無い部位のビット（1 = 眉・2 = 鼻・4 = 口の中）
        public const int MaxAbsentParts = 7;

        // 形状の元（生成 / 既存の BlendShape / 使わない）
        public const int SourceGenerated = 0;
        public const int SourceExisting = 1;
        public const int SourceNone = 2;

        public int version = CurrentVersion;

        // パーフェクトシンク化した元のファイル名と、その bundle の SHA-256（どのアバターから作ったかの記録）
        public string sourceName = string.Empty;
        public string sourceBundleSha256 = string.Empty;

        // 目印の細かさ・左右対称で打つか・歯や舌のメッシュも動かすか
        public int detail = 1;
        public bool mirror = true;
        public bool includeMouthParts = true;

        // 作り方（0 = これまでの式、1 = 形状の転写）。項目が無い古いファイルは式。版は上げない
        // （古いアプリは未知の項目を無視し、形状のバイナリだけで再生できる）
        public const int MethodFormula = 0;
        public const int MethodTransfer = 1;
        public int method = MethodFormula;

        // アバターに無い部位（ビットの組み合わせ。その部位の目印は打たず、形状は既定で作らない）
        public int absentParts;

        // 目印を打った顔メッシュ（アバタールートからの相対パス）
        public string facePath = string.Empty;

        public PerfectSyncLandmark[] landmarks = Array.Empty<PerfectSyncLandmark>();
        public PerfectSyncShapeSetting[] shapes = Array.Empty<PerfectSyncShapeSetting>();
        public PerfectSyncBinding[] bindings = Array.Empty<PerfectSyncBinding>();

        /// <summary>
        /// 何も設定していなければ true（パーフェクトシンク版ではないファイル）。
        /// </summary>
        public bool IsEmpty => (landmarks == null || landmarks.Length == 0) && (shapes == null || shapes.Length == 0)
            && (bindings == null || bindings.Length == 0);

        /// <summary>
        /// 内容を検証し、問題があればエラーメッセージを、無ければ null を返す。
        /// </summary>
        public string Validate()
        {
            // 版と細かさの範囲
            if (version < 1 || version > CurrentVersion)
            {
                return $"Unsupported version: {version}.";
            }

            if (detail < 0 || detail > MaxDetail)
            {
                return $"detail must be 0-{MaxDetail}.";
            }

            // 作り方は式か転写だけ（項目が無いファイルは 0）
            if (method != MethodFormula && method != MethodTransfer)
            {
                return "method is invalid.";
            }

            // 無い部位は定義済みのビットだけ
            if (absentParts < 0 || absentParts > MaxAbsentParts)
            {
                return $"absentParts must be 0-{MaxAbsentParts}.";
            }

            // 文字列は空を許可する（元の情報が無い・ルートのメッシュ）
            if (!ExpressionSet.IsValidString(sourceName, true) || !ExpressionSet.IsValidString(sourceBundleSha256, true)
                || !ExpressionSet.IsValidString(facePath, true))
            {
                return "A text field is invalid.";
            }

            return ValidateLandmarks() ?? ValidateShapes() ?? ValidateBindings();
        }

        private string ValidateLandmarks()
        {
            // 配列欠落と件数上限
            if (landmarks == null || landmarks.Length > MaxLandmarks)
            {
                return $"landmarks must be 0-{MaxLandmarks} items.";
            }

            foreach (PerfectSyncLandmark landmark in landmarks)
            {
                // 番号・三角形の範囲と、重心座標（三角形の内側。誤差を少し許す）
                if (landmark == null || landmark.id < 0 || landmark.id > MaxLandmarkId || landmark.triangle < 0
                    || !IsBarycentric(landmark.u) || !IsBarycentric(landmark.v)
                    || !IsBarycentric(landmark.u + landmark.v))
                {
                    return "A landmark is invalid.";
                }
            }

            return null;
        }

        private string ValidateShapes()
        {
            // 配列欠落と件数上限
            if (shapes == null || shapes.Length > MaxShapes)
            {
                return $"shapes must be 0-{MaxShapes} items.";
            }

            foreach (PerfectSyncShapeSetting shape in shapes)
            {
                // 名前は必須、元は 3 種類のどれか、強さは範囲内
                if (shape == null || !ExpressionSet.IsValidString(shape.name, false)
                    || shape.source < SourceGenerated || shape.source > SourceNone || !IsStrength(shape.strength))
                {
                    return "A shape setting is invalid.";
                }
            }

            return null;
        }

        private string ValidateBindings()
        {
            // 配列欠落と件数上限
            if (bindings == null || bindings.Length > MaxBindings)
            {
                return $"bindings must be 0-{MaxBindings} items.";
            }

            foreach (PerfectSyncBinding binding in bindings)
            {
                // ARKit 名と BlendShape 名は必須、パスは空（ルート）を許可、強さは範囲内
                if (binding == null || !ExpressionSet.IsValidString(binding.name, false)
                    || !ExpressionSet.IsValidString(binding.path, true)
                    || !ExpressionSet.IsValidString(binding.blendShape, false) || !IsStrength(binding.weight))
                {
                    return "A binding is invalid.";
                }
            }

            return null;
        }

        private static bool IsBarycentric(float value)
        {
            // 有限で 0〜1（浮動小数の誤差を少し許す）
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= -0.01f && value <= 1.01f;
        }

        private static bool IsStrength(float value)
        {
            // 有限で 0〜MaxStrength
            return !float.IsNaN(value) && value >= 0f && value <= MaxStrength;
        }
    }

    /// <summary>
    /// 打った目印 1 つ。顔メッシュの三角形の番号と、その中の位置（重心座標。2 番目・3 番目の頂点の重み）。
    /// 姿勢や拡大縮小が変わっても同じ場所を指す。
    /// </summary>
    [Serializable]
    public class PerfectSyncLandmark
    {
        public int id;
        public int triangle;
        public float u;
        public float v;

        // 左右対称で左側から自動で決めた右側なら true（再開後も左側に合わせて動かす）
        public bool mirrored;
    }

    /// <summary>
    /// ARKit 名 1 つの設定（元と強さ）。
    /// </summary>
    [Serializable]
    public class PerfectSyncShapeSetting
    {
        public string name = string.Empty;
        public int source;
        public float strength = 1f;
    }

    /// <summary>
    /// 既存の BlendShape を ARKit 名として動かす割り当て（weight は動かす強さ。1 で 100 まで）。
    /// </summary>
    [Serializable]
    public class PerfectSyncBinding
    {
        public string name = string.Empty;
        public string path = string.Empty;
        public string blendShape = string.Empty;
        public float weight = 1f;
    }
}
