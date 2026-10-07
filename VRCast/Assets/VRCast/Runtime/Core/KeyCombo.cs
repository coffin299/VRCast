using System;
using System.Text;

namespace VRCast.Core
{
    /// <summary>
    /// ショートカットキーの組み合わせ（Windows の仮想キー 1 つと、同時に押す Ctrl / Alt / Shift）。
    /// 修飾キーは完全一致で判定する（Ctrl+N に割り当てた表情は N だけ・Ctrl+Shift+N では切り替わらない）。
    /// キー自体が修飾キー（例: Right Ctrl）のときは、その種類の修飾キーは条件に含めない。
    /// </summary>
    public readonly struct KeyCombo : IEquatable<KeyCombo>
    {
        /// <summary>
        /// 未割り当て。
        /// </summary>
        public static readonly KeyCombo None = default;

        public readonly int VirtualKey;
        public readonly bool Ctrl;
        public readonly bool Alt;
        public readonly bool Shift;

        public KeyCombo(int virtualKey, bool ctrl, bool alt, bool shift)
        {
            // キー自体の種類の修飾キーは押されていて当然なので、条件から外して比較をそろえる
            VirtualKeys.Modifier own = VirtualKeys.ModifierOf(virtualKey);
            VirtualKey = virtualKey;
            Ctrl = ctrl && own != VirtualKeys.Modifier.Ctrl;
            Alt = alt && own != VirtualKeys.Modifier.Alt;
            Shift = shift && own != VirtualKeys.Modifier.Shift;
        }

        /// <summary>
        /// 割り当てられていれば true。
        /// </summary>
        public bool IsAssigned => VirtualKey != 0;

        /// <summary>
        /// 今この組み合わせが押されていれば true（isDown は仮想キーが押されているか）。
        /// </summary>
        public bool IsDown(Func<int, bool> isDown)
        {
            // 未割り当て・キーが離れていれば押されていない
            if (!IsAssigned || !isDown(VirtualKey))
            {
                return false;
            }

            // キー自体の種類を除き、修飾キーの押下が割り当てと完全に一致すること
            VirtualKeys.Modifier own = VirtualKeys.ModifierOf(VirtualKey);
            return Matches(VirtualKeys.Modifier.Ctrl, Ctrl, own, isDown)
                && Matches(VirtualKeys.Modifier.Alt, Alt, own, isDown)
                && Matches(VirtualKeys.Modifier.Shift, Shift, own, isDown);
        }

        /// <summary>
        /// 表示用の文字列（例: "Ctrl+Alt+N"、"Num 1"、"Right Ctrl"）。
        /// keyName は仮想キーの名前を返す関数（null なら配列に依らない名前、無ければ番号）。
        /// </summary>
        public string Describe(Func<int, string> keyName = null)
        {
            // 修飾キーを Ctrl → Alt → Shift の順に前置する
            var text = new StringBuilder();
            if (Ctrl)
            {
                text.Append("Ctrl+");
            }

            if (Alt)
            {
                text.Append("Alt+");
            }

            if (Shift)
            {
                text.Append("Shift+");
            }

            // キーの名前（引けなければ番号で示す）
            string name = keyName != null ? keyName(VirtualKey) : VirtualKeys.Name(VirtualKey);
            return text.Append(string.IsNullOrEmpty(name) ? $"Key 0x{VirtualKey:X2}" : name).ToString();
        }

        public bool Equals(KeyCombo other)
        {
            return VirtualKey == other.VirtualKey && Ctrl == other.Ctrl && Alt == other.Alt && Shift == other.Shift;
        }

        public override bool Equals(object obj)
        {
            return obj is KeyCombo other && Equals(other);
        }

        public override int GetHashCode()
        {
            // キーの番号と修飾キーのビットを組み合わせる
            return (VirtualKey << 3) | (Ctrl ? 1 : 0) | (Alt ? 2 : 0) | (Shift ? 4 : 0);
        }

        private static bool Matches(VirtualKeys.Modifier modifier, bool required, VirtualKeys.Modifier own,
            Func<int, bool> isDown)
        {
            // キー自体の種類は問わず、それ以外は押下が割り当てと一致すること
            return modifier == own || required == VirtualKeys.IsModifierDown(modifier, isDown);
        }
    }
}
