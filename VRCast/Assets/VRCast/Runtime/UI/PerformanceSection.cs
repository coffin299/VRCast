using System.Collections.Generic;
using UnityEngine;
using VRCast.Core;
using VRCast.Platform;
using VRCast.Rendering;

namespace VRCast.UI
{
    /// <summary>
    /// パフォーマンスタブ（軽量モード・動作状況の表示・プロセスの優先度、CPU のコアの選び方、描画に使う GPU、
    /// NVIDIA ShadowPlay に検知させない設定（NVIDIA の PC のみ））。
    /// </summary>
    public class PerformanceSection
    {
        // GPU の候補のうち、Windows の優先設定（GpuPreference）の数（以降が GPU の直接指定）
        private const int PresetCount = 3;

        // GPU の一覧（初回表示時に取得）
        private List<GpuAdapter> _adapters;

        // FPS 上限の欄への直接入力用
        private string _frameRateInput = string.Empty;

        // Fast Sync / VRR が NVIDIA 向けか（初回だけ調べる）、ドライバー書き込みの失敗理由・再起動案内
        private bool? _nvidiaVSyncAvailable;
        private string _vSyncError;
        private bool _vSyncRestartPending;

        private readonly AppSettings _settings;
        private readonly RenderingController _rendering;

        // NVIDIA オーバーレイの除外状態（ドライバー設定の読み込みは重いので初回と変更後だけ調べる）、直前の失敗理由、再起動待ちか
        private NvidiaOverlayExclusion.State? _overlayState;
        private string _overlayError;
        private bool _overlayRestartPending;

        public PerformanceSection(AppSettings settings, RenderingController rendering)
        {
            _settings = settings;
            _rendering = rendering;
        }

        public void Draw()
        {
            DrawPerformance();
            DrawCpu();
            DrawGpuCard();
            DrawNvidiaOverlay();
        }

