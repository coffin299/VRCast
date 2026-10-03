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
    /// Face タブ（揺れもの・自動まばたき・マイクリップシンク）。
    /// </summary>
    public class FaceSection
    {
        // 母音の表示名（VowelAnalyzer の並び A, I, U, E, O）
        private static readonly string[] VowelLabels = { "A", "I", "U", "E", "O" };
        private static readonly string[] VowelLabelsJapanese = { "あ", "い", "う", "え", "お" };

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
            _avatar.Refresh();
            DrawPhysicsAndBlink();
            DrawLipSync();
        }

        private void DrawPhysicsAndBlink()
        {
            GuiControls.BeginCard(Loc.T("Physics / Blink", "揺れもの / まばたき"));
            _settings.physicsEnabled = GUILayout.Toggle(
                _settings.physicsEnabled, Loc.T("PhysBone (approx.)", "揺れもの（PhysBone 近似）"));

            // 揺れものが無いアバターでは注記、あればチェーン数を表示
            var physics = _avatar.Get<PhysBoneSimulator>();
            if (_avatar.HasAvatar)
            {
                GuiControls.Hint(physics != null && physics.IsAvailable
                    ? Loc.T($"{physics.ChainCount} chains", $"{physics.ChainCount} チェーン")
                    : Loc.T("No PhysBones in this avatar", "このアバターには PhysBone がありません"));
            }

            _settings.autoBlink = GUILayout.Toggle(_settings.autoBlink, Loc.T("Auto blink", "自動まばたき"));

            // まぶた設定が無いアバターでは注記を付ける
            var blink = _avatar.Get<BlinkController>();
            if (_avatar.HasAvatar && (blink == null || !blink.IsAvailable))
            {
                GuiControls.Hint(Loc.T("No eyelid blend shape", "まぶたの BlendShape がありません"));
            }

            GuiControls.EndCard();
        }

        private void DrawLipSync()
        {
            GuiControls.BeginCard(Loc.T("Lip sync", "リップシンク"));
            _settings.lipSyncEnabled = GUILayout.Toggle(
                _settings.lipSyncEnabled, Loc.T("Lip sync (microphone)", "マイクで口パク"));

            // リップシンク対象が無いアバターでは注記を付ける
            var lipSync = _avatar.Get<LipSyncController>();
            if (_avatar.HasAvatar && (lipSync == null || !lipSync.IsAvailable))
            {
                GuiControls.Hint(Loc.T("No viseme blend shape", "口の BlendShape（Viseme）がありません"));
            }

            // 無効時は詳細設定を出さない
            if (_settings.lipSyncEnabled)
            {
                DrawDeviceSelector();
                _settings.micGain = GuiControls.Slider(
                    Loc.T("Mic gain", "マイク感度"), _settings.micGain, AppSettings.MinMicGain, AppSettings.MaxMicGain);
                _settings.micThreshold = GuiControls.Slider(
                    Loc.T("Mic gate", "ノイズゲート"), _settings.micThreshold, 0f, AppSettings.MaxMicThreshold);

                // 音量メーター（操作不可のスライダーで表示）
                GUI.enabled = false;
                GuiControls.Slider(Loc.T("Level", "音量"), _microphone.Level, 0f, 1f);
                GUI.enabled = true;
                GuiControls.Hint(_microphone.Status);
                DrawVowels(lipSync);
            }

            GuiControls.EndCard();
        }

        private void DrawVowels(LipSyncController lipSync)
        {
            _settings.lipSyncVowels = GUILayout.Toggle(
                _settings.lipSyncVowels, Loc.T("Vowel mouth shapes (A I U E O)", "母音で口の形を変える（あいうえお）"));

            // 無効時は補正・判定結果を出さない
            if (!_settings.lipSyncVowels)
            {
                return;
            }

            // あいうえおの Viseme が無いアバターは音量のみ
            if (lipSync != null && lipSync.IsAvailable && !lipSync.HasVowels)
            {
                GuiControls.Hint(Loc.T("This avatar has no vowel visemes (volume only)",
                    "このアバターには母音の Viseme がありません（音量のみ）"));
            }

            // 判定がずれるときの補正（声が高いほど右）
            _settings.lipSyncVoiceScale = GuiControls.Slider(Loc.T("Voice pitch", "声の高さ補正"),
                _settings.lipSyncVoiceScale, AppSettings.MinVoiceScale, AppSettings.MaxVoiceScale);

            // 判定中の母音と推定したフォルマント（声が出ている間のみ）
            if (_microphone.Level > 0f)
            {
                Vector2 formants = _microphone.Formants;
                int dominant = _microphone.DominantVowel;
                string vowel = Loc.T(VowelLabels[dominant], VowelLabelsJapanese[dominant]);
                GuiControls.Hint(Loc.T("Vowel", "母音") + $": {vowel}"
                    + $"    F1 {formants.x:F0} Hz / F2 {formants.y:F0} Hz");
            }
        }

        private void DrawDeviceSelector()
        {
            // 候補は「既定」(-1) + 接続中デバイス
            string[] devices = Microphone.devices;
            int current = Array.IndexOf(devices, _settings.microphoneDevice);
            int selected = GuiControls.Selector(
                Loc.T("Microphone", "マイク"), devices, current, Loc.T("Default", "既定のマイク"));

            // 操作されたときだけ設定へ反映（-1 = 既定は空文字）
            if (selected != current)
            {
                _settings.microphoneDevice = selected < 0 ? string.Empty : devices[selected];
            }
        }
    }
}
