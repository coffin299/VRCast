using System;
using System.Collections.Generic;

namespace VRCast.Core
{
    /// <summary>
    /// ショートカットキーで行うリセット（パネル下部のリセットボタンと同じ操作。設定に数値で保存するため並びは変えない）。
    /// </summary>
    public enum ResetAction
    {
        // 顔の向き・上半身・視線を今の向きで正面にする
        Head = 0,

        // 視線だけを正面にする
        Gaze = 1,

        // 表情をニュートラルへ戻し、手動の固定も外す
        Expression = 2,

        // カメラを正面の既定位置へ
        Camera = 3,
    }

    /// <summary>
    /// アプリ全体で覚えるリセットのショートカットキー（virtualKey = 0 なら未割り当て）。
    /// </summary>
    [Serializable]
    public class ResetHotkey : HotkeyBinding
    {
        /// <summary>
        /// リセットの種類の数（ResetAction の値は 0 から連続）。
        /// </summary>
        public const int ActionCount = 4;

        // テンキーの 1（VK_NUMPAD1）。既定は 1 から順に割り当てる
        private const int Numpad1 = 0x61;

        public ResetAction action;

        /// <summary>
        /// 既定の割り当て（顔の向き = テンキー 1、視線 = 2、表情 = 3、カメラ = 4）。
        /// </summary>
        public static List<ResetHotkey> Defaults()
        {
            var hotkeys = new List<ResetHotkey>(ActionCount);
            for (int i = 0; i < ActionCount; i++)
            {
                var hotkey = new ResetHotkey { action = (ResetAction)i };
                hotkey.SetCombo(DefaultCombo(hotkey.action));
                hotkeys.Add(hotkey);
            }

            return hotkeys;
        }

        /// <summary>
        /// 既定の組み合わせ（テンキーの番号順）。
        /// </summary>
        public static KeyCombo DefaultCombo(ResetAction action)
        {
            return new KeyCombo(Numpad1 + (int)action, false, false, false);
        }
    }
}
