using UnityEngine;
using VRCast.Core;
using VRCast.PerfectSync;

namespace VRCast.UI
{
    /// <summary>
    /// パーフェクトシンク化の画面で使う表示名（目印・細かさ・形状の出どころ）と、案内図の目印の位置。
    /// </summary>
    public static class PerfectSyncLabels
    {
        /// <summary>
        /// 目印の名前（左右のあるものは、正面から見た画面上の側と「アバターの左 / 右」を付ける）。
        /// </summary>
        public static string Landmark(LandmarkId id)
        {
            // 左右の別を名前から外し、部位名と左右の表記を組み合わせる（正面から見るのでアバターの左は画面の右）
            string part = Part(id);
            if (FaceLandmarks.IsLeft(id))
            {
                return part + Loc.T(" (screen right, avatar's left)", "（画面の右・アバターの左）",
                    " (화면 오른쪽·아바타의 왼쪽)", "（画面右侧·形象左侧）", "（畫面右側·形象左側）");
            }

            if (FaceLandmarks.IsRight(id))
            {
                return part + Loc.T(" (screen left, avatar's right)", "（画面の左・アバターの右）",
                    " (화면 왼쪽·아바타의 오른쪽)", "（画面左侧·形象右侧）", "（畫面左側·形象右側）");
            }

            return part;
        }

        /// <summary>
        /// 細かさの選択肢（LandmarkDetail の並び順）。
        /// </summary>
        public static string[] Details()
        {
            return new[]
            {
                Loc.T("Minimal (about 10 points)", "最小（約 10 点）", "최소 (약 10점)", "最少（约 10 点）", "最少（約 10 點）"),
                Loc.T("Standard (about 20 points)", "標準（約 20 点）", "표준 (약 20점)", "标准（约 20 点）", "標準（約 20 點）"),
                Loc.T("Detailed (about 40 points)", "詳細（約 40 点）", "상세 (약 40점)", "详细（约 40 点）", "詳細（約 40 點）"),
            };
        }

        /// <summary>
        /// 作り方の選択肢（PerfectSyncSet.Method* の並び順）。
        /// </summary>
        public static string[] Methods()
        {
            return new[]
            {
                Loc.T("Current formulas", "これまでの式", "기존 수식", "原有公式", "原有公式"),
                Loc.T("Shape transfer", "形状の転写", "형상 옮기기", "形状转移", "形狀轉移"),
            };
        }

        /// <summary>
        /// 形状の出どころの選択肢（PerfectSyncSet.Source* の並び順）。
        /// </summary>
        public static string[] Sources()
        {
            return new[]
            {
                Loc.T("Generate", "自動生成", "자동 생성", "自动生成", "自動生成"),
                Loc.T("Existing shape", "既存の BlendShape", "기존 BlendShape", "现有 BlendShape", "現有 BlendShape"),
                Loc.T("None", "なし", "없음", "无", "無"),
            };
        }

        /// <summary>
        /// アバターに無いことがある部位の表示名。
        /// </summary>
        public static string FacePart(FaceParts part)
        {
            switch (part)
            {
                case FaceParts.Brows:
                    return Loc.T("Brows", "眉", "눈썹", "眉毛", "眉毛");
                case FaceParts.Nose:
                    return Loc.T("Nose", "鼻", "코", "鼻子", "鼻子");
                case FaceParts.MouthInterior:
                    return Loc.T("Inside of the mouth", "口の中", "입 안", "口腔内部", "口腔內部");
                default:
                    return part.ToString();
            }
        }

        /// <summary>
        /// 眉・鼻・口の中などが無いアバターでは、作った形状が思いどおりに動かないことがあるという注意。
        /// </summary>
        public static string MissingPartsCaution()
        {
            return Loc.T(
                "On avatars missing some face parts (brows, nose, inside of the mouth, etc.), " +
                "the generated shapes may not move as expected.",
                "眉・鼻・口の中など、存在しない部位があるアバターでは、期待どおりの動きにならない可能性があります。",
                "눈썹·코·입 안 등 없는 부위가 있는 아바타에서는 기대한 대로 움직이지 않을 수 있습니다.",
                "对于缺少眉毛、鼻子、口腔内部等部位的虚拟形象，生成的形状可能无法按预期动作。",
                "對於缺少眉毛、鼻子、口腔內部等部位的虛擬形象，生成的形狀可能無法按預期動作。");
        }

