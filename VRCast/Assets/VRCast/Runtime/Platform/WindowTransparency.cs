using System;
using System.Runtime.InteropServices;
using VRCast.Core;

namespace VRCast.Platform
{
    /// <summary>
    /// アプリのウィンドウ自体を透過させる（alpha 0 の部分からデスクトップや後ろのウィンドウが見える）。
    /// DWM のフレームをクライアント領域全体へ広げ、描画結果の alpha で合成させる（Windows のスタンドアロン実行時のみ）。
    /// DWM は乗算済み alpha として合成するため、透過させる部分は色も黒（0, 0, 0, 0）で描くこと。
    /// D3D11 のフリップモデルのスワップチェーンでは合成されないため、ビルド設定で無効にしておく。
    /// </summary>
    public static class WindowTransparency
    {
        // ログのカテゴリ名
        private const string LogCategory = "Window";

        // DWM のフレームの幅（全辺 -1 でクライアント領域全体、0 で元に戻す）
        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;

            public Margins(int all)
            {
                Left = all;
                Right = all;
                Top = all;
                Bottom = all;
            }
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        // 透過対象のウィンドウ（初回に探す）と現在の状態
        private static IntPtr _window;
        private static bool _transparent;
#endif

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);

        /// <summary>
        /// ウィンドウの透過を切り替える。Editor・Windows 以外では何もしない。
        /// </summary>
        public static void SetTransparent(bool transparent)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // 状態が変わらなければ何もしない
            if (transparent == _transparent)
            {
                return;
            }

            // ウィンドウを探す（見つからなければ透過できない）
            if (_window == IntPtr.Zero)
            {
                _window = UnityWindow.Find();
                if (_window == IntPtr.Zero)
                {
                    VRCastLog.Warning(LogCategory, "Unity window not found. Window transparency is disabled.");
                    return;
                }
            }

            // フレームをクライアント領域全体へ広げる / 元に戻す
            var margins = new Margins(transparent ? -1 : 0);
            int result = DwmExtendFrameIntoClientArea(_window, ref margins);
            if (result != 0)
            {
                VRCastLog.Warning(LogCategory, $"DwmExtendFrameIntoClientArea failed (0x{result:X8}).");
                return;
            }

            _transparent = transparent;
#endif
        }
    }
}
