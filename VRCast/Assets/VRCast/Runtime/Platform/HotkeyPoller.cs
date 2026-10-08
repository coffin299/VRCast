using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Platform
{
    /// <summary>
    /// ショートカットキーの押下判定（表情・リセットで共用）。毎フレーム Poll に組み合わせの一覧を渡すと、
    /// 押された瞬間の組み合わせの位置を返す（押しっぱなしでは繰り返さない）。
    /// 前面のときは VRCast 内のテキスト入力中は反応せず、背面では background が true のときだけ反応する。
    /// キーの割り当て中（Suspend）は全てのショートカットとパネルの Tab 切替を止める。
    /// </summary>
    public sealed class HotkeyPoller
    {
        // キーの割り当て中にショートカットを止めたフレーム（割り当てるキーで表情・リセットが動かないように）
        private static int _suspendedFrame = int.MinValue / 2;

        // 仮想キーが押されているか（毎フレームの判定でデリゲートを作り直さないように控える）
        private static readonly Func<int, bool> IsKeyDown = GlobalKeyboard.IsDown;

        // 前のフレームで押されていたか（一覧の位置ごと）
        private bool[] _wasDown = Array.Empty<bool>();

        /// <summary>
        /// キーの割り当て中なら true（前のフレームまでに止められた。パネルの Tab 切替もこれを見て止める）。
        /// </summary>
        public static bool Suspended => _suspendedFrame >= Time.frameCount - 1;

        /// <summary>
        /// このフレームと次のフレームのショートカットを止める（割り当て中は毎フレーム呼ぶ）。
        /// </summary>
        public static void Suspend()
        {
            _suspendedFrame = Time.frameCount;
        }

        /// <summary>
        /// 押された瞬間の組み合わせの位置を返す（無ければ -1。同時なら先の方）。
        /// background が true なら VRCast が背面でも反応する。
        /// </summary>
        public int Poll(IReadOnlyList<KeyCombo> combos, bool background)
        {
            // 一覧の長さが変わったら押下状態を作り直す（アバターの切替で表情の数が変わる）
            if (_wasDown.Length != combos.Count)
            {
                _wasDown = new bool[combos.Count];
            }

            // 前面のときだけ VRCast 内のテキスト入力を気にする（背面では入力欄に文字は入らない）
            bool focused = Application.isFocused;
            bool blocked = Suspended || (focused && GUIUtility.keyboardControl != 0);

            // Windows のキーの状態を、前面か背面でも使う設定のときだけ読む
            bool active = GlobalKeyboard.IsSupported && (focused || background);

            int pressed = -1;
            for (int i = 0; i < combos.Count; i++)
            {
                // 押された瞬間（前のフレームは離れていた）だけ反応する
                bool down = active && combos[i].IsDown(IsKeyDown);
                if (down && !_wasDown[i] && !blocked && pressed < 0)
                {
                    pressed = i;
                }

                // 止めている間も押下状態は追い続ける（割り当て直後に、離すまで反応しないように）
                _wasDown[i] = down;
            }

            return pressed;
        }
    }
}
