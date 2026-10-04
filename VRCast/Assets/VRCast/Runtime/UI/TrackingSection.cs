using System.Collections.Generic;
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

        // 表情の割り当ての候補（先頭は「割り当てなし」、以降は表情プリセット名）と、作成元のコントローラー
        private readonly List<string> _expressionOptions = new List<string>();
        private ExpressionController _expressionSource;

        // 表情ごとに「自動」で推定されるプリセットの位置（FaceExpression の値で引く、-1 = なし）
        private readonly int[] _expressionGuesses = new int[(int)FaceExpression.Sad + 1];

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

        /// <summary>
        /// 設定値を直接書き換えた後（全設定のリセット等）に、入力途中の値を設定に合わせ直す。
        /// </summary>
        public void SyncFromSettings()
        {
            _portInput = _settings.trackingPort.ToString();
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
            GuiControls.BeginCard(Loc.T("Webcam tracking", "Web カメラトラッキング", "웹캠 트래킹",
                "摄像头追踪", "網路攝影機追蹤"));
            _settings.trackingEnabled = GUILayout.Toggle(
                _settings.trackingEnabled, Loc.T("Enable tracking", "トラッキングを有効にする", "트래킹 사용",
                    "启用追踪", "啟用追蹤"));

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
                Loc.T("MediaPipe (face + hands)", "MediaPipe（顔 + 手）", "MediaPipe (얼굴 + 손)",
                    "MediaPipe（面部 + 手）", "MediaPipe（臉部 + 手）"),
                Loc.T("OpenSeeFace (face only)", "OpenSeeFace（顔のみ）", "OpenSeeFace (얼굴만)",
                    "OpenSeeFace（仅面部）", "OpenSeeFace（僅臉部）"),
            };
            _settings.trackingSource = (TrackingSource)GuiControls.EnumSelector(
                Loc.T("Source", "入力元", "입력 소스", "输入源", "輸入來源"), labels, (int)_settings.trackingSource);

            // 腕・手は MediaPipe のみ
            if (_settings.trackingSource != TrackingSource.MediaPipe)
            {
                return;
            }

            _settings.trackingHands = GUILayout.Toggle(
                _settings.trackingHands, Loc.T("Arms / hands", "腕・手", "팔·손", "手臂 / 手", "手臂 / 手"));

            // 腕・手が有効で、アバターに適用中かどうか（映っていない間は待機ポーズ）
            var driver = _avatar.Get<HandTrackingDriver>();
            if (_settings.trackingHands && driver != null)
            {
                GuiControls.Hint(driver.IsTracking
                    ? Loc.T("Arms / hands: tracking", "腕・手: トラッキング中", "팔·손: 트래킹 중",
                        "手臂 / 手：追踪中", "手臂 / 手：追蹤中")
                    : Loc.T("Arms / hands: not visible (idle pose)", "腕・手: 映っていません（待機ポーズ）",
                        "팔·손: 보이지 않음 (대기 포즈)", "手臂 / 手：未拍到（待机姿势）", "手臂 / 手：未拍到（待機姿勢）"));
            }

            DrawExpressions();
        }

        private void DrawExpressions()
        {
            // 表情反映の ON/OFF（MediaPipe のみ。OFF にすると自動で当てた表情はすぐ戻る）
            _settings.trackingExpressions = GUILayout.Toggle(
                _settings.trackingExpressions,
                Loc.T("Facial expressions", "表情を反映", "표정 반영", "反映表情", "反映表情"));

            // 無効時は詳細設定を出さない
            if (!_settings.trackingExpressions)
            {
                return;
            }

            // 表情データが無いアバターは割り当てできない
            var expressions = _avatar.Get<ExpressionController>();
            if (_avatar.HasAvatar && (expressions == null || expressions.Names.Count == 0))
            {
                GuiControls.Hint(Loc.T("No expressions in this avatar", "このアバターには表情データがありません",
                    "이 아바타에는 표정 데이터가 없습니다", "此虚拟形象没有表情数据", "此虛擬形象沒有表情資料"));
                return;
            }

            _settings.trackingExpressionSensitivity = GuiControls.Slider(
                Loc.T("Expression sensitivity", "表情の感度", "표정 감도", "表情灵敏度", "表情靈敏度"),
                _settings.trackingExpressionSensitivity,
                AppSettings.MinExpressionSensitivity, AppSettings.MaxExpressionSensitivity);

            // アバター未表示なら割り当ては出さない
            if (expressions == null)
            {
                return;
            }

            // 表情ごとの割り当て（ニュートラル以外）
            RefreshExpressionOptions(expressions);
            for (var expression = FaceExpression.Smile; expression <= FaceExpression.Sad; expression++)
            {
                DrawExpressionMapping(expression, expressions.Names);
            }

            // 判定中の表情（受信中のみ）
            var driver = _avatar.Get<FaceTrackingDriver>();
            if (driver != null && driver.IsDetectingExpression)
            {
                GuiControls.Hint(Loc.T("Detected", "検出中", "감지 중", "检测中", "偵測中") + ": "
                    + ExpressionLabel(driver.DetectedExpression));
            }
        }

        private void RefreshExpressionOptions(ExpressionController expressions)
        {
            // アバターが替わったときだけ候補（先頭は「割り当てなし」）を作り直す
            if (_expressionSource != expressions)
            {
                _expressionSource = expressions;
                _expressionOptions.Clear();
                _expressionOptions.Add(string.Empty);
                _expressionOptions.AddRange(expressions.Names);

                // 「自動」で推定されるプリセットも同時に求めておく（OnGUI 毎の文字列比較を避ける）
                for (int i = 0; i < _expressionGuesses.Length; i++)
                {
                    _expressionGuesses[i] = ExpressionMapping.Guess(expressions.Names, (FaceExpression)i);
                }
            }

            // 先頭の表示名は表示言語に合わせる
            _expressionOptions[0] = Loc.T("None", "なし", "없음", "无", "無");
        }

        private void DrawExpressionMapping(FaceExpression expression, IReadOnlyList<string> names)
        {
            // 保存値を候補の位置へ（空欄・見つからない名前 = 自動 -1、割り当てなし = 0、プリセット = 1〜）
            string saved = ExpressionMapping.GetSaved(_settings, expression);
            int found = ExpressionMapping.IndexOf(names, saved);
            int current = saved == ExpressionMapping.None ? 0 : found >= 0 ? found + 1 : -1;

            // 「自動」には推定されたプリセット名を添える
            int guess = _expressionGuesses[(int)expression];
            string auto = Loc.T("Auto", "自動", "자동", "自动", "自動") + ": "
                + (guess >= 0 ? names[guess] : Loc.T("none", "なし", "없음", "无", "無"));

            int selected = GuiControls.Selector(ExpressionLabel(expression), _expressionOptions, current, auto);

            // 操作されたときだけ設定へ反映
            if (selected != current)
            {
                string value = selected < 0 ? string.Empty
                    : selected == 0 ? ExpressionMapping.None
                    : names[selected - 1];
                ExpressionMapping.SetSaved(_settings, expression, value);
            }
        }

        private static string ExpressionLabel(FaceExpression expression)
        {
            // 表情の表示名（表示言語に合わせる）
            switch (expression)
            {
                case FaceExpression.Smile:
                    return Loc.T("Smile", "笑顔", "웃음", "笑脸", "笑臉");
                case FaceExpression.Surprise:
                    return Loc.T("Surprise", "驚き", "놀람", "惊讶", "驚訝");
                case FaceExpression.Angry:
                    return Loc.T("Angry", "怒り", "화남", "生气", "生氣");
                case FaceExpression.Sad:
                    return Loc.T("Sad", "悲しみ", "슬픔", "悲伤", "悲傷");
                default:
                    return Loc.T("Neutral", "ニュートラル", "무표정", "无表情", "無表情");
            }
        }

        private void DrawLauncher()
        {
            // 同梱版が無い（開発ビルド等）か、既にパス指定済みのときだけパス入力を出す
            string executable = TrackerProcess.ExecutableOf(_settings.trackingSource);
            if (!_process.HasBundled || !string.IsNullOrEmpty(_settings.trackerPath))
            {
                string bundled = _process.HasBundled
                    ? Loc.T(" (empty = bundled)", "（空欄 = 同梱版）", " (비워 두면 = 포함된 버전)",
                        "（留空 = 内置版本）", "（留空 = 內建版本）")
                    : string.Empty;
                GuiControls.Hint(executable + Loc.T(" path", " のパス", " 경로", " 路径", " 路徑") + bundled);
                _settings.trackerPath = GUILayout.TextField(_settings.trackerPath);
            }

            // カメラ選択（デバイス名で保存、変更するとトラッカーが起動し直す）
            int current = IndexOfCamera(_settings.trackerCamera);
            int selected = GuiControls.Selector(
                Loc.T("Camera", "カメラ", "카메라", "相机", "相機"), _process.Cameras, current, null);
            if (selected != current && selected >= 0)
            {
                _settings.trackerCamera = _process.Cameras[selected];
            }

            GUILayout.BeginHorizontal();

            // 一覧の再取得（カメラの抜き差し後など）
            GUI.enabled = !_process.IsListing;
            string refresh = _process.IsListing
                ? Loc.T("Listing...", "取得中...", "가져오는 중...", "获取中...", "取得中...")
                : Loc.T("Refresh cameras", "カメラ一覧を更新", "카메라 목록 새로 고침", "刷新相机列表", "重新整理相機清單");
            if (GUILayout.Button(refresh, GuiControls.Shrinkable))
            {
                _process.RefreshCameras();
            }

            // トラッカーの再起動（固まったとき・カメラを他アプリから解放したとき）
            if (GUILayout.Button(Loc.T("Restart tracker", "トラッカーを再起動", "트래커 다시 시작", "重启追踪器", "重新啟動追蹤器"), GuiControls.Shrinkable))
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
            GUILayout.Label(Loc.T("UDP port", "UDP ポート", "UDP 포트", "UDP 端口", "UDP 連接埠"), GUILayout.Width(130f));
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
            GuiControls.BeginCard(Loc.T("Motion", "動き", "움직임", "动作", "動作"));
            _settings.trackingMirror = GUILayout.Toggle(
                _settings.trackingMirror,
                Loc.T("Mirror (move like a mirror)", "ミラー（鏡のように動かす）", "미러 (거울처럼 움직이기)",
                    "镜像（像镜子一样移动）", "鏡像（像鏡子一樣移動）"));

            // 頭の位置に合わせた体の動かし方（傾き / 体全体の移動 / 両方）と強さ
            string[] labels =
            {
                Loc.T("Lean (feet fixed)", "傾ける（足は固定）", "기울이기 (발 고정)", "倾斜（脚部固定）", "傾斜（腳部固定）"),
                Loc.T("Move (whole body)", "体ごと動かす", "몸 전체 움직이기", "整体移动", "整體移動"),
                Loc.T("Lean + move", "傾ける + 体ごと", "기울이기 + 몸 전체", "倾斜 + 整体移动", "傾斜 + 整體移動"),
            };
            _settings.trackingBodyMotion = (BodyMotion)GuiControls.EnumSelector(
                Loc.T("Body", "体の動き", "몸의 움직임", "身体动作", "身體動作"), labels, (int)_settings.trackingBodyMotion);
            _settings.trackingBodyLean = GuiControls.Slider(
                Loc.T("Body strength", "体の動きの強さ", "몸 움직임 강도", "身体动作强度", "身體動作強度"),
                _settings.trackingBodyLean, 0f, AppSettings.MaxTrackingBodyLean);
            _settings.trackingGaze = GuiControls.Slider(
                Loc.T("Eye gaze", "視線の強さ", "시선 강도", "视线强度", "視線強度"),
                _settings.trackingGaze, 0f, AppSettings.MaxTrackingGaze);

            // ウインク用 BlendShape が無いアバターは両目同時のみ（再エクスポートで推定される場合がある）
            var blink = _avatar.Get<BlinkController>();
            if (blink != null && blink.IsAvailable && !blink.HasWink)
            {
                GuiControls.Hint(Loc.T("Wink: not available (both eyes only)", "ウインク: 非対応（両目同時のみ）",
                    "윙크: 미지원 (양쪽 눈 동시만)", "眨单眼：不支持（仅双眼同时）", "眨單眼：不支援（僅雙眼同時）"));
            }

            GuiControls.EndCard();
        }

        private void DrawCalibrate()
        {
            GuiControls.BeginCard(Loc.T("Calibration", "キャリブレーション", "캘리브레이션", "校准", "校準"));
            GuiControls.Hint(Loc.T(
                "Look at the camera and press Reset > Head (head, body and gaze) or Gaze at the bottom of the panel",
                "カメラを見て、パネル下部のリセットの「顔の向き」（顔・上半身・視線）か「視線」を押してください",
                "카메라를 보고 패널 아래쪽 초기화의 \"얼굴 방향\" (얼굴·상체·시선) 또는 \"시선\"을 누르세요",
                "看着相机，点击面板底部重置中的“头部朝向”（头部・上半身・视线）或“视线”",
                "看著相機，點擊面板底部重設中的「頭部朝向」（頭部・上半身・視線）或「視線」"));

            // 正面位置からの頭の移動量（体の動きの強さ調整の目安）
            var driver = _avatar.Get<FaceTrackingDriver>();
            if (driver != null && driver.IsTracking)
            {
                Vector3 offset = driver.HeadOffset;
                GuiControls.Hint(Loc.T("Head offset", "頭の移動量", "머리 이동량", "头部位移", "頭部位移")
                    + $"  x {offset.x:F2}  y {offset.y:F2}  z {offset.z:F2}");
            }

            GuiControls.EndCard();
        }

        private void DrawDebug()
        {
            GuiControls.BeginCard(Loc.T("Debug", "確認", "확인", "调试", "偵錯"));

            // アバターの代わりに受信値をそのまま線で表示（保存しない、起動時は常にアバター）
            _skeleton.Visible = GUILayout.Toggle(
                _skeleton.Visible, Loc.T("Raw view (skeleton instead of avatar)", "生データ表示（アバターの代わりに骨格）",
                    "원시 데이터 표시 (아바타 대신 골격)", "原始数据显示（以骨架代替虚拟形象）",
                    "原始資料顯示（以骨架代替虛擬形象）"));

            // 表示中は顔の値も数値で出す（受信中のみ）
            if (_skeleton.Visible && _tracker.TryGetFrame(out FaceTrackingFrame face))
            {
                GuiControls.Hint(Loc.T("Eye", "目", "눈", "眼", "眼")
                    + $" L {face.EyeOpenLeft:F2}  R {face.EyeOpenRight:F2}    "
                    + Loc.T("Mouth", "口", "입", "嘴", "嘴") + $" {face.MouthOpen:F2}");
                GuiControls.Hint(Loc.T("Gaze", "視線", "시선", "视线", "視線") + $" x {face.Gaze.x:F1}  y {face.Gaze.y:F1}");

                // 表情の強さ（MediaPipe のみ。感度調整の目安）
                if (face.HasExpression)
                {
                    ExpressionScores scores = face.Expression;
                    GuiControls.Hint($"{ExpressionLabel(FaceExpression.Smile)} {scores.Smile:F2}  "
                        + $"{ExpressionLabel(FaceExpression.Surprise)} {scores.Surprise:F2}  "
                        + $"{ExpressionLabel(FaceExpression.Angry)} {scores.Angry:F2}  "
                        + $"{ExpressionLabel(FaceExpression.Sad)} {scores.Sad:F2}");
                }
            }

            GuiControls.EndCard();
        }
    }
}
