using System;

namespace VRCast.Core
{
    /// <summary>
    /// ショートカットキーの割り当て待ち。毎フレーム Poll でキーの状態を渡すと、押された組み合わせを決める。
    /// 修飾キー以外が押されたら、そのとき押している Ctrl / Alt / Shift と合わせて決める。
    /// 修飾キーだけを押して離したら、その修飾キー単独（先に押していた修飾キーがあれば組み合わせ）で決める。
    /// Esc でやめる。使えないキー（Tab など）を押すと、修飾キー単独の候補を取り消す。
    /// </summary>
    public sealed class KeyCapture
    {
        /// <summary>
        /// 割り当て待ちの状態。
        /// </summary>
        public enum Status
        {
            Waiting,
            Cancelled,
            Captured,
        }

        // 前回のキーの状態（押されたか・離されたかを見分ける）
        private readonly bool[] _wasDown = new bool[VirtualKeys.Count];

        // 初回は今の状態を覚えるだけ（開始時に押しっぱなしのキー・クリックしたマウスで決めないように）
        private bool _primed;

        // 修飾キー単独の候補（0 = 無し）
        private KeyCombo _pendingModifier;

        /// <summary>
        /// 決まった組み合わせ（Captured のときだけ有効）。
        /// </summary>
        public KeyCombo Result { get; private set; }

        /// <summary>
        /// キーの状態を読んで進める（isDown は仮想キーが押されているか）。
        /// </summary>
        public Status Poll(Func<int, bool> isDown)
        {
            for (int key = 0; key < VirtualKeys.Count; key++)
            {
                // 見張らない仮想キーは飛ばす
                if (!VirtualKeys.IsWatched(key))
                {
                    continue;
                }

                // 押された瞬間・離された瞬間を見分け、状態を覚え直す
                bool down = isDown(key);
                bool pressed = _primed && down && !_wasDown[key];
                bool released = _primed && !down && _wasDown[key];
                _wasDown[key] = down;

                if (pressed)
                {
                    // Esc はやめる
                    if (key == VirtualKeys.Escape)
                    {
                        return Status.Cancelled;
                    }

                    // 使えないキーは、修飾キー単独の候補を取り消すだけ（Alt+Tab で Alt が割り当てられないように）
                    if (!VirtualKeys.IsAssignable(key))
                    {
                        _pendingModifier = KeyCombo.None;
                        continue;
                    }

                    // そのとき押している修飾キーとの組み合わせ
                    var combo = new KeyCombo(key,
                        VirtualKeys.IsModifierDown(VirtualKeys.Modifier.Ctrl, isDown),
                        VirtualKeys.IsModifierDown(VirtualKeys.Modifier.Alt, isDown),
                        VirtualKeys.IsModifierDown(VirtualKeys.Modifier.Shift, isDown));

                    // 修飾キーは離すまで候補にとどめ（続けてほかのキーを押すかもしれない）、それ以外はすぐ決める
                    if (VirtualKeys.ModifierOf(key) != VirtualKeys.Modifier.None)
                    {
                        _pendingModifier = combo;
                        continue;
                    }

                    Result = combo;
                    return Status.Captured;
                }

                // 候補の修飾キーが、ほかのキーを挟まずに離されたら単独で決める
                if (released && _pendingModifier.IsAssigned && key == _pendingModifier.VirtualKey)
                {
                    Result = _pendingModifier;
                    return Status.Captured;
                }
            }

            _primed = true;
            return Status.Waiting;
        }
    }
}