        /// <summary>
        /// 案内図での位置（x: 見る人の左 -1 〜 右 1、y: 下 -1 〜 上 1。正面から見るのでアバターの左は右側）。
        /// </summary>
        public static Vector2 DiagramPosition(LandmarkId id)
        {
            // 左側の位置を基準にし、右側は x を反転する
            bool right = FaceLandmarks.IsRight(id);
            LandmarkId basis = right && FaceLandmarks.TryGetMirror(id, out LandmarkId left) ? left : id;
            Vector2 position = LeftOrCenterPosition(basis);
            return right ? new Vector2(-position.x, position.y) : position;
        }

        private static Vector2 LeftOrCenterPosition(LandmarkId id)
        {
            switch (id)
            {
                case LandmarkId.EyeUpperLeft:
                    return new Vector2(0.35f, 0.3f);
                case LandmarkId.EyeLowerLeft:
                    return new Vector2(0.35f, 0.1f);
                case LandmarkId.EyeInnerLeft:
                    return new Vector2(0.17f, 0.2f);
                case LandmarkId.EyeOuterLeft:
                    return new Vector2(0.53f, 0.22f);
                case LandmarkId.EyeUpperInnerLeft:
                    return new Vector2(0.25f, 0.28f);
                case LandmarkId.EyeUpperOuterLeft:
                    return new Vector2(0.46f, 0.29f);
                case LandmarkId.EyeLowerInnerLeft:
                    return new Vector2(0.25f, 0.12f);
                case LandmarkId.EyeLowerOuterLeft:
                    return new Vector2(0.46f, 0.13f);
                case LandmarkId.BrowMidLeft:
                    return new Vector2(0.36f, 0.5f);
                case LandmarkId.BrowInnerLeft:
                    return new Vector2(0.16f, 0.46f);
                case LandmarkId.BrowOuterLeft:
                    return new Vector2(0.56f, 0.44f);
                case LandmarkId.NoseTip:
                    return new Vector2(0f, -0.12f);
                case LandmarkId.NoseWingLeft:
                    return new Vector2(0.11f, -0.17f);
                case LandmarkId.CheekLeft:
                    return new Vector2(0.45f, -0.15f);
                case LandmarkId.MouthCornerLeft:
                    return new Vector2(0.21f, -0.43f);
                case LandmarkId.LipUpper:
                    return new Vector2(0f, -0.37f);
                case LandmarkId.LipUpperLeft:
                    return new Vector2(0.11f, -0.38f);
                case LandmarkId.LipLower:
                    return new Vector2(0f, -0.51f);
                case LandmarkId.LipLowerLeft:
                    return new Vector2(0.11f, -0.49f);
                case LandmarkId.Chin:
                    return new Vector2(0f, -0.82f);
                case LandmarkId.JawAngleLeft:
                    return new Vector2(0.68f, -0.45f);
                default:
                    return Vector2.zero;
            }
        }

        /// <summary>
        /// 目の動き（eyeLook*）の形状なら true（目のボーンで視線を動かすため、既定では作らない）。
        /// </summary>
        public static bool IsEyeLook(string arKitName)
        {
            return arKitName.StartsWith("eyeLook", System.StringComparison.Ordinal);
        }

