using System;
using UnityEngine;
using VRCast.Animations;
using VRCast.Audio;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Dynamics;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// Face タブ（揺れもの・自動まばたき・表情中のまばたき停止・カメラ目線・マイクリップシンク・表情中の口の停止）。
    /// </summary>
    public class FaceSection
    {
        // 母音の表示名（VowelAnalyzer の並び A, I, U, E, O。中国語はローマ字表記を使う）
        private static readonly string[] VowelLabels = { "A", "I", "U", "E", "O" };
        private static readonly string[] VowelLabelsJapanese = { "あ", "い", "う", "え", "お" };
        private static readonly string[] VowelLabelsKorean = { "아", "이", "우", "에", "오" };

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
            GuiControls.BeginCard(Loc.T("Physics / Blink / Gaze", "揺れもの / まばたき / 目線",
                "흔들림 / 눈 깜빡임 / 시선", "物理摆动 / 眨眼 / 视线", "物理擺動 / 眨眼 / 視線"));
            _settings.physicsEnabled = GUILayout.Toggle(
                _settings.physicsEnabled, Loc.T("PhysBone (approx.)", "揺れもの（PhysBone 近似）", "흔들림 (PhysBone 근사)",
                    "物理摆动（PhysBone 近似）", "物理擺動（PhysBone 近似）"));

            // 揺れものが無いアバターでは注記、あればチェーン数を表示
            var physics = _avatar.Get<PhysBoneSimulator>();
            if (_avatar.HasAvatar)
            {
                int chains = physics != null ? physics.ChainCount : 0;
                GuiControls.Hint(physics != null && physics.IsAvailable
                    ? Loc.T($"{chains} chains", $"{chains} チェーン", $"{chains} 체인", $"{chains} 条链", $"{chains} 條鏈")
                    : Loc.T("No PhysBones in this avatar", "このアバターには PhysBone がありません",
                        "이 아바타에는 PhysBone이 없습니다", "此虚拟形象没有 PhysBone", "此虛擬形象沒有 PhysBone"));
            }

            _settings.autoBlink = GUILayout.Toggle(
                _settings.autoBlink, Loc.T("Auto blink", "自動まばたき", "자동 눈 깜빡임", "自动眨眼", "自動眨眼"));

            // 自動・トラッキングのどちらのまばたきにも効く
            _settings.blinkPausedByExpression = GUILayout.Toggle(
                _settings.blinkPausedByExpression, Loc.T("Don't blink during expressions", "表情中はまばたきしない",
                    "표정 중에는 눈 깜빡이지 않기", "表情期间不眨眼", "表情期間不眨眼"));
            GuiControls.Hint(Loc.T("Also stops blinks from face tracking", "トラッキングのまばたきも止めます",
                "트래킹의 눈 깜빡임도 멈춥니다", "也会停止面部追踪的眨眼", "也會停止臉部追蹤的眨眼"));

            // まぶた設定が無いアバターでは注記を付ける
            var blink = _avatar.Get<BlinkController>();
            if (_avatar.HasAvatar && (blink == null || !blink.IsAvailable))
            {
                GuiControls.Hint(Loc.T("No eyelid blend shape", "まぶたの BlendShape がありません",
                    "눈꺼풀 BlendShape가 없습니다", "没有眼睑的 BlendShape", "沒有眼瞼的 BlendShape"));
            }

            DrawGaze();
            GuiControls.EndCard();
        }

        private void DrawGaze()
        {
            // カメラ目線（トラッキングの有無によらず効く。ショートカットキー・パネル下部のボタンでも切り替えられる）
            _settings.trackingLookAtCamera = GUILayout.Toggle(
                _settings.trackingLookAtCamera,
                Loc.T("Look at camera", "カメラ目線", "카메라 시선", "看向镜头", "看向鏡頭"));
            GuiControls.Hint(Loc.T(
                "Points the eyes at the screen (with or without tracking). Also toggled by the button at the bottom of the panel or its shortcut key (default: numeric keypad 5).",
                "目を画面に向けます（トラッキングの有無によらず効きます）。パネル下部のボタンやショートカットキー（既定はテンキーの 5）でも切り替えられます。",
                "눈을 화면으로 향하게 합니다 (트래킹 여부와 관계없이). 패널 아래 버튼이나 단축키 (기본값: 숫자 키패드 5)로도 전환할 수 있습니다.",
                "让眼睛看向画面（无论是否追踪都有效）。也可用面板底部的按钮或快捷键（默认：小键盘 5）切换。",
                "讓眼睛看向畫面（無論是否追蹤都有效）。也可用面板底部的按鈕或快捷鍵（預設：數字鍵台 5）切換。"));

            // 目ボーンが無いアバターでは注記を付ける
            var face = _avatar.Get<FaceTrackingDriver>();
            if (_avatar.HasAvatar && (face == null || !face.HasEyes))
            {
                GuiControls.Hint(Loc.T("No eye bones", "目のボーンがありません", "눈 본이 없습니다", "没有眼睛骨骼",
                    "沒有眼睛骨骼"));
            }
        }

        private void DrawLipSync()
        {
            GuiControls.BeginCard(Loc.T("Lip sync", "リップシンク", "립싱크", "口型同步", "口型同步"));
            _settings.lipSyncEnabled = GUILayout.Toggle(
                _settings.lipSyncEnabled, Loc.T("Lip sync (microphone)", "マイクで口パク", "마이크로 립싱크",
                    "用麦克风对口型", "用麥克風對嘴型"));

            // リップシンク対象が無いアバターでは注記を付ける
            var lipSync = _avatar.Get<LipSyncController>();
            if (_avatar.HasAvatar && (lipSync == null || !lipSync.IsAvailable))
            {
                GuiControls.Hint(Loc.T("No viseme blend shape", "口の BlendShape（Viseme）がありません",
                    "입 BlendShape (Viseme)가 없습니다", "没有嘴部的 BlendShape（Viseme）",
                    "沒有嘴部的 BlendShape（Viseme）"));
            }

            // 表情中の口の停止（マイク・トラッキングを別々に選べ、両方 ON も可。マイクを OFF にしていても出す）
            _settings.lipSyncPausedByExpression = GUILayout.Toggle(
                _settings.lipSyncPausedByExpression, Loc.T("Pause microphone lip sync during expressions",
                    "表情中はマイクの口パクを止める", "표정 중에는 마이크 립싱크 멈추기", "表情期间停止麦克风口型同步",
                    "表情期間停止麥克風口型同步"));
            _settings.trackingMouthPausedByExpression = GUILayout.Toggle(
                _settings.trackingMouthPausedByExpression, Loc.T("Pause tracked mouth during expressions",
                    "表情中はトラッキングの口を止める", "표정 중에는 트래킹의 입 멈추기", "表情期间停止追踪的嘴部动作",
                    "表情期間停止追蹤的嘴部動作"));
            GuiControls.Hint(Loc.T("For avatars whose expression mouth breaks when combined with lip sync",
                "表情の口とリップシンクが重なると崩れるアバター向け",
                "표정의 입과 립싱크가 겹치면 깨지는 아바타용",
                "适用于表情的嘴型与口型同步重叠时会变形的虚拟形象",
                "適用於表情的嘴型與口型同步重疊時會變形的虛擬形象"));

            // 無効時は詳細設定を出さない
            if (_settings.lipSyncEnabled)
            {
                DrawDeviceSelector();
                _settings.micGain = GuiControls.Slider(
                    Loc.T("Mic gain", "マイク感度", "마이크 감도", "麦克风灵敏度", "麥克風靈敏度"), _settings.micGain,
                    AppSettings.MinMicGain, AppSettings.MaxMicGain);
                _settings.micThreshold = GuiControls.Slider(
                    Loc.T("Mic gate", "ノイズゲート", "노이즈 게이트", "噪声门", "噪音閘"), _settings.micThreshold,
                    0f, AppSettings.MaxMicThreshold);

                // 音量メーター（操作不可のスライダーで表示）
                GUI.enabled = false;
                GuiControls.Slider(Loc.T("Level", "音量", "음량", "音量", "音量"), _microphone.Level, 0f, 1f);
                GUI.enabled = true;
                GuiControls.Hint(_microphone.Status);
                DrawVowels(lipSync);
            }

            GuiControls.EndCard();
        }

        private void DrawVowels(LipSyncController lipSync)
        {
            _settings.lipSyncVowels = GUILayout.Toggle(
                _settings.lipSyncVowels, Loc.T("Vowel mouth shapes (A I U E O)", "母音で口の形を変える（あいうえお）",
                    "모음으로 입 모양 바꾸기 (아이우에오)", "根据元音改变口型（A I U E O）", "依母音改變嘴型（A I U E O）"));

            // 無効時は補正・判定結果を出さない
            if (!_settings.lipSyncVowels)
            {
                return;
            }

            // あいうえおの Viseme が無いアバターは音量のみ
            if (lipSync != null && lipSync.IsAvailable && !lipSync.HasVowels)
            {
                GuiControls.Hint(Loc.T("This avatar has no vowel visemes (volume only)",
                    "このアバターには母音の Viseme がありません（音量のみ）",
                    "이 아바타에는 모음 Viseme가 없습니다 (음량만)",
                    "此虚拟形象没有元音 Viseme（仅音量）",
                    "此虛擬形象沒有母音 Viseme（僅音量）"));
            }

            // 判定がずれるときの補正（声が高いほど右）
            _settings.lipSyncVoiceScale = GuiControls.Slider(
                Loc.T("Voice pitch", "声の高さ補正", "목소리 높이 보정", "音高校正", "音高校正"),
                _settings.lipSyncVoiceScale, AppSettings.MinVoiceScale, AppSettings.MaxVoiceScale);

            // 判定中の母音と推定したフォルマント（声が出ている間のみ）
            if (_microphone.Level > 0f)
            {
                Vector2 formants = _microphone.Formants;
                int dominant = _microphone.DominantVowel;
                string vowel = Loc.T(VowelLabels[dominant], VowelLabelsJapanese[dominant], VowelLabelsKorean[dominant],
                    VowelLabels[dominant], VowelLabels[dominant]);
                GuiControls.Hint(Loc.T("Vowel", "母音", "모음", "元音", "母音") + $": {vowel}"
                    + $"    F1 {formants.x:F0} Hz / F2 {formants.y:F0} Hz");
            }
        }

        private void DrawDeviceSelector()
        {
            // 候補は「既定」(-1) + 接続中デバイス
            string[] devices = Microphone.devices;
            int current = Array.IndexOf(devices, _settings.microphoneDevice);
            int selected = GuiControls.Selector(
                Loc.T("Microphone", "マイク", "마이크", "麦克风", "麥克風"), devices, current,
                Loc.T("Default", "既定のマイク", "기본 마이크", "默认麦克风", "預設麥克風"));

            // 操作されたときだけ設定へ反映（-1 = 既定は空文字）
            if (selected != current)
            {
                _settings.microphoneDevice = selected < 0 ? string.Empty : devices[selected];
            }
        }
    }
}
