using UnityEngine;
using VRCast.Core;

namespace VRCast.Platform
{
    /// <summary>
    /// VRCast 本体のプロセスを設定の優先度にし、Windows の電力調整から外す。使うコア（2 CCD の X3D でキャッシュの無い側、P コア / E コア）も設定に従う。アプリ全体で 1 つ。
    /// トラッカーのプロセスは TrackerProcess が同じ設定で扱う。エディターでは Unity エディター自体を変えないよう何もしない。
    /// </summary>
    public class ProcessTuner : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "Process";

        private AppSettings _settings;
        private ProcessPriority _appliedPriority;
        private ulong? _appliedCoreMask;

        public void Initialize(AppSettings settings)
        {
            _settings = settings;

            // エディターでは何もしない
            if (Application.isEditor)
            {
                return;
            }

            // 背面にある間に CPU 速度・タイマー精度を落とされないようにする
            if (!ProcessTuning.DisablePowerThrottling(ProcessTuning.CurrentProcess))
            {
                VRCastLog.Info(LogCategory, "Could not opt VRCast out of Windows power throttling");
            }

            ApplyPriority();
            ApplyCoreMask();
        }

        private void Update()
        {
            // 未初期化・エディターなら何もしない
            if (_settings == null || Application.isEditor)
            {
                return;
            }

            // 変わった設定だけ反映する
            if (_appliedPriority != _settings.processPriority)
            {
                ApplyPriority();
            }

            if (_appliedCoreMask != CpuTopology.CoreMaskFor(_settings))
            {
                ApplyCoreMask();
            }
        }

        private void ApplyCoreMask()
        {
            // 試した値は記録して毎フレーム再試行しない（起動時は前の VRCast から引き継いだ制限を解くため全コアでも書く）
            ulong mask = CpuTopology.CoreMaskFor(_settings);
            _appliedCoreMask = mask;
            if (ProcessTuning.SetCoreMask(ProcessTuning.CurrentProcess, mask))
            {
                VRCastLog.Info(LogCategory, $"CPU cores: {(mask == 0 ? "all" : $"0x{mask:X}")} ({CpuTopology.Description})");
            }
            else
            {
                VRCastLog.Warning(LogCategory, "Could not change the CPU cores VRCast runs on");
            }
        }

        private void ApplyPriority()
        {
            // 失敗しても毎フレーム再試行しないよう、試した値を記録する
            _appliedPriority = _settings.processPriority;
            if (ProcessTuning.SetPriority(ProcessTuning.CurrentProcess, _appliedPriority))
            {
                VRCastLog.Info(LogCategory, $"Process priority: {_appliedPriority}");
            }
            else
            {
                VRCastLog.Warning(LogCategory, $"Could not set the VRCast priority to {_appliedPriority}");
            }
        }
    }
}
