using System;
using System.Runtime.InteropServices;

namespace VRCast.Platform
{
    /// <summary>
    /// プロセスの CPU 使用率（タスクマネージャーと同じく全論理コアに対する割合）を、前回の計測からの差分で求める。
    /// 1 つのインスタンスで 1 つのプロセスを追う（PID が変わったら計測をやり直す）。
    /// </summary>
    public sealed class ProcessCpuMeter
    {
        // CPU 時間の取得に必要な権限（PROCESS_QUERY_LIMITED_INFORMATION）
        private const uint ProcessQueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessTimes(
            IntPtr process, out long creationTime, out long exitTime, out long kernelTime, out long userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        // 前回の計測（PID・CPU 時間と実時間。単位はどちらも 100 ns）
        private int _processId = -1;
        private long _lastCpu;
        private long _lastWall;

        /// <summary>
        /// VRCast 自身の CPU 使用率（%）。初回・取得失敗時は負の値。
        /// </summary>
        public float SampleCurrent()
        {
            // 疑似ハンドルは閉じなくてよい
            long cpu = TryGetCpuTime(ProcessTuning.CurrentProcess, out long time) ? time : -1;
            return Update(0, cpu);
        }

        /// <summary>
        /// 別プロセス（トラッカー）の CPU 使用率（%）。初回・PID が変わった直後・取得失敗時は負の値。
        /// </summary>
        public float Sample(int processId)
        {
            // 開けなければ（終了済み・権限なし）失敗
            IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (process == IntPtr.Zero)
            {
                return Update(processId, -1);
            }

            try
            {
                long cpu = TryGetCpuTime(process, out long time) ? time : -1;
                return Update(processId, cpu);
            }
            finally
            {
                CloseHandle(process);
            }
        }

        private float Update(int processId, long cpu)
        {
            long wall = DateTime.UtcNow.Ticks;

            // 取得できなければ次回は計測し直す
            if (cpu < 0)
            {
                _processId = -1;
                return -1f;
            }

            // 別のプロセスに変わった直後は差分が取れないので基準だけ記録する
            bool hasPrevious = _processId == processId && wall > _lastWall;
            float usage = hasPrevious
                ? (float)((cpu - _lastCpu) * 100.0 / ((wall - _lastWall) * (double)Environment.ProcessorCount))
                : -1f;
            _processId = processId;
            _lastCpu = cpu;
            _lastWall = wall;
            return usage;
        }

        private static bool TryGetCpuTime(IntPtr process, out long time)
        {
            // カーネル時間とユーザー時間の合計
            if (GetProcessTimes(process, out _, out _, out long kernel, out long user))
            {
                time = kernel + user;
                return true;
            }

            time = 0;
            return false;
        }
    }
}
