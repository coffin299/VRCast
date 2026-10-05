using UnityEngine;
using VRCast.Core;

namespace VRCast.Platform
{
    /// <summary>
    /// VRCast 本体のプロセスを設定の優先度にし、Windows の電力調整から外す。アプリ全体で 1 つ。
    /// トラッカーのプロセスは TrackerProcess が同じ設定で扱う。エディターでは Unity エディター自体を変えないよう何もしない。
    /// </summary>
    public class ProcessTuner : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "Process";

        private AppSettings _settings;
        private ProcessPriority _appliedPriority;

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
        }

        private void Update()
        {
            // 未初期化・エディター・変更なしなら何もしない
            if (_settings == null || Application.isEditor || _appliedPriority == _settings.processPriority)
            {
                return;
            }

            ApplyPriority();
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