        private void DrawPerformance()
        {
            GuiControls.BeginCard(Loc.T("Performance optimization", "動作最適化設定", "동작 최적화 설정", "运行优化设置",
                "執行最佳化設定"));

            // 値が変わったときだけ反映（トラッカーは TrackerProcess が設定の変化を見て再起動する）
            bool lowLoad = GUILayout.Toggle(_rendering.LowLoadMode, Loc.T(
                "Low load mode", "軽量モード", "저부하 모드", "低负载模式", "低負載模式"));
            if (lowLoad != _rendering.LowLoadMode)
            {
                _rendering.LowLoadMode = lowLoad;
            }

            GuiControls.Hint(Loc.T(
                $"Use this with games or OBS: caps drawing at {RenderingController.LowLoadFrameRate} fps " +
                $"(or your FPS limit if lower) when V-Sync is not On, and OpenSeeFace uses a lighter model. " +
                "For MediaPipe, choose Eco in the Tracking tab's Tracker mode.",
                $"ゲームや OBS と同時に使うときに。垂直同期が On 以外のとき描画を {RenderingController.LowLoadFrameRate}fps" +
                "（またはそれより低い FPS 上限）に抑え、OpenSeeFace は軽いモデルを使います。" +
                "MediaPipe はトラッキングタブの「トラッカーの動作」でエコを選んでください。",
                $"게임이나 OBS와 함께 쓸 때 사용하세요. V-Sync가 On이 아니면 화면을 {RenderingController.LowLoadFrameRate}fps" +
                "(또는 그보다 낮은 FPS 상한)으로 제한하고 OpenSeeFace는 가벼운 모델을 씁니다. " +
                "MediaPipe는 트래킹 탭의 「트래커 동작」에서 절약을 선택하세요.",
                $"与游戏或 OBS 同时使用时开启。垂直同步非 On 时将画面限制为 {RenderingController.LowLoadFrameRate}fps" +
                "（或更低的 FPS 上限），OpenSeeFace 使用较轻的模型。" +
                "MediaPipe 请在追踪标签页的“追踪器模式”中选择节能。",
                $"與遊戲或 OBS 同時使用時開啟。垂直同步非 On 時將畫面限制為 {RenderingController.LowLoadFrameRate}fps" +
                "（或更低的 FPS 上限），OpenSeeFace 使用較輕的模型。" +
                "MediaPipe 請在追蹤分頁的「追蹤器模式」中選擇節能。"));

            DrawVSync();

            // FPS 上限（通常の V-Sync On のときだけ効かない）
            bool fpsLimitActive = _rendering.VSyncMode != VSyncMode.On;
            GUI.enabled = fpsLimitActive;
            int frameRate = Mathf.RoundToInt(GuiControls.SliderOrField(
                Loc.T("FPS limit", "FPS 上限", "FPS 상한", "FPS 上限", "FPS 上限"),
                _rendering.TargetFrameRate, AppSettings.MinTargetFrameRate, AppSettings.MaxTargetFrameRate,
                ref _frameRateInput, "perf-fps", "F0"));
            if (frameRate != _rendering.TargetFrameRate)
            {
                _rendering.TargetFrameRate = frameRate;
            }

            GUI.enabled = true;
            if (!fpsLimitActive)
            {
                GuiControls.Hint(Loc.T(
                    "FPS limit is unused while V-Sync is On.",
                    "垂直同期が On のため、FPS 上限は使われていません。",
                    "수직 동기화가 On이라 FPS 상한은 사용되지 않습니다.",
                    "垂直同步为 On 时不使用 FPS 上限。",
                    "垂直同步為 On 時不使用 FPS 上限。"));
            }
            else if (_rendering.LowLoadMode && _rendering.TargetFrameRate > RenderingController.LowLoadFrameRate)
            {
                GuiControls.Hint(Loc.T(
                    $"Low load mode caps drawing at {RenderingController.LowLoadFrameRate} fps right now.",
                    $"軽量モードのため、いまの描画は {RenderingController.LowLoadFrameRate}fps に抑えられています。",
                    $"저부하 모드라 지금은 {RenderingController.LowLoadFrameRate}fps로 제한됩니다.",
                    $"低负载模式当前将画面限制为 {RenderingController.LowLoadFrameRate}fps。",
                    $"低負載模式目前將畫面限制為 {RenderingController.LowLoadFrameRate}fps。"));
            }

            // パネル下部の動作状況（表示は英語のみ）
            _settings.showPerformanceStats = GUILayout.Toggle(_settings.showPerformanceStats, Loc.T(
                "Show performance stats", "動作状況を表示", "동작 상태 표시", "显示运行状态", "顯示執行狀態"));
            GuiControls.Hint(Loc.T(
                "Shows FPS, CPU, GPU % (share of the frame time used by VRCast's drawing), avatar render size and tracking rate at the bottom of the panel (in English).",
                "パネルの一番下に FPS・CPU・GPU%（1 フレームのうち VRCast の描画が使った割合）・アバターの描画解像度・トラッキングの受信レートを表示します（英語表記）。",
                "패널 맨 아래에 FPS·CPU·GPU%(VRCast 그리기가 쓴 프레임 비율)·아바타 렌더 해상도·트래킹 수신 속도를 표시합니다(영어 표기).",
                "在面板底部显示 FPS、CPU、GPU%（VRCast 绘制所占帧时间比例）、虚拟形象绘制分辨率和追踪接收速率（英文显示）。",
                "在面板底部顯示 FPS、CPU、GPU%（VRCast 繪製所佔幀時間比例）、虛擬形象繪製解析度和追蹤接收速率（英文顯示）。"));

            DrawPriority();
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
            int index = ModeToLabelIndex(previous, nvidia);
            index = GuiControls.EnumSelector(
                Loc.T("V-Sync", "垂直同期（V-Sync）", "수직 동기화(V-Sync)", "垂直同步（V-Sync）", "垂直同步（V-Sync）"),
                labels, index);
            var selected = LabelIndexToMode(index, nvidia);
            if (selected != previous)
            {
                _rendering.VSyncMode = selected;
                _vSyncError = NvidiaVerticalSync.Apply(selected);
                bool needsRestart = selected == VSyncMode.FastSync || (selected == VSyncMode.Vrr && nvidia);
                _vSyncRestartPending = needsRestart && string.IsNullOrEmpty(_vSyncError);
            }

            GuiControls.Hint(Loc.T(
                "On locks to the refresh rate (FPS limit ignored). Fast Sync (NVIDIA) and VRR reduce tearing without that lock. VRR needs a compatible monitor (enable G-Sync/FreeSync for windowed mode if needed).",
                "On はリフレッシュに固定します（FPS 上限は使いません）。Fast Sync（NVIDIA）と VRR は固定せずティアリングを抑えます。VRR は対応モニターが必要で、ウィンドウモード向けの G-Sync / FreeSync も有効にしておくことが多いです。",
                "On은 주사율에 고정합니다(FPS 상한 미사용). Fast Sync(NVIDIA)와 VRR은 고정하지 않고 티어링을 줄입니다. VRR은 지원 모니터가 필요하며, 창 모드용 G-Sync/FreeSync도 켜 두는 경우가 많습니다.",
                "On 锁定刷新率（忽略 FPS 上限）。Fast Sync（NVIDIA）和 VRR 不锁定也能减轻撕裂。VRR 需要兼容显示器，窗口模式往往还要开启 G-Sync/FreeSync。",
                "On 鎖定重新整理率（忽略 FPS 上限）。Fast Sync（NVIDIA）與 VRR 不鎖定也能減輕撕裂。VRR 需要相容顯示器，視窗模式往往還要開啟 G-Sync/FreeSync。"));

            if (!nvidia)
            {
                GuiControls.Hint(Loc.T(
                    "Fast Sync needs an NVIDIA GPU.",
                    "Fast Sync は NVIDIA の GPU が必要です。",
                    "Fast Sync에는 NVIDIA GPU가 필요합니다.",
                    "Fast Sync 需要 NVIDIA GPU。",
                    "Fast Sync 需要 NVIDIA GPU。"));
            }

            if (_vSyncRestartPending)
            {
                GuiControls.Hint(Loc.T(
                    "Restart VRCast for the driver display setting to take effect.",
                    "ドライバーの表示設定を反映するには VRCast を再起動してください。",
                    "드라이버 표시 설정을 적용하려면 VRCast를 다시 시작하세요.",
                    "请重启 VRCast 以使驱动显示设置生效。",
                    "請重新啟動 VRCast 以使驅動顯示設定生效。"));
            }

            if (!string.IsNullOrEmpty(_vSyncError))
            {
                GuiControls.Hint(Loc.T(
                    $"Could not update the NVIDIA display setting: {_vSyncError}",
                    $"NVIDIA の表示設定を書けませんでした: {_vSyncError}",
                    $"NVIDIA 표시 설정을 쓰지 못했습니다: {_vSyncError}",
                    $"无法写入 NVIDIA 显示设置：{_vSyncError}",
                    $"無法寫入 NVIDIA 顯示設定：{_vSyncError}"));
            }
        }

