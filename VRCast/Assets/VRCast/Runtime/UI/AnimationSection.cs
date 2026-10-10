using System.Collections.Generic;
using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// Pose タブ（待機ポーズ・待機モーション・表情）。表示中アバターのコントローラーを操作する。
    /// 表情ごとにショートカットキーを割り当てられる（クリックで割り当て開始、Esc でやめる、アバターごとに記録）。
    /// 割り当ては Windows のキーの状態で読む（テンキー・記号キー・左右の修飾キー単独も区別できる）。
    /// </summary>
    public class AnimationSection
    {
        // ボタンに表示する名前の最大文字数
        private const int MaxButtonLabelLength = 22;

        // キーの割り当てボタンとリセットボタンの幅
        private const float KeyButtonWidth = 120f;
        private const float ResetButtonWidth = 70f;

        // 共通接頭辞を切る位置の区切り文字
        private static readonly char[] PrefixSeparators = { '_', '-', ' ' };

        private readonly AvatarSession _session;
        private readonly AvatarComponentCache _avatar;
        private readonly AppSettings _settings;
        // 表情ボタンの表示名（アバター切替時に作る）
        private string[] _labels = new string[0];
        private PoseController _pose;
        private ExpressionController _expressions;
        private IdleMotionController _idleMotion;

        // 表情のショートカットキーの割り当て（対象 = プリセット、-1 = ニュートラル）
        private readonly KeyCaptureSession _capture = new KeyCaptureSession();

        public AnimationSection(AvatarSession session, AppSettings settings)
        {
            _session = session;
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

                // 別のアバターの表情へ割り当てないよう、割り当て中ならやめる
                _capture.Cancel();
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

            // 表情データが無いアバター
            if (_expressions == null || _expressions.Names.Count == 0)
            {
                GuiControls.Hint(Loc.T("No expressions in this package.", "このアバターには表情データがありません。",
                    "이 아바타에는 표정 데이터가 없습니다.",
                    "此虚拟形象没有表情数据。", "此虛擬形象沒有表情資料。"));
                GuiControls.EndCard();
                _capture.Cancel();
                return;
            }

            // 割り当てが決まったら表情に割り当てて記録する
            if (_capture.Update(out int target, out KeyCombo captured))
            {
                _expressions.SetHotkey(target, captured);
                SaveHotkeys();
            }

            // 手動で選んだ表情の扱い
            GuiControls.Hint(Loc.T(
                "An expression chosen with a button, shortcut key or external control stays until you choose it again, which returns to auto detection.",
                "ボタン・ショートカットキー・外部操作で選んだ表情は固定され、トラッキングの自動検出では変わりません。同じ表情をもう一度選ぶと自動検出に戻ります。",
                "버튼·단축키·외부 조작으로 고른 표정은 고정되어 자동 감지로 바뀌지 않습니다. 같은 표정을 다시 고르면 자동 감지로 돌아갑니다.",
                "通过按钮、快捷键或外部操作选择的表情会被固定，不会被自动检测改变。再次选择同一表情即可恢复自动检测。",
                "透過按鈕、快捷鍵或外部操作選擇的表情會被固定，不會被自動偵測改變。再次選擇同一表情即可恢復自動偵測。"));

            // 割り当て中はやり方を、それ以外は使い方を案内する
            GuiControls.Hint(_capture.IsCapturing
                ? KeyCaptureSession.AssignHint
                : Loc.T("Click a key button to assign a shortcut key. Any key works, including combinations such as Ctrl+Alt+N, the numeric keypad (with NumLock on), symbol keys, left / right Ctrl alone and mouse side buttons. Saved per avatar.",
                    "キーのボタンを押すとショートカットキーを割り当てられます。Ctrl+Alt+N のような組み合わせ、テンキー（NumLock ON で区別）、記号キー、右 Ctrl などの単独、マウスのサイドボタンも使えます。アバターごとに保存されます。",
                    "키 버튼을 누르면 단축키를 할당할 수 있습니다. Ctrl+Alt+N 같은 조합, 숫자 키패드 (NumLock 켜짐에서 구별), 기호 키, 오른쪽 Ctrl 등의 단독 키, 마우스 사이드 버튼도 사용할 수 있습니다. 아바타별로 저장됩니다.",
                    "点击按键按钮即可分配快捷键。也可使用 Ctrl+Alt+N 等组合、小键盘（NumLock 开启时可区分）、符号键、右 Ctrl 等单独按键以及鼠标侧键。按虚拟形象分别保存。",
                    "點擊按鍵按鈕即可分配快捷鍵。也可使用 Ctrl+Alt+N 等組合、數字鍵台（NumLock 開啟時可區分）、符號鍵、右 Ctrl 等單獨按鍵以及滑鼠側鍵。依虛擬形象分別儲存。"));

            // 背面でも使うか（OBS などを操作中でも切り替えられる。単独のキーはほかのアプリでの入力中も反応する）
            _settings.expressionHotkeysInBackground = GUILayout.Toggle(
                _settings.expressionHotkeysInBackground,
                Loc.T("Also work when VRCast is in the background", "VRCast が背面にあるときも使う",
                    "VRCast가 뒤에 있을 때도 사용", "VRCast 在后台时也可使用", "VRCast 在背景時也可使用"));
            if (_settings.expressionHotkeysInBackground)
            {
                GuiControls.Hint(Loc.T(
                    "Keys also react while you type in other apps, so combinations with Ctrl or Alt, or the numeric keypad, are recommended.",
                    "ほかのアプリで文字を入力している間も反応するため、Ctrl や Alt との組み合わせ、またはテンキーがおすすめです。",
                    "다른 앱에서 입력하는 동안에도 반응하므로 Ctrl이나 Alt와의 조합 또는 숫자 키패드를 권장합니다.",
                    "在其他应用中输入时也会响应，建议与 Ctrl 或 Alt 组合使用，或使用小键盘。",
                    "在其他應用程式中輸入時也會回應，建議與 Ctrl 或 Alt 組合使用，或使用數字鍵台。"));
            }

            // 手動で固定中なら、その旨と自動検出へ戻すボタン
            if (_expressions.IsManual)
            {
                GUILayout.BeginHorizontal();
                GuiControls.Status(Loc.T("Expression locked (auto detection paused)", "表情を固定中（自動検出は停止）",
                    "표정 고정 중 (자동 감지 정지)", "表情已固定（自动检测暂停）", "表情已固定（自動偵測暫停）"), true);
                if (GUILayout.Button(Loc.T("Back to auto", "自動検出に戻す", "자동 감지로 복귀", "恢复自动检测", "恢復自動偵測"),
                        GUILayout.ExpandWidth(false)))
                {
                    _expressions.ReleaseManual();
                }

                GUILayout.EndHorizontal();
            }

            // 既定で非表示の表情があるときだけ、一覧に出すかを選べる
            if (_expressions.HiddenCount > 0)
            {
                _settings.showHiddenExpressions = GUILayout.Toggle(
                    _settings.showHiddenExpressions,
                    string.Format(Loc.T(
                            "Also show other clips ({0}: blend shapes taken from clips that also toggle items, etc.)",
                            "ほかのクリップも表示（{0} 個。小物の切り替えなども含むクリップから BlendShape だけを取り出したもの）",
                            "다른 클립도 표시 ({0}개. 소품 전환 등도 포함한 클립에서 BlendShape만 꺼낸 것)",
                            "也显示其他剪辑（{0} 个。从还包含道具切换等的剪辑中只取出 BlendShape）",
                            "也顯示其他剪輯（{0} 個。從還包含道具切換等的剪輯中只取出 BlendShape）"),
                        _expressions.HiddenCount));
            }

            // ニュートラルは常に先頭、以降は表情（選択中はアクセント色。パネル全体がスクロールする）
            DrawExpressionRow(-1, Loc.T("Neutral", "ニュートラル", "무표정", "无表情", "無表情"));
            for (int i = 0; i < _labels.Length; i++)
            {
                // 非表示の表情は設定で表示したときだけ出す（選択中なら戻せるよう出しておく）
                if (_expressions.IsHidden(i) && !_settings.showHiddenExpressions && _expressions.Current != i)
                {
                    continue;
                }

                DrawExpressionRow(i, _labels[i]);
            }

            GuiControls.EndCard();
        }

        private void DrawExpressionRow(int preset, string label)
        {
            GUILayout.BeginHorizontal();

            // 表情ボタン（押すとその表情で固定。固定中の表情を押し直すと自動検出へ戻す）
            bool selected = _expressions.Current == preset;
            if (GUILayout.Toggle(selected, label, GUI.skin.button, GuiControls.Shrinkable) != selected)
            {
                _expressions.Select(preset, true);
            }

            // キーのボタン（押すと割り当て開始、割り当て中の行をもう一度押すとやめる）
            KeyCombo combo = _expressions.GetHotkey(preset);
            _capture.DrawKeyButton(preset, combo, KeyButtonWidth);

            // リセット（割り当てを外す。未割り当てなら押せない）
            GUI.enabled = combo.IsAssigned;
            if (GUILayout.Button(Loc.T("Reset", "リセット", "초기화", "重置", "重設"), GUILayout.Width(ResetButtonWidth)))
            {
                _expressions.SetHotkey(preset, KeyCombo.None);
                SaveHotkeys();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // リセットのショートカットキーと同じなら警告（表情が優先され、そのリセットはこのキーでは動かない）
            if (_settings.TryFindResetHotkey(combo, out ResetAction action))
            {
                GuiControls.Warning(string.Format(Loc.T(
                        "⚠ Same key as the \"{0}\" reset shortcut. This expression takes priority (change it in Settings).",
                        "⚠ リセット「{0}」のショートカットキーと同じです。表情が優先されます（設定タブで変更できます）。",
                        "⚠ 「{0}」 초기화 단축키와 같습니다. 표정이 우선됩니다 (설정 탭에서 변경할 수 있습니다).",
                        "⚠ 与“{0}”重置快捷键相同。表情优先（可在设置标签页中更改）。",
                        "⚠ 與「{0}」重設快捷鍵相同。表情優先（可在設定分頁中變更）。"),
                    ResetActions.Label(action)));
            }
        }

        private void SaveHotkeys()
        {
            // このアバターの分として記録する（保存は終了時）
            if (_session.Current != null)
            {
                _settings.SetExpressionHotkeys(_session.Current.SourcePath, _expressions.ExportHotkeys());
            }
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

                _labels[i] = name;
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
