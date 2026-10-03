using UnityEngine;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// MainPanel 内の Tracking セクション（OpenSeeFace の起動・カメラ選択、受信の ON/OFF・ポート・鏡像・キャリブレーション・状態）。
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

        // 初回表示時にカメラ一覧を自動取得したか
        private bool _autoRefreshed;

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
            // facetracker.exe のパス
            GUILayout.Label("facetracker.exe path");
            _settings.trackerPath = GUILayout.TextField(_settings.trackerPath);

            // 初回表示時は一覧を自動取得（パス未設定なら状態表示で案内される）
            if (!_autoRefreshed && _process.Cameras.Count == 0)
            {
                _autoRefreshed = true;
                _process.RefreshCameras();
            }

            // カメラ選択（デバイス名で保存）
            int current = IndexOfCamera(_settings.trackerCamera);
            int selected = GuiControls.Selector(_process.Cameras, current, null);
            if (selected != current && selected >= 0)
            {
                _settings.trackerCamera = _process.Cameras[selected];
            }

            GUILayout.BeginHorizontal();

            // 一覧の再取得（実行中・取得中は不可）
            GUI.enabled = !_process.IsListing && !_process.IsRunning;
            if (GUILayout.Button(_process.IsListing ? "Listing..." : "Refresh cameras"))
            {
                _process.RefreshCameras();
            }

            // 起動・停止
            GUI.enabled = !_process.IsListing;
            if (GUILayout.Button(_process.IsRunning ? "Stop tracker" : "Start tracker"))
            {
                if (_process.IsRunning)
                {
                    _process.StopTracker();
                }
                else
                {
                    _process.StartTracker();
                }
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
