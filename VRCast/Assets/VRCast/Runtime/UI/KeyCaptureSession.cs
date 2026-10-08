using System;
using UnityEngine;
using VRCast.Core;
using VRCast.Platform;

namespace VRCast.UI
{
    /// <summary>
    /// 操作パネルでのショートカットキーの割り当て（表情・リセットで共用）。キーのボタンを押すと割り当て待ちになり、
    /// 押したキー（Ctrl / Alt / Shift との組み合わせも可）で決まる。Esc・同じボタン・タブの切替・パネルを隠すとやめる。
    /// 割り当て中はほかのショートカットとパネルの Tab 切替を止める。
    /// </summary>
    public sealed class KeyCaptureSession
    {
        // 仮想キーの押下と表示名（描画のたびにデリゲートを作り直さないように控える）
        private static readonly Func<int, bool> IsKeyDown = GlobalKeyboard.IsDown;
        private static readonly Func<int, string> KeyName = GlobalKeyboard.KeyName;

        // 割り当て中か、その対象（呼び出し側が決める番号）、最後に描いたフレーム、最後にキーを読んだフレーム
        private bool _capturing;
        private int _target;
        private int _lastDrawFrame = -1;
        private int _lastPollFrame = -1;
        private KeyCapture _keyCapture = new KeyCapture();

        /// <summary>
        /// 割り当て待ちの最中なら true。
        /// </summary>
        public bool IsCapturing => _capturing;

        /// <summary>
        /// 割り当て中の操作の案内（表示言語に合わせる）。
        /// </summary>
        public static string AssignHint => Loc.T(
            "Press the key to assign (with Ctrl / Alt / Shift if you like). To use Ctrl, Alt or Shift alone, press and release it. Esc to cancel (Tab cannot be used).",
            "割り当てるキーを押してください（Ctrl・Alt・Shift と同時押しも可）。Ctrl・Alt・Shift だけを使うときは押して離します。Esc でやめます（Tab は使えません）。",
            "할당할 키를 누르세요 (Ctrl·Alt·Shift와 동시에 눌러도 됩니다). Ctrl·Alt·Shift만 쓰려면 눌렀다 떼세요. Esc로 취소합니다 (Tab은 사용할 수 없습니다).",
            "请按下要分配的键（也可同时按 Ctrl / Alt / Shift）。单独使用 Ctrl / Alt / Shift 时请按下后松开。按 Esc 取消（不能使用 Tab）。",
            "請按下要分配的鍵（也可同時按 Ctrl / Alt / Shift）。單獨使用 Ctrl / Alt / Shift 時請按下後放開。按 Esc 取消（不能使用 Tab）。");

        /// <summary>
        /// 組み合わせの表示名（未割り当てなら「未割り当て」）。
        /// </summary>
        public static string Describe(KeyCombo combo)
        {
            return combo.IsAssigned
                ? combo.Describe(KeyName)
                : Loc.T("Unassigned", "未割り当て", "미할당", "未分配", "未分配");
        }

        /// <summary>
        /// 割り当てをやめる。
        /// </summary>
        public void Cancel()
        {
            _capturing = false;
        }

        /// <summary>
        /// 対象のキーのボタンを描く（押すと割り当て開始、割り当て中の対象をもう一度押すとやめる）。
        /// </summary>
        public void DrawKeyButton(int target, KeyCombo combo, float width)
        {
            bool capturingThis = _capturing && _target == target;
            string label = capturingThis
                ? Loc.T("Press a key…", "キーを押す…", "키를 누르세요…", "请按键…", "請按鍵…")
                : Describe(combo);
            if (GUILayout.Button(label, GUILayout.Width(width)))
            {
                _capturing = !capturingThis;
                _target = target;

                // 割り当て待ちは毎回まっさらから始める（前回の押下状態を引き継がない）
                _keyCapture = new KeyCapture();
            }
        }

        /// <summary>
        /// 一覧を描くたびに最初に呼ぶ。組み合わせが決まったら対象と組み合わせを返して true。
        /// </summary>
        public bool Update(out int target, out KeyCombo combo)
        {
            target = _target;
            combo = KeyCombo.None;

            // 別のタブにいた・パネルを隠していた等で描画が途切れていたら、割り当てをやめる
            bool interrupted = Time.frameCount - _lastDrawFrame > 1;
            _lastDrawFrame = Time.frameCount;
            if (!_capturing || interrupted)
            {
                _capturing = false;
                return false;
            }

            // 割り当て中は、押したキーで表情・リセットが動いたりパネルが消えたりしないようにする
            HotkeyPoller.Suspend();

            // 割り当て中のキー入力は、ほかの GUI に渡さない
            Event current = Event.current;
            if (current.type == EventType.KeyDown || current.type == EventType.KeyUp)
            {
                current.Use();
            }

            // キーの状態は 1 フレームに 1 回だけ読む（OnGUI はイベントごとに何度も呼ばれる）
            if (_lastPollFrame == Time.frameCount)
            {
                return false;
            }

            _lastPollFrame = Time.frameCount;

            // Esc でやめ、組み合わせが決まったら終わる（Windows のキーの状態で読むので、テンキーや記号キーも区別できる）
            switch (_keyCapture.Poll(IsKeyDown))
            {
                case KeyCapture.Status.Cancelled:
                    _capturing = false;
                    return false;
                case KeyCapture.Status.Captured:
                    _capturing = false;
                    combo = _keyCapture.Result;
                    return true;
                default:
                    return false;
            }
        }
    }
}
