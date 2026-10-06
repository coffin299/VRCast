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
        // 母音の表示名（VowelAnalyzer の並び A, I, U, E, O。中国語はローマ字表記を使う）
        private static readonly string[] VowelLabels = { "A", "I", "U", "E", "O" };
        private static readonly string[] VowelLabelsJapanese = { "あ", "い", "う", "え", "お" };
        private static readonly string[] VowelLabelsKorean = { "아", "이", "우", "에", "오" };

        // BlendShape の上限の一覧に一度に出す行数（多いと操作パネルが重くなるため、超えたら検索で絞り込んでもらう）
        private const int MaxLimitRows = 40;

        private readonly AvatarSession _session;
        private readonly AvatarComponentCache _avatar;
        private readonly MicrophoneInput _microphone;
        private readonly AppSettings _settings;

        // BlendShape の上限の一覧の表示条件（0 = 顔、1 = その他・検索文字列・上限付きだけ）
        private int _limitGroup;
        private string _limitSearch = string.Empty;
        private bool _limitedOnly;

        public FaceSection(AvatarSession session, MicrophoneInput microphone, AppSettings settings)
        {
            _session = session;
            _avatar = new AvatarComponentCache(session);
            _microphone = microphone;
            _settings = settings;
        }

        public void Draw()
        {
            _avatar.Refresh();
            DrawPhysicsAndBlink();
            DrawLipSync();
            DrawBlendShapeLimits();
        }

        private void DrawPhysicsAndBlink()
        {
            GuiControls.BeginCard(Loc.T("Physics / Blink", "揺れもの / まばたき", "흔들림 / 눈 깜빡임",
                "物理摆动 / 眨眼", "物理擺動 / 眨眼"));
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

            // まぶた設定が無いアバターでは注記を付ける
            var blink = _avatar.Get<BlinkController>();
            if (_avatar.HasAvatar && (blink == null || !blink.IsAvailable))
            {
                GuiControls.Hint(Loc.T("No eyelid blend shape", "まぶたの BlendShape がありません",
                    "눈꺼풀 BlendShape가 없습니다", "没有眼睑的 BlendShape", "沒有眼瞼的 BlendShape"));
            }

            GuiControls.EndCard();
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

        private void DrawBlendShapeLimits()
        {
            // アバター未表示なら出さない
            var limiter = _avatar.Get<BlendShapeLimiter>();
            if (limiter == null)
            {
                return;
            }

            GuiControls.BeginCard(Loc.T("Blend shape limits", "BlendShape の上限", "BlendShape 상한", "BlendShape 上限",
                "BlendShape 上限"));
            GuiControls.Hint(Loc.T(
                "Caps how far each blend shape moves (100 = no limit). Lower it when, for example, the eyes disappear while blinking. Saved per avatar.",
                "各 BlendShape が動く最大値です（100 = 制限なし）。まばたきで目が消える等のときに下げます。アバターごとに保存されます。",
                "각 BlendShape가 움직이는 최대값입니다 (100 = 제한 없음). 눈을 깜빡일 때 눈이 사라지는 경우 등에 낮춥니다. 아바타별로 저장됩니다.",
                "每个 BlendShape 可动到的最大值（100 = 不限制）。例如眨眼时眼睛消失时调低。按虚拟形象分别保存。",
                "每個 BlendShape 可動到的最大值（100 = 不限制）。例如眨眼時眼睛消失時調低。依虛擬形象分別儲存。"));

            // 顔（まばたき・口・表情・パーフェクトシンクで動くもの）か、その他か
            string[] groups =
            {
                Loc.T("Face (blink, mouth, expressions)", "顔（まばたき・口・表情）", "얼굴 (눈 깜빡임·입·표정)",
                    "面部（眨眼、嘴、表情）", "臉部（眨眼、嘴、表情）"),
                Loc.T("Others", "その他", "기타", "其他", "其他"),
            };
            _limitGroup = GuiControls.EnumSelector(Loc.T("Show", "対象", "대상", "对象", "對象"), groups, _limitGroup);

            // 名前で絞り込み（大文字・小文字は区別しない）
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("Search", "検索", "검색", "搜索", "搜尋"), GUILayout.ExpandWidth(false));
            _limitSearch = GUILayout.TextField(_limitSearch, GuiControls.Shrinkable);
            GUILayout.EndHorizontal();
            _limitedOnly = GUILayout.Toggle(_limitedOnly, Loc.T(
                $"Limited only ({limiter.LimitedCount})", $"上限を付けたものだけ（{limiter.LimitedCount} 件）",
                $"상한을 설정한 것만 ({limiter.LimitedCount}개)", $"仅显示已设上限的（{limiter.LimitedCount} 个）",
                $"僅顯示已設上限的（{limiter.LimitedCount} 個）"));

            // 条件に合う BlendShape をスライダーで並べる（行数の上限を超えた分は件数だけ出す）
            bool face = _limitGroup == 0;
            bool changed = false;
            int shown = 0;
            int hidden = 0;
            foreach (BlendShapeLimiter.Shape shape in limiter.Shapes)
            {
                // 対象・上限付きだけ・検索文字列で絞り込む
                if (shape.IsFace != face || (_limitedOnly && !shape.IsLimited)
                    || (_limitSearch.Length > 0 && shape.Name.IndexOf(_limitSearch, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                // 行数の上限を超えた分は数えるだけ
                if (shown >= MaxLimitRows)
                {
                    hidden++;
                    continue;
                }

                // 同じ名前が複数のメッシュにあっても分かるようメッシュ名を付ける（欄が狭いときに名前が残るよう後ろに）
                shown++;
                string label = $"{shape.Name} ({shape.Renderer.name})";
                float max = GuiControls.Slider(label, shape.Max, BlendShapeLimit.MinWeight, BlendShapeLimit.MaxWeight, "F0");
                if (!Mathf.Approximately(max, shape.Max))
                {
                    limiter.SetMax(shape, Mathf.Round(max));
                    changed = true;
                }
            }

            // 1 件も無ければ理由を出す
            if (shown == 0)
            {
                GuiControls.Hint(face && limiter.FaceCount == 0
                    ? Loc.T("This avatar has no blend shapes moved by blink, mouth or expressions (see Others)",
                        "このアバターには、まばたき・口・表情で動く BlendShape がありません（その他を見てください）",
                        "이 아바타에는 눈 깜빡임·입·표정으로 움직이는 BlendShape가 없습니다 (기타를 확인하세요)",
                        "此虚拟形象没有由眨眼、嘴、表情驱动的 BlendShape（请查看其他）",
                        "此虛擬形象沒有由眨眼、嘴、表情驅動的 BlendShape（請查看其他）")
                    : Loc.T("No matching blend shapes", "該当する BlendShape がありません", "해당하는 BlendShape가 없습니다",
                        "没有符合的 BlendShape", "沒有符合的 BlendShape"));
            }

            // 出し切れなかった分は絞り込みを促す
            if (hidden > 0)
            {
                GuiControls.Hint(Loc.T($"{hidden} more. Narrow down with Search.",
                    $"ほかに {hidden} 件あります。検索で絞り込んでください。", $"그 외 {hidden}개가 있습니다. 검색으로 좁혀 주세요.",
                    $"还有 {hidden} 个。请用搜索缩小范围。", $"還有 {hidden} 個。請用搜尋縮小範圍。"));
            }

            // 上限をすべて外す
            if (limiter.LimitedCount > 0
                && GUILayout.Button(Loc.T("Remove all limits", "上限をすべて解除", "상한 모두 해제", "解除全部上限", "解除全部上限"),
                    GuiControls.Shrinkable))
            {
                limiter.ClearAll();
                changed = true;
            }

            // 変えたらこのアバターの分として記録する（保存は終了時）
            if (changed && _session.Current != null)
            {
                _settings.SetBlendShapeLimits(_session.Current.SourcePath, limiter.Export());
            }

            GuiControls.EndCard();
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
