using System;
using System.Runtime.InteropServices;
using System.Text;
using AOT;

namespace VRCast.Platform
{
    /// <summary>
    /// Unity のプレイヤーウィンドウ（メインスレッドが持つ UnityWndClass のウィンドウ）を探す。
    /// メインスレッドから呼ぶこと。
    /// </summary>
    public static class UnityWindow
    {
        // Unity のプレイヤーウィンドウのクラス名
        private const string WindowClass = "UnityWndClass";

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr lParam);

        // ネイティブ側から呼ばれるコールバック（GC で回収されないよう静的に保持）
        private static readonly EnumWindowsProc EnumCallback = OnEnumWindow;

        // ウィンドウ検索の結果
        private static IntPtr _found;

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr window, StringBuilder name, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetWindowTextW(IntPtr window, string text);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        /// <summary>
        /// 現在のスレッドの ID（ウィンドウのメッセージを処理するスレッド）。
        /// </summary>
        public static uint CurrentThreadId => GetCurrentThreadId();

        /// <summary>
        /// ウィンドウのハンドルを返す。見つからなければ IntPtr.Zero。
        /// </summary>
        public static IntPtr Find()
        {
            // 現在のスレッドのウィンドウを列挙して Unity のウィンドウクラスのものを探す
            _found = IntPtr.Zero;
            EnumThreadWindows(GetCurrentThreadId(), EnumCallback, IntPtr.Zero);
            return _found;
        }

        /// <summary>
        /// ウィンドウのタイトルを変更する。ウィンドウが見つからない・変更に失敗したら false。
        /// </summary>
        public static bool SetTitle(string title)
        {
            // ウィンドウが無ければ何もしない
            IntPtr window = Find();
            return window != IntPtr.Zero && SetWindowTextW(window, title);
        }

        [MonoPInvokeCallback(typeof(EnumWindowsProc))]
        private static bool OnEnumWindow(IntPtr window, IntPtr lParam)
        {
            // Unity のウィンドウクラスなら記録して列挙を終える
            var name = new StringBuilder(64);
            GetClassNameW(window, name, name.Capacity);
            if (name.ToString() == WindowClass)
            {
                _found = window;
                return false;
            }

            return true;
        }
    }
}
