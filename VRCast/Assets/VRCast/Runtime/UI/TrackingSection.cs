using UnityEngine;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// MainPanel 内の Tracking セクション（OpenSeeFace 受信の ON/OFF・ポート・鏡像・キャリブレーション・状態）。
    /// </summary>
    public class TrackingSection
    {
        private readonly AvatarComponentCache _avatar;
        private readonly IFaceTrackingProvider _tracker;
        private readonly AppSettings _settings;

        // 入力途中のポート文字列（確定するまで設定へ反映しない）
        private string _portInput;

        public TrackingSection(AvatarSession session, IFaceTrackingProvider tracker, AppSettings settings)
        {
            _avatar = new AvatarComponentCache(session);
            _tracker = tracker;
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

            DrawPort();
            _settings.trackingMirror = GUILayout.Toggle(_settings.trackingMirror, " Mirror");
            DrawCalibrate();
            GUILayout.Label(_tracker.Status);
        }

        private void DrawPort()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("UDP port", GUILayout.Width(100f));
            _portInput = GUILayout.TextField(_portInput, 5);

            // 範囲内の数値になったときだけ反映（受信側がポート変更を検出して開き直す）
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
