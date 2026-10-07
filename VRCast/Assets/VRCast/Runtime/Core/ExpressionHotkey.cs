using System;

namespace VRCast.Core
{
    /// <summary>
    /// アバターごとに覚える 1 つの表情のショートカットキー（表情プリセット名・Windows の仮想キー・同時に押す Ctrl / Alt / Shift）。
    /// </summary>
    [Serializable]
    public class ExpressionHotkey
    {
        /// <summary>
        /// ニュートラルを表すプリセット名（プリセット名には使われない文字列）。
        /// </summary>
        public const string NeutralPreset = "<neutral>";

        public string preset = string.Empty;
        public int virtualKey;
        public bool ctrl;
        public bool alt;
        public bool shift;

        /// <summary>
        /// 保存できる値なら true（プリセット名があり、キーが割り当てに使えるもの）。
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(preset) && VirtualKeys.IsAssignable(virtualKey);

        /// <summary>
        /// キーと修飾キーの組み合わせ。
        /// </summary>
        public KeyCombo Combo => new KeyCombo(virtualKey, ctrl, alt, shift);

        /// <summary>
        /// 組み合わせから保存用の値を作る。
        /// </summary>
        public static ExpressionHotkey From(string preset, KeyCombo combo)
        {
            return new ExpressionHotkey
            {
                preset = preset,
                virtualKey = combo.VirtualKey,
                ctrl = combo.Ctrl,
                alt = combo.Alt,
                shift = combo.Shift,
            };
        }
    }
}
