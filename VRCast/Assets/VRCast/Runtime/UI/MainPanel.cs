using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Animations;
using VRCast.Audio;
using VRCast.Avatars;
using VRCast.Cameras;
using VRCast.Core;
using VRCast.Mascot;
using VRCast.Output;
using VRCast.Platform;
using VRCast.Remote;
using VRCast.Rendering;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// IMGUI の操作パネル。左のタブ（はじめに / アバター・オブジェクト・シェイプキー / 顔・表情・体・ポーズ・トラッキング /
    /// 表示・グラフィック・出力 / 操作・連携・パフォーマンス・設定・情報・公式 Discord / パーフェクトシンク化）で表示するセクションを切り替え、
    /// 内容は縦スクロールする（タブ列も収まらなければ縦スクロール）。下部にはリセットボタン・動作状況を常に表示する。
    /// 画面に収まる高さに制限し、Tab キーで表示切替（隠している間は背景も透過）。
    /// 表示言語（見出しの下のボタンでいつでも切替）と UI の大きさは設定に従う。見出しの「?」でヘルプページを開く。
    /// パーフェクトシンク化の編集中は、このパネルの代わりに編集画面（PerfectSyncEditor）を出す。
    /// </summary>
    public class MainPanel : MonoBehaviour
    {
        // パネルの表示切替キー（配信時に UI を隠す）
        private const KeyCode ToggleKey = KeyCode.Tab;

        // IMGUI ウィンドウ ID・大きさ・画面端からの余白
        private const int WindowId = 0x5643;
        // 言語バー・UI 大きさのボタン列が収まる幅（狭いと右が見切れる）
        private const float WindowWidth = 680f;
        private const float MaxWindowHeight = 760f;
        private const float MinWindowHeight = 240f;
        private const float ScreenMargin = 10f;

        // 見出し行（ドラッグで移動できる範囲）とタブ列の最低の幅（長いタブ名の言語では広げる）
        private const float HeaderHeight = 36f;
        private const float SidebarWidth = 150f;

        // タブ列のまとまりの間の余白
        private const float TabGroupSpacing = 8f;

        // 見出しのヘルプボタンの幅
        private const float HelpButtonWidth = 32f;

        // タブの内容と下部のリセットボタンの間隔
        private const float FooterSpacing = 8f;

        // タブの並び（タブ列の上から順。パーフェクトシンク化は別の色で一番下）
        private enum Tab
        {
            Start,
            Avatar,
            Objects,
            ShapeKeys,
            Face,
            Body,
            Tracking,
            Display,
            Graphics,
            Output,
            Control,
            Performance,
            Settings,
            Info,
            Discord,
            PerfectSync,
        }

        private static readonly int TabCount = Enum.GetValues(typeof(Tab)).Length;

        // 前に余白を空けて、まとまりの先頭にするタブ
        private static readonly Tab[] GroupStarts = { Tab.Avatar, Tab.Face, Tab.Display, Tab.Control, Tab.PerfectSync };

        private AvatarSession _session;
        private FileDropReceiver _fileDrop;
        private UpdateChecker _updates;
        private OrbitCameraController _orbit;
        private AppSettings _settings;
        private RenderingController _rendering;
        private VirtualCameraOutput _virtualCamera;
        private SpoutOutput _spout;
        private StartSection _startSection;
        private AvatarSection _avatarSection;
        private AnimationSection _animationSection;
        private ObjectSection _objectSection;
        private ParticleSection _particleSection;
        private FaceSection _faceSection;
        private ExpressionSection _expressionSection;
        private ShapeKeySection _shapeKeySection;
        private PerfectSyncSection _perfectSyncSection;
        private PerfectSyncEditor _perfectSyncEditor;
        private TrackingSection _trackingSection;
        private DisplaySection _displaySection;
        private GraphicsSection _graphicsSection;
        private OutputSection _outputSection;
        private ShortcutSection _shortcutSection;
        private AutoResetSection _autoResetSection;
        private RemoteSection _remoteSection;
        private PerformanceSection _performanceSection;
        private SettingsSection _settingsSection;
        private PresetSection _presetSection;
        private InfoSection _infoSection;
        private readonly DiscordSection _discordSection = new DiscordSection();
        private ResetBar _resetBar;
        private PerformanceBar _performanceBar;
        private readonly SurveyBar _surveyBar = new SurveyBar();
        // 起動時はモード選択。配信は今までの画面、マスコットは選択画面を残して別ウィンドウにモデルを出す
        private enum LaunchMode
        {
            Choose,
            Broadcast,
            Mascot,
        }

        private LaunchMode _launch = LaunchMode.Choose;
        private MascotMode _mascot;
        private IFaceTrackingProvider _tracker;
        private TrackerProcess _trackerProcess;
        private bool _suspendHeld;
        private bool _holdInputHeld;

        // モードを選ぶまではメイン画面にアバターを出さない
        private int _mainMask;
        private bool _mainMaskHidden;

        // テーマ（最初の OnGUI で作成）
        private UiTheme _theme;

        // 表示状態・選択中のタブ・タブごとのスクロール位置
        private bool _visible = true;
        private Tab _tab = Tab.Start;
        private readonly Vector2[] _scroll = new Vector2[TabCount];

        // タブ列のスクロール位置（パネルが低くて全部のタブが収まらないとき）
        private Vector2 _sidebarScroll;

        private Rect _windowRect = new Rect(ScreenMargin, ScreenMargin, WindowWidth, MaxWindowHeight);

        public void Initialize(
            AvatarSession session, OrbitCameraController orbit, RenderingController rendering,
            MicrophoneInput microphone, IFaceTrackingProvider tracker, TrackerProcess trackerProcess,
            TrackingSkeletonView skeleton, VirtualCameraOutput virtualCamera, SpoutOutput spout,
            FileDropReceiver fileDrop, UpdateChecker updates, RemoteControl remote, AppSettings settings, string initialPath)
        {
            // 依存の受け取りと各タブの作成
            _session = session;
            _fileDrop = fileDrop;
            _updates = updates;
            _orbit = orbit;
            _settings = settings;
            _tracker = tracker;
            _rendering = rendering;
            _virtualCamera = virtualCamera;
            _spout = spout;
            _avatarSection = new AvatarSection(session, settings, initialPath);
            _startSection = new StartSection(session, _avatarSection, rendering, virtualCamera, OpenLink);
            _animationSection = new AnimationSection(session, settings);
            _objectSection = new ObjectSection(session, settings);
            _particleSection = new ParticleSection(session, settings);
            _faceSection = new FaceSection(session, microphone, settings);
            _expressionSection = new ExpressionSection(session, settings);
            _shapeKeySection = new ShapeKeySection(session, settings);
            _perfectSyncEditor = new PerfectSyncEditor(session, orbit, settings);
            _perfectSyncSection = new PerfectSyncSection(session, _perfectSyncEditor);
            _trackingSection = new TrackingSection(session, tracker, trackerProcess, skeleton, settings);
            _displaySection = new DisplaySection(orbit, rendering, settings);
            _graphicsSection = new GraphicsSection(rendering, settings, ReapplySettings);

            // 背景透過のスクリーンショットは画面と同じカメラの視点で撮る（出力タブ・パネル下部のボタン・ショートカットキーで共用）
            var screenshot = new AvatarScreenshot(orbit.GetComponent<UnityEngine.Camera>(), settings);
            _outputSection = new OutputSection(virtualCamera, spout, rendering, screenshot, settings);
            _shortcutSection = new ShortcutSection(session, settings);
            _remoteSection = new RemoteSection(session, remote, settings);

            // パーフェクトシンク化の編集中は、外部からアバターを切り替えさせない（編集中のアバターが消えるため）
            if (remote != null)
            {
                remote.AvatarSwitchBlocker = () => _perfectSyncEditor.IsOpen ? "the perfect sync editor is open" : null;
            }
            _performanceSection = new PerformanceSection(settings, rendering);
            _presetSection = new PresetSection(settings, ReapplySettings);
            _settingsSection = new SettingsSection(settings, rendering, ResetAllSettings, _presetSection);
            _infoSection = new InfoSection(settings, updates, new CreditsSection(),
                new LogSection(trackerProcess, tracker, settings));

            // リセットはパネル下部のボタンとショートカットキー（パネルを隠していても動く）で共用する
            var resetActions = new ResetActions(session, orbit, screenshot, settings);
            _resetBar = new ResetBar(resetActions, settings);
            gameObject.AddComponent<ResetHotkeyListener>().Initialize(resetActions, session, settings);

            // 一定時間ごとの自動リセット（パネルを隠していても動く）
            var autoReset = gameObject.AddComponent<AutoResetScheduler>();
            autoReset.Initialize(session, settings);
            _autoResetSection = new AutoResetSection(autoReset, settings);
            _performanceBar = new PerformanceBar(settings, tracker, trackerProcess);
            _trackerProcess = trackerProcess;
            // 配信を選ぶまでカメラを開かない
            if (_trackerProcess != null)
            {
                _trackerProcess.Paused = true;
            }

            _mascot = new MascotMode(this, session, orbit.GetComponent<Camera>(), settings, microphone);

            // ウィンドウへのドロップで読み込む
            _fileDrop.FilesDropped += OnFilesDropped;
        }

        private void OnFilesDropped(IReadOnlyList<string> paths)
        {
            // 設定プリセットは、隠していても表示して Settings タブの確認欄を出す（ドロップしただけでは反映しない）
            string preset = FindPreset(paths);
            if (preset != null)
            {
                _mascot.Close();
                _launch = LaunchMode.Broadcast;
                _presetSection.Open(preset);
                SetVisible(true);
                _tab = Tab.Settings;
                return;
            }

            // 非対応ファイルのときは隠していても表示し、エラーが見える Avatar タブへ
            if (!_avatarSection.LoadDropped(paths))
            {
                _mascot.Close();
                _launch = LaunchMode.Broadcast;
                SetVisible(true);
                _tab = Tab.Avatar;
            }
        }

        private void OpenLink(StartLink link)
        {
            // Start タブの案内・クイック設定から各タブへ
            switch (link)
            {
                case StartLink.Face:
                    _tab = Tab.Face;
                    break;
                case StartLink.Tracking:
                    _tab = Tab.Tracking;
                    break;
                case StartLink.Graphics:
                    _tab = Tab.Graphics;
                    break;
                case StartLink.Performance:
                    _tab = Tab.Performance;
                    break;
                case StartLink.Output:
                    _tab = Tab.Output;
                    break;
                default:
                    _tab = Tab.Avatar;
                    break;
            }

            // タブを開いたら横スクロールを戻す（サイドバー切替と同じ）
            _scroll[(int)_tab].x = 0f;
        }

        private static string FindPreset(IReadOnlyList<string> paths)
        {
            // 最初に見つかった設定プリセット（無ければ null）
            if (paths == null)
            {
                return null;
            }

            foreach (string path in paths)
            {
                if (PresetSection.IsPresetFile(path))
                {
                    return path;
                }
            }

            return null;
        }

        private void ResetAllSettings()
        {
            // 設定値を既定に戻す（ウィンドウサイズ・最後のアバター・アバターごとの記録は保持）
            _settings.ResetToDefaults();
            ReapplySettings();
        }

        private void ReapplySettings()
        {
            // 設定変更時にしか反映しない機能へ反映し直す（他は毎フレーム設定を読む）
            _rendering.ApplyAll();
            _virtualCamera.Enabled = _settings.virtualCameraEnabled;
            _spout.Enabled = _settings.spoutEnabled;
            _trackingSection.SyncFromSettings();
            _remoteSection.SyncFromSettings();

            // GPU の優先設定は Windows 側にも書く（反映は次回起動から）
            GpuSelection.ApplyPreference(_settings);

            // 表示中アバターの向き・待機ポーズ（GetComponent は Unity の null 判定が必要なため明示的に比較）
            PoseController pose = _session.Current != null
                ? _session.Current.Instance.GetComponent<PoseController>()
                : null;
            if (pose != null)
            {
                pose.Reapply();
            }
        }

        private void Update()
        {
            // 未初期化なら何もしない
            if (_orbit == null || _settings == null)
            {
                return;
            }

            // 詳細ログの ON/OFF を反映（設定のリセットにも追従するよう毎フレーム）
            LogBuffer.DetailEnabled = _settings.detailedLogging;

            // 表情のキャリブレーションの記録を進める（タブを切り替えても続ける）
            _trackingSection.Update();

            // モード選択中はパネルを隠さない。配信中だけ、隠すと背景も透過する
            if (Input.GetKeyDown(ToggleKey) && !HotkeyPoller.Suspended && _launch != LaunchMode.Choose)
            {
                SetVisible(!_visible);
            }

            _mascot.Tick();
            ApplyChooserCamera();
            ApplyTrackingGate();

            // パネル上にマウスがある間はカメラ操作を止める（Input は左下原点なので上下を反転し、UI の倍率で割る）
            float scale = _settings.uiScale;
            Vector3 mouse = Input.mousePosition;
            var position = new Vector2(mouse.x / scale, (Screen.height - mouse.y) / scale);

            // カメラの固定・体に入り込まない設定を反映（設定のリセットにも追従するよう毎フレーム）
            _orbit.Locked = _settings.cameraLocked;
            _orbit.ZoomInsteadOfEntering = _settings.cameraZoomInsteadOfEntering;

            // モード選択とマスコット中は、この画面全体を操作としてカメラを止める
            if (_launch != LaunchMode.Broadcast)
            {
                _orbit.InputBlocked = _visible;
                return;
            }
            if (_perfectSyncEditor.IsOpen)
            {
                bool overPanel = _visible && _perfectSyncEditor.IsOverPanel(position);
                _orbit.InputBlocked = overPanel;
                _orbit.Locked = false;
                _perfectSyncEditor.Update(_visible && !overPanel);
                return;
            }

            _orbit.InputBlocked = _visible && _windowRect.Contains(position);
        }

        /// <summary>
        /// 配信中だけトラッキングする。モード選択とマスコットではカメラを止め、アバターへ姿勢を書かない。
        /// 離席中は顔・手・体とカメラ目線を止める。
        /// 「顔が見つからないときはトラッキングしない」で顔が映っていない間は、カメラは動かしたまま
        /// トラッキング入力だけ止める（カメラ目線は続ける。顔が戻ったことを知るため受信も続ける）。
        /// </summary>
        private void ApplyTrackingGate()
        {
            // 同梱トラッカー（カメラ）は配信以外のときだけ止める
            bool broadcasting = _launch == LaunchMode.Broadcast;
            if (_trackerProcess != null)
            {
                _trackerProcess.Paused = !broadcasting;
            }

            bool suspend = !broadcasting || _settings.trackingAway;
            bool hold = _settings.ShouldSuspendTracking(broadcasting, HasFreshFace()) && !suspend;
            bool writeSuspend = suspend || _suspendHeld;
            bool writeHold = hold || _holdInputHeld;
            if (!writeSuspend && !writeHold)
            {
                return;
            }

            // 編集画面が自分で止めている間は、離席を解除しても Suspended を外さない
            if (!suspend && _perfectSyncEditor != null && _perfectSyncEditor.IsOpen)
            {
                writeSuspend = false;
                _suspendHeld = false;
            }

            GameObject instance = _session.Current != null ? _session.Current.Instance : null;
            if (instance != null)
            {
                FaceTrackingDriver face = instance.GetComponent<FaceTrackingDriver>();
                HandTrackingDriver hands = instance.GetComponent<HandTrackingDriver>();
                FullBodyTrackingDriver body = instance.GetComponent<FullBodyTrackingDriver>();
                if (writeSuspend)
                {
                    SetSuspended(face, hands, body, suspend);
                }

                if (writeHold)
                {
                    SetHoldInput(face, hands, body, hold);
                }
            }

            if (writeSuspend)
            {
                _suspendHeld = suspend;
            }

            if (writeHold)
            {
                _holdInputHeld = hold;
            }
        }

        private static void SetSuspended(
            FaceTrackingDriver face, HandTrackingDriver hands, FullBodyTrackingDriver body, bool suspend)
        {
            if (face != null)
            {
                face.Suspended = suspend;
            }

            if (hands != null)
            {
                hands.Suspended = suspend;
            }

            if (body != null)
            {
                body.Suspended = suspend;
            }
        }

        private static void SetHoldInput(
            FaceTrackingDriver face, HandTrackingDriver hands, FullBodyTrackingDriver body, bool hold)
        {
            if (face != null)
            {
                face.HoldInput = hold;
            }

            if (hands != null)
            {
                hands.HoldInput = hold;
            }

            if (body != null)
            {
                body.HoldInput = hold;
            }
        }

        private bool HasFreshFace()
        {
            // 受信中の顔フレームがあるときだけ映っているとみなす（途絶の判定は受信側）
            return _tracker != null && _tracker.TryGetFrame(out _);
        }

        private void ApplyChooserCamera()
        {
            Camera camera = _orbit.GetComponent<Camera>();
            if (camera == null)
            {
                return;
            }

            if (_launch != LaunchMode.Choose)
            {
                ShowAvatarOnMainCamera();
                return;
            }

            if (!_mainMaskHidden)
            {
                _mainMask = camera.cullingMask;
                _mainMaskHidden = true;
            }

            camera.cullingMask = 0;
        }

        private void ShowAvatarOnMainCamera()
        {
            if (!_mainMaskHidden)
            {
                return;
            }

            Camera camera = _orbit.GetComponent<Camera>();
            if (camera != null)
            {
                camera.cullingMask = _mainMask;
            }

            _mainMaskHidden = false;
        }

        private void SetVisible(bool visible)
        {
            // 隠している間は、配信モードのときだけ設定に関係なく背景を透過させる
            _visible = visible;
            _rendering.ForceTransparent = _launch == LaunchMode.Broadcast && !visible;
        }

        private void OnGUI()
        {
            // 非表示または未初期化なら描画しない
            if (!_visible || _session == null)
            {
                return;
            }

            // 言語を先に決める（テーマのフォントの優先順に使う）
            Loc.Apply(_settings.uiLanguage);

            // スキンは OnGUI 内でしか作れないため初回にここで作る。言語・ダークモードが変わったら Layout の時だけ作り直す
            // （Layout と Repaint の間でフォントを変えると配置が食い違うため）
            if (_theme == null)
            {
                _theme = UiTheme.Create(Loc.Current, _settings.darkMode);
            }
            else if ((_theme.Language != Loc.Current || _theme.Dark != _settings.darkMode)
                && Event.current.type == EventType.Layout)
            {
                _theme.Destroy();
                _theme = UiTheme.Create(Loc.Current, _settings.darkMode);
            }

            // スキン・倍率を適用（終わったら元に戻す）
            GUISkin previousSkin = GUI.skin;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousBackground = GUI.backgroundColor;
            float scale = _settings.uiScale;
            GUI.skin = _theme.Skin;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            // 背景（ウィンドウ・カード・ボタン）だけを設定の不透明度で透かす（文字は GUI.color なので薄くならない）
            GUI.backgroundColor = new Color(previousBackground.r, previousBackground.g, previousBackground.b,
                previousBackground.a * _settings.uiOpacity);

            // 起動時の画面（配信を選ぶまではこちら）。マスコット中も残し、下に設定を出す
            if (_launch != LaunchMode.Broadcast)
            {
                int pick = ModeSelectScreen.Draw(
                    _settings, _theme, Screen.width / scale, Screen.height / scale, _launch == LaunchMode.Mascot, _mascot);
                if (pick == 1)
                {
                    ShowAvatarOnMainCamera();
                    _avatarSection.LoadStartupAvatar();
                    _mascot.Close();
                    _launch = LaunchMode.Broadcast;
                    SetVisible(true);
                }
                else if (pick == 2)
                {
                    ShowAvatarOnMainCamera();
                    _avatarSection.LoadStartupAvatar();
                    _launch = LaunchMode.Mascot;
                    _mascot.Open();
                }
            }
            else if (_perfectSyncEditor.IsOpen)
            {
                _perfectSyncEditor.OnGUI(scale);
            }
            else
            {
                FitToScreen(Screen.width / scale, Screen.height / scale);
                _windowRect = GUILayout.Window(
                    WindowId, _windowRect, DrawWindow, GUIContent.none, _theme.Skin.window,
                    GUILayout.Width(_windowRect.width), GUILayout.Height(_windowRect.height));

                // 表情・傾きのキャリブレーション中は、案内を画面の上中央に大きく出す
                ExpressionCalibrationControls.DrawOverlay(_trackingSection.CalibrationRecorder, Screen.width / scale);
                TrackingSection.DrawTiltOverlay(_trackingSection.TiltCalibration, Screen.width / scale);
            }

            GUI.backgroundColor = previousBackground;
            GUI.matrix = previousMatrix;
            GUI.skin = previousSkin;
        }

        private void OnDestroy()
        {
            // ドロップ通知の購読を解除
            if (_fileDrop != null)
            {
                _fileDrop.FilesDropped -= OnFilesDropped;
            }

            // 生成したテクスチャ・フォントを破棄し、Discord の情報の取得を止める
            _theme?.Destroy();
            _theme = null;
            _mascot?.Dispose();
            _discordSection.Dispose();
        }

        private void FitToScreen(float screenWidth, float screenHeight)
        {
            // 高さは画面に収まる範囲（上限あり）、位置は画面内に収める
            _windowRect.width = WindowWidth;
            _windowRect.height = Mathf.Clamp(screenHeight - ScreenMargin * 2f, MinWindowHeight, MaxWindowHeight);
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, screenWidth - _windowRect.width));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, screenHeight - _windowRect.height));
        }

        private void DrawWindow(int id)
        {
            DrawHeader();

            GUILayout.BeginHorizontal();
            DrawSidebar();

            // タブ列の残り幅いっぱい。縦スクロールバーは常に右端に出し、幅の計算がタブでぶれないようにする
            // （横スクロールバーは出さない。長い説明は GUI 側で折り返す）
            Vector2 scroll = _scroll[(int)_tab];
            scroll.x = 0f;
            scroll = GUILayout.BeginScrollView(
                scroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            scroll.x = 0f;
            _scroll[(int)_tab] = scroll;

            GUILayout.BeginVertical(GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            DrawContent();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            GUILayout.EndHorizontal();

            // どのタブでも押せるリセットボタン
            GUILayout.Space(FooterSpacing);
            _resetBar.Draw(_theme.Sidebar);

            // どのタブでも見える動作状況（設定で OFF にできる）
            _performanceBar.Draw();

            // 見出し行でドラッグ移動
            GUI.DragWindow(new Rect(0f, 0f, 10000f, HeaderHeight));
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal(GUILayout.Height(HeaderHeight - 12f));
            GUILayout.Label("VRCast", _theme.Title, GUILayout.ExpandWidth(false));
            if (GUILayout.Button(Loc.T("Modes", "モード", "모드", "模式", "模式"), _theme.HeaderButton, GUILayout.Width(72f)))
            {
                _launch = LaunchMode.Choose;
            }

            // バージョン番号（見出しより小さい文字を下端にそろえる）
            GUILayout.BeginVertical(GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.Label($"v{Application.version}", _theme.Hint, GUILayout.ExpandWidth(false));
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.T("[Tab] Hide all & transparent", "[Tab] 全部隠して透過", "[Tab] 모두 숨기고 투명",
                "[Tab] 全部隐藏并透明", "[Tab] 全部隱藏並透明"), _theme.KeyHint);

            // ヘルプページ（Web）
            if (GUILayout.Button("?", _theme.HeaderButton, GUILayout.Width(HelpButtonWidth)))
            {
                HelpPage.Open();
            }

            GUILayout.EndHorizontal();
            DrawLanguageBar();
            GUILayout.Space(4f);
            DrawUpdateNotice();
        }

        private void DrawUpdateNotice()
        {
            // 前回の自動更新の結果（更新後・失敗後の最初の起動で出す）
            DrawPreviousUpdate();

            // 新しいバージョンがあれば見出しの下に出す
            if (!_updates.IsUpdateAvailable)
            {
                return;
            }

            GuiControls.BeginCard(Loc.T("Update available", "新しいバージョンがあります", "새 버전이 있습니다",
                "有新版本", "有新版本"));
            GuiControls.Hint(Loc.T("Current", "現在", "현재", "当前", "目前") + $": {Application.version} → "
                + Loc.T("Latest", "最新", "최신", "最新", "最新") + $": {_updates.LatestVersion}");
            UpdateDownloadButtons.Draw(_updates);

            // 何が変わったかを GitHub の更新履歴で確かめられる
            if (GUILayout.Button(Loc.T("View changelog (GitHub)", "更新履歴を表示する（GitHub）",
                    "업데이트 내역 보기 (GitHub)", "查看更新日志（GitHub）", "查看更新紀錄（GitHub）"),
                    GuiControls.Shrinkable))
            {
                _updates.OpenChangelog();
            }

            GuiControls.EndCard();
        }

        private void DrawPreviousUpdate()
        {
            UpdateInstaller installer = _updates.Installer;
            if (installer.Previous == UpdateInstaller.PreviousResult.None)
            {
                return;
            }

            bool succeeded = installer.Previous == UpdateInstaller.PreviousResult.Succeeded;
            GuiControls.BeginCard(succeeded
                ? Loc.T("Updated", "更新しました", "업데이트했습니다", "已更新", "已更新") + $": v{Application.version}"
                : Loc.T("The update failed", "更新に失敗しました", "업데이트에 실패했습니다", "更新失败", "更新失敗"));
            if (succeeded)
            {
                // 書き出しツールはアバターの Unity プロジェクトにあるため自動では更新できない
                GuiControls.Hint(Loc.T(
                    "The exporter (unitypackage) in your avatar project is not updated automatically. "
                    + "When you have time, import the latest one from the GitHub / BOOTH zip",
                    "アバターのプロジェクトに入れた書き出しツール（unitypackage）は自動では更新されません。"
                    + "お手すきの際に、GitHub / BOOTH の zip から最新版をインポートしてください",
                    "아바타 프로젝트에 넣은 내보내기 도구(unitypackage)는 자동으로 업데이트되지 않습니다. "
                    + "시간이 있을 때 GitHub / BOOTH의 zip에서 최신 버전을 가져오세요",
                    "放入头像工程中的导出工具（unitypackage）不会自动更新。方便时请从 GitHub / BOOTH 的 zip 导入最新版",
                    "放入頭像專案中的匯出工具（unitypackage）不會自動更新。方便時請從 GitHub / BOOTH 的 zip 匯入最新版"));
                if (GUILayout.Button(Loc.T("View changelog (GitHub)", "更新履歴を表示する（GitHub）",
                        "업데이트 내역 보기 (GitHub)", "查看更新日志（GitHub）", "查看更新紀錄（GitHub）"),
                        GuiControls.Shrinkable))
                {
                    _updates.OpenChangelog();
                }
            }
            else
            {
                // 元のバージョンのまま起動している。理由と、手動で入れる方法を案内する
                GuiControls.Warning(Loc.T("VRCast was kept at the previous version",
                    "VRCast は元のバージョンのままです", "VRCast는 이전 버전 그대로입니다",
                    "VRCast 保持为原来的版本", "VRCast 保持為原來的版本") + $" ({installer.PreviousDetail})");
                GuiControls.Hint(Loc.T(
                    "Close apps that use the camera (OBS, Discord, etc.) and try again, or download the zip and replace the folder",
                    "カメラを使うアプリ（OBS・Discord など）を閉じてもう一度試すか、zip をダウンロードしてフォルダを入れ替えてください",
                    "카메라를 사용하는 앱(OBS·Discord 등)을 닫고 다시 시도하거나, zip을 다운로드해 폴더를 교체하세요",
                    "请关闭使用摄像头的应用（OBS、Discord 等）后重试，或下载 zip 替换文件夹",
                    "請關閉使用攝影機的應用程式（OBS、Discord 等）後再試一次，或下載 zip 替換資料夾"));
            }

            if (GUILayout.Button(Loc.T("Close", "閉じる", "닫기", "关闭", "關閉"), GuiControls.Shrinkable))
            {
                installer.DismissPrevious();
            }

            GuiControls.EndCard();
        }

        private void DrawLanguageBar()
        {
            // どのタブからでも切り替えられる表示言語（横並び。選択中はアクセント色、押すとその言語にする）
            // 幅が足りないときは縮めて、右の「繁體中文」などが見切れないようにする
            string[] labels = Loc.LanguageLabels(Loc.T("Auto", "自動", "자동", "自动", "自動"));
            GUILayout.BeginHorizontal();
            for (int i = 0; i < labels.Length; i++)
            {
                bool selected = (int)_settings.uiLanguage == i;
                if (GUILayout.Toggle(selected, labels[i], _theme.OverflowButton, GuiControls.Shrinkable)
                    && !selected)
                {
                    _settings.uiLanguage = (UiLanguage)i;
                }
            }

            GUILayout.EndHorizontal();
        }

        private float SidebarWidthFor()
        {
            // 一番長いタブ名が収まる幅（縦スクロールバーが出ても右が見切れないよう、その分も足す。最低は SidebarWidth）
            float widest = 0f;
            for (int i = 0; i < TabCount; i++)
            {
                var tab = (Tab)i;
                GUIStyle style = tab == Tab.PerfectSync ? _theme.FeatureTab : _theme.Tab;
                widest = Mathf.Max(widest, style.CalcSize(new GUIContent(TabLabel(tab))).x + style.margin.horizontal);
            }

            GUIStyle scrollbar = GUI.skin.verticalScrollbar;
            float width = widest + _theme.Sidebar.padding.horizontal + scrollbar.fixedWidth + scrollbar.margin.horizontal;
            return Mathf.Max(SidebarWidth, Mathf.Ceil(width));
        }

        private void DrawSidebar()
        {
            GUILayout.BeginVertical(_theme.Sidebar, GUILayout.Width(SidebarWidthFor()), GUILayout.ExpandHeight(true));

            // 収まらないときだけ縦スクロールバーを出す（横には出さない）
            _sidebarScroll = GUILayout.BeginScrollView(
                _sidebarScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);

            // 押したタブを選択（選択中はアクセント色。まとまりの先頭の前は少し空ける）
            for (int i = 0; i < TabCount; i++)
            {
                var tab = (Tab)i;
                if (Array.IndexOf(GroupStarts, tab) >= 0)
                {
                    GUILayout.Space(TabGroupSpacing);
                }

                // パーフェクトシンク化だけは別の色で目立たせる
                GUIStyle style = tab == Tab.PerfectSync ? _theme.FeatureTab : _theme.Tab;
                if (GUILayout.Toggle(_tab == tab, TabLabel(tab), style) && _tab != tab)
                {
                    _tab = tab;
                    // 前のタブの横幅計算の名残で右が見切れないよう、移動先の横スクロールを戻す
                    _scroll[(int)_tab].x = 0f;
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawContent()
        {
            // 選択中のタブのセクションだけを描画
            switch (_tab)
            {
                case Tab.Start:
                    _startSection.Draw();
                    break;
                case Tab.Avatar:
                    _avatarSection.Draw();
                    break;
                case Tab.Objects:
                    _objectSection.Draw();
                    _particleSection.Draw();
                    break;
                case Tab.ShapeKeys:
                    _shapeKeySection.Draw();
                    break;
                case Tab.Face:
                    _faceSection.Draw();
                    _expressionSection.Draw();
                    break;
                case Tab.Body:
                    _animationSection.Draw();
                    break;
                case Tab.Tracking:
                    _trackingSection.Draw();
                    break;
                case Tab.Display:
                    _displaySection.Draw();
                    break;
                case Tab.Graphics:
                    _graphicsSection.Draw();
                    break;
                case Tab.Output:
                    _outputSection.Draw();
                    break;
                case Tab.Control:
                    _shortcutSection.Draw();
                    _autoResetSection.Draw();
                    _remoteSection.Draw();
                    break;
                case Tab.Performance:
                    _performanceSection.Draw();
                    break;
                case Tab.Info:
                    _infoSection.Draw();
                    break;
                case Tab.Discord:
                    // 公式 Discord の下に、次に対応してほしい機能などのアンケート
                    _discordSection.Draw();
                    _surveyBar.Draw();
                    break;
                case Tab.PerfectSync:
                    _perfectSyncSection.Draw();
                    break;
                default:
                    _settingsSection.Draw();
                    break;
            }
        }

        private static string TabLabel(Tab tab)
        {
            // タブ名（表示言語に合わせる）
            switch (tab)
            {
                case Tab.Start:
                    return Loc.T("Start", "はじめに", "시작하기", "开始", "開始");
                case Tab.Avatar:
                    return Loc.T("Avatar", "アバター", "아바타", "虚拟形象", "虛擬形象");
                case Tab.Objects:
                    return Loc.T("Objects", "オブジェクト", "오브젝트", "对象", "物件");
                case Tab.ShapeKeys:
                    return Loc.T("Shape key setup", "シェイプキー設定", "셰이프 키 설정", "形态键设置", "形態鍵設定");
                case Tab.Face:
                    return Loc.T("Face & expressions", "顔・表情", "얼굴·표정", "面部·表情", "臉部·表情");
                case Tab.Body:
                    return Loc.T("Body & pose", "体・ポーズ", "몸·포즈", "身体·姿势", "身體·姿勢");
                case Tab.Tracking:
                    return Loc.T("Tracking", "トラッキング", "트래킹", "追踪", "追蹤");
                case Tab.Display:
                    return Loc.T("Display", "表示", "표시", "显示", "顯示");
                case Tab.Graphics:
                    return Loc.T("Graphics", "グラフィック", "그래픽", "图形", "圖形");
                case Tab.Output:
                    return Loc.T("Output", "出力", "출력", "输出", "輸出");
                case Tab.Control:
                    return Loc.T("Hotkeys / OSC", "操作・連携", "단축키·연동", "快捷键·联动", "快捷鍵·連動");
                case Tab.Performance:
                    return Loc.T("Performance", "パフォーマンス", "성능", "性能", "效能");
                case Tab.Info:
                    return Loc.T("Info", "情報", "정보", "信息", "資訊");
                case Tab.Discord:
                    return Loc.T("Official Discord", "公式 Discord", "공식 Discord", "官方 Discord", "官方 Discord");
                case Tab.PerfectSync:
                    return Loc.T("Perfect sync setup", "パーフェクトシンク化", "퍼펙트 싱크화", "完美同步化", "完美同步化");
                default:
                    return Loc.T("Settings", "設定", "설정", "设置", "設定");
            }
        }
    }
}