        private static int ModeToLabelIndex(VSyncMode mode, bool nvidia)
        {
            if (nvidia)
            {
                return mode == VSyncMode.Vrr ? 3
                    : mode == VSyncMode.FastSync ? 2
                    : (int)mode;
            }

            return mode == VSyncMode.Vrr ? 2 : mode == VSyncMode.On ? 1 : 0;
        }

        private static VSyncMode LabelIndexToMode(int index, bool nvidia)
        {
            if (nvidia)
            {
                return index == 3 ? VSyncMode.Vrr
                    : index == 2 ? VSyncMode.FastSync
                    : (VSyncMode)index;
            }

            return index == 2 ? VSyncMode.Vrr : index == 1 ? VSyncMode.On : VSyncMode.Off;
        }

        private void DrawCpu()
        {
            // CPU はメーカーごとに小見出しで分ける
            GuiControls.BeginCard(Loc.T("CPU settings", "CPU 設定", "CPU 설정", "CPU 设置", "CPU 設定"));
            GuiControls.SubHeading("AMD (X3D)");
            DrawCacheCcd();
            GuiControls.SubHeading(Loc.T("Intel (P-cores / E-cores)", "Intel（P コア / E コア）", "Intel (P 코어 / E 코어)",
                "Intel（P 核 / E 核）", "Intel（P 核 / E 核）"));
            DrawHybridCores();
            GuiControls.EndCard();
        }

        private void DrawGpuCard()
        {
            GuiControls.BeginCard(Loc.T("GPU settings", "GPU 設定", "GPU 설정", "GPU 设置", "GPU 設定"));
            DrawGpu();
            GuiControls.EndCard();
        }

