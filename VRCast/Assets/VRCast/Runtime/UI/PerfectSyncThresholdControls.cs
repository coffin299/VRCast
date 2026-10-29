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
        /// 目の動きの上限（常に表示。1 = 今まで通り）。目のボーンとパーフェクトシンクの「見る」方向の両方に効く。
        /// </summary>
        public static void DrawEyeLookLimit(AppSettings settings)
        {
            // 隣の口・頬のスライダーと同じ操作にする（数値欄だとドラッグが欄の値に戻ることがある）
            settings.trackingPerfectSyncEyeLookLimit = GuiControls.Slider(
                Loc.T("Eye movement limit", "目の動きの上限", "눈 움직임 상한", "眼睛动作上限", "眼睛動作上限"),
                settings.trackingPerfectSyncEyeLookLimit, 0f, 1f, "F2");
            GuiControls.Hint(Loc.T(
                "Scales how far the eyes can look, including eye bones and perfect sync look shapes. 1 keeps the current movement, and 0 holds the eyes forward.",
                "目のボーンと、パーフェクトシンクの「見る」方向（上下左右）の両方が、この割合まで小さくなります。1 は今まで通り、0 で目は正面のままです。",
                "눈 뼈대와 퍼펙트 싱크의 보는 방향(상하좌우)이 모두 이 비율까지 작아집니다. 1은 지금과 같고, 0이면 눈은 정면을 유지합니다.",
                "眼球骨骼和完美同步的“看向”方向（上下左右）都会缩小到这个比例。1 保持现在的动作，0 则眼睛保持看向前方。",
                "眼球骨骼和完美同步的「看向」方向（上下左右）都會縮小到這個比例。1 維持現在的動作，0 則眼睛保持看向前方。"));
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

            // 名前の系統（アバターが無いときは設定の固定。自動・手動生成は ARKit 表示名）
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
