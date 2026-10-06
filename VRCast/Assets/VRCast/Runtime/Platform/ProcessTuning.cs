using System;
using System.Runtime.InteropServices;
using VRCast.Core;

namespace VRCast.Platform
{
    /// <summary>
    /// プロセスの優先度と Windows の電力調整（EcoQoS）の設定。VRCast 本体とトラッカーの両方に使う。
    /// Windows 11 は前面にないアプリやウィンドウを持たないプロセス（トラッカー）の CPU 速度・タイマー精度を落とすことがあり、
    /// VRCast が背面にある間だけトラッカーの推定が遅れて手を見失う原因になる。
    /// </summary>
    public static class ProcessTuning
    {
        // SetProcessInformation の情報の種類（PROCESS_INFORMATION_CLASS.ProcessPowerThrottling）
        private const int ProcessPowerThrottling = 4;

        // PROCESS_POWER_THROTTLING_STATE の版
        private const uint PowerThrottlingVersion = 1;

        // 実行速度の調整と、タイマー精度の要求の無視を対象にする
        private const uint ExecutionSpeed = 0x1;
        private const uint IgnoreTimerResolution = 0x4;

        // SetPriorityClass の優先度クラス
        private const uint BelowNormalPriorityClass = 0x4000;
        private const uint NormalPriorityClass = 0x20;
        private const uint AboveNormalPriorityClass = 0x8000;
        private const uint HighPriorityClass = 0x80;

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessPowerThrottlingState
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(
            IntPtr process, int informationClass, ref ProcessPowerThrottlingState information, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetPriorityClass(IntPtr process, uint priorityClass);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        // 優先度・電力調整の変更に必要な権限（PROCESS_SET_INFORMATION）
        private const uint ProcessSetInformation = 0x0200;

        /// <summary>
        /// 自分自身（VRCast）のプロセスの疑似ハンドル（閉じる必要はない）。
        /// </summary>
        public static IntPtr CurrentProcess => GetCurrentProcess();

        /// <summary>
        /// 別プロセス（トラッカー）を PID で開いて電力調整の対象から外す。失敗時は false。
        /// IL2CPP では Process.Handle が Windows のハンドルとして使える保証がないため、PID から開き直す。
        /// </summary>
        public static bool DisablePowerThrottling(int processId)
        {
            return WithProcess(processId, DisablePowerThrottling);
        }

        /// <summary>
        /// 別プロセス（トラッカー）を PID で開いて優先度を設定する。失敗時は false。
        /// </summary>
        public static bool SetPriority(int processId, ProcessPriority priority)
        {
            return WithProcess(processId, process => SetPriority(process, priority));
        }

        /// <summary>
        /// プロセスを電力調整の対象から外す。非対応の Windows（10 1709 より前）や失敗時は false。
        /// </summary>
        public static bool DisablePowerThrottling(IntPtr process)
        {
            // 無効なハンドルは対象外
            if (process == IntPtr.Zero)
            {
                return false;
            }

            // ControlMask で指定した項目を StateMask = 0（調整しない）にする
            var state = new ProcessPowerThrottlingState
            {
                Version = PowerThrottlingVersion,
                ControlMask = ExecutionSpeed | IgnoreTimerResolution,
                StateMask = 0,
            };

            try
            {
                return SetProcessInformation(process, ProcessPowerThrottling, ref state, Marshal.SizeOf(state));
            }
            catch (EntryPointNotFoundException)
            {
                // Windows 8 より前は API 自体が無い
                return false;
            }
        }

        /// <summary>
        /// プロセスの優先度を設定する。失敗したら false。
        /// </summary>
        public static bool SetPriority(IntPtr process, ProcessPriority priority)
        {
            // 無効なハンドルは対象外
            return process != IntPtr.Zero && SetPriorityClass(process, ToPriorityClass(priority));
        }

        private static bool WithProcess(int processId, Func<IntPtr, bool> action)
        {
            // 開けなければ（終了済み・権限なし）失敗
            IntPtr process = OpenProcess(ProcessSetInformation, false, processId);
            if (process == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return action(process);
            }
            finally
            {
                // 疑似ハンドルと違い、開いたハンドルは閉じる
                CloseHandle(process);
            }
        }

        private static uint ToPriorityClass(ProcessPriority priority)
        {
            // 設定値を Windows の優先度クラスへ（未知の値は通常）
            switch (priority)
            {
                case ProcessPriority.BelowNormal:
                    return BelowNormalPriorityClass;
                case ProcessPriority.AboveNormal:
                    return AboveNormalPriorityClass;
                case ProcessPriority.High:
                    return HighPriorityClass;
                default:
                    return NormalPriorityClass;
            }
        }
    }
}