        private void DrawPriority()
        {
            // 並びは ProcessPriority と同じ（ProcessTuner / TrackerProcess が変化を見て反映する）
            string[] labels =
            {
                Loc.T("Below normal", "通常以下", "보통 이하", "低于正常", "低於正常"),
                Loc.T("Normal", "通常", "보통", "正常", "正常"),
                Loc.T("Above normal", "通常以上", "보통 이상", "高于正常", "高於正常"),
                Loc.T("High", "高", "높음", "高", "高"),
            };
            _settings.processPriority = (ProcessPriority)GuiControls.EnumSelector(
                Loc.T("Process priority", "プロセスの優先度", "프로세스 우선순위", "进程优先级", "處理程序優先順序"),
                labels, (int)_settings.processPriority);
            GuiControls.Hint(Loc.T(
                "Applies to VRCast and the bundled tracker right away. Raise it if tracking stutters while a game is running " +
                "(High can make other apps slower).",
                "VRCast 本体と同梱トラッカーにすぐ反映されます。ゲーム中にトラッキングがカクつくときは上げてください" +
                "（「高」にすると他のアプリが重くなることがあります）。",
                "VRCast 본체와 내장 트래커에 바로 적용됩니다. 게임 중에 트래킹이 끊기면 올려 주세요" +
                "(「높음」으로 하면 다른 앱이 느려질 수 있습니다).",
                "立即应用于 VRCast 本体和内置追踪器。游戏时追踪卡顿可调高（设为“高”可能使其他应用变慢）。",
                "立即套用於 VRCast 本體和內建追蹤器。遊戲時追蹤卡頓可調高（設為「高」可能使其他應用程式變慢）。"));
        }

        private void DrawCacheCcd()
        {
            // 全ての PC に出し、効果があるのは 2 CCD の X3D だけと示す（ProcessTuner / TrackerProcess が変化を見て反映する）
            _settings.avoidCacheCcd = GUILayout.Toggle(_settings.avoidCacheCcd, Loc.T(
                "Run on the cores without 3D V-Cache", "3D V-Cache の無い側のコアで動かす",
                "3D V-Cache가 없는 쪽 코어에서 실행", "在没有 3D V-Cache 的核心上运行",
                "在沒有 3D V-Cache 的核心上執行"));
            GuiControls.Hint(Loc.T(
                "While a game runs, Windows moves apps onto the same cores as the game. This keeps VRCast and the bundled " +
                "tracker on the other cores so they do not compete with the game. Applies right away.",
                "ゲーム中は Windows がアプリをゲームと同じコアに寄せます。VRCast 本体と同梱トラッカーを反対側のコアで動かし、" +
                "ゲームとコアを取り合わないようにします。すぐに反映されます。",
                "게임 중에는 Windows가 앱을 게임과 같은 코어로 모읍니다. VRCast 본체와 내장 트래커를 반대쪽 코어에서 실행해 " +
                "게임과 코어를 다투지 않게 합니다. 바로 적용됩니다.",
                "游戏运行时，Windows 会把应用集中到与游戏相同的核心上。此选项让 VRCast 本体和内置追踪器在另一侧核心上运行，" +
                "避免与游戏争抢核心。立即生效。",
                "遊戲執行時，Windows 會把應用程式集中到與遊戲相同的核心上。此選項讓 VRCast 本體和內建追蹤器在另一側核心上執行，" +
                "避免與遊戲爭搶核心。立即生效。"));
            DrawCpuSupport(Loc.T(
                "Only takes effect on AMD X3D CPUs with 3D V-Cache on just one CCD (7950X3D / 9950X3D, etc.).",
                "3D V-Cache が片方の CCD にだけある AMD の X3D（7950X3D / 9950X3D など）でのみ効果があります。",
                "3D V-Cache가 한쪽 CCD에만 있는 AMD X3D(7950X3D / 9950X3D 등)에서만 효과가 있습니다.",
                "仅对 3D V-Cache 只位于一个 CCD 的 AMD X3D（7950X3D / 9950X3D 等）有效。",
                "僅對 3D V-Cache 只位於一個 CCD 的 AMD X3D（7950X3D / 9950X3D 等）有效。"),
                CpuTopology.HasAsymmetricCache);
        }

