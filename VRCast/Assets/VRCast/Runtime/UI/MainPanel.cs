using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Audio;
using VRCast.Avatars;
using VRCast.Cameras;
using VRCast.Core;
using VRCast.Output;
using VRCast.Platform;
using VRCast.Rendering;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// IMGUI の操作パネル。左のタブ（Avatar / Pose / Face / Tracking / Display / Output / Settings）で
    /// 表示するセクションを切り替え、内容は縦スクロールする。画面に収まる高さに制限し、Tab キーで表示切替。
    /// 表示言語（日本語 / 英語）と UI の大きさは設定に従う。
    /// </summary>
    public class MainPanel : MonoBehaviour
    {
        // パネルの表示切替キー（配信時に UI を隠す）
        private const KeyCode ToggleKey = KeyCode.Tab;

        // IMGUI ウィンドウ ID・大きさ・画面端からの余白
        private const int WindowId = 0x5643;
        private const float WindowWidth = 560f;
        private const float MaxWindowHeight = 760f;
        private const float MinWindowHeight = 240f;
        private const float ScreenMargin = 10f;

        // 見出し行（ドラッグで移動できる範囲）とタブ列の幅
        private const float HeaderHeight = 36f;
        private const float SidebarWidth = 132f;

        // タブの並び
        private enum Tab
        {
            Avatar,
            Pose,
            Face,
            Tracking,
            Display,
            Output,
            Settings,
        }

        private static readonly int TabCount = Enum.GetValues(typeof(Tab)).Length;

        private AvatarSession _session;
        private FileDropReceiver _fileDrop;
        private OrbitCameraController _orbit;
        private AppSettings _settings;
        private AvatarSection _avatarSection;
        private AnimationSection _animationSection;
        private FaceSection _faceSection;
        private TrackingSection _trackingSection;
        private DisplaySection _displaySection;
        private OutputSection _outputSection;
        private SettingsSection _settingsSection;

        // テーマ（最初の OnGUI で作成）
        private UiTheme _theme;

        // 表示状態・選択中のタブ・タブごとのスクロール位置
        private bool _visible = true;
        private Tab _tab = Tab.Avatar;
        private readonly Vector2[] _scroll = new Vector2[TabCount];
        private Rect _windowRect = new Rect(ScreenMargin, ScreenMargin, WindowWidth, MaxWindowHeight);

        public void Initialize(
            AvatarSession session, OrbitCameraController orbit, RenderingController rendering,
            MicrophoneInput microphone, IFaceTrackingProvider tracker, TrackerProcess trackerProcess,
            TrackingSkeletonView skeleton, VirtualCameraOutput virtualCamera, FileDropReceiver fileDrop,
            AppSettings settings, string initialPath)
        {
            // 依存の受け取りと各タブの作成
            _session = session;
            _fileDrop = fileDrop;
            _orbit = orbit;
            _settings = settings;
            _avatarSection = new AvatarSection(session, initialPath);
            _animationSection = new AnimationSection(session);
            _faceSection = new FaceSection(session, microphone, settings);
            _trackingSection = new TrackingSection(session, tracker, trackerProcess, skeleton, settings);
            _displaySection = new DisplaySection(orbit, rendering);
            _outputSection = new OutputSection(virtualCamera);
            _settingsSection = new SettingsSection(settings);

            // ウィンドウへのドロップで読み込む
            _fileDrop.FilesDropped += OnFilesDropped;
        }

        private void OnFilesDropped(IReadOnlyList<string> paths)
        {
            // 結果が見えるよう Avatar タブへ切り替え、非対応ファイルのときは隠していても表示する
            _tab = Tab.Avatar;
            if (!_avatarSection.LoadDropped(paths))
            {
                _visible = true;
            }
        }

        private void Update()
        {
            // 表示切替
            if (Input.GetKeyDown(ToggleKey))
            {
                _visible = !_visible;
            }

            // 未初期化なら何もしない
            if (_orbit == null || _settings == null)
            {
                return;
            }

            // パネル上にマウスがある間はカメラ操作を止める（Input は左下原点なので上下を反転し、UI の倍率で割る）
            float scale = _settings.uiScale;
            Vector3 mouse = Input.mousePosition;
            var position = new Vector2(mouse.x / scale, (Screen.height - mouse.y) / scale);
            _orbit.InputBlocked = _visible && _windowRect.Contains(position);
        }

        private void OnGUI()
        {
            // 非表示または未初期化なら描画しない
            if (!_visible || _session == null)
            {
                return;
            }

            // スキンは OnGUI 内でしか作れないため初回にここで作る
            if (_theme == null)
            {
                _theme = UiTheme.Create();
            }

            // 言語・スキン・倍率を適用（終わったら元に戻す）
            Loc.Apply(_settings.uiLanguage);
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
            DrawContent();
            GUILayout.EndScrollView();
            GUILayout.EndHorizontal();

            // 見出し行でドラッグ移動
            GUI.DragWindow(new Rect(0f, 0f, 10000f, HeaderHeight));
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal(GUILayout.Height(HeaderHeight - 12f));
            GUILayout.Label("VRCast", _theme.Title);
            GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.T("Tab: hide", "Tab: 隠す"), _theme.Hint);
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
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
                case Tab.Avatar:
                    _avatarSection.Draw();
                    break;
                case Tab.Pose:
                    _animationSection.Draw();
                    break;
                case Tab.Face:
                    _faceSection.Draw();
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
                case Tab.Avatar:
                    return Loc.T("Avatar", "アバター");
                case Tab.Pose:
                    return Loc.T("Pose", "ポーズ・表情");
                case Tab.Face:
                    return Loc.T("Face", "顔");
                case Tab.Tracking:
                    return Loc.T("Tracking", "トラッキング");
                case Tab.Display:
                    return Loc.T("Display", "表示");
                case Tab.Output:
                    return Loc.T("Output", "出力");
                default:
                    return Loc.T("Settings", "設定");
            }
        }
    }
}
