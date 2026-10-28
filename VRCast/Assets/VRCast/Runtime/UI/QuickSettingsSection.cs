using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Output;
using VRCast.Platform;
using VRCast.Rendering;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// クイック設定タブ。よく使う設定を同じ AppSettings / コントローラー経由で変え、他タブと常に同期する。
    /// </summary>
    public class QuickSettingsSection
    {
        private static readonly TrackerMode[] TrackerModeOrder =
        {
            TrackerMode.Fluid, TrackerMode.Smooth, TrackerMode.Eco,
        };

        private readonly AvatarSession _session;
        private readonly AvatarComponentCache _avatar;
        private readonly AvatarSection _avatarSection;
        private readonly TrackerProcess _process;
        private readonly RenderingController _rendering;
        private readonly VirtualCameraOutput _virtualCamera;
        private readonly SpoutOutput _spout;
        private readonly AppSettings _settings;
        private readonly Action<StartLink> _open;

        private string _trackingFpsInput = string.Empty;
        private string _frameRateInput = string.Empty;
        private bool? _nvidiaVSyncAvailable;

        public QuickSettingsSection(
            AvatarSession session, AvatarSection avatarSection, TrackerProcess process,
            RenderingController rendering, VirtualCameraOutput virtualCamera, SpoutOutput spout,
            AppSettings settings, Action<StartLink> open)
        {
            _session = session;
            _avatar = new AvatarComponentCache(session);
            _avatarSection = avatarSection;
            _process = process;
            _rendering = rendering;
            _virtualCamera = virtualCamera;
            _spout = spout;
            _settings = settings;
            _open = open;
        }

        public void Draw()
        {
            _avatar.Refresh();

            GuiControls.Hint(Loc.T(
                "Same settings as the other tabs — changes here apply everywhere.",
                "他のタブと同じ設定です。ここで変えた内容はそのまま全体に反映されます。",
                "다른 탭과 같은 설정입니다. 여기서 바꾼 내용은 그대로 전체에 반영됩니다.",
                "与其他标签页是同一套设置。此处的更改会同步到各处。",
                "與其他分頁是同一套設定。此處的變更會同步到各處。"));

            DrawAvatar();
            DrawTracking();
            DrawFace();
            DrawBody();
            DrawGraphics();
            DrawPerformance();
            DrawOutput();
        }

        private void DrawAvatar()
        {
            GuiControls.BeginCard(Loc.T("Avatar", "アバター", "아바타", "虚拟形象", "虛擬形象"));

            GUILayout.BeginHorizontal();
            GUI.enabled = !_session.IsLoading;
            if (GUILayout.Button(Loc.T("Choose file...", "ファイルを選ぶ...", "파일 선택...", "选择文件...", "選擇檔案..."),
                    GuiControls.Shrinkable))
            {
                _avatarSection.Browse();
            }

            GUI.enabled = true;
            if (GUILayout.Button(Loc.T("Avatar tab", "アバタータブへ", "아바타 탭으로", "前往虚拟形象标签页", "前往虛擬形象分頁"),
                    GuiControls.Shrinkable))
            {
                _open(StartLink.Avatar);
            }

            GUILayout.EndHorizontal();

            if (_session.IsLoading)
            {
                GUILayout.Label(Loc.T("Loading...", "読み込み中...", "불러오는 중...", "加载中...", "載入中..."));
            }
            else if (_avatarSection.HasError)
            {
                GuiControls.Error(Loc.T(
                    "Could not load. See the Avatar tab for details.",
                    "読み込めませんでした。詳しくはアバタータブを見てください。",
                    "불러오지 못했습니다. 자세한 내용은 아바타 탭을 확인하세요.",
                    "加载失败。详情请查看虚拟形象标签页。",
                    "載入失敗。詳細請查看虛擬形象分頁。"));
            }
            else if (_session.Current != null)
            {
                GuiControls.Hint(Loc.T("Loaded: ", "読み込み済み: ", "불러옴: ", "已加载：", "已載入：")
                    + _session.Current.Name);
            }
            else
            {
                GuiControls.Hint(Loc.T(
                    "Drag and drop a .vrcaster or .vrm file, or choose one above.",
                    ".vrcaster / .vrm をドラッグ＆ドロップするか、上のボタンで選んでください。",
                    ".vrcaster / .vrm 을 드래그 앤 드롭하거나 위 버튼으로 선택하세요.",
                    "拖放 .vrcaster / .vrm，或用上方按钮选择。",
                    "拖放 .vrcaster / .vrm，或用上方按鈕選擇。"));
            }

            GuiControls.EndCard();
        }

        private void DrawTracking()
        {
            GuiControls.BeginCard(Loc.T("Tracking", "トラッキング", "트래킹", "追踪", "追蹤"));

            _settings.trackingEnabled = GUILayout.Toggle(
                _settings.trackingEnabled, Loc.T("Enable tracking", "トラッキングを有効にする", "트래킹 사용",
                    "启用追踪", "啟用追蹤"));
            if (!_settings.trackingEnabled)
            {
                MoreTabButton(StartLink.Tracking, Loc.T("Tracking tab", "トラッキングタブへ", "트래킹 탭으로",
                    "前往追踪标签页", "前往追蹤分頁"));
                GuiControls.EndCard();
                return;
            }

            _settings.trackingMirror = GUILayout.Toggle(
                _settings.trackingMirror,
                Loc.T("Mirror (move like a mirror)", "ミラー（鏡のように動かす）", "미러 (거울처럼 움직이기)",
                    "镜像（像镜子一样移动）", "鏡像（像鏡子一樣移動）"));

            if (TrackingSourceInfo.HasArms(_settings.trackingSource))
            {
                _settings.trackingHands = GUILayout.Toggle(
                    _settings.trackingHands, Loc.T("Arms / hands", "腕・手", "팔·손", "手臂 / 手", "手臂 / 手"));
            }

            if (TrackingSourceInfo.BundledTrackerOf(_settings.trackingSource) == TrackingSource.MediaPipe)
            {
                DrawTrackerModeAndFps();
                DrawCameraLauncher();
            }
            else
            {
                GuiControls.Hint(Loc.T(
                    "Camera and tracker mode are for the webcam (MediaPipe) source. Change the source in the Tracking tab.",
                    "カメラとトラッカーの動作は Web カメラ（MediaPipe）向けです。入力元の切替はトラッキングタブで。",
                    "카메라와 트래커 동작은 웹캠(MediaPipe)용입니다. 입력 전환은 트래킹 탭에서.",
                    "摄像头与追踪器模式适用于摄像头（MediaPipe）。请在追踪标签页切换输入源。",
                    "攝影機與追蹤器模式適用於網路攝影機（MediaPipe）。請在追蹤分頁切換輸入來源。"));
            }

            if (_process != null && !string.IsNullOrEmpty(_process.Status))
            {
                GuiControls.Hint(_process.Status);
            }

            MoreTabButton(StartLink.Tracking, Loc.T("Tracking tab", "トラッキングタブへ", "트래킹 탭으로",
                "前往追踪标签页", "前往追蹤分頁"));
            GuiControls.EndCard();
        }

        private void DrawTrackerModeAndFps()
        {
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

            int trackingFps = Mathf.RoundToInt(GuiControls.SliderOrField(
                Loc.T("Tracking FPS", "トラッキング FPS", "트래킹 FPS", "追踪 FPS", "追蹤 FPS"),
                _settings.trackingFps, AppSettings.MinTrackingFps, AppSettings.MaxTrackingFps,
                ref _trackingFpsInput, "quick-track-fps", "F0"));
            if (trackingFps != _settings.trackingFps)
            {
                _settings.trackingFps = trackingFps;
            }

            GuiControls.Error(Loc.T(
                "Cannot go above what the camera actually delivers (often 30 fps).",
                "カメラが実際に出せるフレームレートより上にはなりません（多くのカメラは 30fps 止まり）。",
                "카메라가 실제로 내는 프레임레이트보다 높게는 되지 않습니다(대개 30fps).",
                "无法超过摄像头实际输出的帧率（多数为 30fps）。",
                "無法超過攝影機實際輸出的影格率（多數為 30fps）。"));
        }

        private void DrawCameraLauncher()
        {
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

            GUILayout.BeginHorizontal();
            GUI.enabled = !_process.IsListing;
            if (GUILayout.Button(_process.IsListing
                    ? Loc.T("Listing...", "取得中...", "가져오는 중...", "获取中...", "取得中...")
                    : Loc.T("Refresh cameras", "カメラ一覧を更新", "카메라 목록 새로 고침", "刷新相机列表", "重新整理相機清單"),
                    GuiControls.Shrinkable))
            {
                _process.RefreshCameras();
            }

            if (GUILayout.Button(Loc.T("Restart tracker", "トラッカーを再起動", "트래커 다시 시작", "重启追踪器", "重新啟動追蹤器"),
                    GuiControls.Shrinkable))
            {
                _process.Restart();
            }

            GUI.enabled = _process.IsRunning;
            if (GUILayout.Button(Loc.T("Camera settings", "カメラ設定", "카메라 설정", "摄像头设置", "攝影機設定"),
                    GuiControls.Shrinkable))
            {
                _process.OpenCameraSettings();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GuiControls.Error(Loc.T(
                "Camera settings are the camera’s own Windows settings, so they also apply in other apps.",
                "カメラ設定は Windows のカメラ本体の設定なので、他のアプリにも影響します。",
                "카메라 설정은 Windows 카메라 자체 설정이므로 다른 앱에도 영향을 줍니다.",
                "摄像头设置是 Windows 摄像头本身的设置，也会影响其他应用。",
                "攝影機設定是 Windows 攝影機本身的設定，也會影響其他應用程式。"));
        }

        private void DrawFace()
        {
            GuiControls.BeginCard(Loc.T("Face & expressions", "顔・表情", "얼굴·표정", "面部·表情", "臉部·表情"));

            _settings.trackingLookAtCamera = GUILayout.Toggle(
                _settings.trackingLookAtCamera,
                Loc.T("Look at camera", "カメラ目線", "카메라 시선", "看向镜头", "看向鏡頭"));
            _settings.autoBlink = GUILayout.Toggle(
                _settings.autoBlink, Loc.T("Auto blink", "自動まばたき", "자동 눈 깜빡임", "自动眨眼", "自動眨眼"));
            _settings.lipSyncEnabled = GUILayout.Toggle(
                _settings.lipSyncEnabled, Loc.T("Lip sync (microphone)", "マイクで口パク", "마이크로 립싱크",
                    "用麦克风对口型", "用麥克風對嘴型"));

            MoreTabButton(StartLink.Face, Loc.T("Face & expressions tab", "顔・表情タブへ", "얼굴·표정 탭으로",
                "前往面部·表情标签页", "前往臉部·表情分頁"));
            GuiControls.EndCard();
        }

        private void DrawBody()
        {
            GuiControls.BeginCard(Loc.T("Body & pose", "体・ポーズ", "몸·포즈", "身体·姿势", "身體·姿勢"));

            var pose = _avatar.Get<PoseController>();
            if (!_avatar.HasAvatar || pose == null)
            {
                GuiControls.Hint(Loc.T("Load an avatar first.", "先にアバターを読み込んでください。", "먼저 아바타를 불러오세요.",
                    "请先加载虚拟形象。", "請先載入虛擬形象。"));
            }
            else
            {
                pose.BodyYaw = GuiControls.Slider(
                    Loc.T("Body yaw", "体の向き", "몸 방향", "身体朝向", "身體朝向"), pose.BodyYaw, -180f, 180f);

                if (pose.IsAvailable)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(Loc.T("Attention", "気を付け", "차렷", "立正", "立正"), GuiControls.Shrinkable))
                    {
                        pose.ArmDown = 1f;
                        pose.ElbowBend = 0f;
                    }

                    if (GUILayout.Button(Loc.T("Relaxed", "リラックス", "편안하게", "放松", "放鬆"), GuiControls.Shrinkable))
                    {
                        pose.ArmDown = 0.85f;
                        pose.ElbowBend = 0.3f;
                    }

                    if (GUILayout.Button(Loc.T("T-Pose", "T ポーズ", "T 포즈", "T 姿势", "T 姿勢"), GuiControls.Shrinkable))
                    {
                        pose.ArmDown = 0f;
                        pose.ElbowBend = 0f;
                    }

                    GUILayout.EndHorizontal();

                    pose.ArmDown = GuiControls.Slider(
                        Loc.T("Arms down", "腕を下ろす", "팔 내리기", "放下手臂", "放下手臂"), pose.ArmDown, 0f, 1f);
                    pose.ElbowBend = GuiControls.Slider(
                        Loc.T("Elbow bend", "肘の曲げ", "팔꿈치 굽힘", "肘部弯曲", "肘部彎曲"), pose.ElbowBend, 0f, 1f);
                }
            }

            _settings.idleMotionEnabled = GUILayout.Toggle(
                _settings.idleMotionEnabled, Loc.T("Breathing and sway", "呼吸・体の揺れ", "호흡 · 몸 흔들림",
                    "呼吸和身体摇摆", "呼吸與身體搖擺"));
            _settings.physicsEnabled = GUILayout.Toggle(
                _settings.physicsEnabled, Loc.T("PhysBone (approx.)", "揺れもの（PhysBone 近似）", "흔들림 (PhysBone 근사)",
                    "物理摆动（PhysBone 近似）", "物理擺動（PhysBone 近似）"));

            MoreTabButton(StartLink.Body, Loc.T("Body & pose tab", "体・ポーズタブへ", "몸·포즈 탭으로",
                "前往身体·姿势标签页", "前往身體·姿勢分頁"));
            GuiControls.EndCard();
        }

        private void DrawGraphics()
        {
            GuiControls.BeginCard(Loc.T("Graphics", "グラフィック", "그래픽", "图形", "圖形"));

            _rendering.TransparentBackground = GUILayout.Toggle(
                _rendering.TransparentBackground,
                Loc.T("Transparent background (for OBS)", "背景を透過（OBS 用）", "배경 투명 (OBS용)",
                    "透明背景（用于 OBS）", "透明背景（用於 OBS）"));
            GuiControls.Hint(Loc.T(
                "The window may still show a background color; OBS Game Capture / Spout sees transparency.",
                "この画面では背景色のまま見えることがあります。OBS のゲームキャプチャ / Spout では抜けます。",
                "이 창에서는 배경색이 보일 수 있습니다. OBS 게임 캡처 / Spout에서는 투명해집니다.",
                "此窗口可能仍显示背景色；OBS 游戏采集 / Spout 中会透明。",
                "此視窗可能仍顯示背景色；OBS 遊戲擷取 / Spout 中會透明。"));

            MoreTabButton(StartLink.Graphics, Loc.T("Graphics tab", "グラフィックタブへ", "그래픽 탭으로",
                "前往图形标签页", "前往圖形分頁"));
            GuiControls.EndCard();
        }

        private void DrawPerformance()
        {
            GuiControls.BeginCard(Loc.T("Performance", "パフォーマンス", "성능", "性能", "效能"));

            _rendering.LowLoadMode = GUILayout.Toggle(
                _rendering.LowLoadMode,
                Loc.T("Low load mode", "軽量モード", "저부하 모드", "低负载模式", "低負載模式"));

            DrawVSync();

            bool fpsLimitActive = _rendering.VSyncMode != VSyncMode.On;
            GUI.enabled = fpsLimitActive;
            int frameRate = Mathf.RoundToInt(GuiControls.SliderOrField(
                Loc.T("FPS limit", "FPS 上限", "FPS 상한", "FPS 上限", "FPS 上限"),
                _rendering.TargetFrameRate, AppSettings.MinTargetFrameRate, AppSettings.MaxTargetFrameRate,
                ref _frameRateInput, "quick-perf-fps", "F0"));
            if (frameRate != _rendering.TargetFrameRate)
            {
                _rendering.TargetFrameRate = frameRate;
            }

            GUI.enabled = true;

            MoreTabButton(StartLink.Performance, Loc.T("Performance tab", "パフォーマンスタブへ", "성능 탭으로",
                "前往性能标签页", "前往效能分頁"));
            GuiControls.EndCard();
        }

        private void DrawVSync()
        {
            _nvidiaVSyncAvailable ??= NvidiaVerticalSync.IsAvailable;
            bool nvidia = _nvidiaVSyncAvailable == true;
            string[] labels = nvidia
                ? new[]
                {
                    Loc.T("Off", "オフ", "끔", "关", "關"),
                    Loc.T("On", "オン", "켬", "开", "開"),
                    "Fast Sync",
                    "VRR",
                }
                : new[]
                {
                    Loc.T("Off", "オフ", "끔", "关", "關"),
                    Loc.T("On", "オン", "켬", "开", "開"),
                    "VRR",
                };

            var previous = _rendering.VSyncMode;
            int index = nvidia
                ? previous == VSyncMode.Vrr ? 3 : previous == VSyncMode.FastSync ? 2 : (int)previous
                : previous == VSyncMode.Vrr ? 2 : previous == VSyncMode.On ? 1 : 0;
            index = GuiControls.EnumSelector(
                Loc.T("V-Sync", "垂直同期（V-Sync）", "수직 동기화(V-Sync)", "垂直同步（V-Sync）", "垂直同步（V-Sync）"),
                labels, index);
            VSyncMode selected = nvidia
                ? index == 3 ? VSyncMode.Vrr : index == 2 ? VSyncMode.FastSync : (VSyncMode)index
                : index == 2 ? VSyncMode.Vrr : index == 1 ? VSyncMode.On : VSyncMode.Off;
            if (selected != previous)
            {
                _rendering.VSyncMode = selected;
                NvidiaVerticalSync.Apply(selected);
            }
        }

        private void DrawOutput()
        {
            GuiControls.BeginCard(Loc.T("Output", "出力", "출력", "输出", "輸出"));

            _virtualCamera.Enabled = GUILayout.Toggle(
                _virtualCamera.Enabled,
                Loc.T("Virtual camera", "仮想カメラ", "가상 카메라", "虚拟摄像头", "虛擬攝影機"));
            _spout.Enabled = GUILayout.Toggle(
                _spout.Enabled,
                Loc.T("Spout2 output", "Spout2 出力", "Spout2 출력", "Spout2 输出", "Spout2 輸出")
                + $" ({SpoutOutput.SenderName})");
            if (_spout.Enabled)
            {
                GuiControls.Hint(_spout.Status);
            }

            MoreTabButton(StartLink.Output, Loc.T("Output tab", "出力タブへ", "출력 탭으로",
                "前往输出标签页", "前往輸出分頁"));
            GuiControls.EndCard();
        }

        private void MoreTabButton(StartLink link, string label)
        {
            if (GUILayout.Button(label))
            {
                _open(link);
            }
        }
    }
}
