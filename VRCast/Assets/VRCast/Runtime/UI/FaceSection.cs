using System;
using UnityEngine;
using VRCast.Animations;
using VRCast.Audio;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Dynamics;

namespace VRCast.UI
{
    /// <summary>
    /// MainPanel 内の Face / Physics セクション（揺れもの・自動まばたき・マイクリップシンク）。
    /// </summary>
    public class FaceSection
    {
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
            GUILayout.Label("Face / Physics");
            _avatar.Refresh();
            DrawPhysics();
            DrawBlink();
            DrawLipSync();
        }

        private void DrawPhysics()
        {
            // 揺れものが無いアバターでは注記、あればチェーン数を表示
            var physics = _avatar.Get<PhysBoneSimulator>();
            string suffix = !_avatar.HasAvatar ? string.Empty
                : physics != null && physics.IsAvailable ? $" ({physics.ChainCount} chains)"
                : " (no PhysBones)";
            _settings.physicsEnabled = GUILayout.Toggle(_settings.physicsEnabled, " PhysBone (approx.)" + suffix);
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
            // 候補は「既定」(-1) + 接続中デバイス
            string[] devices = Microphone.devices;
            int current = Array.IndexOf(devices, _settings.microphoneDevice);
            int selected = GuiControls.Selector(devices, current, "Default microphone");

            // 操作されたときだけ設定へ反映（-1 = 既定は空文字）
            if (selected != current)
            {
                _settings.microphoneDevice = selected < 0 ? string.Empty : devices[selected];
            }
        }
    }
}
