using System;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// アバターに無いことがある顔の部位（眉の無いデフォルメ顔・鼻の無い顔・口の中が作られていない顔など）。
    /// 値は perfect_sync.json の absentParts に保存するため変えない。
    /// </summary>
    [Flags]
    public enum FaceParts
    {
        None = 0,
        Brows = 1,
        Nose = 2,
        MouthInterior = 4,
        All = Brows | Nose | MouthInterior,
    }

    /// <summary>
    /// 目印と ARKit の形状が、どの部位のものか。
    /// </summary>
    public static class FacePartRules
    {
        // 口の中が見える（口を開ける・唇を離す・舌を出す）形状
        private static readonly string[] MouthInteriorShapes =
        {
            "jawOpen", "mouthClose", "mouthFunnel", "mouthLowerDownLeft", "mouthLowerDownRight",
            "mouthUpperUpLeft", "mouthUpperUpRight", "tongueOut",
        };

        /// <summary>
        /// 「無い」と選べる部位（画面に並べる順）。
        /// </summary>
        public static FaceParts[] Selectable { get; } = { FaceParts.Brows, FaceParts.Nose, FaceParts.MouthInterior };

        /// <summary>
        /// 目印の部位（眉・鼻のもの以外は None）。
        /// </summary>
        public static FaceParts Landmark(LandmarkId id)
        {
            // 名前の先頭で分ける（眉は Brow〜、鼻は Nose〜）
            string name = id.ToString();
            if (name.StartsWith("Brow", StringComparison.Ordinal))
            {
                return FaceParts.Brows;
            }

            return name.StartsWith("Nose", StringComparison.Ordinal) ? FaceParts.Nose : FaceParts.None;
        }

        /// <summary>
        /// ARKit の形状の部位（どれにも当たらなければ None）。
        /// </summary>
        public static FaceParts Shape(string arKitName)
        {
            // 眉の形状（browDownLeft など）
            if (arKitName.StartsWith("brow", StringComparison.Ordinal))
            {
                return FaceParts.Brows;
            }

            // 鼻の形状（noseSneerLeft / Right）
            if (arKitName.StartsWith("nose", StringComparison.Ordinal))
            {
                return FaceParts.Nose;
            }

            // 口の中が見える形状
            return Array.IndexOf(MouthInteriorShapes, arKitName) >= 0 ? FaceParts.MouthInterior : FaceParts.None;
        }
    }
}
