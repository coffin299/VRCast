using System;
using UnityEngine;
using VRCast.Animations;
using VRCast.Audio;
using VRCast.Avatars;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// MainPanel 内の Face セクション（自動まばたき・マイクリップシンク）。
    /// </summary>
    public class FaceSection
    {
        // マイクのデバイス名の最大表示文字数
        private const int MaxDeviceLabelLength = 28;

        private readonly AvatarComponentCache _avatar;
        private readonly MicrophoneInput _microphone;
        private readonly AppSettings _settings;

        public FaceSection(AvatarSession session, MicrophoneInput microphone, AppSettings settings)
        {
            _avatar = new AvatarComponentCache(session);
            _microphone = microphone;
            _settings = settings;
        }

        public void Draw()
        {
            GUILayout.Label("Face");
            _avatar.Refresh();
            DrawBlink();
            DrawLipSync();
        }

        private void DrawBlink()
        {
            // まぶた設定が無いアバターでは注記を付ける
            var blink = _avatar.Get<BlinkController>();
            string suffix = _avatar.HasAvatar && (blink == null || !blink.IsAvailable) ? " (no eyelid blend shape)" : string.Empty;
            _settings.autoBlink = GUILayout.Toggle(_settings.autoBlink, " Auto blink" + suffix);
        }

        private void DrawLipSync()
        {
            // リップシンク対象が無いアバターでは注記を付ける
            var lipSync = _avatar.Get<LipSyncController>();
            string suffix = _avatar.HasAvatar && (lipSync == null || !lipSync.IsAvailable) ? " (no viseme)" : string.Empty;
            _settings.lipSyncEnabled = GUILayout.Toggle(_settings.lipSyncEnabled, " Lip sync (microphone)" + suffix);

            // 無効時は詳細設定を出さない
            if (!_settings.lipSyncEnabled)
            {
                return;
            }

            DrawDeviceSelector();
            _settings.micGain = GuiControls.Slider("Mic gain", _settings.micGain, AppSettings.MinMicGain, AppSettings.MaxMicGain);
            _settings.micThreshold = GuiControls.Slider("Mic gate", _settings.micThreshold, 0f, AppSettings.MaxMicThreshold);

            // 音量メーター（操作不可のスライダーで表示）
            GUI.enabled = false;
            GuiControls.Slider("Level", _microphone.Level, 0f, 1f);
            GUI.enabled = true;
            GUILayout.Label(_microphone.Status);
        }

        private void DrawDeviceSelector()
        {
            // 候補は「既定」+ 接続中デバイス
            string[] devices = Microphone.devices;
            int current = Array.IndexOf(devices, _settings.microphoneDevice);

            GUILayout.BeginHorizontal();

            // 前のデバイスへ（-1 = 既定）
            if (GUILayout.Button("<", GUILayout.Width(24f)))
            {
                current = current <= -1 ? devices.Length - 1 : current - 1;
                _settings.microphoneDevice = current < 0 ? string.Empty : devices[current];
            }

            // 現在のデバイス名
            string label = current < 0 ? "Default microphone" : devices[current];
            if (label.Length > MaxDeviceLabelLength)
            {
                label = label.Substring(0, MaxDeviceLabelLength - 1) + "…";
            }

            GUILayout.Label(label, GUILayout.ExpandWidth(true));

            // 次のデバイスへ（末尾の次は既定）
            if (GUILayout.Button(">", GUILayout.Width(24f)))
            {
                current = current >= devices.Length - 1 ? -1 : current + 1;
                _settings.microphoneDevice = current < 0 ? string.Empty : devices[current];
            }

            GUILayout.EndHorizontal();
        }
    }
}
