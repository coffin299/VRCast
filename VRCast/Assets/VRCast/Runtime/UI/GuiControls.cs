using System.Collections.Generic;
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

        // 選択行の < > ボタン幅と、項目名の最大表示文字数
        private const float ArrowWidth = 24f;
        private const int MaxOptionLabelLength = 28;

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

        /// <summary>
        /// &lt; &gt; で候補を巡回選択する 1 行を描画し、選択後のインデックスを返す。
        /// noneLabel を渡すと -1（「なし」・既定）も選択肢に含める。候補外のインデックスは「なし」として表示する。
        /// </summary>
        public static int Selector(IReadOnlyList<string> options, int index, string noneLabel)
        {
            // 巡回範囲（「なし」を含むなら -1 から）
            int min = noneLabel != null ? -1 : 0;
            int max = options.Count - 1;

            GUILayout.BeginHorizontal();

            // 前の候補へ（先頭の前は末尾）
            if (GUILayout.Button("<", GUILayout.Width(ArrowWidth)) && max >= min)
            {
                index = index <= min || index > max ? max : index - 1;
            }

            // 現在の候補名（範囲外は「なし」表示）
            string label = index >= 0 && index <= max ? options[index] : noneLabel ?? "(none)";
            if (label.Length > MaxOptionLabelLength)
            {
                label = label.Substring(0, MaxOptionLabelLength - 1) + "…";
            }

            GUILayout.Label(label, GUILayout.ExpandWidth(true));

            // 次の候補へ（末尾の次は先頭）
            if (GUILayout.Button(">", GUILayout.Width(ArrowWidth)) && max >= min)
            {
                index = index >= max || index < min ? min : index + 1;
            }

            GUILayout.EndHorizontal();
            return index;
        }
    }
}
