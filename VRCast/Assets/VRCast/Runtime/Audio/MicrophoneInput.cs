using System;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Audio
{
    /// <summary>
    /// マイク入力の音量（0〜1）を提供する。アプリ全体で 1 つ。
    /// 録音は lipSyncEnabled の間だけ行い、デバイス変更・切断時は再開する。
    /// </summary>
    public class MicrophoneInput : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "Microphone";

        // 録音バッファ長（秒）とサンプリングレート
        private const int ClipSeconds = 1;
        private const int PreferredFrequency = 48000;

        // 音量計算に使う直近サンプル数（48kHz で約 21ms）
        private const int WindowSamples = 1024;

        // RMS を 0〜1 に引き伸ばす基準倍率（感度 1 のとき）
        private const float BaseGain = 10f;

        // 口の開き・閉じの追従速度（1 秒あたり）
        private const float AttackSpeed = 30f;
        private const float ReleaseSpeed = 12f;

        // 開始失敗・切断時の再試行間隔（秒）
        private const float RetryInterval = 3f;

        private readonly float[] _samples = new float[WindowSamples];
        private AppSettings _settings;
        private AudioClip _clip;

        // 設定上のデバイス名と、Microphone API に実際に渡した名前（既定デバイスは null）
        private string _requestedDevice;
        private string _startedDevice;
        private float _nextRetryTime;

        /// <summary>
        /// 平滑化済みの音量（0〜1）。録音していなければ 0。
        /// </summary>
        public float Level { get; private set; }

        public bool IsRecording => _clip != null;

        /// <summary>
        /// 直近の状態（UI 表示用）。
        /// </summary>
        public string Status { get; private set; } = "Stopped";

        public void Initialize(AppSettings settings)
        {
            _settings = settings;
        }

        private void Update()
        {
            // 未初期化なら何もしない
            if (_settings == null)
            {
                return;
            }

            // 無効化されたら停止し、再有効化時はすぐ開始できるようにする
            if (!_settings.lipSyncEnabled)
            {
                StopRecording("Disabled");
                _nextRetryTime = 0f;
                return;
            }

            // 録音中にデバイスが切断された
            if (IsRecording && !Microphone.IsRecording(_startedDevice))
            {
                StopRecording("Device lost");
            }

            // デバイス変更時は即時、未録音時は間隔を空けて（再）開始
            bool deviceChanged = _requestedDevice != _settings.microphoneDevice;
            if (deviceChanged || (!IsRecording && Time.unscaledTime >= _nextRetryTime))
            {
                StartRecording(_settings.microphoneDevice);
            }

            // 現在の音量へ追従
            float target = IsRecording ? MeasureLevel() : 0f;
            float speed = target > Level ? AttackSpeed : ReleaseSpeed;
            Level = Mathf.MoveTowards(Level, target, speed * Time.deltaTime);
        }

        private void StartRecording(string device)
        {
            StopRecording("Stopped");
            _requestedDevice = device;
            _nextRetryTime = Time.unscaledTime + RetryInterval;

            // マイクが無い環境
            if (Microphone.devices.Length == 0)
            {
                Status = "No microphone";
                return;
            }

            // 指定デバイスが無ければ既定デバイス（null）を使う
            _startedDevice = string.IsNullOrEmpty(device) || Array.IndexOf(Microphone.devices, device) < 0
                ? null
                : device;

            // デバイスが対応するレートに合わせる（0 / 0 は任意レート対応）
            Microphone.GetDeviceCaps(_startedDevice, out int minFrequency, out int maxFrequency);
            int frequency = maxFrequency == 0
                ? PreferredFrequency
                : Mathf.Clamp(PreferredFrequency, minFrequency, maxFrequency);

            // ループ録音で開始
            _clip = Microphone.Start(_startedDevice, true, ClipSeconds, frequency);
            Status = _clip != null ? $"Recording: {_startedDevice ?? "Default"}" : "Failed to start";
            VRCastLog.Info(LogCategory, Status);
        }

        private void StopRecording(string status)
        {
            // 録音中なら停止してクリップを破棄
            if (_clip != null)
            {
                Microphone.End(_startedDevice);
                Destroy(_clip);
                _clip = null;
            }

            Level = 0f;
            Status = status;
        }

        private float MeasureLevel()
        {
            // 録音位置の直前 WindowSamples 分を読む（GetData は末尾で先頭へ折り返す）
            int offset = Microphone.GetPosition(_startedDevice) - WindowSamples;
            if (offset < 0)
            {
                offset += _clip.samples;
            }

            _clip.GetData(_samples, offset);

            // RMS を計算
            float sum = 0f;
            foreach (float sample in _samples)
            {
                sum += sample * sample;
            }

            float rms = Mathf.Sqrt(sum / _samples.Length);

            // しきい値以下は無音、それ以上を感度で 0〜1 へ
            return Mathf.Clamp01((rms - _settings.micThreshold) * BaseGain * _settings.micGain);
        }

        private void OnDestroy()
        {
            // 終了時にマイクを解放
            StopRecording("Stopped");
        }
    }
}
