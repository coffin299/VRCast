using UnityEngine;

namespace VRCast.UI
{
    /// <summary>
    /// 各セクションで共通の IMGUI 部品。
    /// </summary>
    public static class GuiControls
    {
        // ラベル列（名前 + 数値）の幅
        private const float LabelWidth = 100f;

        /// <summary>
        /// ラベル + 数値 + スライダーの 1 行を描画し、操作後の値を返す。
        /// </summary>
        public static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label} {value:F2}", GUILayout.Width(LabelWidth));
            float result = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return result;
        }
    }
}
