using System.Collections.Generic;
using UnityEngine;

namespace VRCast.UI
{
    /// <summary>
    /// 各セクションで共通の IMGUI 部品（カード、ラベル付きスライダー、巡回選択、補足文）。
    /// 見た目は UiTheme.Current（未作成なら既定スキン）に従う。
    /// </summary>
    public static class GuiControls
    {
        // ラベル列と数値列の幅
        private const float LabelWidth = 130f;
        private const float ValueWidth = 44f;

        // 選択行の < > ボタン幅と、項目名の最大表示文字数
        private const float ArrowWidth = 30f;
        private const int MaxOptionLabelLength = 32;

        /// <summary>
        /// 横に並べるボタン用の配置指定。行の幅が足りないときに文字の幅より縮めて、パネルの右へはみ出さないようにする。
        /// </summary>
        public static readonly GUILayoutOption Shrinkable = GUILayout.MinWidth(0f);

        /// <summary>
        /// 見出し付きのカードを開始する（EndCard で閉じる）。
        /// </summary>
        public static void BeginCard(string title)
        {
            UiTheme theme = UiTheme.Current;
            GUILayout.BeginVertical(theme != null ? theme.Card : GUI.skin.box);
            GUILayout.Label(title, theme != null ? theme.SectionTitle : GUI.skin.label);
        }

        public static void EndCard()
        {
            GUILayout.EndVertical();
        }

        /// <summary>
        /// カード内の項目のまとまりを区切る小見出し。
        /// </summary>
        public static void SubHeading(string text)
        {
            UiTheme theme = UiTheme.Current;
            GUILayout.Label(text, theme != null ? theme.SubTitle : GUI.skin.label);
        }

        /// <summary>
        /// 手順のカードを開始する（「1. 見出し」と、右端に完了 / 未完了の表示。EndCard で閉じる）。
        /// done が null の手順（アプリ側で確認できない操作）は状態を出さない。
        /// </summary>
        public static void BeginStep(int number, string title, bool? done)
        {
            UiTheme theme = UiTheme.Current;
            GUILayout.BeginVertical(theme != null ? theme.Card : GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{number}. {title}", theme != null ? theme.SectionTitle : GUI.skin.label);
            GUILayout.FlexibleSpace();

            // 完了は緑、未完了は控えめな文字
            if (done == true)
            {
                GUILayout.Label(Loc.T("Done", "完了", "완료", "已完成", "已完成"), theme != null ? theme.Success : GUI.skin.label);
            }
            else if (done == false)
            {
                GUILayout.Label(Loc.T("To do", "未完了", "미완료", "未完成", "未完成"), theme != null ? theme.Hint : GUI.skin.label);
            }

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 取り返しのつかない操作用の赤いボタン。押されたら true。
        /// </summary>
        public static bool DangerButton(string text)
        {
            UiTheme theme = UiTheme.Current;
            return GUILayout.Button(text, theme != null ? theme.Danger : GUI.skin.button);
        }

        /// <summary>
        /// 補足・状態表示用の控えめな文字。
        /// </summary>
        public static void Hint(string text)
        {
            UiTheme theme = UiTheme.Current;
            GUILayout.Label(text, theme != null ? theme.Hint : GUI.skin.label);
        }

        /// <summary>
        /// ラベル + ボタンの 1 行を描画し、ボタンが押されたら true。
        /// </summary>
        public static bool LabeledButton(string label, string text)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            bool pressed = GUILayout.Button(text, Shrinkable);
            GUILayout.EndHorizontal();
            return pressed;
        }

        /// <summary>
        /// ラベル + スライダー + 数値（format の書式）の 1 行を描画し、操作後の値を返す。
        /// </summary>
        public static float Slider(string label, float value, float min, float max, string format = "F2")
        {
            UiTheme theme = UiTheme.Current;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            float result = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(result.ToString(format), theme != null ? theme.Value : GUI.skin.label, GUILayout.Width(ValueWidth));
            GUILayout.EndHorizontal();
            return result;
        }

        /// <summary>
        /// ラベル + &lt; 候補名 &gt; の 1 行を描画し、選択後のインデックスを返す。
        /// noneLabel を渡すと -1（「なし」・既定）も選択肢に含める。候補外のインデックスは「なし」として表示する。
        /// </summary>
        public static int Selector(string label, IReadOnlyList<string> options, int index, string noneLabel)
        {
            // 巡回範囲（「なし」を含むなら -1 から）
            int min = noneLabel != null ? -1 : 0;
            int max = options.Count - 1;

            GUILayout.BeginHorizontal();
            if (label != null)
            {
                GUILayout.Label(label, GUILayout.Width(LabelWidth));
            }

            // 前の候補へ（先頭の前は末尾）
            if (GUILayout.Button("<", GUILayout.Width(ArrowWidth)) && max >= min)
            {
                index = index <= min || index > max ? max : index - 1;
            }

            // 現在の候補名（範囲外は「なし」表示、長い名前は省略）
            string text = index >= 0 && index <= max ? options[index] : noneLabel ?? "-";
            if (text.Length > MaxOptionLabelLength)
            {
                text = text.Substring(0, MaxOptionLabelLength - 1) + "…";
            }

            UiTheme theme = UiTheme.Current;
            GUILayout.Label(text, theme != null ? theme.Centered : GUI.skin.label, GUILayout.ExpandWidth(true), Shrinkable);

            // 次の候補へ（末尾の次は先頭）
            if (GUILayout.Button(">", GUILayout.Width(ArrowWidth)) && max >= min)
            {
                index = index >= max || index < min ? min : index + 1;
            }

            GUILayout.EndHorizontal();
            return index;
        }

        /// <summary>
        /// 列挙値を巡回選択する（labels は列挙値の並び順）。
        /// </summary>
        public static int EnumSelector(string label, string[] labels, int current)
        {
            // 範囲外の選択（「なし」）は起こらないので、変わったときだけ新しい値を返す
            int selected = Selector(label, labels, current, null);
            return selected >= 0 ? selected : current;
        }
    }
}