        private void DrawHybridCores()
        {
            // 全ての PC に出し、効果があるのは P コア / E コアのある CPU だけと示す。並びは HybridCoreSelection と同じ
            string[] labels =
            {
                Loc.T("Auto", "自動", "자동", "自动", "自動"),
                Loc.T("E-cores only", "E コアのみ", "E 코어만", "仅 E 核", "僅 E 核"),
                Loc.T("P-cores only", "P コアのみ", "P 코어만", "仅 P 核", "僅 P 核"),
            };
            _settings.hybridCores = (HybridCoreSelection)GuiControls.EnumSelector(
                Loc.T("Cores to use", "使うコア", "사용할 코어", "使用的核心", "使用的核心"),
                labels, (int)_settings.hybridCores);
            GuiControls.Hint(Loc.T(
                "Which cores VRCast and the bundled tracker run on. E-cores only leaves the P-cores to the game but may " +
                "make tracking and drawing slower. P-cores only is faster but competes with the game. Applies right away.",
                "VRCast 本体と同梱トラッカーを動かすコアを選びます。「E コアのみ」は P コアをゲームに譲りますが、" +
                "トラッキングや描画が遅くなることがあります。「P コアのみ」は速く動きますがゲームとコアを取り合います。すぐに反映されます。",
                "VRCast 본체와 내장 트래커를 실행할 코어를 고릅니다. 「E 코어만」은 P 코어를 게임에 양보하지만 트래킹과 렌더링이 " +
                "느려질 수 있습니다. 「P 코어만」은 빠르지만 게임과 코어를 다툽니다. 바로 적용됩니다.",
                "选择运行 VRCast 本体和内置追踪器的核心。“仅 E 核”把 P 核让给游戏，但追踪和渲染可能变慢。" +
                "“仅 P 核”速度快，但会与游戏争抢核心。立即生效。",
                "選擇執行 VRCast 本體和內建追蹤器的核心。「僅 E 核」把 P 核讓給遊戲，但追蹤和繪製可能變慢。" +
                "「僅 P 核」速度快，但會與遊戲爭搶核心。立即生效。"));
            DrawCpuSupport(Loc.T(
                "Only takes effect on Intel CPUs with P-cores and E-cores (12th gen or later, Core Ultra).",
                "P コアと E コアがある Intel の CPU（第 12 世代以降・Core Ultra）でのみ効果があります。",
                "P 코어와 E 코어가 있는 Intel CPU(12세대 이후, Core Ultra)에서만 효과가 있습니다.",
                "仅对具有 P 核和 E 核的 Intel CPU（第 12 代及以后、Core Ultra）有效。",
                "僅對具有 P 核和 E 核的 Intel CPU（第 12 代及以後、Core Ultra）有效。"),
                CpuTopology.IsHybrid);
        }

        private static void DrawCpuSupport(string target, bool supported)
        {
            // 対象の CPU の説明と、その下にこの PC が対象かどうかを別の行で
            GuiControls.Hint(target);
            GuiControls.Status(supported
                ? Loc.T("This PC: supported", "この PC: 対象です", "이 PC: 대상입니다", "本机：支持", "本機：支援")
                : Loc.T("This PC: not supported (no effect)", "この PC: 対象外です（効果はありません）",
                    "이 PC: 대상이 아닙니다 (효과 없음)", "本机：不支持（无效果）", "本機：不支援（無效果）"), supported);
        }

