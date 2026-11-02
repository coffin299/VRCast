using System;
using UnityEngine;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// パーフェクトシンクの「見る」方向の上限と、シェイプごとのしきい値。
    /// トラッキングタブで、口・頬の調整のあとに出す。しきい値の開閉は画面だけの状態。
    /// </summary>
    public static class PerfectSyncThresholdControls
    {
        /// <summary>
        /// BlendShape 名の系統ラベル（自動 / ARKit / Unified / 手動生成）。目の上限と本体で共有する。
        /// </summary>
        public static string[] NamingLabels()
        {
            return new[]
            {
                Loc.T("Auto", "自動", "자동", "自动", "自動"),
                "ARKit",
                "Unified Expressions",
                Loc.T("Handmade", "手動生成", "수동 생성", "手动生成", "手動生成"),
            };
        }

        /// <summary>
        /// BlendShape 名系統・目のトラッキング元・目の動きの上限。
        /// 名前系統か目の駆動が変わったら true（呼び出し側でパーフェクトシンクを作り直す）。
        /// </summary>
        public static bool DrawEyeLookLimit(AppSettings settings)
        {
            PerfectSyncNamingMode namingBefore = settings.trackingPerfectSyncNaming;
            settings.trackingPerfectSyncNaming = (PerfectSyncNamingMode)GuiControls.EnumSelector(
                Loc.T("Blend shape names", "BlendShape の名前", "BlendShape 이름", "BlendShape 名称", "BlendShape 名稱"),
                NamingLabels(), (int)settings.trackingPerfectSyncNaming);

            EyeLookDriveMode eyeBefore = settings.trackingEyeLookDrive;
            string[] eyeLabels =
            {
                Loc.T("Auto", "自動", "자동", "自动", "自動"),
                Loc.T("Bones", "ボーン", "본", "骨骼", "骨骼"),
                Loc.T("Keys", "キー", "키", "键", "鍵"),
            };
            settings.trackingEyeLookDrive = (EyeLookDriveMode)GuiControls.EnumSelector(
                Loc.T("Eye tracking", "目のトラッキング", "눈 트래킹", "眼睛追踪", "眼睛追蹤"),
                eyeLabels, (int)settings.trackingEyeLookDrive);
            GuiControls.Hint(Loc.T(
                "Default is Auto (bones if present, otherwise keys). If thresholds do not affect the eyes, choose Bones or Keys.",
                "既定は自動（目ボーンがあればボーン、無ければキー）です。しきい値が目に効かないときは、ボーンかキーを選んでください。",
                "기본값은 자동(눈 본이 있으면 본, 없으면 키)입니다. 임계값이 눈에 먹지 않으면 본 또는 키를 선택하세요.",
                "默认是自动（有眼球骨骼则用骨骼，否则用键）。若阈值对眼睛无效，请选择骨骼或键。",
                "預設是自動（有眼球骨骼則用骨骼，否則用鍵）。若閾值對眼睛無效，請選擇骨骼或鍵。"));

            // 隣の口・頬のスライダーと同じ操作にする（数値欄だとドラッグが欄の値に戻ることがある）
            settings.trackingPerfectSyncEyeLookLimit = GuiControls.Slider(
                Loc.T("Eye movement limit", "目の動きの上限", "눈 움직임 상한", "眼睛动作上限", "眼睛動作上限"),
                settings.trackingPerfectSyncEyeLookLimit, 0f, 1f, "F2");
            GuiControls.Hint(Loc.T(
                "Scales how far the eyes can look. 1 keeps the current movement, and 0 holds the eyes forward.",
                "目の動きが、この割合まで小さくなります。1 は今まで通り、0 で目は正面のままです。",
                "눈 움직임이 이 비율까지 작아집니다. 1은 지금과 같고, 0이면 눈은 정면을 유지합니다.",
                "眼睛动作会缩小到这个比例。1 保持现在的动作，0 则眼睛保持看向前方。",
                "眼睛動作會縮小到這個比例。1 維持現在的動作，0 則眼睛保持看向前方。"));

            if (settings.trackingPerfectSyncNaming == PerfectSyncNamingMode.Auto)
            {
                GuiControls.Hint(Loc.T(
                    "Auto picks the name set with more matches. Perfect sync converted avatars use Handmade.",
                    "自動は一致数の多い名前系統を選びます。パーフェクトシンク化済みは手動生成です。",
                    "자동은 일치 수가 많은 이름 체계를 고릅니다. 퍼펙트 싱크 변환 아바타는 수동 생성입니다.",
                    "自动会选用匹配更多的名称体系。已做完美同步化的虚拟形象为手动生成。",
                    "自動會選用匹配較多的名稱體系。已做完美同步化的虛擬形象為手動生成。"));
            }
            else if (settings.trackingPerfectSyncNaming == PerfectSyncNamingMode.Handmade)
            {
                GuiControls.Hint(Loc.T(
                    "Handmade prefers ARKit names, and fills missing slots with Unified Expressions names.",
                    "手動生成は ARKit 名を優先し、足りない枠だけ Unified Expressions 名も使います。",
                    "수동 생성은 ARKit 이름을 우선하고, 비어 있는 칸만 Unified Expressions 이름도 씁니다.",
                    "手动生成优先使用 ARKit 名称，并用 Unified Expressions 名称补足缺失项。",
                    "手動生成優先使用 ARKit 名稱，並用 Unified Expressions 名稱補足缺失項。"));
            }
            else if (settings.trackingPerfectSyncNaming == PerfectSyncNamingMode.Unified)
            {
                GuiControls.Hint(Loc.T(
                    "Unified Expressions names (v2/JawOpen → JawOpen). Missing slots fall back to ARKit names.",
                    "Unified Expressions 名（v2/JawOpen → JawOpen）。無い枠は ARKit 名へフォールバックします。",
                    "Unified Expressions 이름(v2/JawOpen → JawOpen). 없는 칸은 ARKit 이름으로 대체합니다.",
                    "Unified Expressions 名称（v2/JawOpen → JawOpen）。缺失项回退到 ARKit 名称。",
                    "Unified Expressions 名稱（v2/JawOpen → JawOpen）。缺失項回退到 ARKit 名稱。"));
            }

            return namingBefore != settings.trackingPerfectSyncNaming
                || eyeBefore != settings.trackingEyeLookDrive;
        }

        /// <summary>
        /// シェイプごとのしきい値。閉じた状態で返し、開いていれば一覧を描いてから開閉状態を返す。
        /// </summary>
        public static bool DrawThresholds(AppSettings settings, FaceTrackingDriver driver, bool open)
        {
            open = DrawDisclosure(Loc.T("Perfect sync thresholds", "パーフェクトシンクの閾値設定", "퍼펙트 싱크 임계값 설정",
                "完美同步阈值设置", "完美同步閾值設定"), open);
            if (!open)
            {
                return false;
            }

            GuiControls.Hint(Loc.T(
                "0 leaves a shape unchanged. At or below the threshold it stops, and anything above keeps its strength. 1 stops that shape. While tracking, each row shows received → applied.",
                "0 は補正なしです。しきい値以下は止まり、超えた値はそのまま動きます。1 にするとそのシェイプは動きません。トラッキング中は各行に「受信 → 反映」を出します。",
                "0은 보정 없음입니다. 임계값 이하는 멈추고, 넘은 값은 그대로 움직입니다. 1이면 그 셰이프는 움직이지 않습니다. 트래킹 중에는 각 행에 '수신 → 반영'을 표시합니다.",
                "0 为不校正。阈值及以下会停住，超过的值保持原有强度。设为 1 则该形状不动。追踪时每行显示“接收 → 应用”。",
                "0 為不校正。閾值及以下會停住，超過的值保持原有強度。設為 1 則該形狀不動。追蹤時每行顯示「接收 → 套用」。"));

            settings.EnsurePerfectSyncThresholds();
            if (GUILayout.Button(Loc.T("Reset all to 0", "すべて 0 に戻す", "모두 0으로 되돌리기", "全部恢复为 0", "全部恢復為 0")))
            {
                Array.Clear(settings.trackingPerfectSyncThresholds, 0, settings.trackingPerfectSyncThresholds.Length);
            }

            // 名前の系統（アバターが無いときは設定の固定。自動・手動生成・ARKit は ARKit 表示名）
            PerfectSyncNaming naming = driver != null
                ? driver.ActivePerfectSyncNaming
                : settings.trackingPerfectSyncNaming == PerfectSyncNamingMode.Unified
                    ? PerfectSyncNaming.Unified
                    : settings.trackingPerfectSyncNaming == PerfectSyncNamingMode.Handmade
                        ? PerfectSyncNaming.Handmade
                        : PerfectSyncNaming.ArKit;
            string[] arKitNames = ArKitFace.BlendShapeNames;
            string group = null;
            bool tracking = driver != null && driver.IsTracking;
            for (int i = 0; i < arKitNames.Length; i++)
            {
                string nextGroup = GroupLabel(arKitNames[i]);
                if (nextGroup != group)
                {
                    group = nextGroup;
                    GuiControls.SubHeading(group);
                }

                // 名前は固定（毎フレーム変わるとスライダーのドラッグが戻る）。受信と反映は別の行
                settings.trackingPerfectSyncThresholds[i] = GuiControls.Slider(
                    PerfectSyncBlendShapes.DisplayName(i, naming),
                    settings.trackingPerfectSyncThresholds[i], 0f, 1f, "F2");
                if (tracking && driver.TryPerfectSyncPreThreshold(i, out float now))
                {
                    float applied = PerfectSyncBlendShapes.ApplyThreshold(now, settings.trackingPerfectSyncThresholds[i]);
                    applied = PerfectSyncBlendShapes.ApplyEyeLookLimit(i, applied, settings.trackingPerfectSyncEyeLookLimit);
                    GuiControls.Status($"{now:0.00} → {applied:0.00}", applied > 0.001f);
                }
            }

            return true;
        }

        private static bool DrawDisclosure(string label, bool open)
        {
            // 行全体がボタン（小さな三角だけだと見出しが見つけにくい）
            string caption = (open ? "▼  " : "▶  ") + label;
            if (GUILayout.Button(caption))
            {
                open = !open;
            }

            return open;
        }

        private static string GroupLabel(string arKitName)
        {
            if (arKitName.StartsWith("brow", StringComparison.Ordinal))
            {
                return Loc.T("Brows", "眉", "눈썹", "眉毛", "眉毛");
            }

            if (arKitName.StartsWith("cheek", StringComparison.Ordinal))
            {
                return Loc.T("Cheeks", "頬", "볼", "脸颊", "臉頰");
            }

            if (arKitName.StartsWith("eye", StringComparison.Ordinal))
            {
                return Loc.T("Eyes", "目", "눈", "眼睛", "眼睛");
            }

            if (arKitName.StartsWith("jaw", StringComparison.Ordinal))
            {
                return Loc.T("Jaw", "顎", "턱", "下颌", "下顎");
            }

            if (arKitName.StartsWith("mouth", StringComparison.Ordinal))
            {
                return Loc.T("Mouth", "口", "입", "嘴", "嘴");
            }

            if (arKitName.StartsWith("nose", StringComparison.Ordinal))
            {
                return Loc.T("Nose", "鼻", "코", "鼻子", "鼻子");
            }

            return Loc.T("Tongue", "舌", "혀", "舌头", "舌頭");
        }
    }
}
