using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// Tracking タブ（ON で同梱トラッカーを自動起動。入力元・カメラ・ポート・鏡像・体の動き・キャリブレーション・確認表示・状態）。
    /// </summary>
    public class TrackingSection
    {
        private readonly AvatarComponentCache _avatar;
        private readonly IFaceTrackingProvider _tracker;
        private readonly TrackerProcess _process;
        private readonly TrackingSkeletonView _skeleton;
        private readonly AppSettings _settings;

        // 入力途中のポート文字列（確定するまで設定へ反映しない）
        private string _portInput;

        public TrackingSection(
            AvatarSession session, IFaceTrackingProvider tracker, TrackerProcess process,
            TrackingSkeletonView skeleton, AppSettings settings)
        {
            _avatar = new AvatarComponentCache(session);
            _tracker = tracker;
            _process = process;
            _skeleton = skeleton;
            _settings = settings;
            _portInput = settings.trackingPort.ToString();
        }

        public void Draw()
        {
            _avatar.Refresh();
            DrawGeneral();

            // 無効時は詳細設定を出さない
            if (!_settings.trackingEnabled)
            {
                return;
            }

            DrawMotion();
            DrawCalibrate();
            DrawDebug();
        }

        private void DrawGeneral()
        {
            GuiControls.BeginCard(Loc.T("Webcam tracking", "Web カメラトラッキング"));
            _settings.trackingEnabled = GUILayout.Toggle(
                _settings.trackingEnabled, Loc.T("Enable tracking", "トラッキングを有効にする"));

            // 無効時は ON/OFF だけ
            if (_settings.trackingEnabled)
            {
                DrawSource();
                DrawLauncher();
                DrawPort();
                GuiControls.Hint(_tracker.Status);
            }

            GuiControls.EndCard();
        }

        private void DrawSource()
        {
            // 入力元の切替（変更するとトラッカー・受信が起動し直す）
            string[] labels =
            {
                Loc.T("MediaPipe (face + hands)", "MediaPipe（顔 + 手）"),
                Loc.T("OpenSeeFace (face only)", "OpenSeeFace（顔のみ）"),
            };
            _settings.trackingSource = (TrackingSource)GuiControls.EnumSelector(
                Loc.T("Source", "入力元"), labels, (int)_settings.trackingSource);

            // 腕・手は MediaPipe のみ
            if (_settings.trackingSource != TrackingSource.MediaPipe)
            {
                return;
            }

            _settings.trackingHands = GUILayout.Toggle(_settings.trackingHands, Loc.T("Arms / hands", "腕・手"));

            // 腕・手が有効で、アバターに適用中かどうか（映っていない間は待機ポーズ）
            var driver = _avatar.Get<HandTrackingDriver>();
            if (_settings.trackingHands && driver != null)
            {
                GuiControls.Hint(driver.IsTracking
                    ? Loc.T("Arms / hands: tracking", "腕・手: トラッキング中")
                    : Loc.T("Arms / hands: not visible (idle pose)", "腕・手: 映っていません（待機ポーズ）"));
            }
        }

        private void DrawLauncher()
        {
            // 同梱版が無い（開発ビルド等）か、既にパス指定済みのときだけパス入力を出す
            string executable = TrackerProcess.ExecutableOf(_settings.trackingSource);
            if (!_process.HasBundled || !string.IsNullOrEmpty(_settings.trackerPath))
            {
                string bundled = _process.HasBundled ? Loc.T(" (empty = bundled)", "（空欄 = 同梱版）") : string.Empty;
                GuiControls.Hint(executable + Loc.T(" path", " のパス") + bundled);
                _settings.trackerPath = GUILayout.TextField(_settings.trackerPath);
            }

            // カメラ選択（デバイス名で保存、変更するとトラッカーが起動し直す）
            int current = IndexOfCamera(_settings.trackerCamera);
            int selected = GuiControls.Selector(Loc.T("Camera", "カメラ"), _process.Cameras, current, null);
            if (selected != current && selected >= 0)
            {
                _settings.trackerCamera = _process.Cameras[selected];
            }

            GUILayout.BeginHorizontal();

            // 一覧の再取得（カメラの抜き差し後など）
            GUI.enabled = !_process.IsListing;
            string refresh = _process.IsListing ? Loc.T("Listing...", "取得中...") : Loc.T("Refresh cameras", "カメラ一覧を更新");
            if (GUILayout.Button(refresh))
            {
                _process.RefreshCameras();
            }

            // トラッカーの再起動（固まったとき・カメラを他アプリから解放したとき）
            if (GUILayout.Button(Loc.T("Restart tracker", "トラッカーを再起動")))
            {
                _process.Restart();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GuiControls.Hint(_process.Status);
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
            GUILayout.Label(Loc.T("UDP port", "UDP ポート"), GUILayout.Width(130f));
            _portInput = GUILayout.TextField(_portInput, 5);

            // 範囲内の数値になったときだけ反映（受信側・起動中のトラッカーがポート変更に追従する）
            if (int.TryParse(_portInput, out int port)
                && port >= AppSettings.MinTrackingPort && port <= AppSettings.MaxTrackingPort)
            {
                _settings.trackingPort = port;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawMotion()
        {
            GuiControls.BeginCard(Loc.T("Motion", "動き"));
            _settings.trackingMirror = GUILayout.Toggle(
                _settings.trackingMirror, Loc.T("Mirror (move like a mirror)", "ミラー（鏡のように動かす）"));

            // 頭の位置に合わせた体の動かし方（傾き / 体全体の移動 / 両方）と強さ
            string[] labels =
            {
                Loc.T("Lean (feet fixed)", "傾ける（足は固定）"),
                Loc.T("Move (whole body)", "体ごと動かす"),
                Loc.T("Lean + move", "傾ける + 体ごと"),
            };
            _settings.trackingBodyMotion = (BodyMotion)GuiControls.EnumSelector(
                Loc.T("Body", "体の動き"), labels, (int)_settings.trackingBodyMotion);
            _settings.trackingBodyLean = GuiControls.Slider(
                Loc.T("Body strength", "体の動きの強さ"), _settings.trackingBodyLean, 0f, AppSettings.MaxTrackingBodyLean);
            _settings.trackingGaze = GuiControls.Slider(
                Loc.T("Eye gaze", "視線の強さ"), _settings.trackingGaze, 0f, AppSettings.MaxTrackingGaze);

            // ウインク用 BlendShape が無いアバターは両目同時のみ（再エクスポートで推定される場合がある）
            var blink = _avatar.Get<BlinkController>();
            if (blink != null && blink.IsAvailable && !blink.HasWink)
            {
                GuiControls.Hint(Loc.T("Wink: not available (both eyes only)", "ウインク: 非対応（両目同時のみ）"));
            }

            GuiControls.EndCard();
        }

        private void DrawCalibrate()
        {
            GuiControls.BeginCard(Loc.T("Calibration", "キャリブレーション"));
            GuiControls.Hint(Loc.T("Look at the camera and press", "カメラを見て押してください"));

            // 受信中のアバターがあるときだけ押せる
            var driver = _avatar.Get<FaceTrackingDriver>();
            GUI.enabled = driver != null && driver.IsTracking;
            GUILayout.BeginHorizontal();

            // 頭・上半身・視線をまとめて正面に
            if (GUILayout.Button(Loc.T("Reset pose", "姿勢をリセット")))
            {
                driver.Calibrate();
            }

            // 視線だけを正面に
            if (GUILayout.Button(Loc.T("Reset gaze", "視線をリセット")))
            {
                driver.CalibrateGaze();
            }

            GUILayout.EndHorizontal();
            GUI.enabled = true;

            // 正面位置からの頭の移動量（体の動きの強さ調整の目安）
            if (driver != null && driver.IsTracking)
            {
                Vector3 offset = driver.HeadOffset;
                GuiControls.Hint(Loc.T("Head offset", "頭の移動量") + $"  x {offset.x:F2}  y {offset.y:F2}  z {offset.z:F2}");
            }

            GuiControls.EndCard();
        }

        private void DrawDebug()
        {
            GuiControls.BeginCard(Loc.T("Debug", "確認"));

            // アバターの代わりに受信値をそのまま線で表示（保存しない、起動時は常にアバター）
            _skeleton.Visible = GUILayout.Toggle(
                _skeleton.Visible, Loc.T("Raw view (skeleton instead of avatar)", "生データ表示（アバターの代わりに骨格）"));

            // 表示中は顔の値も数値で出す（受信中のみ）
            if (_skeleton.Visible && _tracker.TryGetFrame(out FaceTrackingFrame face))
            {
                GuiControls.Hint(Loc.T("Eye", "目") + $" L {face.EyeOpenLeft:F2}  R {face.EyeOpenRight:F2}    "
                    + Loc.T("Mouth", "口") + $" {face.MouthOpen:F2}");
                GuiControls.Hint(Loc.T("Gaze", "視線") + $" x {face.Gaze.x:F1}  y {face.Gaze.y:F1}");
            }

            GuiControls.EndCard();
        }
    }
}
