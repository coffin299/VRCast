using UnityEngine;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// MainPanel 内の Tracking セクション（ON で同梱 OpenSeeFace を自動起動。カメラ選択・ポート・鏡像・キャリブレーション・状態）。
    /// </summary>
    public class TrackingSection
    {
        // ラベル列の幅
        private const float LabelWidth = 100f;

        private readonly AvatarComponentCache _avatar;
        private readonly IFaceTrackingProvider _tracker;
        private readonly FaceTrackerProcess _process;
        private readonly AppSettings _settings;

        // 入力途中のポート文字列（確定するまで設定へ反映しない）
        private string _portInput;

        public TrackingSection(
            AvatarSession session, IFaceTrackingProvider tracker, FaceTrackerProcess process, AppSettings settings)
        {
            _avatar = new AvatarComponentCache(session);
            _tracker = tracker;
            _process = process;
            _settings = settings;
            _portInput = settings.trackingPort.ToString();
        }

        public void Draw()
        {
            GUILayout.Label("Tracking (OpenSeeFace)");
            _avatar.Refresh();
            _settings.trackingEnabled = GUILayout.Toggle(_settings.trackingEnabled, " Face tracking");

            // 無効時は詳細設定を出さない
            if (!_settings.trackingEnabled)
            {
                return;
            }

            DrawLauncher();
            DrawPort();
            _settings.trackingMirror = GUILayout.Toggle(_settings.trackingMirror, " Mirror");
            DrawCalibrate();
            GUILayout.Label(_tracker.Status);
        }

        private void DrawLauncher()
        {
            // 同梱版が無い（開発ビルド等）か、既にパス指定済みのときだけパス入力を出す
            if (!_process.HasBundled || !string.IsNullOrEmpty(_settings.trackerPath))
            {
                GUILayout.Label("facetracker.exe path" + (_process.HasBundled ? " (empty = bundled)" : string.Empty));
                _settings.trackerPath = GUILayout.TextField(_settings.trackerPath);
            }

            // カメラ選択（デバイス名で保存、変更するとトラッカーが起動し直す）
            int current = IndexOfCamera(_settings.trackerCamera);
            int selected = GuiControls.Selector(_process.Cameras, current, null);
            if (selected != current && selected >= 0)
            {
                _settings.trackerCamera = _process.Cameras[selected];
            }

            GUILayout.BeginHorizontal();

            // 一覧の再取得（カメラの抜き差し後など）
            GUI.enabled = !_process.IsListing;
            if (GUILayout.Button(_process.IsListing ? "Listing..." : "Refresh cameras"))
            {
                _process.RefreshCameras();
            }

            // トラッカーの再起動（固まったとき・カメラを他アプリから解放したとき）
            if (GUILayout.Button("Restart tracker"))
            {
                _process.Restart();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(_process.Status);
        }

        private int IndexOfCamera(string name)
        {
            // 一覧内の位置（見つからなければ -1）
            for (int i = 0; i < _process.Cameras.Count; i++)
            {
                if (_process.Cameras[i] == name)
                {
                    return i;
                }
            }

            return -1;
        }

        private void DrawPort()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("UDP port", GUILayout.Width(LabelWidth));
            _portInput = GUILayout.TextField(_portInput, 5);

            // 範囲内の数値になったときだけ反映（受信側・起動中のトラッカーがポート変更に追従する）
            if (int.TryParse(_portInput, out int port)
                && port >= AppSettings.MinTrackingPort && port <= AppSettings.MaxTrackingPort)
            {
                _settings.trackingPort = port;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawCalibrate()
        {
            // 受信中のアバターがあるときだけ押せる
            var driver = _avatar.Get<FaceTrackingDriver>();
            GUI.enabled = driver != null && driver.IsTracking;
            if (GUILayout.Button("Calibrate (look at camera)"))
            {
                driver.Calibrate();
            }

            GUI.enabled = true;
        }
    }
}
