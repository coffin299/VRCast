using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Animations;
using VRCast.Audio;
using VRCast.Avatars;
using VRCast.Cameras;
using VRCast.Core;
using VRCast.Output;
using VRCast.Platform;
using VRCast.Remote;
using VRCast.Rendering;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// IMGUI の操作パネル。左のタブ（Start / Avatar / Pose / Face / Shape keys / Tracking / Display / Output / OSC / HTTP / Settings / Log / Credits）で
    /// 表示するセクションを切り替え、内容は縦スクロールする。下部にはリセットボタン・アンケート欄・動作状況を常に表示する。
    /// 画面に収まる高さに制限し、Tab キーで表示切替（隠している間は背景も透過）。
    /// 表示言語（見出しの下のボタンでいつでも切替）と UI の大きさは設定に従う。見出しの「?」でヘルプページを開く。
    /// </summary>
    public class MainPanel : MonoBehaviour
    {
        // パネルの表示切替キー（配信時に UI を隠す）
        private const KeyCode ToggleKey = KeyCode.Tab;

        // IMGUI ウィンドウ ID・大きさ・画面端からの余白
        private const int WindowId = 0x5643;
        private const float WindowWidth = 600f;
        private const float MaxWindowHeight = 760f;
        private const float MinWindowHeight = 240f;
        private const float ScreenMargin = 10f;

        // 見出し行（ドラッグで移動できる範囲）とタブ列の幅
        private const float HeaderHeight = 36f;
        private const float SidebarWidth = 132f;

        // 見出しのヘルプボタンの幅
        private const float HelpButtonWidth = 32f;

        // タブの内容と下部のリセットボタンの間隔
        private const float FooterSpacing = 8f;

        // タブの並び
        private enum Tab
        {
            Start,
            Avatar,
            Pose,
            Face,
            ShapeKeys,
            Tracking,
            Display,
            Output,
            Remote,
            Settings,
            Log,
            Credits,
        }

        private static readonly int TabCount = Enum.GetValues(typeof(Tab)).Length;

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
        private FaceSection _faceSection;
        private ShapeKeySection _shapeKeySection;
        private TrackingSection _trackingSection;
        private DisplaySection _displaySection;
        private OutputSection _outputSection;
        private RemoteSection _remoteSection;
        private SettingsSection _settingsSection;
        private LogSection _logSection;
        private CreditsSection _creditsSection;
        private ResetBar _resetBar;
        private readonly SurveyBar _surveyBar = new SurveyBar();
        private PerformanceBar _performanceBar;

        // テーマ（最初の OnGUI で作成）
        private UiTheme _theme;

        // 表示状態・選択中のタブ・タブごとのスクロール位置
        private bool _visible = true;
        private Tab _tab = Tab.Start;
        private readonly Vector2[] _scroll = new Vector2[TabCount];

        // タブの内容の幅（前回の描画で測った見えている幅。0 = 未計測）
        private float _contentWidth;
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
            _rendering = rendering;
            _virtualCamera = virtualCamera;
            _spout = spout;
            _avatarSection = new AvatarSection(session, settings, initialPath);
            _startSection = new StartSection(session, _avatarSection, rendering, virtualCamera, OpenLink);
            _animationSection = new AnimationSection(session, settings);
            _faceSection = new FaceSection(session, microphone, settings);
            _shapeKeySection = new ShapeKeySection(session, settings);
            _trackingSection = new TrackingSection(session, tracker, trackerProcess, skeleton, settings);
            _displaySection = new DisplaySection(orbit, rendering, settings);
            _outputSection = new OutputSection(virtualCamera, spout, rendering);
            _remoteSection = new RemoteSection(session, remote, settings);
            _settingsSection = new SettingsSection(session, settings, rendering, updates, ResetAllSettings);
            _logSection = new LogSection(trackerProcess, tracker, settings);
            _creditsSection = new CreditsSection();

            // リセットはパネル下部のボタンとショートカットキー（パネルを隠していても動く）で共用する
            var resetActions = new ResetActions(session, orbit);
            _resetBar = new ResetBar(resetActions, settings);
            gameObject.AddComponent<ResetHotkeyListener>().Initialize(resetActions, session, settings);
            _performanceBar = new PerformanceBar(settings, tracker, trackerProcess);

            // ウィンドウへのドロップで読み込む
            _fileDrop.FilesDropped += OnFilesDropped;
        }

        private void OnFilesDropped(IReadOnlyList<string> paths)
        {
            // 非対応ファイルのときは隠していても表示し、エラーが見える Avatar タブへ
            if (!_avatarSection.LoadDropped(paths))
            {
                SetVisible(true);
                _tab = Tab.Avatar;
            }
        }

        private void OpenLink(StartLink link)
        {
            // Start タブの案内から各タブへ
            switch (link)
            {
                case StartLink.Face:
                    _tab = Tab.Face;
                    break;
                case StartLink.Tracking:
                    _tab = Tab.Tracking;
                    break;
                case StartLink.Output:
                    _tab = Tab.Output;
                    break;
                default:
                    _tab = Tab.Avatar;
                    break;
            }
        }

        private void ResetAllSettings()
        {
            // 設定値を既定に戻す（ウィンドウサイズ・最後のアバター・アバターごとの記録は保持）
            _settings.ResetToDefaults();

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

            // 表示切替（隠すと背景も透過）。表情のキーの割り当て中は、押したキーでパネルを消さない
            if (Input.GetKeyDown(ToggleKey) && !HotkeyPoller.Suspended)
            {
                SetVisible(!_visible);
            }

            // パネル上にマウスがある間はカメラ操作を止める（Input は左下原点なので上下を反転し、UI の倍率で割る）
            float scale = _settings.uiScale;
            Vector3 mouse = Input.mousePosition;
            var position = new Vector2(mouse.x / scale, (Screen.height - mouse.y) / scale);
            _orbit.InputBlocked = _visible && _windowRect.Contains(position);

            // カメラの固定を反映（設定のリセットにも追従するよう毎フレーム）
            _orbit.Locked = _settings.cameraLocked;
        }

        private void SetVisible(bool visible)
        {
            // 隠している間は設定に関係なく背景を透過させ、表示に戻したら設定どおりに戻す
            _visible = visible;
            _rendering.ForceTransparent = !visible;
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
            float scale = _settings.uiScale;
            GUI.skin = _theme.Skin;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            FitToScreen(Screen.width / scale, Screen.height / scale);
            _windowRect = GUILayout.Window(
                WindowId, _windowRect, DrawWindow, GUIContent.none, _theme.Skin.window,
                GUILayout.Width(_windowRect.width), GUILayout.Height(_windowRect.height));

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

            // 生成したテクスチャ・フォントを破棄
            _theme?.Destroy();
            _theme = null;
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

            // 選択中のタブの内容（横スクロールバーは出さない）
            _scroll[(int)_tab] = GUILayout.BeginScrollView(
                _scroll[(int)_tab], false, false, GUIStyle.none, GUI.skin.verticalScrollbar);

            // 内容の幅を見えている幅に固定する（ボタンの多い行は右へはみ出さず、行の中で縮む）
            GUILayout.BeginVertical(_contentWidth > 0f ? GUILayout.Width(_contentWidth) : GUILayout.ExpandWidth(true));
            DrawContent();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            // 見えている幅（スクロールバーの分を除く）を次のフレームの配置に使う
            if (Event.current.type == EventType.Repaint)
            {
                GUIStyle scrollbar = GUI.skin.verticalScrollbar;
                float scrollbarWidth = scrollbar.fixedWidth + scrollbar.margin.horizontal;
                _contentWidth = Mathf.Max(0f, GUILayoutUtility.GetLastRect().width - scrollbarWidth);
            }

            GUILayout.EndHorizontal();

            // どのタブでも押せるリセットボタン
            GUILayout.Space(FooterSpacing);
            _resetBar.Draw(_theme.Sidebar);

            // どのタブでも見えるアンケート欄（次に対応してほしい機能などの募集）
            _surveyBar.Draw();

            // どのタブでも見える動作状況（設定で OFF にできる）
            _performanceBar.Draw();

            // 見出し行でドラッグ移動
            GUI.DragWindow(new Rect(0f, 0f, 10000f, HeaderHeight));
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal(GUILayout.Height(HeaderHeight - 12f));
            GUILayout.Label("VRCast", _theme.Title, GUILayout.ExpandWidth(false));

            // バージョン番号（見出しより小さい文字を下端にそろえる）
            GUILayout.BeginVertical(GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.Label($"v{Application.version}", _theme.Hint, GUILayout.ExpandWidth(false));
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.T("[Tab] Hide all & transparent", "[Tab] 全部隠して透過", "[Tab] 모두 숨기고 투명",
                "[Tab] 全部隐藏并透明", "[Tab] 全部隱藏並透明"), _theme.KeyHint);

            // ヘルプページ（Web）
            if (GUILayout.Button("?", GUILayout.Width(HelpButtonWidth)))
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
                    + "If the changelog mentions the exporter, import it again from the GitHub / BOOTH zip",
                    "アバターのプロジェクトに入れた書き出しツール（unitypackage）は自動では更新されません。"
                    + "更新履歴に書き出しツールの変更があれば、GitHub / BOOTH の zip から入れ直してください",
                    "아바타 프로젝트에 넣은 내보내기 도구(unitypackage)는 자동으로 업데이트되지 않습니다. "
                    + "업데이트 내역에 내보내기 도구 변경이 있으면 GitHub / BOOTH의 zip에서 다시 가져오세요",
                    "放入头像工程中的导出工具（unitypackage）不会自动更新。若更新日志中有导出工具的变更，请从 GitHub / BOOTH 的 zip 重新导入",
                    "放入頭像專案中的匯出工具（unitypackage）不會自動更新。若更新紀錄中有匯出工具的變更，請從 GitHub / BOOTH 的 zip 重新匯入"));
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
            string[] labels = Loc.LanguageLabels(Loc.T("Auto", "自動", "자동", "自动", "自動"));
            GUILayout.BeginHorizontal();
            for (int i = 0; i < labels.Length; i++)
            {
                bool selected = (int)_settings.uiLanguage == i;
                if (GUILayout.Toggle(selected, labels[i], _theme.OverflowButton) && !selected)
                {
                    _settings.uiLanguage = (UiLanguage)i;
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawSidebar()
        {
            GUILayout.BeginVertical(_theme.Sidebar, GUILayout.Width(SidebarWidth), GUILayout.ExpandHeight(true));

            // 押したタブを選択（選択中はアクセント色）
            for (int i = 0; i < TabCount; i++)
            {
                var tab = (Tab)i;
                if (GUILayout.Toggle(_tab == tab, TabLabel(tab), _theme.Tab) && _tab != tab)
                {
                    _tab = tab;
                }
            }

            GUILayout.FlexibleSpace();
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
                case Tab.Pose:
                    _animationSection.Draw();
                    break;
                case Tab.Face:
                    _faceSection.Draw();
                    break;
                case Tab.ShapeKeys:
                    _shapeKeySection.Draw();
                    break;
                case Tab.Tracking:
                    _trackingSection.Draw();
                    break;
                case Tab.Display:
                    _displaySection.Draw();
                    break;
                case Tab.Output:
                    _outputSection.Draw();
                    break;
                case Tab.Remote:
                    _remoteSection.Draw();
                    break;
                case Tab.Log:
                    _logSection.Draw();
                    break;
                case Tab.Credits:
                    _creditsSection.Draw();
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
                case Tab.Pose:
                    return Loc.T("Pose", "ポーズ・表情", "포즈·표정", "姿势·表情", "姿勢·表情");
                case Tab.Face:
                    return Loc.T("Face", "顔", "얼굴", "面部", "臉部");
                case Tab.ShapeKeys:
                    return Loc.T("Shape key setup", "シェイプキー設定", "셰이프 키 설정", "形态键设置", "形態鍵設定");
                case Tab.Tracking:
                    return Loc.T("Tracking", "トラッキング", "트래킹", "追踪", "追蹤");
                case Tab.Display:
                    return Loc.T("Display", "表示", "표시", "显示", "顯示");
                case Tab.Output:
                    return Loc.T("Output", "出力", "출력", "输出", "輸出");
                case Tab.Remote:
                    return "OSC / HTTP";
                case Tab.Log:
                    return Loc.T("Debug log", "デバッグログ", "디버그 로그", "调试日志", "偵錯日誌");
                case Tab.Credits:
                    return Loc.T("Credits", "クレジット", "크레딧", "致谢", "致謝");
                default:
                    return Loc.T("Settings", "設定", "설정", "设置", "設定");
            }
        }
    }
}
