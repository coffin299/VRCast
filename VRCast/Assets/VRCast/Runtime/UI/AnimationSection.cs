using System.Collections.Generic;
using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// Pose タブ（待機ポーズ・待機モーション・表情）。表示中アバターのコントローラーを操作する。
    /// </summary>
    public class AnimationSection
    {
        // 表情ボタンの列数（全ボタン同じ幅で並べる）
        private const int ExpressionColumns = 2;

        // ボタンに表示する名前の最大文字数
        private const int MaxButtonLabelLength = 22;

        // 共通接頭辞を切る位置の区切り文字
        private static readonly char[] PrefixSeparators = { '_', '-', ' ' };

        private readonly AvatarComponentCache _avatar;
        private readonly AppSettings _settings;
        // 表情ボタンの表示名（アバター切替時に作る）
        private string[] _labels = new string[0];
        private PoseController _pose;
        private ExpressionController _expressions;
        private IdleMotionController _idleMotion;

        public AnimationSection(AvatarSession session, AppSettings settings)
        {
            _avatar = new AvatarComponentCache(session);
            _settings = settings;
        }

        public void Draw()
        {
            // アバター未表示なら案内だけ
            if (!RefreshControllers())
            {
                GuiControls.BeginCard(Loc.T("Pose", "ポーズ", "포즈", "姿势", "姿勢"));
                GuiControls.Hint(Loc.T("Load an avatar first.", "先にアバターを読み込んでください。", "먼저 아바타를 불러오세요.",
                    "请先加载虚拟形象。", "請先載入虛擬形象。"));
                GuiControls.EndCard();
                return;
            }

            DrawPose();
            DrawIdleMotion();
            DrawExpressions();
        }

        private bool RefreshControllers()
        {
            // アバターが替わったときだけ取り直す
            if (_avatar.Refresh())
            {
                _pose = _avatar.Get<PoseController>();
                _expressions = _avatar.Get<ExpressionController>();
                _idleMotion = _avatar.Get<IdleMotionController>();
                BuildLabels();
            }

            return _avatar.HasAvatar;
        }

        private void DrawPose()
        {
            GuiControls.BeginCard(Loc.T("Pose", "ポーズ", "포즈", "姿势", "姿勢"));
            if (_pose == null)
            {
                GuiControls.EndCard();
                return;
            }

            GuiControls.Hint(Loc.T("Body yaw and arm pose are remembered for each avatar.",
                "体の向きと腕のポーズはアバターごとに記憶されます。",
                "몸 방향과 팔 포즈는 아바타마다 기억됩니다.",
                "身体朝向和手臂姿势会按虚拟形象分别记住。",
                "身體朝向和手臂姿勢會依虛擬形象分別記住。"));

            // アバターの向き（非 Humanoid でも有効）
            _pose.BodyYaw = GuiControls.Slider(Loc.T("Body yaw", "体の向き", "몸 방향", "身体朝向", "身體朝向"), _pose.BodyYaw, -180f, 180f);

            // 非 Humanoid は腕の操作不可
            if (!_pose.IsAvailable)
            {
                GuiControls.Hint(Loc.T("Arm pose requires a Humanoid avatar.", "腕のポーズは Humanoid アバターのみ対応です。",
                    "팔 포즈는 Humanoid 아바타만 지원합니다.",
                    "手臂姿势仅支持 Humanoid 虚拟形象。", "手臂姿勢僅支援 Humanoid 虛擬形象。"));
                GuiControls.EndCard();
                return;
            }

            // よく使う 3 状態へのショートカット
            GUILayout.BeginHorizontal();

            // 気を付け（既定）: 腕を下ろし切り、肘はまっすぐ
            if (GUILayout.Button(Loc.T("Attention", "気を付け", "차렷", "立正", "立正"), GuiControls.Shrinkable))
            {
                _pose.ArmDown = 1f;
                _pose.ElbowBend = 0f;
            }

            // 腕を少し開き、肘を軽く曲げる
            if (GUILayout.Button(Loc.T("Relaxed", "リラックス", "편안하게", "放松", "放鬆"), GuiControls.Shrinkable))
            {
                _pose.ArmDown = 0.85f;
                _pose.ElbowBend = 0.3f;
            }

            // 読込時の姿勢（通常 T ポーズ）
            if (GUILayout.Button(Loc.T("T-Pose", "T ポーズ", "T 포즈", "T 姿势", "T 姿勢"), GuiControls.Shrinkable))
            {
                _pose.ArmDown = 0f;
                _pose.ElbowBend = 0f;
            }

            GUILayout.EndHorizontal();

            // 腕と肘の度合い
            _pose.ArmDown = GuiControls.Slider(Loc.T("Arms down", "腕を下ろす", "팔 내리기", "放下手臂", "放下手臂"), _pose.ArmDown, 0f, 1f);
            _pose.ElbowBend = GuiControls.Slider(Loc.T("Elbow bend", "肘の曲げ", "팔꿈치 굽힘", "肘部弯曲", "肘部彎曲"), _pose.ElbowBend, 0f, 1f);
            GuiControls.EndCard();
        }

        private void DrawIdleMotion()
        {
            GuiControls.BeginCard(Loc.T("Idle motion", "待機モーション", "대기 모션", "待机动作", "待機動作"));

            // 非 Humanoid は動かせるボーンが無い
            if (_idleMotion == null || !_idleMotion.IsAvailable)
            {
                GuiControls.Hint(Loc.T("Idle motion requires a Humanoid avatar.", "待機モーションは Humanoid アバターのみ対応です。",
                    "대기 모션은 Humanoid 아바타만 지원합니다.",
                    "待机动作仅支持 Humanoid 虚拟形象。", "待機動作僅支援 Humanoid 虛擬形象。"));
                GuiControls.EndCard();
                return;
            }

            _settings.idleMotionEnabled = GUILayout.Toggle(
                _settings.idleMotionEnabled, Loc.T("Breathing and sway", "呼吸・体の揺れ", "호흡 · 몸 흔들림",
                    "呼吸 · 身体摇摆", "呼吸 · 身體搖擺"));

            // OFF の間は強さの調整を出さない
            if (!_settings.idleMotionEnabled)
            {
                GuiControls.EndCard();
                return;
            }

            GuiControls.Hint(Loc.T("Head motion pauses while face tracking is active.",
                "顔のトラッキング中は、頭のゆらぎを止めて本人の動きに任せます。",
                "얼굴 트래킹 중에는 머리 흔들림을 멈추고 본인의 움직임을 따릅니다.",
                "面部追踪时会停止头部晃动，跟随本人的动作。",
                "臉部追蹤時會停止頭部晃動，跟隨本人的動作。"));

            // 強さ（0 = 動かさない、1 = 標準）と速さの倍率
            float max = AppSettings.MaxIdleMotionStrength;
            _settings.idleBreathing = GuiControls.Slider(
                Loc.T("Breathing", "呼吸", "호흡", "呼吸", "呼吸"), _settings.idleBreathing, 0f, max);
            _settings.idleSway = GuiControls.Slider(
                Loc.T("Body sway", "体の揺れ", "몸 흔들림", "身体摇摆", "身體搖擺"), _settings.idleSway, 0f, max);
            _settings.idleHeadMotion = GuiControls.Slider(
                Loc.T("Head motion", "頭のゆらぎ", "머리 흔들림", "头部晃动", "頭部晃動"), _settings.idleHeadMotion, 0f, max);
            _settings.idleMotionSpeed = GuiControls.Slider(
                Loc.T("Speed", "速さ", "속도", "速度", "速度"), _settings.idleMotionSpeed,
                AppSettings.MinIdleMotionSpeed, AppSettings.MaxIdleMotionSpeed);
            GuiControls.EndCard();
        }

        private void DrawExpressions()
        {
            GuiControls.BeginCard(Loc.T("Expressions", "表情", "표정", "表情", "表情"));
            GuiControls.Hint(Loc.T("Keys 1-9 to switch, 0 for neutral", "キー 1〜9 で切り替え、0 でニュートラル",
                "키 1~9로 전환, 0으로 무표정",
                "按 1-9 键切换，0 键恢复无表情", "按 1-9 鍵切換，0 鍵恢復無表情"));

            // 表情データが無いアバター
            if (_expressions == null || _expressions.Names.Count == 0)
            {
                GuiControls.Hint(Loc.T("No expressions in this package.", "このアバターには表情データがありません。",
                    "이 아바타에는 표정 데이터가 없습니다.",
                    "此虚拟形象没有表情数据。", "此虛擬形象沒有表情資料。"));
                GuiControls.EndCard();
                return;
            }

            // ニュートラルは常に先頭
            if (GUILayout.Toggle(_expressions.Current < 0, Loc.T("Neutral", "ニュートラル", "무표정", "无表情", "無表情"), GUI.skin.button))
            {
                if (_expressions.Current >= 0)
                {
                    _expressions.ResetToNeutral();
                }
            }

            // 表情ボタンを同じ幅の格子に並べる（選択中はアクセント色。パネル全体がスクロールする）
            int selected = GUILayout.SelectionGrid(
                _expressions.Current, _labels, ExpressionColumns, GUI.skin.button, GuiControls.Shrinkable);
            if (selected != _expressions.Current && selected >= 0)
            {
                _expressions.Apply(selected);
            }

            GuiControls.EndCard();
        }

        private void BuildLabels()
        {
            // 表情が無ければ空
            if (_expressions == null)
            {
                _labels = new string[0];
                return;
            }

            // 全表情に共通する接頭辞（区切り文字まで）を表示から省く
            IReadOnlyList<string> names = _expressions.Names;
            int prefixLength = CommonPrefixLength(names);
            _labels = new string[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                // 接頭辞を除き、長い名前は省略
                string name = names[i].Substring(prefixLength);
                if (name.Length > MaxButtonLabelLength)
                {
                    name = name.Substring(0, MaxButtonLabelLength - 1) + "…";
                }

                // ホットキー対象には番号を前置
                _labels[i] = i < 9 ? $"{i + 1}: {name}" : name;
            }
        }

        private static int CommonPrefixLength(IReadOnlyList<string> names)
        {
            // 1 件以下なら省略しない
            if (names.Count < 2)
            {
                return 0;
            }

            // 全名前で一致する先頭文字数を求める
            int length = names[0].Length;
            for (int i = 1; i < names.Count; i++)
            {
                int max = Mathf.Min(length, names[i].Length);
                int j = 0;
                while (j < max && names[i][j] == names[0][j])
                {
                    j++;
                }

                length = j;
            }

            // 単語の途中で切らないよう、最後の区切り文字の直後まで戻す
            int cut = names[0].LastIndexOfAny(PrefixSeparators, Mathf.Max(0, length - 1)) + 1;
            if (length == 0 || cut <= 0)
            {
                return 0;
            }

            // 名前が空になる場合は省略しない
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i].Length <= cut)
                {
                    return 0;
                }
            }

            return cut;
        }
    }
}
