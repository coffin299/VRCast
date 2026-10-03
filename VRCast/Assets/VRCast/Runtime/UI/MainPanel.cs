using UnityEngine;
using VRCast.Avatars;
using VRCast.Cameras;

namespace VRCast.UI
{
    /// <summary>
    /// IMGUI による最小操作パネル（Avatar / Camera）。Tab キーで表示切替。
    /// </summary>
    public class MainPanel : MonoBehaviour
    {
        // パネルの表示切替キー（配信時に UI を隠す）
        private const KeyCode ToggleKey = KeyCode.Tab;

        // IMGUI ウィンドウ ID と初期位置・幅
        private const int WindowId = 0x5643;
        private const float WindowWidth = 360f;

        // FOV スライダーの範囲
        private const float MinFov = 10f;
        private const float MaxFov = 90f;

        private AvatarSession _session;
        private OrbitCameraController _orbit;
        private string _pathInput = string.Empty;
        private bool _visible = true;
        private Rect _windowRect = new Rect(10f, 10f, WindowWidth, 0f);

        public void Initialize(AvatarSession session, OrbitCameraController orbit, string initialPath)
        {
            // 依存の受け取りと入力欄の初期値設定
            _session = session;
            _orbit = orbit;
            _pathInput = initialPath ?? string.Empty;
        }

        private void Update()
        {
            // 表示切替
            if (Input.GetKeyDown(ToggleKey))
            {
                _visible = !_visible;
            }

            // 非表示時はカメラ入力を常に許可
            if (!_visible && _orbit != null)
            {
                _orbit.InputBlocked = false;
            }
        }

        private void OnGUI()
        {
            // 非表示または未初期化なら描画しない
            if (!_visible || _session == null)
            {
                return;
            }

            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawWindow, "VRCast  (Tab: hide)");

            // パネル上にマウスがある間はカメラ操作を止める
            if (Event.current.type == EventType.Repaint && _orbit != null)
            {
                _orbit.InputBlocked = _windowRect.Contains(Event.current.mousePosition);
            }
        }

        private void DrawWindow(int id)
        {
            DrawAvatarSection();
            GUILayout.Space(8f);
            DrawCameraSection();

            // タイトルバーでドラッグ移動
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        private void DrawAvatarSection()
        {
            GUILayout.Label("Avatar (.vavatar path)");
            _pathInput = GUILayout.TextField(_pathInput);

            GUILayout.BeginHorizontal();

            // 読込中はボタンを無効化
            GUI.enabled = !_session.IsLoading;
            if (GUILayout.Button("Load"))
            {
                _session.Load(_pathInput);
            }

            // 表示中のアバターがある場合のみ有効
            GUI.enabled = !_session.IsLoading && _session.Current != null;
            if (GUILayout.Button("Reload"))
            {
                _session.Reload();
            }

            if (GUILayout.Button("Unload"))
            {
                _session.Unload();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // 状態表示: 読込中 > エラー > アバター情報 の優先順
            if (_session.IsLoading)
            {
                GUILayout.Label("Loading...");
            }
            else if (_session.LastError != null)
            {
                GUILayout.Label("Error: " + _session.LastError);
            }
            else if (_session.Current != null)
            {
                LoadedAvatar avatar = _session.Current;
                GUILayout.Label($"Name: {avatar.Manifest.name}");
                GUILayout.Label($"Humanoid: {(avatar.IsHumanoid ? "Yes" : "No")}  Renderers: {avatar.RendererCount}");
                GUILayout.Label($"Built with Unity {avatar.Manifest.unityVersion}");
            }
            else
            {
                GUILayout.Label("No avatar loaded.");
            }
        }

        private void DrawCameraSection()
        {
            GUILayout.Label("Camera (RMB: rotate / MMB: pan / Wheel: zoom)");

            // FOV スライダー
            GUILayout.BeginHorizontal();
            GUILayout.Label($"FOV {_orbit.FieldOfView:F0}", GUILayout.Width(60f));
            _orbit.FieldOfView = GUILayout.HorizontalSlider(_orbit.FieldOfView, MinFov, MaxFov);
            GUILayout.EndHorizontal();

            // 視点リセット
            if (GUILayout.Button("Reset Camera"))
            {
                _orbit.ResetView();
            }
        }
    }
}
