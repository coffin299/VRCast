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

        // 入力途中のポート文字列（同梱トラッカー用と VMC 用。確定するまで設定へ反映しない）
        private string _portInput;
        private string _vmcPortInput;

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

        // iPhone の IP アドレスの入力欄の最大文字数（IPv4 の最長 15 文字に余裕を持たせる）
        private const int MaxAddressLength = 40;

        // 表情の割り当ての候補（先頭は「割り当てなし」、以降は表情プリセット名）と、作成元のコントローラー
        private readonly List<string> _expressionOptions = new List<string>();
        private ExpressionController _expressionSource;

        // 表情ごとに「自動」で推定されるプリセットの位置（FaceExpression の値で引く、-1 = なし）
        private readonly int[] _expressionGuesses = new int[(int)FaceExpressions.Last + 1];

        public TrackingSection(
            AvatarSession session, IFaceTrackingProvider tracker, TrackerProcess process,
            TrackingSkeletonView skeleton, AppSettings settings)
        {
            _avatar = new AvatarComponentCache(session);
            _tracker = tracker;
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
            // 見出しは入力元に合わせる（Web カメラ / iPhone 等の外部アプリ）
            bool external = !TrackingSourceInfo.UsesBundledTracker(_settings.trackingSource);
            GuiControls.BeginCard(external
                ? Loc.T("Tracking (external app)", "トラッキング（外部アプリ）", "트래킹 (외부 앱)",
                    "追踪（外部应用）", "追蹤（外部應用程式）")
                : Loc.T("Webcam tracking", "Web カメラトラッキング", "웹캠 트래킹", "摄像头追踪", "網路攝影機追蹤"));
            _settings.trackingEnabled = GUILayout.Toggle(
                _settings.trackingEnabled, Loc.T("Enable tracking", "トラッキングを有効にする", "트래킹 사용",
                    "启用追踪", "啟用追蹤"));

            // 無効時は ON/OFF だけ
            if (_settings.trackingEnabled)
            {
                DrawSource();

                // 外部アプリは、接続に必要な IP アドレス・ポートを入力元のすぐ下に出す（見落とさないように）
                if (_settings.trackingSource == TrackingSource.Vmc)
                {
                    DrawVmcConnection();
                }
                else if (_settings.trackingSource == TrackingSource.IFacialMocap)
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
                Loc.T("iPhone / external app (VMC, face only)", "iPhone・外部アプリ（VMC、顔のみ）",
                    "iPhone·외부 앱 (VMC, 얼굴만)", "iPhone / 外部应用（VMC，仅面部）", "iPhone / 外部應用程式（VMC，僅臉部）"),
                Loc.T("iFacialMocap (iPhone, face only)", "iFacialMocap（iPhone、顔のみ）",
                    "iFacialMocap (iPhone, 얼굴만)", "iFacialMocap（iPhone，仅面部）", "iFacialMocap（iPhone，僅臉部）"),
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
                // MediaPipe はエコ・腕と手の推定を止めるとそれぞれ 1 段軽くなる
                case TrackingSource.MediaPipe:
                    int level = (_settings.trackingHands ? 1 : 0) + (_settings.trackerMode == TrackerMode.Eco ? 0 : 1);
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
            // トラッカーの動作は同梱の MediaPipe 版のみ
            if (_settings.trackingSource == TrackingSource.MediaPipe)
            {
                DrawTrackerMode();
            }

            // 腕・手は受信できる入力元（MediaPipe）のみ
            if (TrackingSourceInfo.HasArms(_settings.trackingSource))
            {
                DrawHands();
            }

            // パーフェクトシンク・表情の反映は ARKit の値を受信できる入力元（MediaPipe・VMC・iFacialMocap）のみ
            if (TrackingSourceInfo.HasArKit(_settings.trackingSource))
            {
                DrawPerfectSync();
                DrawExpressions();
            }
        }

        private void DrawTrackerMode()
        {
            // 並びは TrackerMode と同じ（変更すると TrackerProcess がトラッカーを起動し直す）
            string[] labels =
            {
                Loc.T("Smooth", "なめらか", "부드럽게", "流畅", "流暢"),
                Loc.T("Eco", "エコ", "절약", "节能", "節能"),
            };
            _settings.trackerMode = (TrackerMode)GuiControls.EnumSelector(
                Loc.T("Tracker mode", "トラッカーの動作", "트래커 동작", "追踪器模式", "追蹤器模式"),
                labels, (int)_settings.trackerMode);
            GuiControls.Hint(_settings.trackerMode == TrackerMode.Eco
                ? Loc.T(
                    "Eco: up to 20 fps, arms and hands are updated less often. About half the PC load of Smooth.",
                    "エコ: 最大 20fps。腕・手の更新を減らし、PC 負荷は「なめらか」の半分ほどです。",
                    "절약: 최대 20fps. 팔·손 갱신을 줄여 PC 부하가 「부드럽게」의 절반 정도입니다.",
                    "节能：最高 20fps，减少手臂和手的更新，电脑负载约为“流畅”的一半。",
                    "節能：最高 20fps，減少手臂和手的更新，電腦負載約為「流暢」的一半。")
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

            // 上半身の向き（両肩の線）は腕・手の体の推定を使うため、腕・手が ON のときだけ選べる
            if (_settings.trackingHands)
            {
                DrawTorso();
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

        private void DrawVmcConnection()
        {
            // 送信側アプリ（Waidayo 等）の設定手順
            GuiControls.Hint(Loc.T(
                "In the app on your phone (e.g. Waidayo), set the VMC protocol destination to one of these addresses and the port below. Use the same Wi-Fi as this PC.",
                "スマートフォンのアプリ（Waidayo など）で、VMC プロトコルの送信先に下のどれかの IP アドレスとポートを入力してください。この PC と同じ Wi-Fi につないでください。",
                "스마트폰 앱 (Waidayo 등)에서 VMC 프로토콜 전송 대상에 아래 IP 주소 중 하나와 포트를 입력하세요. 이 PC와 같은 Wi-Fi에 연결하세요.",
                "在手机应用（如 Waidayo）中，将 VMC 协议的发送目标设为下方任一 IP 地址和端口。请连接与此电脑相同的 Wi-Fi。",
                "在手機應用程式（如 Waidayo）中，將 VMC 協定的傳送目標設為下方任一 IP 位址和連接埠。請連接與此電腦相同的 Wi-Fi。"));

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

            // VMC の受信ポート（範囲内の数値になったときだけ反映。受信側が追従する）
            _settings.vmcPort = GuiControls.PortField(
                Loc.T("UDP port", "UDP ポート", "UDP 포트", "UDP 端口", "UDP 連接埠"), ref _vmcPortInput,
                _settings.vmcPort, AppSettings.MinTrackingPort, AppSettings.MaxTrackingPort);

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

        private static void DrawFirewallHint()
        {
            // 届かないときの主な原因（初回のファイアウォールの許可）
            GuiControls.Hint(Loc.T(
                "If nothing arrives, allow VRCast on private networks in Windows Firewall (asked the first time) and set your Wi-Fi to a private network.",
                "届かないときは、Windows ファイアウォールで VRCast をプライベートネットワークで許可し（初回に確認が出ます）、Wi-Fi をプライベートネットワークにしてください。",
                "데이터가 오지 않으면 Windows 방화벽에서 VRCast를 개인 네트워크에서 허용하고 (처음에 확인 창이 나옵니다), Wi-Fi를 개인 네트워크로 설정하세요.",
                "如果收不到数据，请在 Windows 防火墙中允许 VRCast 使用专用网络（首次会弹出确认），并将 Wi-Fi 设为专用网络。",
                "如果收不到資料，請在 Windows 防火牆中允許 VRCast 使用私人網路（首次會跳出確認），並將 Wi-Fi 設為私人網路。"));
        }

        private void DrawBlink()
        {
            // まばたきのトラッキングの ON/OFF（どの入力元でも使う。OFF の間は顔タブの自動まばたきに任せる）
            _settings.trackingBlink = GUILayout.Toggle(
                _settings.trackingBlink,
                Loc.T("Track blinks", "まばたきをトラッキング", "눈 깜빡임 트래킹", "追踪眨眼", "追蹤眨眼"));

            // OFF のときは自動まばたきになることを伝える
            if (!_settings.trackingBlink)
            {
                GuiControls.Hint(Loc.T("Uses auto blink (Face tab) instead", "代わりに自動まばたき（顔タブ）を使います",
                    "대신 자동 눈 깜빡임 (얼굴 탭)을 사용합니다", "改用自动眨眼（面部标签）", "改用自動眨眼（臉部分頁）"));
            }
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

            // 有効にする条件（ARKit 名が規定数以上 / 1 種類でも）。排他で選ぶ
            int min = PerfectSyncBlendShapes.MinMatchedShapes;
            string[] labels =
            {
                Loc.T($"{min}+ shapes", $"{min} 種類以上", $"{min}종 이상", $"{min} 种以上", $"{min} 種以上"),
                Loc.T("Any shape", "1 種類でも", "1종이라도", "有 1 种即可", "有 1 種即可"),
            };
            _settings.trackingPerfectSyncAnyShape = GuiControls.EnumSelector(
                Loc.T("Enable when", "有効にする条件", "활성화 조건", "启用条件", "啟用條件"),
                labels, _settings.trackingPerfectSyncAnyShape ? 1 : 0) == 1;

            // アバター未表示なら状態を出さない
            var driver = _avatar.Get<FaceTrackingDriver>();
            if (driver == null)
            {
                return;
            }

            // 対応状況（見つかった ARKit 名の数）
            int count = driver.PerfectSyncShapeCount;
            int total = ArKitFace.BlendShapeNames.Length;
            int required = _settings.trackingPerfectSyncAnyShape ? 1 : min;
            GuiControls.Hint(driver.SupportsPerfectSync
                ? Loc.T($"Supported ({count}/{total} ARKit blend shapes)",
                    $"対応アバター（ARKit 名の BlendShape {count}/{total} 個）",
                    $"지원 아바타 (ARKit 이름의 BlendShape {count}/{total}개)",
                    $"支持的虚拟形象（ARKit 名称的 BlendShape {count}/{total} 个）",
                    $"支援的虛擬形象（ARKit 名稱的 BlendShape {count}/{total} 個）")
                : Loc.T($"This avatar does not support it ({count}/{total} ARKit blend shapes, needs {required}+)",
                    $"このアバターは非対応です（ARKit 名の BlendShape {count}/{total} 個、{required} 個以上で対応）",
                    $"이 아바타는 지원하지 않습니다 (ARKit 이름의 BlendShape {count}/{total}개, {required}개 이상 필요)",
                    $"此虚拟形象不支持（ARKit 名称的 BlendShape {count}/{total} 个，需要 {required} 个以上）",
                    $"此虛擬形象不支援（ARKit 名稱的 BlendShape {count}/{total} 個，需要 {required} 個以上）"));

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

            // しきい値は表情ごと（下の割り当ての各行。生データ表示の数値と同じ目盛り）
            GuiControls.Hint(Loc.T(
                "Each expression switches when its value in the raw data reaches its threshold (lower = reacts more easily)",
                "表情ごとに、生データの値がしきい値を超えると切り替わります（低いほど反応しやすい）",
                "표정마다 원시 데이터의 값이 임계값을 넘으면 전환됩니다 (낮을수록 쉽게 반응)",
                "每种表情在原始数据中的值超过其阈值时切换（越低越容易反应）",
                "每種表情在原始資料中的值超過其閾值時切換（越低越容易反應）"));

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

            // その表情のしきい値（動かしたときだけ表情ごとの値として保存）
            float threshold = ExpressionMapping.GetThreshold(_settings, expression);
            float moved = GuiControls.Slider(
                "  " + Loc.T("Threshold", "しきい値", "임계값", "阈值", "閾值"),
                threshold, AppSettings.MinExpressionThreshold, AppSettings.MaxExpressionThreshold);
            if (moved != threshold)
            {
                ExpressionMapping.SetThreshold(_settings, expression, moved);
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
            // 範囲内の数値になったときだけ反映（受信側・起動中のトラッカーがポート変更に追従する）
            _settings.trackingPort = GuiControls.PortField(
                Loc.T("UDP port", "UDP ポート", "UDP 포트", "UDP 端口", "UDP 連接埠"), ref _portInput,
                _settings.trackingPort, AppSettings.MinTrackingPort, AppSettings.MaxTrackingPort);
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

                // 表情の強さ（MediaPipe・VMC・iFacialMocap のみ。しきい値調整の目安）
                if (face.HasExpression)
                {
                    ExpressionScores scores = face.Expression;
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