        private static string Part(LandmarkId id)
        {
            // 左右を区別しない部位名（左右の組は左の値で判定する）
            LandmarkId basis = FaceLandmarks.IsRight(id) && FaceLandmarks.TryGetMirror(id, out LandmarkId left) ? left : id;
            switch (basis)
            {
                case LandmarkId.EyeUpperLeft:
                    return Loc.T("Upper eyelid (center)", "上まぶた（中央）", "윗눈꺼풀 (가운데)", "上眼睑（中间）", "上眼瞼（中間）");
                case LandmarkId.EyeLowerLeft:
                    return Loc.T("Lower eyelid (center)", "下まぶた（中央）", "아랫눈꺼풀 (가운데)", "下眼睑（中间）", "下眼瞼（中間）");
                case LandmarkId.EyeInnerLeft:
                    return Loc.T("Inner eye corner", "目頭", "눈 앞머리", "内眼角", "內眼角");
                case LandmarkId.EyeOuterLeft:
                    return Loc.T("Outer eye corner", "目尻", "눈꼬리", "外眼角", "外眼角");
                case LandmarkId.EyeUpperInnerLeft:
                    return Loc.T("Upper eyelid (inner)", "上まぶた（目頭寄り）", "윗눈꺼풀 (안쪽)", "上眼睑（内侧）", "上眼瞼（內側）");
                case LandmarkId.EyeUpperOuterLeft:
                    return Loc.T("Upper eyelid (outer)", "上まぶた（目尻寄り）", "윗눈꺼풀 (바깥쪽)", "上眼睑（外侧）", "上眼瞼（外側）");
                case LandmarkId.EyeLowerInnerLeft:
                    return Loc.T("Lower eyelid (inner)", "下まぶた（目頭寄り）", "아랫눈꺼풀 (안쪽)", "下眼睑（内侧）", "下眼瞼（內側）");
                case LandmarkId.EyeLowerOuterLeft:
                    return Loc.T("Lower eyelid (outer)", "下まぶた（目尻寄り）", "아랫눈꺼풀 (바깥쪽)", "下眼睑（外侧）", "下眼瞼（外側）");
                case LandmarkId.BrowMidLeft:
                    return Loc.T("Eyebrow (middle)", "眉（中央）", "눈썹 (가운데)", "眉毛（中间）", "眉毛（中間）");
                case LandmarkId.BrowInnerLeft:
                    return Loc.T("Eyebrow (inner end)", "眉頭", "눈썹 앞머리", "眉头", "眉頭");
                case LandmarkId.BrowOuterLeft:
                    return Loc.T("Eyebrow (outer end)", "眉尻", "눈썹 꼬리", "眉尾", "眉尾");
                case LandmarkId.NoseTip:
                    return Loc.T("Nose tip", "鼻先", "코끝", "鼻尖", "鼻尖");
                case LandmarkId.NoseWingLeft:
                    return Loc.T("Side of the nose", "小鼻", "콧방울", "鼻翼", "鼻翼");
                case LandmarkId.CheekLeft:
                    return Loc.T("Cheek", "頬", "볼", "脸颊", "臉頰");
                case LandmarkId.MouthCornerLeft:
                    return Loc.T("Mouth corner", "口角", "입꼬리", "嘴角", "嘴角");
                case LandmarkId.LipUpper:
                    return Loc.T("Upper lip (center)", "上唇（中央）", "윗입술 (가운데)", "上唇（中间）", "上唇（中間）");
                case LandmarkId.LipUpperLeft:
                    return Loc.T("Upper lip (side)", "上唇（横）", "윗입술 (옆)", "上唇（侧边）", "上唇（側邊）");
                case LandmarkId.LipLower:
                    return Loc.T("Lower lip (center)", "下唇（中央）", "아랫입술 (가운데)", "下唇（中间）", "下唇（中間）");
                case LandmarkId.LipLowerLeft:
                    return Loc.T("Lower lip (side)", "下唇（横）", "아랫입술 (옆)", "下唇（侧边）", "下唇（側邊）");
                case LandmarkId.Chin:
                    return Loc.T("Chin tip", "あご先", "턱 끝", "下巴尖", "下巴尖");
                case LandmarkId.JawAngleLeft:
                    return Loc.T("Jaw corner (below the ear)", "エラ（耳の下の角）", "턱 모서리 (귀 아래)", "下颌角（耳下）", "下頜角（耳下）");
                default:
                    return id.ToString();
            }
        }
    }
}
