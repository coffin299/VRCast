using System;
using System.Collections.Generic;
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
        private const string EyeLimitInputKey = "ps-eye-limit";
        private const string ThresholdInputPrefix = "ps-th-";

        /// <summary>
        /// 「見る」方向の上限（常に表示。1 = 今まで通り）。
        /// </summary>
        public static void DrawEyeLookLimit(AppSettings settings, Dictionary<string, string> inputs)
        {
            inputs.TryGetValue(EyeLimitInputKey, out string input);
            settings.trackingPerfectSyncEyeLookLimit = GuiControls.SliderOrField(
                Loc.T("Eye movement limit", "目の動きの上限", "눈 움직임 상한", "眼睛动作上限", "眼睛動作上限"),
                settings.trackingPerfectSyncEyeLookLimit, 0f, 1f, ref input, EyeLimitInputKey, "F2");
            inputs[EyeLimitInputKey] = input ?? string.Empty;
            GuiControls.Hint(Loc.T(
                "Caps perfect sync look shapes (up, down, left and right) at this value. 1 keeps the current movement, and 0 stops only those shapes. Eye bones still follow Eye gaze under Motion.",
                "パーフェクトシンクの「見る」方向（上下左右）だけを、この値までに抑えます。1 は今まで通り、0 でその動きだけ止まります。目のボーンは「動き」の「視線の強さ」のままです。",
                "퍼펙트 싱크의 보는 방향(상하좌우)만 이 값까지로 제한합니다. 1은 지금과 같고, 0이면 그 움직임만 멈춥니다. 눈 뼈대는 '움직임'의 '시선 강도'를 그대로 따릅니다.",
                "只把完美同步的“看向”方向（上下左右）限制到这个值。1 保持现在的动作，0 只停住这些形状。眼球骨骼仍跟随“动作”里的“视线强度”。",
                "只把完美同步的「看向」方向（上下左右）限制到這個值。1 維持現在的動作，0 只停住這些形狀。眼球骨骼仍跟隨「動作」裡的「視線強度」。"));
        }

        /// <summary>
        /// シェイプごとのしきい値。閉じた状態で返し、開いていれば一覧を描いてから開閉状態を返す。
        /// </summary>
        public static bool DrawThresholds(AppSettings settings, FaceTrackingDriver driver, bool open,
            Dictionary<string, string> inputs)
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
                ClearThresholdInputs(inputs);
                GUIUtility.keyboardControl = 0;
            }

            // 名前の系統（アバターが無いときは設定の固定。自動は ARKit 名）
            PerfectSyncNaming naming = driver != null
                ? driver.ActivePerfectSyncNaming
                : settings.trackingPerfectSyncNaming == PerfectSyncNamingMode.Unified
                    ? PerfectSyncNaming.Unified
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

                string label = PerfectSyncBlendShapes.DisplayName(i, naming);
                if (tracking && driver.TryPerfectSyncPreThreshold(i, out float now))
                {
                    // 受信はそのまま、反映は今のしきい値と見る方向の上限をかけた値（スライダーを動かすと次の表示で変わる）
                    float applied = PerfectSyncBlendShapes.ApplyThreshold(now, settings.trackingPerfectSyncThresholds[i]);
                    applied = PerfectSyncBlendShapes.ApplyEyeLookLimit(i, applied, settings.trackingPerfectSyncEyeLookLimit);
                    label = $"{now:0.00}→{applied:0.00} {label}";
                }

                string key = ThresholdInputPrefix + i;
                inputs.TryGetValue(key, out string input);
                settings.trackingPerfectSyncThresholds[i] = GuiControls.SliderOrField(
                    label, settings.trackingPerfectSyncThresholds[i], 0f, 1f, ref input, key, "F2");
                inputs[key] = input ?? string.Empty;
            }

            return true;
        }

        private static bool DrawDisclosure(string label, bool open)
        {
            UiTheme theme = UiTheme.Current;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(open ? "▼" : "▶", theme != null ? theme.FoldButton : GUI.skin.button))
            {
                open = !open;
            }

            if (GUILayout.Button(label, theme != null ? theme.SubTitle : GUI.skin.label, GUILayout.ExpandWidth(false)))
            {
                open = !open;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return open;
        }

        private static void ClearThresholdInputs(Dictionary<string, string> inputs)
        {
            // 入力途中の文字列を残すと、欄を離したときに 0 へ戻した値を上書きする
            var remove = new List<string>();
            foreach (string key in inputs.Keys)
            {
                if (key.StartsWith(ThresholdInputPrefix, StringComparison.Ordinal))
                {
                    remove.Add(key);
                }
            }

            foreach (string key in remove)
            {
                inputs.Remove(key);
            }
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