        private void DrawGpu()
        {
            // GPU の一覧は初回だけ取得（DXGI の列挙は毎フレームするほど軽くない）
            _adapters ??= GpuAdapters.Enumerate();

            // 候補: Windows の優先設定 3 種 + 各 GPU の名前（保存済みの GPU が見つからなければ末尾に残す）
            var labels = new List<string>
            {
                Loc.T("Auto (Windows decides)", "自動（Windows に任せる）", "자동 (Windows에 맡김)", "自动（由 Windows 决定）",
                    "自動（由 Windows 決定）"),
                Loc.T("Power saving", "省電力", "절전", "节能", "省電"),
                Loc.T("High performance", "高パフォーマンス", "고성능", "高性能", "高效能"),
            };
            var names = new List<string>();
            foreach (GpuAdapter adapter in _adapters)
            {
                names.Add(adapter.Name);
            }

            if (_settings.gpuAdapter.Length > 0 && !names.Contains(_settings.gpuAdapter))
            {
                names.Add(_settings.gpuAdapter);
            }

            foreach (string name in names)
            {
                labels.Add(name == _settings.gpuAdapter && GpuAdapters.IndexOf(_adapters, name) < 0
                    ? name + Loc.T(" (not found)", "（見つかりません）", " (찾을 수 없음)", "（未找到）", "（找不到）")
                    : name);
            }

            // 直接指定中はその GPU、それ以外は優先設定を選択中とする
            int current = _settings.gpuAdapter.Length > 0
                ? PresetCount + names.IndexOf(_settings.gpuAdapter)
                : (int)_settings.gpuPreference;
            int selected = GuiControls.EnumSelector(
                Loc.T("GPU for drawing", "描画に使う GPU", "렌더링에 사용할 GPU", "渲染使用的 GPU", "繪製使用的 GPU"),
                labels.ToArray(), current);

            // 変わったときだけ設定と Windows のレジストリへ書く
            if (selected != current)
            {
                bool preset = selected < PresetCount;
                _settings.gpuPreference = preset ? (GpuPreference)selected : GpuPreference.Auto;
                _settings.gpuAdapter = preset ? string.Empty : names[selected - PresetCount];
                GpuSelection.ApplyPreference(_settings);
            }

            // 今使っている GPU
            GuiControls.Hint(Loc.T("Current GPU", "現在の GPU", "현재 GPU", "当前 GPU", "目前的 GPU") +
                $": {SystemInfo.graphicsDeviceName}");

            // 直接指定に切り替えられなかった
            if (GpuSelection.AdapterNotApplied)
            {
                GuiControls.Hint(Loc.T(
                    "Could not switch to the selected GPU. Try Power saving or High performance instead.",
                    "指定した GPU に切り替えられませんでした。「省電力」か「高パフォーマンス」をお試しください。",
                    "지정한 GPU로 전환하지 못했습니다. 「절전」 또는 「고성능」을 사용해 보세요.",
                    "无法切换到指定的 GPU。请尝试“节能”或“高性能”。",
                    "無法切換到指定的 GPU。請嘗試「省電」或「高效能」。"));
            }

            // 変更は次回起動から（エディターでは再起動できない）
            if (GpuSelection.RestartPending(_settings) && !Application.isEditor)
            {
                GuiControls.Hint(Loc.T(
                    "Restart VRCast to apply the GPU change (no need to restart your PC).",
                    "GPU の変更は VRCast を起動し直すと反映されます（PC の再起動は不要です）。",
                    "GPU 변경은 VRCast를 다시 시작하면 적용됩니다(PC 재시작은 필요 없습니다).",
                    "重新启动 VRCast 后 GPU 更改生效（无需重启电脑）。",
                    "重新啟動 VRCast 後 GPU 變更生效（不需要重新啟動電腦）。"));
                DrawRestartButton();
            }

            GuiControls.Hint(Loc.T(
                "Only drawing uses the GPU (the bundled trackers run on the CPU). For OBS capture or the virtual camera, " +
                "using the same GPU as OBS is lightest.",
                "GPU を使うのは描画だけです（同梱トラッカーは CPU で動きます）。OBS のキャプチャや仮想カメラを使うときは、" +
                "OBS と同じ GPU にすると最も軽くなります。",
                "GPU는 렌더링에만 사용됩니다(내장 트래커는 CPU로 동작). OBS 캡처나 가상 카메라를 쓸 때는 OBS와 같은 GPU가 가장 가볍습니다.",
                "只有渲染使用 GPU（内置追踪器在 CPU 上运行）。使用 OBS 捕获或虚拟摄像头时，与 OBS 使用同一 GPU 最省资源。",
                "只有繪製使用 GPU（內建追蹤器在 CPU 上執行）。使用 OBS 擷取或虛擬攝影機時，與 OBS 使用同一 GPU 最省資源。"));
        }

