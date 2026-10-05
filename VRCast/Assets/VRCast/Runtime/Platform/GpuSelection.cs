using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Platform
{
    /// <summary>
    /// 描画に使う GPU の選択。どちらも次回起動から反映される。
    /// - Windows の優先設定（自動 / 省電力 / 高パフォーマンス）: 「グラフィックの設定」と同じレジストリ値を VRCast.exe について書く。
    /// - GPU の直接指定: 起動時に指定の GPU でなければ、Unity の起動引数（-force-device-index / -adapter）を付けて起動し直す。
    /// エディターでは Unity エディター自体の設定を変えないよう何もしない。
    /// </summary>
    public static class GpuSelection
    {
        // ログのカテゴリ名
        private const string LogCategory = "GPU";

        // Windows の「グラフィックの設定」の保存先（HKEY_CURRENT_USER 配下、値の名前 = 実行ファイルのフルパス）
        private const string PreferencesKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
        private const string PreferenceName = "GpuPreference";

        // GPU を指定する Unity の起動引数（D3D12 / Vulkan は前者、D3D11 は後者で GPU が決まる）
        private const string ForceDeviceArgument = "-force-device-index";
        private const string AdapterArgument = "-adapter";

        // HKEY_CURRENT_USER、REG_SZ、読み書きの権限、RegGetValue の文字列型指定、値が無いときのエラー番号
        private static readonly IntPtr CurrentUser = new IntPtr(unchecked((int)0x80000001));
        private const uint StringType = 1;
        private const uint KeyAllAccess = 0xF003F;
        private const uint StringValueOnly = 0x00000002;
        private const int ErrorFileNotFound = 2;

        // レジストリ値の最大長（文字）
        private const int MaxValueLength = 1024;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegGetValueW(
            IntPtr key, string subKey, string valueName, uint flags, IntPtr type, StringBuilder data, ref uint size);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegCreateKeyExW(IntPtr key, string subKey, uint reserved, string className,
            uint options, uint desired, IntPtr security, out IntPtr result, IntPtr disposition);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegSetValueExW(
            IntPtr key, string valueName, uint reserved, uint type, string data, uint size);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegDeleteValueW(IntPtr key, string valueName);

        [DllImport("advapi32.dll")]
        private static extern int RegCloseKey(IntPtr key);

        /// <summary>
        /// 起動直後に別の GPU で起動し直すため、この起動では何も始めない（AppRoot は生成しない）。
        /// </summary>
        public static bool IsRelaunching { get; private set; }

        /// <summary>
        /// この起動で反映されている設定（UI で「再起動すると反映」を出すための比較用）。
        /// </summary>
        public static GpuPreference StartupPreference { get; private set; }
        public static string StartupAdapter { get; private set; } = string.Empty;

        /// <summary>
        /// 直接指定した GPU に切り替えられなかったとき true（描画 API・ドライバーが起動引数に対応していない等）。
        /// </summary>
        public static bool AdapterNotApplied { get; private set; }

        /// <summary>
        /// 設定が変わって再起動待ちなら true。
        /// </summary>
        public static bool RestartPending(AppSettings settings)
        {
            return settings.gpuPreference != StartupPreference || settings.gpuAdapter != StartupAdapter;
        }

        /// <summary>
        /// 起動時（シーン読込前）に呼ぶ。指定の GPU で動いていなければ起動し直して true を返す（呼び出し側は以降の初期化をやめる）。
        /// </summary>
        public static bool RelaunchIfNeeded(AppSettings settings)
        {
            // この起動の設定を記録
            StartupPreference = settings.gpuPreference;
            StartupAdapter = settings.gpuAdapter;

            // エディター・直接指定なしなら何もしない
            if (Application.isEditor || string.IsNullOrEmpty(settings.gpuAdapter))
            {
                return false;
            }

            // 既に指定の GPU で動いていれば何もしない
            string current = SystemInfo.graphicsDeviceName;
            if (current == settings.gpuAdapter)
            {
                VRCastLog.Info(LogCategory, $"Running on the selected GPU: {current}");
                return false;
            }

            // 起動し直した後なのに違う GPU なら、繰り返さずに警告だけ出す
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, ForceDeviceArgument) >= 0)
            {
                AdapterNotApplied = true;
                VRCastLog.Warning(LogCategory,
                    $"Could not switch to \"{settings.gpuAdapter}\" (running on \"{current}\"). " +
                    "Use the Windows preference (power saving / high performance) instead");
                return false;
            }

            // 指定の GPU が見つからなければ（取り外した等）そのまま起動
            int index = GpuAdapters.IndexOf(GpuAdapters.Enumerate(), settings.gpuAdapter);
            if (index < 0)
            {
                AdapterNotApplied = true;
                VRCastLog.Warning(LogCategory, $"Selected GPU \"{settings.gpuAdapter}\" was not found; using \"{current}\"");
                return false;
            }

            // GPU の番号を付けて起動し直す
            string extra = $"{ForceDeviceArgument} {index} {AdapterArgument} {index}";
            VRCastLog.Info(LogCategory, $"Relaunching on \"{settings.gpuAdapter}\" (index {index}, was \"{current}\")");
            IsRelaunching = StartSelf(extra);
            if (IsRelaunching)
            {
                Application.Quit();
            }

            return IsRelaunching;
        }

        /// <summary>
        /// VRCast を起動し直す（GPU の設定を反映するため。設定は呼び出し側で保存しておく）。失敗したら false。
        /// </summary>
        public static bool Restart()
        {
            // エディターでは再起動できない
            if (Application.isEditor)
            {
                return false;
            }

            // GPU の引数は次の起動時に設定から付け直すので引き継がない
            if (!StartSelf(string.Empty))
            {
                return false;
            }

            Application.Quit();
            return true;
        }

        /// <summary>
        /// Windows の優先設定をレジストリへ書く（GPU を直接指定している間は列挙順を変えないよう Windows に任せる）。
        /// 同じ値に入っている他の項目（ウィンドウゲームの最適化など）は残す。失敗したら false。
        /// </summary>
        public static bool ApplyPreference(AppSettings settings)
        {
            // エディターでは Unity エディターの設定を変えない
            string exe = ExecutablePath();
            if (Application.isEditor || exe == null)
            {
                return false;
            }

            // 既存の値から GpuPreference の項目を除き、自動以外なら付け直す
            List<string> entries = ParseEntries(ReadPreference(exe));
            entries.RemoveAll(entry => entry.StartsWith(PreferenceName + "=", StringComparison.OrdinalIgnoreCase));
            bool windowsDecides = settings.gpuPreference == GpuPreference.Auto || !string.IsNullOrEmpty(settings.gpuAdapter);
            if (!windowsDecides)
            {
                entries.Add($"{PreferenceName}={(int)settings.gpuPreference}");
            }

            bool ok = WritePreference(exe, entries);
            VRCastLog.Info(LogCategory, ok
                ? $"Windows GPU preference: {(windowsDecides ? "Auto" : settings.gpuPreference.ToString())} (applies after restart)"
                : "Could not write the Windows GPU preference");
            return ok;
        }

        private static List<string> ParseEntries(string value)
        {
            // "名前=値;名前=値;" を項目ごとに分ける（空の項目は捨てる）
            var entries = new List<string>();
            foreach (string entry in (value ?? string.Empty).Split(';'))
            {
                if (entry.Trim().Length > 0)
                {
                    entries.Add(entry.Trim());
                }
            }

            return entries;
        }

        private static string ReadPreference(string exe)
        {
            // 値が無い・読めなければ空
            var data = new StringBuilder(MaxValueLength);
            uint size = (uint)(data.Capacity * sizeof(char));
            int error = RegGetValueW(CurrentUser, PreferencesKey, exe, StringValueOnly, IntPtr.Zero, data, ref size);
            return error == 0 ? data.ToString() : string.Empty;
        }

        private static bool WritePreference(string exe, List<string> entries)
        {
            // キーを開く（無ければ作る）
            if (RegCreateKeyExW(CurrentUser, PreferencesKey, 0, null, 0, KeyAllAccess, IntPtr.Zero,
                    out IntPtr key, IntPtr.Zero) != 0)
            {
                return false;
            }

            try
            {
                // 項目が残らなければ値ごと消す（元から無ければ成功扱い）
                if (entries.Count == 0)
                {
                    int error = RegDeleteValueW(key, exe);
                    return error == 0 || error == ErrorFileNotFound;
                }

                // 「名前=値;」をつなげて書く（サイズは終端の NUL を含むバイト数）
                string value = string.Join(";", entries) + ";";
                return RegSetValueExW(key, exe, 0, StringType, value, (uint)((value.Length + 1) * sizeof(char))) == 0;
            }
            finally
            {
                RegCloseKey(key);
            }
        }

        private static bool StartSelf(string extraArguments)
        {
            // 実行ファイルが分からなければ起動できない
            string exe = ExecutablePath();
            if (exe == null)
            {
                return false;
            }

            try
            {
                // 元の引数（GPU の指定は除く）に追加分を付けて起動
                string arguments = JoinArguments(Environment.GetCommandLineArgs());
                if (extraArguments.Length > 0)
                {
                    arguments = arguments.Length > 0 ? arguments + " " + extraArguments : extraArguments;
                }

                Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false })?.Dispose();
                return true;
            }
            catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            {
                VRCastLog.Warning(LogCategory, "Could not restart VRCast: " + e.Message);
                return false;
            }
        }

        private static string JoinArguments(string[] args)
        {
            // 先頭（実行ファイル）と GPU の指定（名前 + 番号）を除き、空白を含む引数は引用符で囲む
            var parts = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if ((args[i] == ForceDeviceArgument || args[i] == AdapterArgument) && i + 1 < args.Length)
                {
                    i++;
                    continue;
                }

                parts.Add(args[i].Length == 0 || args[i].IndexOf(' ') >= 0 ? $"\"{args[i]}\"" : args[i]);
            }

            return string.Join(" ", parts);
        }

        private static string ExecutablePath()
        {
            // 取得できない環境では null
            try
            {
                using (Process current = Process.GetCurrentProcess())
                {
                    return current.MainModule?.FileName;
                }
            }
            catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception
                || e is NotSupportedException)
            {
                return null;
            }
        }
    }
}
