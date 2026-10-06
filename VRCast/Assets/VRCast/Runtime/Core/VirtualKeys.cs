using System;

namespace VRCast.Core
{
    /// <summary>
    /// Windows の仮想キー番号（VK_*）の分類と表示名。ショートカットキーは仮想キーで記録・判定する
    /// （テンキー・記号キー・変換 / 無変換・左右の修飾キーも区別でき、背面でも同じ番号で読めるため）。
    /// </summary>
    public static class VirtualKeys
    {
        /// <summary>
        /// 仮想キー番号の数（0〜255）。
        /// </summary>
        public const int Count = 256;

        // 割り当てに関わる仮想キー
        public const int Tab = 0x09;
        public const int Escape = 0x1B;
        public const int LeftShift = 0xA0;
        public const int RightShift = 0xA1;
        public const int LeftControl = 0xA2;
        public const int RightControl = 0xA3;
        public const int LeftAlt = 0xA4;
        public const int RightAlt = 0xA5;

        // 割り当てに使わない仮想キー（マウスの左右・中ボタン、左右を区別しない修飾キー、IME の内部キー、半角 / 全角）
        private const int LeftButton = 0x01;
        private const int RightButton = 0x02;
        private const int MiddleButton = 0x04;
        private const int AnyShift = 0x10;
        private const int AnyControl = 0x11;
        private const int AnyAlt = 0x12;
        private const int ProcessKey = 0xE5;
        private const int Packet = 0xE7;
        private const int HankakuA = 0xF3;
        private const int HankakuB = 0xF4;

        /// <summary>
        /// 修飾キーの種類（Ctrl / Alt / Shift。それ以外は None）。
        /// </summary>
        public enum Modifier
        {
            None,
            Ctrl,
            Alt,
            Shift,
        }

        /// <summary>
        /// 左右の修飾キーなら種類を返す（それ以外は None）。
        /// </summary>
        public static Modifier ModifierOf(int virtualKey)
        {
            // 左右それぞれの仮想キーだけを修飾キーとして扱う
            switch (virtualKey)
            {
                case LeftControl:
                case RightControl:
                    return Modifier.Ctrl;
                case LeftAlt:
                case RightAlt:
                    return Modifier.Alt;
                case LeftShift:
                case RightShift:
                    return Modifier.Shift;
                default:
                    return Modifier.None;
            }
        }

        /// <summary>
        /// ショートカットのキーに使えるなら true（修飾キー単独も可）。
        /// Esc（割り当てをやめる）・Tab（パネルの表示切替）・マウスの左右 / 中ボタン・半角 / 全角（押すたびに番号が変わる）などは不可。
        /// </summary>
        public static bool IsAssignable(int virtualKey)
        {
            // 範囲外・未割り当て（0 と 255）は不可
            if (virtualKey <= 0 || virtualKey >= Count - 1)
            {
                return false;
            }

            // 予約済み・判定に使えない仮想キーを除く
            switch (virtualKey)
            {
                case LeftButton:
                case RightButton:
                case MiddleButton:
                case Tab:
                case Escape:
                case AnyShift:
                case AnyControl:
                case AnyAlt:
                case ProcessKey:
                case Packet:
                case HankakuA:
                case HankakuB:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 割り当て中に見張る仮想キーなら true（左右を区別しない修飾キーは左右の方で見るため除く）。
        /// </summary>
        public static bool IsWatched(int virtualKey)
        {
            // 0・255 と、左右の区別が付かない修飾キーは見ない
            return virtualKey > 0 && virtualKey < Count - 1
                && virtualKey != AnyShift && virtualKey != AnyControl && virtualKey != AnyAlt;
        }

        /// <summary>
        /// その種類の修飾キー（左右どちらか）が押されていれば true。
        /// </summary>
        public static bool IsModifierDown(Modifier modifier, Func<int, bool> isDown)
        {
            // 種類ごとに左右の仮想キーを見る
            switch (modifier)
            {
                case Modifier.Ctrl:
                    return isDown(LeftControl) || isDown(RightControl);
                case Modifier.Alt:
                    return isDown(LeftAlt) || isDown(RightAlt);
                case Modifier.Shift:
                    return isDown(LeftShift) || isDown(RightShift);
                default:
                    return false;
            }
        }

        /// <summary>
        /// 配列に依らない仮想キーの表示名（記号キーなど配列で変わるものは null）。
        /// </summary>
        public static string Name(int virtualKey)
        {
            // 英字・数字・テンキー・ファンクションキーは連続した範囲
            if (virtualKey >= 'A' && virtualKey <= 'Z')
            {
                return ((char)virtualKey).ToString();
            }

            if (virtualKey >= '0' && virtualKey <= '9')
            {
                return ((char)virtualKey).ToString();
            }

            if (virtualKey >= 0x60 && virtualKey <= 0x69)
            {
                return "Num " + (virtualKey - 0x60);
            }

            if (virtualKey >= 0x70 && virtualKey <= 0x87)
            {
                return "F" + (virtualKey - 0x70 + 1);
            }

            // それ以外は個別の対応表
            switch (virtualKey)
            {
                case 0x03: return "Break";
                case 0x05: return "Mouse 4";
                case 0x06: return "Mouse 5";
                case 0x08: return "Backspace";
                case 0x0C: return "Clear";
                case 0x0D: return "Enter";
                case 0x13: return "Pause";
                case 0x14: return "CapsLock";
                case 0x15: return "かな";
                case 0x19: return "漢字";
                case 0x1C: return "変換";
                case 0x1D: return "無変換";
                case 0x20: return "Space";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "←";
                case 0x26: return "↑";
                case 0x27: return "→";
                case 0x28: return "↓";
                case 0x2C: return "PrintScreen";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x5B: return "Left Win";
                case 0x5C: return "Right Win";
                case 0x5D: return "Menu";
                case 0x6A: return "Num *";
                case 0x6B: return "Num +";
                case 0x6C: return "Num ,";
                case 0x6D: return "Num -";
                case 0x6E: return "Num .";
                case 0x6F: return "Num /";
                case 0x90: return "NumLock";
                case 0x91: return "ScrollLock";
                case LeftShift: return "Left Shift";
                case RightShift: return "Right Shift";
                case LeftControl: return "Left Ctrl";
                case RightControl: return "Right Ctrl";
                case LeftAlt: return "Left Alt";
                case RightAlt: return "Right Alt";
                case 0xAD: return "Mute";
                case 0xAE: return "Volume Down";
                case 0xAF: return "Volume Up";
                case 0xB0: return "Next Track";
                case 0xB1: return "Prev Track";
                case 0xB2: return "Stop";
                case 0xB3: return "Play/Pause";
                case 0xF0: return "英数";
                default: return null;
            }
        }
    }
}