        private void DrawNvidiaOverlay()
        {
            // NVIDIA の GPU・ドライバーが無い PC では出さない
            _overlayState ??= NvidiaOverlayExclusion.Query();
            if (_overlayState == NvidiaOverlayExclusion.State.Unavailable)
            {
                return;
            }

            GuiControls.BeginCard(Loc.T("NVIDIA Instant Replay", "NVIDIA インスタントリプレイ", "NVIDIA 인스턴트 리플레이",
                "NVIDIA 即时重放", "NVIDIA 即時重播"));
            GuiControls.Hint(Loc.T(
                "Keeps NVIDIA Instant Replay (ShadowPlay) from detecting VRCast as a game. It writes a VRCast profile " +
                "into the NVIDIA driver settings. This uses an unofficial setting and may not work on some drivers.",
                "VRCast が NVIDIA のインスタントリプレイ（ShadowPlay）にゲームとして検知されないようにします。" +
                "NVIDIA のドライバー設定に VRCast 用のプロファイルを書きます。非公式の設定のため、ドライバーによっては効きません。",
                "VRCast가 NVIDIA 인스턴트 리플레이(ShadowPlay)에 게임으로 감지되지 않도록 합니다. " +
                "NVIDIA 드라이버 설정에 VRCast용 프로필을 씁니다. 비공식 설정이라 드라이버에 따라 효과가 없을 수 있습니다.",
                "使 VRCast 不被 NVIDIA 即时重放（ShadowPlay）识别为游戏。会在 NVIDIA 驱动设置中写入 VRCast 专用配置文件。" +
                "这是非官方设置，部分驱动可能无效。",
                "使 VRCast 不被 NVIDIA 即時重播（ShadowPlay）視為遊戲。會在 NVIDIA 驅動程式設定中寫入 VRCast 專用設定檔。" +
                "這是非官方設定，部分驅動程式可能無效。"));

            // 現在の状態
            bool excluded = _overlayState == NvidiaOverlayExclusion.State.Excluded;
            GuiControls.Hint(excluded
                ? Loc.T("Status: excluded", "状態: 検知させない設定済み", "상태: 감지 제외됨", "状态：已排除", "狀態：已排除")
                : Loc.T("Status: not excluded", "状態: 未設定", "상태: 설정 안 됨", "状态：未设置", "狀態：未設定"));

            // エディターでは Unity.exe を登録してしまうので押せない
            if (Application.isEditor)
            {
                GuiControls.Hint(Loc.T("Only available in the built app.", "ビルドした VRCast でのみ使えます。",
                    "빌드한 VRCast에서만 사용할 수 있습니다.", "仅在构建后的 VRCast 中可用。", "僅在建置後的 VRCast 中可用。"));
                GuiControls.EndCard();
                return;
            }

            // 押したときだけドライバー設定を書く / 消す
            bool pressed = excluded
                ? GUILayout.Button(Loc.T("Let NVIDIA Instant Replay detect VRCast again",
                    "NVIDIA のインスタントリプレイの検知を元に戻す", "NVIDIA 인스턴트 리플레이 감지 되돌리기",
                    "恢复 NVIDIA 即时重放的检测", "還原 NVIDIA 即時重播的偵測"))
                : GUILayout.Button(Loc.T("Hide VRCast from NVIDIA Instant Replay",
                    "NVIDIA のインスタントリプレイに検知させない", "NVIDIA 인스턴트 리플레이가 감지하지 않게 하기",
                    "不让 NVIDIA 即时重放检测", "不讓 NVIDIA 即時重播偵測"));
            if (pressed)
            {
                _overlayError = excluded ? NvidiaOverlayExclusion.Restore() : NvidiaOverlayExclusion.Exclude();
                _overlayRestartPending |= _overlayError == null;
                _overlayState = NvidiaOverlayExclusion.Query();
            }

            // 失敗したら理由と、管理者として実行する案内
            if (_overlayError != null)
            {
                GuiControls.Hint(Loc.T(
                    "Could not change the driver settings. Try running VRCast as administrator.",
                    "ドライバー設定を変更できませんでした。VRCast を管理者として実行してお試しください。",
                    "드라이버 설정을 변경하지 못했습니다. VRCast를 관리자 권한으로 실행해 보세요.",
                    "无法更改驱动设置。请尝试以管理员身份运行 VRCast。",
                    "無法變更驅動程式設定。請嘗試以系統管理員身分執行 VRCast。") + $" ({_overlayError})");
            }

            // 設定は起動時にしか読まれないので再起動を促す
            if (_overlayRestartPending)
            {
                GuiControls.Hint(Loc.T("Restart VRCast to apply the change.", "VRCast を起動し直すと反映されます。",
                    "VRCast를 다시 시작하면 적용됩니다.", "重新启动 VRCast 后生效。", "重新啟動 VRCast 後生效。"));
                DrawRestartButton();
            }

            GuiControls.EndCard();
        }

        private static void DrawRestartButton()
        {
            if (GUILayout.Button(Loc.T("Restart VRCast now", "VRCast を今すぐ起動し直す", "VRCast 지금 다시 시작",
                    "立即重新启动 VRCast", "立即重新啟動 VRCast")))
            {
                // 新しい起動が古い設定を読まないよう先に保存する
                AppBootstrap.Save();
                GpuSelection.Restart();
            }
        }
    }
}
