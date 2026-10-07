using System;
using System.Runtime.InteropServices;
using System.Text;
using VRCast.Core;

namespace VRCast.Platform
{
    /// <summary>
    /// VRCast のウィンドウが前面に無くても読めるキーの状態（Windows の GetAsyncKeyState）と、仮想キーの表示名。
    /// キーをほかのアプリから奪わず、押されているかを見るだけ。
    /// </summary>
    public static class GlobalKeyboard
    {
        // 押されている状態を表すビット
        private const int DownBit = 0x8000;

        // 仮想キー → スキャンコードの変換の種類（MAPVK_VK_TO_VSC）
        private const uint VirtualKeyToScanCode = 0;

        // キー名を受け取る長さ
        private const int NameCapacity = 64;

        // 一度でも呼び出せなかったら以降は使わない（Windows 以外・user32 が無い環境）
        private static bool _unavailable;

        // 配列に依るキー名（記号キーなど）の控え（毎フレームの描画で何度も問い合わせないように）
        private static readonly string[] LayoutNames = new string[VirtualKeys.Count];

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint MapVirtualKeyW(uint code, uint mapType);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetKeyNameTextW(int lParam, StringBuilder name, int size);

        /// <summary>
        /// 使える環境なら true（Windows のプレイヤー・エディター）。
        /// </summary>
        public static bool IsSupported
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return !_unavailable;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 仮想キーが押されていれば true（使えない環境では常に false）。
        /// </summary>
        public static bool IsDown(int virtualKey)
        {
            // 使えない環境では呼ばない
            if (!IsSupported)
            {
                return false;
            }

            try
            {
                return (GetAsyncKeyState(virtualKey) & DownBit) != 0;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                // 以降はショートカットキーを使わない
                _unavailable = true;
                return false;
            }
        }

        /// <summary>
        /// 仮想キーの表示名（配列に依らない名前を優先し、無ければ今のキーボード配列での名前。引けなければ null）。
        /// </summary>
        public static string KeyName(int virtualKey)
        {
            // 決まった名前があればそれを使う
            string name = VirtualKeys.Name(virtualKey);
            if (name != null || virtualKey <= 0 || virtualKey >= VirtualKeys.Count)
            {
                return name;
            }

            // 一度引いた名前は使い回す（空文字 = 引けなかった）
            LayoutNames[virtualKey] ??= LookUpLayoutName(virtualKey);
            return LayoutNames[virtualKey].Length > 0 ? LayoutNames[virtualKey] : null;
        }

        private static string LookUpLayoutName(int virtualKey)
        {
            // 使えない環境では引かない
            if (!IsSupported)
            {
                return string.Empty;
            }

            try
            {
                // 仮想キーをスキャンコードにして、キーボード配列の名前を引く（記号キーなら ";" のような文字）
                uint scanCode = MapVirtualKeyW((uint)virtualKey, VirtualKeyToScanCode);
                if (scanCode == 0)
                {
                    return string.Empty;
                }

                var buffer = new StringBuilder(NameCapacity);
                int length = GetKeyNameTextW((int)(scanCode << 16), buffer, buffer.Capacity);
                return length > 0 ? buffer.ToString() : string.Empty;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                // 名前が引けなくても番号で表示できる
                return string.Empty;
            }
        }
    }
}
