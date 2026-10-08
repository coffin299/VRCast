using System;

namespace VRCast.Core
{
    /// <summary>
    /// アバターごとに覚える 1 つの表情のショートカットキー（表情プリセット名・Windows の仮想キー・同時に押す Ctrl / Alt / Shift）。
    /// </summary>
    [Serializable]
    public class ExpressionHotkey : HotkeyBinding
    {
        /// <summary>
        /// ニュートラルを表すプリセット名（プリセット名には使われない文字列）。
        /// </summary>
        public const string NeutralPreset = "<neutral>";

        public string preset = string.Empty;

        /// <summary>
        /// 保存できる値なら true（プリセット名があり、キーが割り当てに使えるもの）。
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(preset) && VirtualKeys.IsAssignable(virtualKey);

        /// <summary>
        /// 組み合わせから保存用の値を作る。
        /// </summary>
        public static ExpressionHotkey From(string preset, KeyCombo combo)
        {
            var hotkey = new ExpressionHotkey { preset = preset };
            hotkey.SetCombo(combo);
            return hotkey;
        }
    }
}
