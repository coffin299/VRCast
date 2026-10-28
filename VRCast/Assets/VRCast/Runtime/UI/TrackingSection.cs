using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Platform;
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
        private readonly IBodyTrackingProvider _body;
        private readonly TrackerProcess _process;
        private readonly TrackingSkeletonView _skeleton;
        private readonly AppSettings _settings;

        // 入力途中のポート文字列（同梱トラッカー用と VMC 用。確定するまで設定へ反映しない）
        private string _portInput;
        private string _vmcPortInput;

        // 表情のしきい値の入力途中の文字列（表情ごとに保持）
        private readonly Dictionary<string, string> _thresholdInputs = new Dictionary<string, string>();

        // ファイアウォール許可の実行中と結果（null = 未表示、空文字 = 成功、それ以外 = 失敗理由）
        private Task<string> _firewallPending;
        private bool _firewallHasResult;
        private string _firewallError;

        // この PC の LAN のアドレス（VMC の送信先として表示。取得に時間がかかることがあるため間隔を空けて取り直す）
        private const float AddressRefreshSeconds = 5f;
        private List<string> _localAddresses = new List<string>();
        private float _nextAddressRefresh;

        // 入力元の行の幅（前回の描画で測った値。0 = 未計測）と、推奨・PC 負荷の注記をスイッチの右に並べるか
        private float _sourceRowWidth;
        private bool _sourceNoteInline;

        // 注記をスイッチの下に出すときの字下げ（スイッチの幅）と、横に並べるときの最小の間隔
        private const float SourceNoteIndent = 48f;
        private const float SourceNoteGap = 12f;

        // トラッカーの動作の選択肢の並び（重い順。TrackerMode の数値は保存値なので並びとは別）
        private static readonly TrackerMode[] TrackerModeOrder = { TrackerMode.Fluid, TrackerMode.Smooth, TrackerMode.Eco };

        // iPhone の IP アドレスの入力欄の最大文字数（IPv4 の最長 15 文字に余裕を持たせる）
        private const int MaxAddressLength = 40;

        // 表情の割り当ての候補（先頭は「割り当てなし」、以降は表情プリセット名）と、作成元のコントローラー
        private readonly List<string> _expressionOptions = new List<string>();
        private ExpressionController _expressionSource;

        // 表情ごとに「自動」で推定されるプリセットの位置（FaceExpression の値で引く、-1 = なし）
        private readonly int[] _expressionGuesses = new int[(int)FaceExpressions.Last + 1];

        // 表情のキャリブレーション（登録の操作と記録の進行）
        private readonly ExpressionCalibrationControls _calibration = new ExpressionCalibrationControls();

        // カメラの傾き補正（任意。押してから数秒後に記録）
        private readonly TiltCalibrationRecorder _tiltCalibration = new TiltCalibrationRecorder();

        // 傾きキャリブの案内オーバーレイ（テーマが替わったら作り直す）
        private static GUIStyle _tiltPromptStyle;
        private static UiTheme _tiltPromptTheme;
        private const float TiltOverlayWidth = 560f;
        private const float TiltOverlayHeight = 150f;
        private const float TiltOverlayTop = 40f;
        private const int TiltPromptFontSize = 30;

        // 案内ウィンドウの ID（操作パネル 0x5643・表情の案内 0x5660 と重ならない）
        private const int TiltOverlayWindowId = 0x5661;

        // ウィンドウ関数から読む、いま出している案内
        private static TiltCalibrationRecorder _tiltOverlayRecorder;
        private static UiTheme _tiltOverlayTheme;

        /// <summary>
        /// 表情のキャリブレーションの記録係（画面中央の案内の表示用）。
        /// </summary>
        public ExpressionCalibrationRecorder CalibrationRecorder => _calibration.Recorder;

        /// <summary>
        /// カメラの傾き補正の記録係（画面中央の案内の表示用）。
        /// </summary>
        public TiltCalibrationRecorder TiltCalibration => _tiltCalibration;

        public TrackingSection(
            AvatarSession session, IFaceTrackingProvider tracker, TrackerProcess process,
            TrackingSkeletonView skeleton, AppSettings settings)
        {
            _avatar = new AvatarComponentCache(session);
            _tracker = tracker;
            // 傾きキャリブは両肩が取れるとき体を優先する（顔プロバイダが体も実装している）
            _body = tracker as IBodyTrackingProvider;
            _process = process;
            _skeleton = skeleton;
            _settings = settings;
            SyncFromSettings();
        }

        /// <summary>
        /// 設定値を直接書き換えた後（全設定のリセット等）に、入力途中の値を設定に合わせ直す。
        /// </summary>
        public void SyncFromSettings()
        {
            _portInput = _settings.trackingPort.ToString();
            _vmcPortInput = _settings.vmcPort.ToString();
        }

        /// <summary>
        /// 毎フレーム呼ぶ（パネルを隠していても表情・傾きのキャリブレーションの記録を進める）。
        /// </summary>
        public void Update()
        {
            _calibration.Tick(_tracker, _settings);
            if (_tiltCalibration.Tick(_tracker, _body, _settings, Time.unscaledDeltaTime))
            {
                AppBootstrap.Save();
            }
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
            // 見出しは入力元に合わせる（Web カメラ / iPhone 等の外部アプリ / 両方の併用）
            TrackingSource source = _settings.trackingSource;
            bool external = !TrackingSourceInfo.UsesBundledTracker(source);
            GuiControls.BeginCard(TrackingSourceInfo.IsHybrid(source)
                ? Loc.T("Tracking (iPhone + webcam)", "トラッキング（iPhone + Web カメラ）", "트래킹 (iPhone + 웹캠)",
                    "追踪（iPhone + 摄像头）", "追蹤（iPhone + 網路攝影機）")
                : external
                ? Loc.T("Tracking (external app)", "トラッキング（外部アプリ）", "트래킹 (외부 앱)",
                    "追踪（外部应用）", "追蹤（外部應用程式）")
                : Loc.T("Webcam tracking", "Web カメラトラッキング", "웹캠 트래킹", "摄像头追踪", "網路攝影機追蹤"));
            _settings.trackingEnabled = GUILayout.Toggle(
                _settings.trackingEnabled, Loc.T("Enable tracking", "トラッキングを有効にする", "트래킹 사용",
                    "启用追踪", "啟用追蹤"));

            // 無効時は ON/OFF だけ
            if (_settings.trackingEnabled)
            {
                DrawHold();
                DrawSource();

                // 外部アプリ（併用を含む）は、接続に必要な IP アドレス・ポートを入力元のすぐ下に出す（見落とさないように）
                TrackingSource network = TrackingSourceInfo.NetworkSourceOf(_settings.trackingSource);
                if (network == TrackingSource.Vmc)
                {
                    DrawVmcConnection();
                }
                else if (network == TrackingSource.IFacialMocap)
                {
                    DrawIFacialMocapConnection();
                }

                DrawSourceFeatures();
                DrawBlink();

                // 同梱トラッカーはカメラ・起動・ポートの設定
                if (!external)
                {
                    DrawLauncher();
                    DrawPort();
                }

                GuiControls.Hint(_tracker.Status);
            }

            GuiControls.EndCard();
        }

        private void DrawHold()
        {
            // 顔が映っていない間は手・体も含めて止める（既定 OFF。カメラは起動したまま）
            _settings.trackingStopWithoutFace = GUILayout.Toggle(
                _settings.trackingStopWithoutFace,
                Loc.T("Don't track unless a face is found", "顔が見つからないときはトラッキングしない",
                    "얼굴을 찾지 못하면 트래킹하지 않기", "找不到脸时不进行追踪", "找不到臉時不進行追蹤"));
            GuiControls.Hint(Loc.T(
                "While this is on, the head, tracked gaze, expressions, hands and body all return to rest when your face is not in frame. Look at camera keeps working. The camera stays on, and tracking resumes when your face returns. Off by default.",
                "ON の間は、顔が映っていないとき、頭・トラッキングの視線・表情・手・体をすべて止めて待機の姿勢に戻します。カメラ目線は続きます。カメラは起動したままなので、顔が戻ると再開します。既定は OFF です。",
                "켜져 있는 동안 얼굴이 화면에 없으면 머리·트래킹 시선·표정·손·몸을 모두 멈추고 대기 자세로 되돌립니다. 카메라 시선은 계속됩니다. 카메라는 켜 둔 채로 얼굴이 돌아오면 다시 시작합니다. 기본값은 꺼짐입니다.",
                "开启后，画面中没有脸时，头部、追踪的视线、表情、手和身体都会停止并回到待机姿势。看向镜头会继续。摄像头保持开启，脸回来后会继续追踪。默认关闭。",
                "開啟後，畫面中沒有臉時，頭部、追蹤的視線、表情、手和身體都會停止並回到待機姿勢。看向鏡頭會繼續。攝影機保持開啟，臉回來後會繼續追蹤。預設關閉。"));

            // 離席はパネル下部のボタンでも切り替える。止まっている理由を知らせる
            if (_settings.trackingAway)
            {
                GuiControls.Hint(Loc.T(
                    "Away: tracking is paused. Press Away again, or its shortcut, to resume.",
                    "離席中です。パネル下部の「離席」か、そのショートカットキーをもう一度押すと戻ります。",
                    "자리 비움 중입니다. 패널 아래의 「자리 비움」이나 단축키를 다시 누르면 돌아옵니다.",
                    "正在离开。再按一次面板底部的「离开」或其快捷键即可恢复。",
                    "正在離席。再按一次面板底部的「離席」或其快捷鍵即可恢復。"));
            }
            else if (_settings.trackingStopWithoutFace && (_tracker == null || !_tracker.TryGetFrame(out _)))
            {
                GuiControls.Hint(Loc.T(
                    "No face is in frame, so tracking is paused.",
                    "顔が見つからないため、トラッキングを止めています。",
                    "얼굴을 찾지 못해 트래킹을 멈추고 있습니다.",
                    "找不到脸，因此已暂停追踪。",
                    "找不到臉，因此已暫停追蹤。"));
            }
        }

        private void DrawSource()
        {
            // 入力元ごとのスイッチを縦に並べ、ON にしたものへ切り替える（変更するとトラッカー・受信が起動し直す）
            GUILayout.Label(Loc.T("Source", "入力元", "입력 소스", "输入源", "輸入來源"));

            // 見出しは行の幅いっぱいに広がるので、描画時にその幅を行の幅として測っておく
            if (Event.current.type == EventType.Repaint)
            {
                _sourceRowWidth = GUILayoutUtility.GetLastRect().width;
            }

            string[] labels =
            {
                Loc.T("MediaPipe (face + hands)", "MediaPipe（顔 + 手）", "MediaPipe (얼굴 + 손)",
                    "MediaPipe（面部 + 手）", "MediaPipe（臉部 + 手）"),
                Loc.T("OpenSeeFace (face only)", "OpenSeeFace（顔のみ）", "OpenSeeFace (얼굴만)",
                    "OpenSeeFace（仅面部）", "OpenSeeFace（僅臉部）"),
                Loc.T("iPhone / external app (VMC, face + full body)", "iPhone・外部アプリ（VMC、顔 + 全身）",
                    "iPhone·외부 앱 (VMC, 얼굴 + 전신)", "iPhone / 外部应用（VMC，面部 + 全身）",
                    "iPhone / 外部應用程式（VMC，臉部 + 全身）"),
                Loc.T("iFacialMocap (iPhone, face only)", "iFacialMocap（iPhone、顔のみ）",
                    "iFacialMocap (iPhone, 얼굴만)", "iFacialMocap（iPhone，仅面部）", "iFacialMocap（iPhone，僅臉部）"),
                Loc.T("iPhone (VMC) face + MediaPipe hands", "iPhone（VMC）の顔 + MediaPipe の手",
                    "iPhone (VMC) 얼굴 + MediaPipe 손", "iPhone（VMC）面部 + MediaPipe 手",
                    "iPhone（VMC）臉部 + MediaPipe 手"),
                Loc.T("iFacialMocap face + MediaPipe hands", "iFacialMocap の顔 + MediaPipe の手",
                    "iFacialMocap 얼굴 + MediaPipe 손", "iFacialMocap 面部 + MediaPipe 手",
                    "iFacialMocap 臉部 + MediaPipe 手"),
            };
            // 注記を横に並べるかは Layout のときだけ決める（Layout と Repaint で要素の数を変えない）
            if (Event.current.type == EventType.Layout)
            {
                _sourceNoteInline = SourceNotesFit(labels);
            }

            for (int i = 0; i < labels.Length; i++)
            {
                var source = (TrackingSource)i;
                GUILayout.BeginHorizontal();

                // 選択中を OFF にはできない（どれか 1 つは必ず ON）
                bool selected = _settings.trackingSource == source;
                if (GUILayout.Toggle(selected, labels[i]) && !selected)
                {
                    _settings.trackingSource = source;
                }

                // 収まるときはスイッチの右端に、収まらないときはスイッチの下に字下げして出す
                if (!_sourceNoteInline)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(SourceNoteIndent);
                }
                else
                {
                    GUILayout.FlexibleSpace();
                }

                DrawSourceNote(source);
                if (!_sourceNoteInline)
                {
                    GUILayout.FlexibleSpace();
                }

                GUILayout.EndHorizontal();
            }

            GuiControls.Hint(Loc.T(
                "PC load is a rough guide for this PC's CPU (MediaPipe: Eco lowers it, OpenSeeFace: Low load mode lowers it)",
                "PC 負荷はこの PC の CPU 負荷の目安です（MediaPipe はエコ、OpenSeeFace は軽量モードで下がります）",
                "PC 부하는 이 PC의 CPU 부하 기준입니다 (MediaPipe는 절약, OpenSeeFace는 저부하 모드에서 낮아집니다)",
                "电脑负载为本机 CPU 负载的参考（MediaPipe 在节能模式、OpenSeeFace 在低负载模式下会降低）",
                "電腦負載為本機 CPU 負載的參考（MediaPipe 在節能模式、OpenSeeFace 在低負載模式下會降低）"));
        }

        private bool SourceNotesFit(string[] labels)
        {
            // 未計測なら折り返す側にしておく（はみ出してタブ全体の幅が広がらないように）
            if (_sourceRowWidth <= 0f)
            {
                return false;
            }

            // どれか 1 行でも収まらなければ、すべての行で折り返す（行ごとに揃わない見た目にしない）
            for (int i = 0; i < labels.Length; i++)
            {
                var source = (TrackingSource)i;
                float width = WidthOf(GUI.skin.toggle, labels[i]) + SourceNoteGap;
                string tag = SourceTag(source, out GUIStyle tagStyle);
                if (tag != null)
                {
                    width += WidthOf(tagStyle, tag);
                }

                width += WidthOf(LoadStyle, LoadText(source));
                if (width > _sourceRowWidth)
                {
                    return false;
                }
            }

            return true;
        }

        private void DrawSourceNote(TrackingSource source)
        {
            // 推奨（緑の太字）と PC 負荷の目安
            string tag = SourceTag(source, out GUIStyle tagStyle);
            if (tag != null)
            {
                GUILayout.Label(tag, tagStyle);
            }

            GUILayout.Label(LoadText(source), LoadStyle);
        }

        private static string SourceTag(TrackingSource source, out GUIStyle style)
        {
            // MediaPipe は推奨、それ以外は表記なし
            if (source == TrackingSource.MediaPipe)
            {
                style = UiTheme.Current != null ? UiTheme.Current.Success : GUI.skin.label;
                return Loc.T("Recommended", "推奨", "권장", "推荐", "推薦");
            }

            style = null;
            return null;
        }

        private static GUIStyle LoadStyle => UiTheme.Current != null ? UiTheme.Current.Value : GUI.skin.label;

        private string LoadText(TrackingSource source)
        {
            // PC 負荷の目安（スマートフォンから受信する入力元は推定をスマートフォン側で行うため軽い）
            return Loc.T("PC load", "PC 負荷", "PC 부하", "电脑负载", "電腦負載") + ": " + DescribeLoad(source);
        }

        private static float WidthOf(GUIStyle style, string text)
        {
            // 1 行で描いたときの幅（余白を含む）
            return style.CalcSize(new GUIContent(text)).x + style.margin.horizontal;
        }

        private string DescribeLoad(TrackingSource source)
        {
            switch (source)
            {
                // MediaPipe（併用を含む）はエコ・腕と手の推定を止めるとそれぞれ 1 段軽くなる
                case TrackingSource.MediaPipe:
                case TrackingSource.MediaPipeVmc:
                case TrackingSource.MediaPipeIFacialMocap:
                    int level = (_settings.trackingHands ? 1 : 0) + (_settings.trackerMode == TrackerMode.Eco ? 0 : 1);
                    // ぬるぬるは手を毎フレーム推定するので、手を使うときだけさらに重い
                    if (level >= 2 && _settings.trackerMode == TrackerMode.Fluid)
                    {
                        return Loc.T("Very high", "とても高", "매우 높음", "很高", "很高");
                    }

                    return level >= 2
                        ? Loc.T("High", "高", "높음", "高", "高")
                        : level == 1
                            ? Loc.T("Medium", "中", "보통", "中", "中")
                            : Loc.T("Low", "低", "낮음", "低", "低");
                case TrackingSource.OpenSeeFace:
                    return Loc.T("Medium", "中", "보통", "中", "中");
                default:
                    return Loc.T("Low", "低", "낮음", "低", "低");
            }
        }

        private void DrawSourceFeatures()
        {
            // 併用では、顔と腕・手をどちらから受け取るかを伝える
            if (TrackingSourceInfo.IsHybrid(_settings.trackingSource))
            {
                GuiControls.Hint(Loc.T(
                    "The face comes from the iPhone and the arms / hands from the webcam (MediaPipe). While nothing arrives from the iPhone, the webcam's face is used instead.",
                    "顔は iPhone、腕・手は Web カメラ（MediaPipe）から受け取ります。iPhone から届かない間は Web カメラの顔を使います。",
                    "얼굴은 iPhone, 팔·손은 웹캠 (MediaPipe)에서 받습니다. iPhone에서 데이터가 오지 않는 동안에는 웹캠의 얼굴을 사용합니다.",
                    "面部来自 iPhone，手臂和手来自摄像头（MediaPipe）。iPhone 没有数据时改用摄像头的面部。",
                    "臉部來自 iPhone，手臂和手來自網路攝影機（MediaPipe）。iPhone 沒有資料時改用網路攝影機的臉部。"));
            }

            DrawSmoothing();

            // トラッカーの動作は同梱の MediaPipe 版（併用を含む）のみ
            if (TrackingSourceInfo.BundledTrackerOf(_settings.trackingSource) == TrackingSource.MediaPipe)
            {
                DrawTrackerMode();
            }

            // 腕・手は受信できる入力元（MediaPipe）のみ
            if (TrackingSourceInfo.HasArms(_settings.trackingSource))
            {
                DrawHands();
            }

            // 全身は受信できる入力元（VMC）のみ
            if (TrackingSourceInfo.HasFullBody(_settings.trackingSource))
            {
                DrawFullBody();
            }

            // パーフェクトシンク・表情の反映は ARKit の値を受信できる入力元（MediaPipe・VMC・iFacialMocap）のみ
            if (TrackingSourceInfo.HasArKit(_settings.trackingSource))
            {
                DrawPerfectSync();
                DrawExpressions();
            }
        }

        private void DrawSmoothing()
        {
            // 低い順（MotionSmoothing の数値の順）。変更すると MediaPipe 版はトラッカーを起動し直す（揺れを抑えるフィルターの強さが変わる）
            string[] labels =
            {
                Loc.T("Lowest", "最低", "최저", "最低", "最低"),
                Loc.T("Low", "低", "낮음", "低", "低"),
                Loc.T("Medium", "中", "보통", "中", "中"),
                Loc.T("High", "高", "높음", "高", "高"),
                Loc.T("Highest", "最高", "최고", "最高", "最高"),
            };
            int selected = GuiControls.EnumSelector(
                Loc.T("Motion smoothing", "動きのなめらかさ", "움직임 부드러움", "动作顺滑度", "動作順滑度"),
                labels, (int)_settings.trackingSmoothing);
            _settings.trackingSmoothing = (MotionSmoothing)Mathf.Clamp(selected, 0, labels.Length - 1);
            GuiControls.Hint(_settings.trackingSmoothing == MotionSmoothing.Lowest
                ? Loc.T(
                    "Lowest: the avatar follows the camera with the least delay. Raise it if the motion looks shaky.",
                    "最低: カメラに一番遅れずに追従します。動きが震えて見えるときは上げてください。",
                    "최저: 카메라를 가장 늦지 않게 따라갑니다. 움직임이 떨려 보이면 올리세요.",
                    "最低：以最小的延迟跟随摄像头。动作看起来抖动时请调高。",
                    "最低：以最小的延遲跟隨攝影機。動作看起來抖動時請調高。")
                : Loc.T(
                    "Higher settings smooth out shaking and steps in the head, arms, hands and expressions for more fluid motion, but movements lag further behind the camera.",
                    "高いほど頭・腕・手・表情の揺れや段差を抑えてぬるぬる動きますが、そのぶん動きがカメラより遅れます。",
                    "높을수록 머리·팔·손·표정의 흔들림과 끊김을 억제해 부드럽게 움직이지만, 그만큼 카메라보다 늦어집니다.",
                    "越高越能抑制头部、手臂、手和表情的抖动与卡顿，动作更顺滑，但会比摄像头延迟更多。",
                    "越高越能抑制頭部、手臂、手和表情的抖動與卡頓，動作更順滑，但會比攝影機延遲更多。"));
        }

        private void DrawTrackerMode()
        {
            // 重い順に並べる（変更すると TrackerProcess がトラッカーを起動し直す）
            string[] labels =
            {
                Loc.T("Fluid (high load)", "ぬるぬる（高負荷）", "매끄럽게 (고부하)", "丝滑（高负载）", "絲滑（高負載）"),
                Loc.T("Smooth", "なめらか", "부드럽게", "流畅", "流暢"),
                Loc.T("Eco", "エコ", "절약", "节能", "節能"),
            };
            int selected = GuiControls.EnumSelector(
                Loc.T("Tracker mode", "トラッカーの動作", "트래커 동작", "追踪器模式", "追蹤器模式"),
                labels, Array.IndexOf(TrackerModeOrder, _settings.trackerMode));
            _settings.trackerMode = TrackerModeOrder[Mathf.Clamp(selected, 0, TrackerModeOrder.Length - 1)];
            GuiControls.Hint(_settings.trackerMode == TrackerMode.Fluid
                ? Loc.T(
                    "Fluid: face, arms and hands are all tracked every frame, so arms and fingers follow the camera with the least delay. Uses more CPU.",
                    "ぬるぬる: 顔・腕・手をすべて毎フレーム推定し、腕や指がカメラに一番遅れずに追従します。CPU 負荷は上がります。",
                    "매끄럽게: 얼굴·팔·손을 모두 매 프레임 추정해, 팔과 손가락이 카메라를 가장 늦지 않게 따라갑니다. CPU 부하가 늘어납니다.",
                    "丝滑：每帧估计面部、手臂和手，手臂和手指以最小的延迟跟随摄像头。CPU 负载会增加。",
                    "絲滑：每格估計臉部、手臂和手，手臂和手指以最小的延遲跟隨攝影機。CPU 負載會增加。")
                : _settings.trackerMode == TrackerMode.Eco
                ? Loc.T(
                    "Eco: up to 20 fps, arms and hands update every other frame. About 2/3 of the PC load of Smooth.",
                    "エコ: 最大 20fps。腕・手は 2 フレームに 1 回の更新で、PC 負荷は「なめらか」の 3 分の 2 ほどです。",
                    "절약: 최대 20fps. 팔·손은 2프레임에 1번 갱신하며, PC 부하는 「부드럽게」의 3분의 2 정도입니다.",
                    "节能：最高 20fps，手臂和手每 2 帧更新 1 次，电脑负载约为“流畅”的三分之二。",
                    "節能：最高 20fps，手臂和手每 2 格更新 1 次，電腦負載約為「流暢」的三分之二。")
                : Loc.T(
                    "Smooth: face and expressions follow at the camera's frame rate (up to 30 fps). Arms and hands update every other frame.",
                    "なめらか: 顔・表情をカメラのフレームレート（最大 30fps）で追従します。腕・手は 2 フレームに 1 回の更新です。",
                    "부드럽게: 얼굴·표정을 카메라 프레임 레이트(최대 30fps)로 따라갑니다. 팔·손은 2프레임에 1번 갱신합니다.",
                    "流畅：面部和表情以摄像头帧率（最高 30fps）跟随。手臂和手每 2 帧更新 1 次。",
                    "流暢：臉部和表情以攝影機影格率（最高 30fps）跟隨。手臂和手每 2 格更新 1 次。"));
        }

        private void DrawHands()
        {
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

            // 腕の位置合わせと上半身の向き（両肩の線）は腕・手の体の推定を使うため、腕・手が ON のときだけ選べる
            if (_settings.trackingHands)
            {
                DrawArmMatch();
                DrawTorso();
            }
        }

        private void DrawArmMatch()
        {
            _settings.trackingArmMatchAvatar = GUILayout.Toggle(
                _settings.trackingArmMatchAvatar,
                Loc.T("Match hand positions to the avatar", "手の位置をアバターに合わせる", "손 위치를 아바타에 맞추기",
                    "让手的位置匹配虚拟形象", "讓手的位置符合虛擬形象"));

            // ON の間だけ、何をするかを案内する
            if (!_settings.trackingArmMatchAvatar)
            {
                return;
            }

            GuiControls.Hint(Loc.T(
                "Places the hands by the avatar's body size instead of only copying arm angles. A hand in front of your face stays in front of the avatar's face. Turn off for the previous behavior.",
                "腕の角度をまねるだけでなく、アバターの体格に合わせて手の位置を決めます。顔の前の手はアバターの顔の前に来ます。OFF で以前の動きに戻ります。",
                "팔의 각도만 따라 하지 않고 아바타의 체격에 맞춰 손 위치를 정합니다. 얼굴 앞의 손은 아바타의 얼굴 앞에 옵니다. 끄면 이전 동작으로 돌아갑니다.",
                "不只是模仿手臂角度，而是按虚拟形象的体型决定手的位置。脸前的手会位于虚拟形象的脸前。关闭则恢复以前的动作。",
                "不只是模仿手臂角度，而是依虛擬形象的體型決定手的位置。臉前的手會位於虛擬形象的臉前。關閉則恢復以前的動作。"));

            _settings.trackingKeepHandsInFront = GUILayout.Toggle(
                _settings.trackingKeepHandsInFront,
                Loc.T("Keep a hand in front of the face", "顔の前の手を後ろに入れない", "얼굴 앞의 손을 뒤로 넣지 않기",
                    "不让脸前的手穿到脸后", "不讓臉前的手穿到臉後"));
            if (_settings.trackingKeepHandsInFront)
            {
                GuiControls.Hint(Loc.T(
                    "A peace sign or similar pose in front of the face stays in front, even when the depth estimate puts the fingers behind the face. Hands beside the head stay where they are.",
                    "顔の前のピースなどが、奥行きの推定のずれで指が顔の後ろへ入るのを抑えます。頭の横の手はそのままです。",
                    "얼굴 앞에서 피스 등을 할 때, 깊이 추정이 어긋나 손가락이 얼굴 뒤로 들어가는 것을 줄입니다. 머리 옆의 손은 그대로입니다.",
                    "在脸前比耶等姿势时，减轻深度估计偏差导致手指穿到脸后的情况。头侧面的手保持原样。",
                    "在臉前比耶等姿勢時，減輕深度估計偏差導致手指穿到臉後的情況。頭側面的手保持原樣。"));
            }
        }

        private void DrawTorso()
        {
            _settings.trackingTorso = GUILayout.Toggle(
                _settings.trackingTorso,
                Loc.T("Upper body twist / tilt (shoulders)", "上半身のひねり・傾き（肩）", "상체 비틀기·기울기 (어깨)",
                    "上半身扭转 / 倾斜（肩部）", "上半身扭轉 / 傾斜（肩部）"));

            // 無効時は状態を出さない
            if (!_settings.trackingTorso)
            {
                return;
            }

            // ひねりだけを正面に固定する（左右の傾きは肩に合わせたまま）
            _settings.trackingTorsoLockTwist = GUILayout.Toggle(
                _settings.trackingTorsoLockTwist,
                Loc.T("Lock upper body twist", "上半身のひねりを固定", "상체 비틀기 고정", "固定上半身扭转", "固定上半身扭轉"));

            // 両肩が映っていれば肩の線で動かし、映っていない間は頭の位置による傾きのまま
            var face = _avatar.Get<FaceTrackingDriver>();
            if (face != null && face.IsTracking)
            {
                GuiControls.Hint(face.IsTrackingTorso
                    ? Loc.T("Upper body: following your shoulders", "上半身: 肩に合わせて動かしています",
                        "상체: 어깨에 맞춰 움직이는 중", "上半身：正在跟随肩部", "上半身：正在跟隨肩部")
                    : Loc.T("Upper body: shoulders not visible (uses head position)",
                        "上半身: 肩が映っていません（頭の位置で傾けます）",
                        "상체: 어깨가 보이지 않음 (머리 위치로 기울임)",
                        "上半身：未拍到肩部（按头部位置倾斜）", "上半身：未拍到肩部（依頭部位置傾斜）"));
            }
        }

        private void DrawFullBody()
        {
            _settings.trackingVmcBody = GUILayout.Toggle(
                _settings.trackingVmcBody,
                Loc.T("Full body (from the sender's avatar)", "全身（送信側アバターの動き）", "전신 (전송 측 아바타의 움직임)",
                    "全身（发送端虚拟形象的动作）", "全身（傳送端虛擬形象的動作）"));
            GuiControls.Hint(Loc.T(
                "Moves the hips, legs, arms, fingers and whole-body position like the avatar in the sender app (e.g. VirtualMotionCapture). Face-only apps keep the idle pose.",
                "送信側アプリ（VirtualMotionCapture など）のアバターと同じように、腰・脚・腕・指と体全体の位置を動かします。顔だけを送るアプリでは待機ポーズのままです。",
                "전송 측 앱 (VirtualMotionCapture 등)의 아바타처럼 허리·다리·팔·손가락과 몸 전체 위치를 움직입니다. 얼굴만 보내는 앱에서는 대기 포즈 그대로입니다.",
                "像发送端应用（如 VirtualMotionCapture）中的虚拟形象一样，移动腰部、腿、手臂、手指和整体位置。仅发送面部的应用会保持待机姿势。",
                "像傳送端應用程式（如 VirtualMotionCapture）中的虛擬形象一樣，移動腰部、腿、手臂、手指和整體位置。僅傳送臉部的應用程式會保持待機姿勢。"));

            // 無効時は足の接地・状態を出さない
            if (!_settings.trackingVmcBody)
            {
                return;
            }

            // 足の接地（送信側とアバターの脚の比率の違いで足が浮く・めり込むのを抑える）
            _settings.trackingVmcGroundFeet = GUILayout.Toggle(
                _settings.trackingVmcGroundFeet,
                Loc.T("Keep feet on the floor", "足を床に合わせる", "발을 바닥에 맞추기", "脚部贴合地面", "腳部貼合地面"));
            GuiControls.Hint(Loc.T(
                "Bends the knees so the feet follow the sender's feet without floating or sinking. Stand straight facing the camera and press Reset > Head to set where the feet rest.",
                "膝の曲げで足を送信側の足の位置へ合わせ、浮いたりめり込んだりしないようにします。まっすぐ立ってカメラ側を向き、リセットの「顔の向き」を押すと足の基準がそろいます。",
                "무릎을 굽혀 발을 전송 측 발 위치에 맞추고, 뜨거나 파묻히지 않게 합니다. 똑바로 서서 카메라 쪽을 보고 초기화의 \"얼굴 방향\"을 누르면 발의 기준이 맞춰집니다.",
                "通过弯曲膝盖让脚跟随发送端的脚部位置，避免悬空或陷入地面。站直面向相机，点击重置中的“头部朝向”即可对齐脚的基准。",
                "透過彎曲膝蓋讓腳跟隨傳送端的腳部位置，避免懸空或陷入地面。站直面向相機，點擊重設中的「頭部朝向」即可對齊腳的基準。"));

            // アバターが無ければ状態を出さない
            var driver = _avatar.Get<FullBodyTrackingDriver>();
            if (driver == null)
            {
                return;
            }

            // 対象外のアバター・受信中（受け取ったボーンの数）・全身が届いていない
            if (!driver.IsAvailable)
            {
                GuiControls.Hint(Loc.T("Full body: not available for this avatar", "全身: このアバターでは使えません",
                    "전신: 이 아바타에서는 사용할 수 없음", "全身：此虚拟形象不可用", "全身：此虛擬形象無法使用"));
            }
            else if (driver.IsTracking)
            {
                GuiControls.Hint(Loc.T("Full body: tracking", "全身: トラッキング中", "전신: 트래킹 중", "全身：追踪中",
                    "全身：追蹤中") + $" ({driver.ReceivedBones} " + Loc.T("bones", "ボーン", "본", "骨骼", "骨骼") + ")");
            }
            else
            {
                GuiControls.Hint(Loc.T("Full body: no body data (idle pose)", "全身: 体のデータが届いていません（待機ポーズ）",
                    "전신: 몸 데이터가 오지 않음 (대기 포즈)", "全身：未收到身体数据（待机姿势）", "全身：未收到身體資料（待機姿勢）"));
            }
        }

        private void DrawVmcConnection()
        {
            // 送信側アプリ（Waidayo・VirtualMotionCapture 等）の設定手順
            GuiControls.Hint(Loc.T(
                "In the sender app (e.g. Waidayo on your phone, VirtualMotionCapture on a PC), set the VMC protocol destination to one of these addresses and the port below. Use the same network as this PC (127.0.0.1 if the app runs on this PC).",
                "送信側アプリ（スマートフォンの Waidayo、PC の VirtualMotionCapture など）で、VMC プロトコルの送信先に下のどれかの IP アドレスとポートを入力してください。この PC と同じネットワークにつないでください（この PC で動かすアプリなら 127.0.0.1）。",
                "전송 측 앱 (스마트폰의 Waidayo, PC의 VirtualMotionCapture 등)에서 VMC 프로토콜 전송 대상에 아래 IP 주소 중 하나와 포트를 입력하세요. 이 PC와 같은 네트워크에 연결하세요 (이 PC에서 실행하는 앱이면 127.0.0.1).",
                "在发送端应用（如手机上的 Waidayo、电脑上的 VirtualMotionCapture）中，将 VMC 协议的发送目标设为下方任一 IP 地址和端口。请连接与此电脑相同的网络（在此电脑上运行的应用请用 127.0.0.1）。",
                "在傳送端應用程式（如手機上的 Waidayo、電腦上的 VirtualMotionCapture）中，將 VMC 協定的傳送目標設為下方任一 IP 位址和連接埠。請連接與此電腦相同的網路（在此電腦上執行的應用程式請用 127.0.0.1）。"));

            // この PC の IP アドレス（間隔を空けて取り直す。Wi-Fi の切り替え後にも追従）
            if (Time.unscaledTime >= _nextAddressRefresh)
            {
                _localAddresses = LocalNetwork.GetIPv4Addresses();
                _nextAddressRefresh = Time.unscaledTime + AddressRefreshSeconds;
            }

            // 見出しの下に 1 行ずつ、目立つ太字とコピーボタンで出す（見つからなければ ipconfig を案内）
            GuiControls.SubHeading(Loc.T("This PC's IP address (destination)", "この PC の IP アドレス（送信先）",
                "이 PC의 IP 주소 (전송 대상)", "此电脑的 IP 地址（发送目标）", "此電腦的 IP 位址（傳送目標）"));
            if (_localAddresses.Count == 0)
            {
                GuiControls.Hint(Loc.T("Not found (check with ipconfig)", "見つかりません（ipconfig で確認）",
                    "찾을 수 없음 (ipconfig로 확인)", "未找到（请用 ipconfig 确认）", "找不到（請用 ipconfig 確認）"));
            }

            UiTheme theme = UiTheme.Current;
            foreach (string address in _localAddresses)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(address, theme != null ? theme.Title : GUI.skin.label, GuiControls.Shrinkable);
                GuiControls.CopyButton(address);
                GUILayout.EndHorizontal();
            }

            // VMC の受信ポート（確定するまで設定へ反映しない）
            DrawPortApply(Loc.T("UDP port", "UDP ポート", "UDP 포트", "UDP 端口", "UDP 連接埠"),
                ref _vmcPortInput, ref _settings.vmcPort);

            DrawFirewallHint();
        }

        private void DrawIFacialMocapConnection()
        {
            // iFacialMocap の接続手順（こちらから iPhone へ送信開始の合図を送るため、iPhone の IP アドレスが要る）
            GuiControls.Hint(Loc.T(
                "Open iFacialMocap on your iPhone and enter the IP address shown at the top of its screen below. Use the same Wi-Fi as this PC.",
                "iPhone で iFacialMocap を開き、画面上部に表示される IP アドレスを下に入力してください。この PC と同じ Wi-Fi につないでください。",
                "iPhone에서 iFacialMocap을 열고 화면 위쪽에 표시되는 IP 주소를 아래에 입력하세요. 이 PC와 같은 Wi-Fi에 연결하세요.",
                "在 iPhone 上打开 iFacialMocap，并在下方输入其屏幕上方显示的 IP 地址。请连接与此电脑相同的 Wi-Fi。",
                "在 iPhone 上開啟 iFacialMocap，並在下方輸入其畫面上方顯示的 IP 位址。請連接與此電腦相同的 Wi-Fi。"));

            // iPhone の IP アドレス（前後の空白は除く。送信開始の合図は受信側が設定から読む）
            _settings.iFacialMocapAddress = GuiControls.TextField(
                Loc.T("iPhone IP address", "iPhone の IP アドレス", "iPhone IP 주소", "iPhone 的 IP 地址", "iPhone 的 IP 位址"),
                _settings.iFacialMocapAddress, MaxAddressLength).Trim();

            // 入力済みで形式が違えば知らせる（未入力は案内文で足りる）
            if (_settings.iFacialMocapAddress.Length > 0
                && !System.Net.IPAddress.TryParse(_settings.iFacialMocapAddress, out _))
            {
                GuiControls.Hint(Loc.T("Not a valid IP address (e.g. 192.168.1.20)", "IP アドレスの形式ではありません（例: 192.168.1.20）",
                    "IP 주소 형식이 아닙니다 (예: 192.168.1.20)", "不是有效的 IP 地址（例：192.168.1.20）",
                    "不是有效的 IP 位址（例：192.168.1.20）"));
            }

            // ポートはアプリ側で固定
            GuiControls.Hint(Loc.T("UDP port", "UDP ポート", "UDP 포트", "UDP 端口", "UDP 連接埠") + ": "
                + AppSettings.IFacialMocapPort);
            DrawFirewallHint();
        }

        private void DrawFirewallHint()
        {
            // 届かないときの主な原因（初回・更新後のファイアウォール）
            GuiControls.Hint(Loc.T(
                "If nothing arrives, allow VRCast on private networks in Windows Firewall (button below; also after an update) and set your Wi-Fi to a private network.",
                "届かないときは、下のボタンで Windows ファイアウォールのプライベートネットワーク向け許可を付け直してください（更新後も同じ）。Wi-Fi はプライベートネットワークにしてください。",
                "데이터가 오지 않으면 아래 버튼으로 Windows 방화벽의 개인 네트워크 허용을 다시 설정하세요 (업데이트 후에도 동일). Wi-Fi는 개인 네트워크로 설정하세요.",
                "如果收不到数据，请用下方按钮重新允许 VRCast 通过 Windows 防火墙的专用网络（更新后也一样），并将 Wi-Fi 设为专用网络。",
                "如果收不到資料，請用下方按鈕重新允許 VRCast 通過 Windows 防火牆的私人網路（更新後也一樣），並將 Wi-Fi 設為私人網路。"));

            PollFirewall();

            GUI.enabled = _firewallPending == null;
            if (GUILayout.Button(Loc.T("Allow in Windows Firewall", "Windows ファイアウォールで許可",
                    "Windows 방화벽에서 허용", "在 Windows 防火墙中允许", "在 Windows 防火牆中允許"),
                    GuiControls.Shrinkable))
            {
                _firewallPending = WindowsFirewall.AllowUdpInboundAsync();
                _firewallHasResult = false;
                _firewallError = null;
            }

            GUI.enabled = true;

            if (_firewallHasResult)
            {
                if (_firewallError == null)
                {
                    GuiControls.Status(Loc.T("Firewall rule updated", "ファイアウォールの許可を更新しました",
                        "방화벽 허용이 업데이트됨", "已更新防火墙规则", "已更新防火牆規則"), true);
                }
                else if (_firewallError == "Cancelled")
                {
                    GuiControls.Hint(Loc.T("Cancelled (administrator permission was denied)",
                        "キャンセルされました（管理者権限が拒否されました）",
                        "취소됨 (관리자 권한이 거부됨)", "已取消（管理员权限被拒绝）", "已取消（管理員權限被拒絕）"));
                }
                else
                {
                    GuiControls.Status(Loc.T("Could not update the firewall rule", "ファイアウォールの許可を更新できませんでした",
                        "방화벽 허용을 업데이트할 수 없음", "无法更新防火墙规则", "無法更新防火牆規則")
                        + ": " + _firewallError, false);
                }
            }
        }

        private void PollFirewall()
        {
            // 別スレッドの結果が来たら表示用に取り込む
            if (_firewallPending == null || !_firewallPending.IsCompleted)
            {
                return;
            }

            _firewallError = _firewallPending.IsFaulted
                ? _firewallPending.Exception?.GetBaseException().Message ?? "Unknown error"
                : _firewallPending.Result;
            _firewallHasResult = true;
            _firewallPending = null;
        }

        private static void DrawPortApply(string label, ref string input, ref int port)
        {
            // 入力中は設定へ書かず、確定で範囲内なら反映する
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(130f));
            input = GUILayout.TextField(input, 5, GuiControls.Shrinkable);
            bool valid = int.TryParse(input, out int parsed)
                && parsed >= AppSettings.MinTrackingPort && parsed <= AppSettings.MaxTrackingPort;
            bool changed = valid && parsed != port;
            GUI.enabled = changed;
            if (GUILayout.Button(Loc.T("Apply", "確定", "적용", "确定", "確定"), GUILayout.Width(70f)) && changed)
            {
                port = parsed;
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawBlink()
        {
            // まばたきのトラッキングの ON/OFF（どの入力元でも使う。OFF の間は顔・表情タブの自動まばたきに任せる）
            _settings.trackingBlink = GUILayout.Toggle(
                _settings.trackingBlink,
                Loc.T("Track blinks", "まばたきをトラッキング", "눈 깜빡임 트래킹", "追踪眨眼", "追蹤眨眼"));

            // OFF のときは自動まばたきになることを伝える
            if (!_settings.trackingBlink)
            {
                GuiControls.Hint(Loc.T("Uses auto blink (Face & expressions tab) instead",
                    "代わりに自動まばたき（顔・表情タブ）を使います", "대신 자동 눈 깜빡임 (얼굴·표정 탭)을 사용합니다",
                    "改用自动眨眼（面部·表情标签页）", "改用自動眨眼（臉部·表情分頁）"));
                return;
            }

            // 目が細く映って半目になる人向けに、この閉じ具合までは開いた目とみなす
            _settings.trackingEyeOpenOffset = GuiControls.Slider(
                Loc.T("Eye open correction", "目の開きの補正", "눈 뜨기 보정", "睁眼校正", "睜眼校正"),
                _settings.trackingEyeOpenOffset, 0f, AppSettings.MaxTrackingEyeOpenOffset);
            GuiControls.Hint(Loc.T(
                "Raise this if the avatar's eyes look half-closed while yours are open. Blinks still close the eyes fully, and \"Half-closed eyes\" expression detection also uses this.",
                "目を開けているのにアバターが半目になるときに上げます。まばたきでは閉じ切ります。ジト目の判定にも効きます。",
                "눈을 뜨고 있는데 아바타가 반쯤 감은 눈이 될 때 올립니다. 깜빡임에서는 완전히 감깁니다. 실눈 판정에도 적용됩니다.",
                "睁着眼时虚拟形象却半闭眼时调高。眨眼时仍会完全闭上。也会用于眯眼的判定。",
                "睜著眼時虛擬形象卻半閉眼時調高。眨眼時仍會完全閉上。也會用於瞇眼的判定。"));

            // まばたきの左右をそろえる（検出のぶれ・横を向いたときに片目だけ閉じるのを防ぐ）
            string[] syncLabels =
            {
                Loc.T("Off", "なし", "끄기", "关闭", "關閉"),
                Loc.T("Always", "常に", "항상", "始终", "始終"),
                Loc.T("When turned", "横を向いたときだけ", "옆을 볼 때만", "仅侧脸时", "僅側臉時"),
            };
            _settings.trackingBlinkSync = (BlinkSyncMode)GuiControls.EnumSelector(
                Loc.T("Match both eyes", "左右の目をそろえる", "양쪽 눈 맞추기", "双眼同步", "雙眼同步"),
                syncLabels, (int)_settings.trackingBlinkSync);
            GuiControls.Hint(Loc.T(
                "Keeps both eyes equally open so they don't wink by mistake. You can still blink, but not wink.",
                "左右の目の開き具合をそろえ、誤ってウインクにならないようにします。まばたきはできますが、ウインクはできなくなります。",
                "양쪽 눈이 뜨인 정도를 맞춰 실수로 윙크가 되지 않게 합니다. 눈 깜빡임은 되지만 윙크는 할 수 없습니다.",
                "使双眼的睁开程度一致，避免误判为眨单眼。仍可眨眼，但无法眨单眼。",
                "使雙眼的睜開程度一致，避免誤判為眨單眼。仍可眨眼，但無法眨單眼。"));
        }

        private void DrawPerfectSync()
        {
            // パーフェクトシンクの ON/OFF（MediaPipe・VMC・iFacialMocap のみ。対応していないアバターでは何も起きない）
            _settings.trackingPerfectSync = GUILayout.Toggle(
                _settings.trackingPerfectSync,
                Loc.T("Perfect sync", "パーフェクトシンク", "퍼펙트 싱크", "完美同步", "完美同步"));

            // 無効時は条件・状態を出さない
            if (!_settings.trackingPerfectSync)
            {
                return;
            }

            // 有効にする条件（規定数以上 / 1 種類でも）。排他で選ぶ。変わると ARKit / UE の自動選択も変わる
            int min = PerfectSyncBlendShapes.MinMatchedShapes;
            string[] labels =
            {
                Loc.T($"{min}+ shapes", $"{min} 種類以上", $"{min}종 이상", $"{min} 种以上", $"{min} 種以上"),
                Loc.T("Any shape", "1 種類でも", "1종이라도", "有 1 种即可", "有 1 種即可"),
            };
            bool anyShape = _settings.trackingPerfectSyncAnyShape;
            _settings.trackingPerfectSyncAnyShape = GuiControls.EnumSelector(
                Loc.T("Enable when", "有効にする条件", "활성화 조건", "启用条件", "啟用條件"),
                labels, anyShape ? 1 : 0) == 1;

            // 口の開きの補正と口・頬の強調（アバター未表示でも設定はできる）
            var driver = _avatar.Get<FaceTrackingDriver>();
            if (driver != null && anyShape != _settings.trackingPerfectSyncAnyShape)
            {
                driver.RefreshPerfectSyncNaming();
            }

            PerfectSyncBoostControls.Draw(_settings, driver);

            // アバター未表示なら状態を出さない
            if (driver == null)
            {
                return;
            }

            // 対応状況（選ばれた名前系統と見つかった種類数）
            int count = driver.PerfectSyncShapeCount;
            int total = ArKitFace.BlendShapeNames.Length;
            int required = _settings.trackingPerfectSyncAnyShape ? 1 : min;
            string naming = driver.ActivePerfectSyncNaming == PerfectSyncNaming.Unified
                ? "Unified Expressions"
                : "ARKit";
            string namingJa = driver.ActivePerfectSyncNaming == PerfectSyncNaming.Unified
                ? "Unified Expressions"
                : "ARKit";
            GuiControls.Hint(driver.SupportsPerfectSync
                ? Loc.T($"Supported ({naming} {count}/{total})",
                    $"対応アバター（{namingJa} {count}/{total}）",
                    $"지원 아바타 ({naming} {count}/{total})",
                    $"支持的虚拟形象（{naming} {count}/{total}）",
                    $"支援的虛擬形象（{naming} {count}/{total}）")
                : Loc.T($"This avatar does not support it ({naming} {count}/{total}, needs {required}+)",
                    $"このアバターは非対応です（{namingJa} {count}/{total}、{required} 個以上で対応）",
                    $"이 아바타는 지원하지 않습니다 ({naming} {count}/{total}, {required}개 이상 필요)",
                    $"此虚拟形象不支持（{naming} {count}/{total}，需要 {required} 个以上）",
                    $"此虛擬形象不支援（{naming} {count}/{total}，需要 {required} 個以上）"));

            // ARKit / Unified Expressions の自動選択を伝える
            GuiControls.Hint(Loc.T(
                "Picks ARKit or Unified Expressions names automatically (whichever matches more)",
                "ARKit 名と Unified Expressions 名の多い方を自動で使います",
                "ARKit 이름과 Unified Expressions 이름 중 더 많이 맞는 쪽을 자동으로 사용합니다",
                "自动选用匹配更多的 ARKit 或 Unified Expressions 名称",
                "自動選用匹配較多的 ARKit 或 Unified Expressions 名稱"));

            // 規定数未満で動かしている間は、表情プリセットも併用されることを伝える
            if (driver.SupportsPerfectSync && count < min)
            {
                GuiControls.Hint(Loc.T(
                    $"Fewer than {min}: only the found shapes move, and expressions stay on",
                    $"{min} 個未満のため、見つかった BlendShape だけを動かし、表情の反映も併用します",
                    $"{min}개 미만이므로 찾은 BlendShape만 움직이고 표정 반영도 함께 사용합니다",
                    $"少于 {min} 个，只驱动找到的 BlendShape，并同时使用表情反映",
                    $"少於 {min} 個，只驅動找到的 BlendShape，並同時使用表情反映"));
            }
        }

        private void DrawExpressions()
        {
            // 表情反映の ON/OFF（MediaPipe・VMC・iFacialMocap のみ。OFF にすると自動で当てた表情はすぐ戻る）
            _settings.trackingExpressions = GUILayout.Toggle(
                _settings.trackingExpressions,
                Loc.T("Facial expressions", "表情を反映", "표정 반영", "反映表情", "反映表情"));

            // 無効時は詳細設定を出さない
            if (!_settings.trackingExpressions)
            {
                return;
            }

            DrawExpressionMappings();

            // 本人の顔に合わせた表情のキャリブレーション（アバターが無くても登録できる）
            _calibration.Draw(_tracker, _settings);
        }

        private void DrawExpressionMappings()
        {
            // パーフェクトシンク中は表情プリセットへ切り替えない（顔の動きで表情が出るため）
            var face = _avatar.Get<FaceTrackingDriver>();
            if (face != null && face.PausesExpressions)
            {
                GuiControls.Hint(Loc.T("Paused during perfect sync", "パーフェクトシンク中は止まります",
                    "퍼펙트 싱크 중에는 멈춥니다", "完美同步期间暂停", "完美同步期間暫停"));
            }

            // 表情データが無いアバターは割り当てできない
            var expressions = _avatar.Get<ExpressionController>();
            if (_avatar.HasAvatar && (expressions == null || expressions.Names.Count == 0))
            {
                GuiControls.Hint(Loc.T("No expressions in this avatar", "このアバターには表情データがありません",
                    "이 아바타에는 표정 데이터가 없습니다", "此虚拟形象没有表情数据", "此虛擬形象沒有表情資料"));
                return;
            }

            // 強さは 0〜1。しきい値はその強さで切り替わる目安（1 より大きいと届かないので切り替わらない）
            GuiControls.Hint(Loc.T(
                "Raw view shows each expression's strength from 0 (not that face) to 1 (fully that face). It switches after the strength stays at or above the threshold for a moment. 0.1 reacts to a slight face, 0.3 (default) when it becomes clear, and about 1 only for a full one. Above 1 it never switches, because the strength does not go past 1. Type a number in the field, or drag the slider.",
                "生データ表示の強さは 0（その顔をしていない）〜 1（はっきりその顔）です。強さがしきい値以上のまま少し続くと切り替わります。0.1 は少しの顔でも、0.3（既定）ははっきりし始めたら、1 前後はその顔をしっかりしたときだけ切り替わります。1 より大きいと強さは 1 までしか出ないので切り替わりません。欄に数値を入れるか、スライダーを動かします。",
                "원시 데이터 표시의 세기는 0(그 표정이 아님)〜1(분명한 표정)입니다. 세기가 임계값 이상으로 잠시 유지되면 바뀝니다. 0.1은 약한 표정에도, 0.3(기본)은 분명해지면, 1 전후는 표정을 확실히 했을 때만 바뀝니다. 1보다 크면 세기는 1까지만 나오므로 바뀌지 않습니다. 칸에 숫자를 입력하거나 슬라이더를 움직이세요.",
                "原始数据显示的强度为 0（不是该表情）到 1（明显是该表情）。强度达到阈值并保持片刻后才会切换。0.1 时轻微表情也会切换，0.3（默认）是表情开始明显时，1 左右只在表情做到位时切换。大于 1 时强度最高只有 1，因此不会切换。可在栏中输入数字，或拖动滑块。",
                "原始資料顯示的強度為 0（不是該表情）到 1（明顯是該表情）。強度達到閾值並保持片刻後才會切換。0.1 時輕微表情也會切換，0.3（預設）是表情開始明顯時，1 左右只在表情做到位時切換。大於 1 時強度最高只有 1，因此不會切換。可在欄中輸入數字，或拖動滑桿。"));

            // アバター未表示なら割り当ては出さない
            if (expressions == null)
            {
                return;
            }

            // 表情ごとの割り当て（ニュートラル以外）
            RefreshExpressionOptions(expressions);
            for (var expression = FaceExpressions.First; expression <= FaceExpressions.Last; expression++)
            {
                DrawExpressionMapping(expression, expressions.Names);
            }

            // 割り当て先の無い表情は判定に使わないことを補足
            GuiControls.Hint(Loc.T(
                "Expressions set to None (or Auto with nothing found) are not detected and do not override the others",
                "「なし」や、自動で見つからない表情は検出しません（ほかの表情の邪魔をしません）",
                "「없음」이거나 자동으로 찾지 못한 표정은 감지하지 않습니다 (다른 표정을 방해하지 않습니다)",
                "设为“无”或自动未找到的表情不会被检测（不会干扰其他表情）",
                "設為「無」或自動未找到的表情不會被偵測（不會干擾其他表情）"));

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

            // 割り当てなしの表情は判定しないため、しきい値も出さない
            if (ExpressionMapping.GetSaved(_settings, expression) == ExpressionMapping.None)
            {
                return;
            }

            // その表情のしきい値（動かしたときだけ表情ごとの値として保存。欄へ直接入力もできる）
            float threshold = ExpressionMapping.GetThreshold(_settings, expression);
            float moved = ThresholdField(expression, threshold);
            if (!Mathf.Approximately(moved, threshold))
            {
                ExpressionMapping.SetThreshold(_settings, expression, moved);
            }
        }

        private float ThresholdField(FaceExpression expression, float value)
        {
            string id = expression.ToString();
            _thresholdInputs.TryGetValue(id, out string input);
            float result = GuiControls.SliderOrField(
                "  " + Loc.T("Threshold", "しきい値", "임계값", "阈值", "閾值"),
                value, AppSettings.MinExpressionThreshold, AppSettings.MaxExpressionThreshold,
                ref input, "expr-" + id, "F2");
            _thresholdInputs[id] = input ?? string.Empty;
            return result;
        }

        internal static string ExpressionLabel(FaceExpression expression)
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
                case FaceExpression.Wink:
                    return Loc.T("Wink", "ウインク", "윙크", "眨单眼", "眨單眼");
                case FaceExpression.Squint:
                    return Loc.T("Half-closed eyes", "ジト目", "실눈", "眯眼", "瞇眼");
                case FaceExpression.Pout:
                    return Loc.T("Pout", "ふくれっ面", "뾰로통", "嘟嘴", "嘟嘴");
                default:
                    return Loc.T("Neutral", "ニュートラル", "무표정", "无表情", "無表情");
            }
        }

        private void DrawLauncher()
        {
            // 同梱版が無い（開発ビルド等）か、既にパス指定済みのときだけパス入力を出す
            string executable = TrackerProcess.ExecutableOf(TrackingSourceInfo.BundledTrackerOf(_settings.trackingSource));
            if (!_process.HasBundled || !string.IsNullOrEmpty(_settings.trackerPath))
            {
                string bundled = _process.HasBundled
                    ? Loc.T(" (empty = bundled)", "（空欄 = 同梱版）", " (비워 두면 = 포함된 버전)",
                        "（留空 = 内置版本）", "（留空 = 內建版本）")
                    : string.Empty;
                GuiControls.Hint(executable + Loc.T(" path", " のパス", " 경로", " 路径", " 路徑") + bundled);
                _settings.trackerPath = GUILayout.TextField(_settings.trackerPath);
            }

            // カメラ選択（デバイス名と番号で保存、変更するとトラッカーが起動し直す。同名は表示に番号を添える）
            IReadOnlyList<string> cameras = _process.Cameras;
            int current = TrackerProcess.ResolveCameraIndex(
                cameras, _settings.trackerCamera, _settings.trackerCameraIndex);
            int selected = GuiControls.Selector(
                Loc.T("Camera", "カメラ", "카메라", "相机", "相機"),
                TrackerProcess.CameraDisplayNames(cameras), current, null);
            if (selected != current && selected >= 0 && selected < cameras.Count)
            {
                _settings.trackerCamera = cameras[selected];
                _settings.trackerCameraIndex = selected;
            }

            // 仮想カメラ・赤外線カメラでは顔が映らないため、実際の Web カメラを選ぶよう促す
            if (TrackerProcess.IsLikelyUnusableCamera(_settings.trackerCamera))
            {
                GuiControls.Hint(Loc.T(
                    "This looks like a virtual or infrared camera, so your face will not be detected. Select your webcam.",
                    "仮想カメラまたは赤外線カメラのようです。顔が認識されないため、お使いの Web カメラを選んでください。",
                    "가상 카메라 또는 적외선 카메라로 보입니다. 얼굴이 인식되지 않으므로 사용 중인 웹캠을 선택하세요.",
                    "这似乎是虚拟摄像头或红外摄像头，无法识别面部。请选择您的网络摄像头。",
                    "這似乎是虛擬攝影機或紅外線攝影機，無法辨識臉部。請選擇您的網路攝影機。"));
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

        private void DrawPort()
        {
            // 確定するまで設定へ反映しない（受信側・起動中のトラッカーがポート変更に追従する）
            DrawPortApply(Loc.T("UDP port", "UDP ポート", "UDP 포트", "UDP 端口", "UDP 連接埠"),
                ref _portInput, ref _settings.trackingPort);
        }

        private void DrawMotion()
        {
            GuiControls.BeginCard(Loc.T("Motion", "動き", "움직임", "动作", "動作"));
            _settings.trackingMirror = GUILayout.Toggle(
                _settings.trackingMirror,
                Loc.T("Mirror (move like a mirror)", "ミラー（鏡のように動かす）", "미러 (거울처럼 움직이기)",
                    "镜像（像镜子一样移动）", "鏡像（像鏡子一樣移動）"));

            // 顔（まばたき・ウインク・パーフェクトシンク・視線）の左右だけを入れ替える（頭の傾き・腕はそのまま）
            _settings.trackingSwapFaceSides = GUILayout.Toggle(
                _settings.trackingSwapFaceSides,
                Loc.T("Swap face left / right", "顔の左右を入れ替える", "얼굴 좌우 바꾸기", "交换面部左右", "交換臉部左右"));
            GuiControls.Hint(Loc.T(
                "Turn on if the closed eye or wink is on the wrong side compared with your head tilt and arms. Only the face (blink, perfect sync, gaze) is swapped.",
                "閉じた目・ウインクが頭の傾きや腕と逆側になるときに ON にします。顔（まばたき・パーフェクトシンク・視線）だけが入れ替わります。",
                "감은 눈·윙크가 머리 기울기나 팔과 반대쪽이 될 때 켭니다. 얼굴 (눈 깜빡임·퍼펙트 싱크·시선)만 바뀝니다.",
                "当闭上的眼睛或单眼眨眼与头部倾斜、手臂方向相反时开启。只交换面部（眨眼、完美同步、视线）。",
                "當閉上的眼睛或單眼眨眼與頭部傾斜、手臂方向相反時開啟。只交換臉部（眨眼、完美同步、視線）。"));

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

            // 全身を映すアバター向けに、足を固定して腰も上半身についていかせる
            _settings.trackingPlantFeet = GUILayout.Toggle(
                _settings.trackingPlantFeet,
                Loc.T("Full body (keep feet planted)", "全身モード（足を固定）", "전신 모드 (발 고정)", "全身模式（固定脚部）",
                    "全身模式（固定腳部）"));
            GuiControls.Hint(Loc.T(
                "For showing the whole body. The hips follow the upper body's lean and movement while the feet stay on the floor and the knees bend.",
                "全身を映すとき向けです。足を床に付けたまま、腰も上半身の傾き・移動についていき、膝の曲げで吸収します。",
                "전신을 비출 때 사용합니다. 발을 바닥에 둔 채 허리도 상체의 기울기·이동을 따라가고 무릎을 굽혀 흡수합니다.",
                "适合显示全身时使用。脚保持贴地，腰部也跟随上半身的倾斜和移动，由膝盖弯曲吸收。",
                "適合顯示全身時使用。腳保持貼地，腰部也跟隨上半身的傾斜和移動，由膝蓋彎曲吸收。"));

            _settings.trackingGaze = GuiControls.Slider(
                Loc.T("Eye gaze", "視線の強さ", "시선 강도", "视线强度", "視線強度"),
                _settings.trackingGaze, 0f, AppSettings.MaxTrackingGaze);
            GuiControls.Hint(Loc.T("To point the eyes at the screen, use Face & expressions tab > Look at camera",
                "目を画面に向けるには、顔・表情タブの「カメラ目線」を使います",
                "눈을 화면으로 향하게 하려면 얼굴·표정 탭의 \"카메라 시선\"을 사용합니다",
                "要让眼睛看向画面，请使用面部·表情标签页的“看向镜头”",
                "要讓眼睛看向畫面，請使用臉部·表情分頁的「看向鏡頭」"));

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

            // カメラが斜めのときだけ使う任意の補正（使わなければ従来どおり。両肩が見えると背骨から取る）
            GuiControls.Hint(Loc.T(
                "Optional: if the camera is tilted, stand straight with both shoulders visible and press Align camera tilt. After 5 seconds, head, arms and the raw skeleton use the same upright reference (from your spine when the body is tracked). Clear tilt correction restores the previous behavior.",
                "任意: カメラが斜めのときだけ、両肩が見えるようまっすぐ立って「カメラの傾きを合わせる」を押してください。5 秒後に頭・腕・確認用の骨格が同じ直立の基準になります（体が取れているときは背骨から合わせます）。「傾き補正を解除」で今までどおりに戻せます。",
                "선택: 카메라가 기울어져 있을 때만 양어깨가 보이게 바로 서서 \"카메라 기울기 맞추기\"를 누르세요. 5초 후 머리·팔·원시 골격이 같은 바른 기준을 씁니다 (몸이 잡히면 척추 기준). \"기울기 보정 해제\"로 이전처럼 되돌립니다.",
                "可选：仅在摄像头倾斜时，直立并露出双肩后按下“对齐摄像头倾斜”。5 秒后头部、手臂和原始骨架使用同一直立基准（能追踪身体时按脊柱对齐）。用“清除倾斜校正”可恢复原样。",
                "可選：僅在攝影機傾斜時，直立並露出雙肩後按下「對齊攝影機傾斜」。5 秒後頭部、手臂和原始骨架使用同一直立基準（能追蹤身體時按脊椎對齊）。用「清除傾斜校正」可恢復原樣。"));

            GUILayout.BeginHorizontal();
            // 体か顔のどちらかが取れていれば開始できる
            bool tracking = (_body != null && _body.TryGetBody(out BodyTrackingFrame bodyFrame)
                    && bodyFrame.Left.HasShoulder && bodyFrame.Right.HasShoulder)
                || _tracker.TryGetFrame(out _);
            // カウント中にもう一度押すとやり直す
            GUI.enabled = tracking;
            if (GUILayout.Button(
                    Loc.T("Align camera tilt", "カメラの傾きを合わせる", "카메라 기울기 맞추기", "对齐摄像头倾斜", "對齊攝影機傾斜"),
                    GuiControls.Shrinkable))
            {
                _tiltCalibration.Start();
            }

            GUI.enabled = _settings.HasTrackingTiltCorrection;
            if (GuiControls.DangerButton(
                    Loc.T("Clear tilt correction", "傾き補正を解除", "기울기 보정 해제", "清除倾斜校正", "清除傾斜校正"),
                    GuiControls.Shrinkable))
            {
                _tiltCalibration.Cancel();
                _settings.TrackingTiltCorrection = Quaternion.identity;
                AppBootstrap.Save();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (_tiltCalibration.IsRunning)
            {
                int seconds = Mathf.CeilToInt(_tiltCalibration.Remaining);
                GuiControls.Hint(Loc.T(
                    $"Stand straight. Recording in {seconds}...",
                    $"まっすぐ立ってください。{seconds} 秒後に記録します…",
                    $"바로 서 주세요. {seconds}초 후 기록합니다…",
                    $"请站直。{seconds} 秒后记录…",
                    $"請站直。{seconds} 秒後記錄…"));
            }

            // 記録済みの傾き補正（度、-180〜180。未補正は 0）
            Vector3 tilt = EulerSigned(_settings.TrackingTiltCorrection);
            GuiControls.Hint(Loc.T("Tilt correction", "傾き補正", "기울기 보정", "倾斜校正", "傾斜校正")
                + $"  {Loc.T("pitch", "前後", "앞뒤", "前后", "前後")} {tilt.x:F1}°  "
                + $"{Loc.T("yaw", "左右", "좌우", "左右", "左右")} {tilt.y:F1}°  "
                + $"{Loc.T("roll", "回転", "회전", "旋转", "旋轉")} {tilt.z:F1}°");

            GuiControls.EndCard();
        }

        private static Vector3 EulerSigned(Quaternion rotation)
        {
            Vector3 euler = rotation.eulerAngles;
            return new Vector3(
                Mathf.DeltaAngle(0f, euler.x),
                Mathf.DeltaAngle(0f, euler.y),
                Mathf.DeltaAngle(0f, euler.z));
        }

        /// <summary>
        /// 傾きキャリブのカウントダウン中なら、画面の上中央に案内を出す（screenWidth は UI の倍率で割った幅）。
        /// </summary>
        public static void DrawTiltOverlay(TiltCalibrationRecorder recorder, float screenWidth)
        {
            if (recorder == null || !recorder.IsRunning)
            {
                return;
            }

            UiTheme theme = UiTheme.Current;
            if (_tiltPromptStyle == null || _tiltPromptTheme != theme)
            {
                _tiltPromptTheme = theme;
                _tiltPromptStyle = new GUIStyle(theme != null ? theme.Title : GUI.skin.label)
                {
                    fontSize = TiltPromptFontSize,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                };
            }

            float width = Mathf.Min(TiltOverlayWidth, screenWidth - 20f);
            var rect = new Rect((screenWidth - width) * 0.5f, TiltOverlayTop, width, TiltOverlayHeight);
            _tiltOverlayRecorder = recorder;
            _tiltOverlayTheme = theme;
            FrontPrompt.Draw(TiltOverlayWindowId, rect, theme != null ? theme.Card : GUI.skin.box, DrawTiltWindow);
        }

        private static void DrawTiltWindow(int id)
        {
            UiTheme theme = _tiltOverlayTheme;
            GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.T(
                "Stand straight — both shoulders visible",
                "まっすぐ立ってください（両肩が見えるように）",
                "바로 서 주세요 (양어깨가 보이게)",
                "请站直（露出双肩）",
                "請站直（露出雙肩）"), _tiltPromptStyle);
            int seconds = Mathf.CeilToInt(_tiltOverlayRecorder.Remaining);
            GUILayout.Label(Loc.T($"Recording in {seconds}...", $"{seconds} 秒後に記録します…",
                $"{seconds}초 후 기록합니다…", $"{seconds} 秒后开始记录…", $"{seconds} 秒後開始記錄…"),
                theme != null ? theme.Centered : GUI.skin.label);
            GUILayout.FlexibleSpace();
        }

        private void DrawDebug()
        {
            GuiControls.BeginCard(Loc.T("Debug", "確認", "확인", "调试", "偵錯"));

            // アバターの代わりに受信値をそのまま線で表示（保存しない、起動時は常にアバター）
            _skeleton.Visible = GUILayout.Toggle(
                _skeleton.Visible, Loc.T("Raw view (skeleton instead of avatar)", "生データ表示（アバターの代わりに骨格）",
                    "원시 데이터 표시 (아바타 대신 골격)", "原始数据显示（以骨架代替虚拟形象）",
                    "原始資料顯示（以骨架代替虛擬形象）"));
            if (_skeleton.Visible)
            {
                GuiControls.Hint(Loc.T(
                    "Drawn at the avatar's size. The green box is the upper body direction from your shoulders (gray when not used for the avatar).",
                    "アバターの大きさに合わせて描きます。緑の箱は両肩から求めた上半身の向きです（アバターに使わない設定では灰色）。",
                    "아바타 크기에 맞춰 그립니다. 초록 상자는 양어깨로 구한 상체 방향입니다 (아바타에 쓰지 않는 설정에서는 회색).",
                    "按虚拟形象的大小绘制。绿色方框是由双肩求得的上半身朝向（不用于虚拟形象的设置下为灰色）。",
                    "依虛擬形象的大小繪製。綠色方框是由雙肩求得的上半身朝向（不用於虛擬形象的設定下為灰色）。"));
            }

            // 表示中は顔の値も数値で出す（受信中のみ）
            if (_skeleton.Visible && _tracker.TryGetFrame(out FaceTrackingFrame face))
            {
                GuiControls.Hint(Loc.T("Eye", "目", "눈", "眼", "眼")
                    + $" L {face.EyeOpenLeft:F2}  R {face.EyeOpenRight:F2}    "
                    + Loc.T("Mouth", "口", "입", "嘴", "嘴") + $" {face.MouthOpen:F2}");
                GuiControls.Hint(Loc.T("Gaze", "視線", "시선", "视线", "視線") + $" x {face.Gaze.x:F1}  y {face.Gaze.y:F1}");

                // 両肩が映っていれば上半身の向き（緑の箱。正面の補正前の生の角度）
                if (_skeleton.TryGetTorsoAngles(out Vector2 torso))
                {
                    GuiControls.Hint(Loc.T("Upper body", "上半身", "상체", "上半身", "上半身")
                        + $" {Loc.T("twist", "ひねり", "비틀기", "扭转", "扭轉")} {torso.x:F1}°  "
                        + $"{Loc.T("tilt", "傾き", "기울기", "倾斜", "傾斜")} {torso.y:F1}°");
                }

                // 表情の強さ（MediaPipe・VMC・iFacialMocap のみ。しきい値調整の目安なので、判定と同じく目の開きの補正と
                // 表情のキャリブレーションを反映）
                if (face.HasExpression)
                {
                    ExpressionScores scores = ExpressionCalibration.Apply(
                        ArKitFace.AdjustExpression(face, _settings.trackingEyeOpenOffset), face.BlendShapes,
                        _settings.expressionCalibrations, _tracker.FaceSource);
                    GuiControls.Hint($"{ExpressionLabel(FaceExpression.Smile)} {scores.Smile:F2}  "
                        + $"{ExpressionLabel(FaceExpression.Surprise)} {scores.Surprise:F2}  "
                        + $"{ExpressionLabel(FaceExpression.Angry)} {scores.Angry:F2}  "
                        + $"{ExpressionLabel(FaceExpression.Sad)} {scores.Sad:F2}");
                    GuiControls.Hint($"{ExpressionLabel(FaceExpression.Wink)} {scores.Wink:F2}  "
                        + $"{ExpressionLabel(FaceExpression.Squint)} {scores.Squint:F2}  "
                        + $"{ExpressionLabel(FaceExpression.Pout)} {scores.Pout:F2}");
                }
            }

            GuiControls.EndCard();
        }
    }
}
