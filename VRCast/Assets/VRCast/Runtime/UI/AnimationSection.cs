using System.Collections.Generic;
using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Platform;
using VRCast.Remote;

namespace VRCast.UI
{
    /// <summary>
    /// Pose タブ（待機ポーズ・表情）。表示中アバターのコントローラーを操作する。
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

        // 外部操作の待ち受け（状態の表示用）と、入力途中のポート番号
        private readonly RemoteControl _remote;
        private string _oscPortInput;
        private string _httpPortInput;

        // 仮想キーの押下と表示名（描画のたびにデリゲートを作り直さないように控える）
        private static readonly System.Func<int, bool> IsKeyDown = GlobalKeyboard.IsDown;
        private static readonly System.Func<int, string> KeyName = GlobalKeyboard.KeyName;

        // キーの割り当て中か、その対象（-1 = ニュートラル）、最後に表情の一覧を描いたフレーム、最後にキーを読んだフレーム
        private bool _capturing;
        private int _captureTarget;
        private int _lastDrawFrame = -1;
        private int _lastPollFrame = -1;
        private KeyCapture _keyCapture = new KeyCapture();

        public AnimationSection(AvatarSession session, RemoteControl remote, AppSettings settings)
        {
            _session = session;
            _avatar = new AvatarComponentCache(session);
            _remote = remote;
            _settings = settings;
            SyncFromSettings();
        }

        /// <summary>
        /// 設定値を直接書き換えた後（全設定のリセット等）に、入力途中の値を設定に合わせ直す。
        /// </summary>
        public void SyncFromSettings()
        {
            _oscPortInput = _settings.remoteOscPort.ToString();
            _httpPortInput = _settings.remoteHttpPort.ToString();
        }

        public void Draw()
        {
            // アバター未表示なら案内と外部操作の設定だけ
            if (!RefreshControllers())
            {
                GuiControls.BeginCard(Loc.T("Pose", "ポーズ", "포즈", "姿势", "姿勢"));
                GuiControls.Hint(Loc.T("Load an avatar first.", "先にアバターを読み込んでください。", "먼저 아바타를 불러오세요.",
                    "请先加载虚拟形象。", "請先載入虛擬形象。"));
                GuiControls.EndCard();
                DrawRemote();
                return;
            }

            DrawPose();
            DrawExpressions();
            DrawRemote();
        }

