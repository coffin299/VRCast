using System;

namespace VRCast.Core
{
    /// <summary>
    /// 保存するショートカットキー 1 つ分（Windows の仮想キーと、同時に押す Ctrl / Alt / Shift）。表情・リセットで共用する。
    /// </summary>
    [Serializable]
    public class HotkeyBinding
    {
        public int virtualKey;
        public bool ctrl;
        public bool alt;
        public bool shift;

        /// <summary>
        /// キーと修飾キーの組み合わせ。
        /// </summary>
        public KeyCombo Combo => new KeyCombo(virtualKey, ctrl, alt, shift);

        /// <summary>
        /// 組み合わせを書き込む（None で未割り当て）。
        /// </summary>
        public void SetCombo(KeyCombo combo)
        {
            virtualKey = combo.VirtualKey;
            ctrl = combo.Ctrl;
            alt = combo.Alt;
            shift = combo.Shift;
        }
    }
}
