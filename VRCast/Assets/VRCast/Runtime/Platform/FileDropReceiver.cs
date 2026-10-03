using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using AOT;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Platform
{
    /// <summary>
    /// エクスプローラーからアプリのウィンドウへドロップされたファイルを受け取る（Windows のスタンドアロン実行時のみ）。
    /// ウィンドウにドロップを許可し、メインスレッドのメッセージを WH_GETMESSAGE フックで監視して WM_DROPFILES を取り出す。
    /// フックはメッセージ処理中に呼ばれるため、受け取ったパスは溜めておき Update で通知する。
    /// </summary>
    public class FileDropReceiver : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "FileDrop";

        // Win32 定数（フック種別・処理対象・取り出し済みメッセージ・ドロップ通知・件数取得・パス長）
        private const int WhGetMessage = 3;
        private const int HcAction = 0;
        private const int PmRemove = 1;
        private const int WmDropFiles = 0x0233;
        private const int WmNull = 0x0000;
        private const uint QueryCount = 0xFFFFFFFF;
        private const int MaxPathLength = 32768;

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        // ネイティブ側から呼ばれるコールバック（GC で回収されないよう静的に保持）
        private static readonly HookProc HookCallback = OnHook;

        // フックから Update へ渡すドロップ済みパス（メインスレッドのみで使う）
        private static readonly List<string> Pending = new List<string>();

        private IntPtr _window;
        private IntPtr _hook;

        /// <summary>
        /// ファイル（フォルダを含む）がドロップされたときに、ドロップされた全パスを通知する。
        /// </summary>
        public event Action<IReadOnlyList<string>> FilesDropped;

        /// <summary>
        /// この環境でドロップを受け取れるなら true。
        /// </summary>
        public bool IsAvailable => _hook != IntPtr.Zero;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookExW(int hookId, HookProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string moduleName);

        [DllImport("shell32.dll")]
        private static extern void DragAcceptFiles(IntPtr window, bool accept);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFileW(IntPtr drop, uint index, StringBuilder file, uint size);

        [DllImport("shell32.dll")]
        private static extern void DragFinish(IntPtr drop);

        private void OnEnable()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Register();
#endif
        }

        private void OnDisable()
        {
            Unregister();
        }

        private void Update()
        {
            // 溜まっていなければ何もしない
            if (Pending.Count == 0)
            {
                return;
            }

            // 通知中に次のドロップが来ても混ざらないよう写してから空にする
            var paths = new List<string>(Pending);
            Pending.Clear();
            FilesDropped?.Invoke(paths);
        }

        private void Register()
        {
            // メインスレッド（ウィンドウのメッセージを処理するスレッド）の Unity ウィンドウを探す
            uint threadId = UnityWindow.CurrentThreadId;
            _window = UnityWindow.Find();
            if (_window == IntPtr.Zero)
            {
                VRCastLog.Warning(LogCategory, "Unity window not found. Drag and drop is disabled.");
                return;
            }

            // ドロップを許可し、このスレッドのメッセージだけを監視する
            DragAcceptFiles(_window, true);
            _hook = SetWindowsHookExW(WhGetMessage, HookCallback, GetModuleHandleW(null), threadId);
            if (_hook == IntPtr.Zero)
            {
                VRCastLog.Warning(LogCategory, $"SetWindowsHookEx failed ({Marshal.GetLastWin32Error()}).");
                DragAcceptFiles(_window, false);
            }
        }

        private void Unregister()
        {
            // フックを外してドロップの許可を戻す（未登録なら何もしない）
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            if (_window != IntPtr.Zero)
            {
                DragAcceptFiles(_window, false);
                _window = IntPtr.Zero;
            }
        }

        [MonoPInvokeCallback(typeof(HookProc))]
        private static IntPtr OnHook(int code, IntPtr wParam, IntPtr lParam)
        {
            // キューから取り出された（PM_REMOVE）WM_DROPFILES だけを処理する（覗き見の段階では二重に処理しない）
            if (code == HcAction && wParam.ToInt64() == PmRemove && lParam != IntPtr.Zero)
            {
                // MSG 構造体: hwnd, message, wParam, ...（message は hwnd の直後、wParam は 64 bit で 8 バイト境界）
                int message = Marshal.ReadInt32(lParam, IntPtr.Size);
                if (message == WmDropFiles)
                {
                    IntPtr drop = Marshal.ReadIntPtr(lParam, IntPtr.Size * 2);
                    ReadDroppedFiles(drop);

                    // ハンドルは解放済みなのでウィンドウ側では処理させない
                    Marshal.WriteInt32(lParam, IntPtr.Size, WmNull);
                }
            }

            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        private static void ReadDroppedFiles(IntPtr drop)
        {
            // 件数を取得して 1 件ずつパスを読む
            uint count = DragQueryFileW(drop, QueryCount, null, 0);
            var buffer = new StringBuilder(MaxPathLength);
            for (uint i = 0; i < count; i++)
            {
                buffer.Clear();
                if (DragQueryFileW(drop, i, buffer, (uint)buffer.Capacity) > 0)
                {
                    Pending.Add(buffer.ToString());
                }
            }

            // ドロップのハンドルを解放
            DragFinish(drop);
        }
    }
}