        private bool RefreshControllers()
        {
            // アバターが替わったときだけ取り直す
            if (_avatar.Refresh())
            {
                _pose = _avatar.Get<PoseController>();
                _expressions = _avatar.Get<ExpressionController>();
                BuildLabels();

                // 別のアバターの表情へ割り当てないよう、割り当て中ならやめる
                _capturing = false;
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
                _capturing = false;
                return;
            }

            HandleCapture();

            // 手動で選んだ表情の扱い
            GuiControls.Hint(Loc.T(
                "An expression chosen with a button, shortcut key or external control stays until you choose it again, which returns to auto detection.",
                "ボタン・ショートカットキー・外部操作で選んだ表情は固定され、トラッキングの自動検出では変わりません。同じ表情をもう一度選ぶと自動検出に戻ります。",
                "버튼·단축키·외부 조작으로 고른 표정은 고정되어 자동 감지로 바뀌지 않습니다. 같은 표정을 다시 고르면 자동 감지로 돌아갑니다.",
                "通过按钮、快捷键或外部操作选择的表情会被固定，不会被自动检测改变。再次选择同一表情即可恢复自动检测。",
                "透過按鈕、快捷鍵或外部操作選擇的表情會被固定，不會被自動偵測改變。再次選擇同一表情即可恢復自動偵測。"));

            // 割り当て中はやり方を、それ以外は使い方を案内する
            GuiControls.Hint(_capturing
                ? Loc.T("Press the key to assign (with Ctrl / Alt / Shift if you like). To use Ctrl, Alt or Shift alone, press and release it. Esc to cancel (Tab cannot be used).",
                    "割り当てるキーを押してください（Ctrl・Alt・Shift と同時押しも可）。Ctrl・Alt・Shift だけを使うときは押して離します。Esc でやめます（Tab は使えません）。",
                    "할당할 키를 누르세요 (Ctrl·Alt·Shift와 동시에 눌러도 됩니다). Ctrl·Alt·Shift만 쓰려면 눌렀다 떼세요. Esc로 취소합니다 (Tab은 사용할 수 없습니다).",
                    "请按下要分配的键（也可同时按 Ctrl / Alt / Shift）。单独使用 Ctrl / Alt / Shift 时请按下后松开。按 Esc 取消（不能使用 Tab）。",
                    "請按下要分配的鍵（也可同時按 Ctrl / Alt / Shift）。單獨使用 Ctrl / Alt / Shift 時請按下後放開。按 Esc 取消（不能使用 Tab）。")
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

            // ニュートラルは常に先頭、以降は表情（選択中はアクセント色。パネル全体がスクロールする）
            DrawExpressionRow(-1, Loc.T("Neutral", "ニュートラル", "무표정", "无表情", "無表情"));
            for (int i = 0; i < _labels.Length; i++)
            {
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
            bool capturingThis = _capturing && _captureTarget == preset;
            KeyCombo combo = _expressions.GetHotkey(preset);
            string keyLabel = capturingThis
                ? Loc.T("Press a key…", "キーを押す…", "키를 누르세요…", "请按键…", "請按鍵…")
                : combo.IsAssigned
                    ? combo.Describe(KeyName)
                    : Loc.T("Unassigned", "未割り当て", "미할당", "未分配", "未分配");
            if (GUILayout.Button(keyLabel, GUILayout.Width(KeyButtonWidth)))
            {
                _capturing = !capturingThis;
                _captureTarget = preset;

                // 割り当て待ちは毎回まっさらから始める（前回の押下状態を引き継がない）
                _keyCapture = new KeyCapture();
            }

            // リセット（割り当てを外す。未割り当てなら押せない）
            GUI.enabled = combo.IsAssigned;
            if (GUILayout.Button(Loc.T("Reset", "リセット", "초기화", "重置", "重設"), GUILayout.Width(ResetButtonWidth)))
            {
                _expressions.SetHotkey(preset, KeyCombo.None);
                SaveHotkeys();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawRemote()
        {
            GuiControls.BeginCard(Loc.T("External control (OSC / HTTP)", "外部から操作（OSC / HTTP）", "외부 조작 (OSC / HTTP)",
                "外部操作（OSC / HTTP）", "外部操作（OSC / HTTP）"));
            _settings.remoteControlEnabled = GUILayout.Toggle(
                _settings.remoteControlEnabled,
                Loc.T("Allow expression control from Stream Deck, OSC apps, etc.",
                    "Stream Deck や OSC アプリなどから表情を操作できるようにする",
                    "Stream Deck·OSC 앱 등에서 표정을 조작할 수 있게 하기",
                    "允许从 Stream Deck、OSC 应用等操作表情",
                    "允許從 Stream Deck、OSC 應用程式等操作表情"));

            // 無効時は ON/OFF だけ
            if (!_settings.remoteControlEnabled)
            {
                GuiControls.EndCard();
                return;
            }

            // 待ち受けポート（範囲内の数値になったときだけ反映し、待ち受けが開き直す）と状態
            _settings.remoteOscPort = GuiControls.PortField(
                Loc.T("OSC port (UDP)", "OSC ポート（UDP）", "OSC 포트 (UDP)", "OSC 端口（UDP）", "OSC 連接埠（UDP）"),
                ref _oscPortInput, _settings.remoteOscPort, AppSettings.MinTrackingPort, AppSettings.MaxTrackingPort);
            GuiControls.Hint(_remote != null ? _remote.OscStatus : string.Empty);
            _settings.remoteHttpPort = GuiControls.PortField(
                Loc.T("HTTP port", "HTTP ポート", "HTTP 포트", "HTTP 端口", "HTTP 連接埠"),
                ref _httpPortInput, _settings.remoteHttpPort, AppSettings.MinTrackingPort, AppSettings.MaxTrackingPort);
            GuiControls.Hint(_remote != null ? _remote.HttpStatus : string.Empty);

            // 使い方（コマンドは言語に依らない）
            GuiControls.Hint(Loc.T(
                "Only this PC can connect. Expressions are given by name or number (0 = neutral, 1 and up = the order above). Add toggle to return to auto detection when the same expression is chosen again.",
                "この PC からだけ接続できます。表情は名前か番号（0 = ニュートラル、1 以降 = 上の並び順）で指定します。toggle を付けると、同じ表情をもう一度選んだときに自動検出へ戻ります。",
                "이 PC에서만 연결할 수 있습니다. 표정은 이름이나 번호 (0 = 무표정, 1 이후 = 위의 순서)로 지정합니다. toggle을 붙이면 같은 표정을 다시 고를 때 자동 감지로 돌아갑니다.",
                "仅限本机连接。表情可用名称或编号指定（0 = 无表情，1 起 = 上方的顺序）。加上 toggle 后，再次选择同一表情会恢复自动检测。",
                "僅限本機連線。表情可用名稱或編號指定（0 = 無表情，1 起 = 上方的順序）。加上 toggle 後，再次選擇同一表情會恢復自動偵測。"));
            GuiControls.Hint($"HTTP: http://127.0.0.1:{_settings.remoteHttpPort}/expression?name=Smile&toggle=1  /expression?index=1  /neutral  /auto  /status");
            GuiControls.Hint("OSC: /vrcast/expression \"Smile\" | 1   /vrcast/toggle/Smile   /vrcast/neutral   /vrcast/auto");
            GuiControls.EndCard();
        }

        private void HandleCapture()
        {
            // 別のタブにいた・パネルを隠していた等で描画が途切れていたら、割り当てをやめる
            bool interrupted = Time.frameCount - _lastDrawFrame > 1;
            _lastDrawFrame = Time.frameCount;
            if (!_capturing || interrupted)
            {
                _capturing = false;
                return;
            }

            // 割り当て中は、押したキーで表情が変わったりパネルが消えたりしないようにする
            ExpressionController.SuspendHotkeys();

            // 割り当て中のキー入力は、ほかの GUI に渡さない
            Event current = Event.current;
            if (current.type == EventType.KeyDown || current.type == EventType.KeyUp)
            {
                current.Use();
            }

            // キーの状態は 1 フレームに 1 回だけ読む（OnGUI はイベントごとに何度も呼ばれる）
            if (_lastPollFrame == Time.frameCount)
            {
                return;
            }

            _lastPollFrame = Time.frameCount;

            // Esc でやめ、組み合わせが決まったら割り当てて終わる（Windows のキーの状態で読むので、テンキーや記号キーも区別できる）
            switch (_keyCapture.Poll(IsKeyDown))
            {
                case KeyCapture.Status.Cancelled:
                    _capturing = false;
                    break;
                case KeyCapture.Status.Captured:
                    _expressions.SetHotkey(_captureTarget, _keyCapture.Result);
                    SaveHotkeys();
                    _capturing = false;
                    break;
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
