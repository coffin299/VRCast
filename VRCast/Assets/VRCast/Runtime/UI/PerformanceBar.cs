using System.Globalization;
using System.Text;
using UnityEngine;
using VRCast.Core;
using VRCast.Platform;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// パネルの一番下に常に表示する動作状況（描画の fps・VRCast の CPU・GPU の処理時間・トラッカーの CPU・トラッキングの受信レート）。
    /// 表示は英語のみ。1 秒ごとにまとめて計測し直し、設定の showPerformanceStats で ON/OFF する。
    /// </summary>
    public class PerformanceBar
    {
        // 表示を更新する間隔（秒）
        private const float RefreshInterval = 1f;

        // 項目の区切り
        private const string Separator = "  |  ";

        private readonly AppSettings _settings;
        private readonly IFaceTrackingProvider _tracker;
        private readonly TrackerProcess _trackerProcess;
        private readonly ProcessCpuMeter _appCpu = new ProcessCpuMeter();
        private readonly ProcessCpuMeter _trackerCpu = new ProcessCpuMeter();
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private readonly StringBuilder _builder = new StringBuilder();

        // Layout で決めた表示状態（同じフレームの他のイベント中に設定が変わっても配置を食い違わせない）
        private bool _visible;

        // 計測中の期間（開始時刻・開始フレーム。-1 = 未開始）と、その間の GPU の処理時間の合計・回数
        private float _windowStart;
        private int _windowFrame = -1;
        private double _gpuTotal;
        private int _gpuSamples;

        private string _text = "Measuring...";

        public PerformanceBar(AppSettings settings, IFaceTrackingProvider tracker, TrackerProcess trackerProcess)
        {
            _settings = settings;
            _tracker = tracker;
            _trackerProcess = trackerProcess;
        }

        public void Draw()
        {
            // 表示するかは Layout のときだけ決める（OFF の間は計測もしない）
            if (Event.current.type == EventType.Layout)
            {
                bool visible = _settings.showPerformanceStats;
                if (visible && !_visible)
                {
                    // 表示し直したら古い値を出さず計測からやり直す
                    _windowFrame = -1;
                    _text = "Measuring...";
                }

                _visible = visible;
            }

            if (!_visible)
            {
                return;
            }

            // 1 フレームに 1 回だけ来る Repaint で計測する
            if (Event.current.type == EventType.Repaint)
            {
                Sample();
            }

            GUILayout.Label(_text, UiTheme.Current.Hint);
        }

        private void Sample()
        {
            // GPU の処理時間（取れない環境・グラフィックス API では 0 になるので数えない）
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0 && _timings[0].gpuFrameTime > 0.0)
            {
                _gpuTotal += _timings[0].gpuFrameTime;
                _gpuSamples++;
            }

            float now = Time.unscaledTime;
            if (_windowFrame < 0)
            {
                // CPU 使用率は差分で求めるため、期間の始めに基準を取っておく
                _appCpu.SampleCurrent();
                SampleTrackerCpu();
                StartWindow(now);
                return;
            }

            // 期間が終わるまでは前回の表示のまま
            float elapsed = now - _windowStart;
            int frames = Time.frameCount - _windowFrame;
            if (elapsed < RefreshInterval || frames <= 0)
            {
                return;
            }

            _text = Format(frames / elapsed, elapsed * 1000f / frames);
            StartWindow(now);
        }

        private void StartWindow(float now)
        {
            // 次の期間の計測を始める
            _windowStart = now;
            _windowFrame = Time.frameCount;
            _gpuTotal = 0.0;
            _gpuSamples = 0;
        }

        private float SampleTrackerCpu()
        {
            // 同梱トラッカーを起動していなければ（iPhone などから受信中も）計測しない
            int processId = _trackerProcess.ProcessId;
            return processId != 0 ? _trackerCpu.Sample(processId) : -1f;
        }

        private string Format(float fps, float frameMs)
        {
            _builder.Clear();
            _builder.Append("FPS ").Append(fps.ToString("F0", Invariant))
                .Append(" (").Append(frameMs.ToString("F1", Invariant)).Append(" ms)");

            // VRCast 本体の CPU 使用率
            _builder.Append(Separator).Append("CPU ").Append(Percent(_appCpu.SampleCurrent()));

            // GPU は 1 フレームの処理時間の平均（使用率は Windows から取れないため）
            _builder.Append(Separator).Append("GPU ");
            _builder.Append(_gpuSamples > 0
                ? (_gpuTotal / _gpuSamples).ToString("F1", Invariant) + " ms"
                : "-");

            // 同梱トラッカーを起動中ならその CPU 使用率
            if (_trackerProcess.IsRunning)
            {
                _builder.Append(Separator).Append("Tracker CPU ").Append(Percent(SampleTrackerCpu()));
            }

            // トラッキングの受信レート（受信していなければ -）
            int trackingFps = _tracker.FramesPerSecond;
            _builder.Append(Separator).Append("Tracking ")
                .Append(trackingFps > 0 ? trackingFps.ToString(Invariant) + " fps" : "-");
            return _builder.ToString();
        }

        private static string Percent(float usage)
        {
            // 計測できなかったときは -
            return usage >= 0f ? usage.ToString("F1", Invariant) + "%" : "-";
        }

        // 小数点がカンマになる言語の OS でも同じ表記にする
        private static CultureInfo Invariant => CultureInfo.InvariantCulture;
    }
}
