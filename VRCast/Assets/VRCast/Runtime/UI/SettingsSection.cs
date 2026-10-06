using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Core;
using VRCast.Platform;
using VRCast.Rendering;

namespace VRCast.UI
{
    /// <summary>
    /// Settings タブ（表示言語、UI の大きさ、テーマ（ライト / ダーク）、軽量モード・プロセスの優先度・描画に使う GPU、
    /// アップデートの確認、ヘルプ、全設定のリセット（2 段階確認）、バージョン情報）。
    /// </summary>
    public class SettingsSection
    {
        // UI の大きさのプリセット（スライダーだとドラッグ中にパネルが伸縮して操作しにくいためボタンで選ぶ）
        private static readonly float[] ScalePresets = { 0.75f, 1f, 1.25f, 1.5f, 2f };

        // GPU の候補のうち、Windows の優先設定（GpuPreference）の数（以降が GPU の直接指定）
        private const int PresetCount = 3;

        // GPU の一覧（初回表示時に取得）
        private List<GpuAdapter> _adapters;

        private readonly AppSettings _settings;
        private readonly RenderingController _rendering;
        private readonly UpdateChecker _updates;
        private readonly Action _resetAll;

        // リセットの確認中か、直前にリセットしたか
        private bool _confirmingReset;
        private bool _resetDone;

        /// <param name="resetAll">全設定を既定値に戻して各機能へ反映する処理</param>
        public SettingsSection(AppSettings settings, RenderingController rendering, UpdateChecker updates, Action resetAll)
        {
            _settings = settings;
            _rendering = rendering;
            _updates = updates;
            _resetAll = resetAll;
        }

        public void Draw()
        {
            DrawLanguage();
            DrawScale();
            DrawTheme();
            DrawPerformance();
            DrawUpdates();
            DrawHelp();
            DrawReset();
            DrawAbout();
        }

        private void DrawLanguage()
        {
            GuiControls.BeginCard(Loc.T("Language", "言語", "언어", "语言", "語言"));

            // Auto は OS の言語（日本語・韓国語・中国語以外は英語）。並びは UiLanguage と同じ
            string[] labels = Loc.LanguageLabels(
                Loc.T("Auto (OS)", "自動（OS に合わせる）", "자동 (OS에 맞춤)", "自动（跟随系统）", "自動（跟隨系統）"));
            _settings.uiLanguage = (UiLanguage)GuiControls.EnumSelector(
                Loc.T("Display language", "表示言語", "표시 언어", "显示语言", "顯示語言"),
                labels, (int)_settings.uiLanguage);

            GuiControls.EndCard();
        }

        private void DrawScale()
        {
            GuiControls.BeginCard(Loc.T("UI size", "UI の大きさ", "UI 크기", "界面大小", "介面大小"));
            GUILayout.BeginHorizontal();

            // 選択中のプリセットはアクセント色（押すとその倍率にする）
            foreach (float preset in ScalePresets)
            {
                bool selected = Mathf.Approximately(_settings.uiScale, preset);
                if (GUILayout.Toggle(selected, $"{preset * 100f:F0}%", GUI.skin.button, GuiControls.Shrinkable) && !selected)
                {
                    _settings.uiScale = preset;
                }
            }

            GUILayout.EndHorizontal();
            GuiControls.EndCard();
        }

        private void DrawTheme()
        {
            GuiControls.BeginCard(Loc.T("Theme", "テーマ", "테마", "主题", "主題"));
            GUILayout.BeginHorizontal();

            // 選択中はアクセント色（パネルの配色は次の Layout で MainPanel が作り直す。背景色は既定色のときだけ追従）
            bool dark = _rendering.DarkMode;
            if (GUILayout.Toggle(!dark, Loc.T("Light", "ライト", "라이트", "浅色", "淺色"), GUI.skin.button,
                    GuiControls.Shrinkable) && dark)
            {
                _rendering.DarkMode = false;
            }

            if (GUILayout.Toggle(dark, Loc.T("Dark", "ダーク", "다크", "深色", "深色"), GUI.skin.button,
                    GuiControls.Shrinkable) && !dark)
            {
                _rendering.DarkMode = true;
            }

            GUILayout.EndHorizontal();
            GuiControls.Hint(Loc.T(
                "The background color follows the theme unless you changed it in the Display tab",
                "背景色は、表示タブで変更していなければテーマに合わせて切り替わります",
                "배경색은 표시 탭에서 바꾸지 않았다면 테마에 맞춰 바뀝니다",
                "如果未在显示标签页中更改背景色，背景色会随主题切换",
                "若未在顯示分頁中變更背景色，背景色會隨主題切換"));
            GuiControls.EndCard();
        }

        private void DrawPerformance()
        {
            GuiControls.BeginCard(Loc.T("Performance", "動作の軽さ", "성능", "性能", "效能"));

            // 値が変わったときだけ反映（トラッカーは TrackerProcess が設定の変化を見て再起動する）
            bool lowLoad = GUILayout.Toggle(_rendering.LowLoadMode, Loc.T(
                "Low load mode", "軽量モード", "저부하 모드", "低负载模式", "低負載模式"));
            if (lowLoad != _rendering.LowLoadMode)
            {
                _rendering.LowLoadMode = lowLoad;
            }

            GuiControls.Hint(Loc.T(
                $"Use this with games or OBS: drawing is limited to {RenderingController.LowLoadFrameRate} fps " +
                $"(normally {RenderingController.NormalFrameRate}) and the bundled tracker runs lighter. " +
                "Tracking becomes slightly less smooth.",
                $"ゲームや OBS と同時に使うときに。描画を {RenderingController.LowLoadFrameRate}fps " +
                $"（通常は {RenderingController.NormalFrameRate}fps）に抑え、同梱トラッカーの処理も軽くします。" +
                "トラッキングの滑らかさは少し下がります。",
                $"게임이나 OBS와 함께 쓸 때 사용하세요. 화면을 {RenderingController.LowLoadFrameRate}fps" +
                $"(평소 {RenderingController.NormalFrameRate}fps)로 제한하고 내장 트래커의 처리도 가볍게 합니다. " +
                "트래킹의 부드러움은 조금 떨어집니다.",
                $"与游戏或 OBS 同时使用时开启。将画面限制为 {RenderingController.LowLoadFrameRate}fps" +
                $"（通常为 {RenderingController.NormalFrameRate}fps），并减轻内置追踪器的处理。追踪的流畅度会略有下降。",
                $"與遊戲或 OBS 同時使用時開啟。將畫面限制為 {RenderingController.LowLoadFrameRate}fps" +
                $"（通常為 {RenderingController.NormalFrameRate}fps），並減輕內建追蹤器的處理。追蹤的流暢度會略有下降。"));

            DrawPriority();
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
                if (GUILayout.Button(Loc.T("Restart VRCast now", "VRCast を今すぐ起動し直す", "VRCast 지금 다시 시작",
                        "立即重新启动 VRCast", "立即重新啟動 VRCast")))
                {
                    // 新しい起動が古い設定を読まないよう先に保存する
                    AppBootstrap.Save();
                    GpuSelection.Restart();
                }
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

        private void DrawUpdates()
        {
            GuiControls.BeginCard(Loc.T("Updates", "アップデート", "업데이트", "更新", "更新"));

            // ON にしたらその場で確認する（OFF の間は通信しない）
            bool check = GUILayout.Toggle(_settings.checkForUpdates, Loc.T(
                "Check for updates at startup", "起動時に新しいバージョンを確認する", "시작할 때 새 버전 확인",
                "启动时检查新版本", "啟動時檢查新版本"));
            if (check != _settings.checkForUpdates)
            {
                _settings.checkForUpdates = check;
                if (check)
                {
                    _updates.Check();
                }
            }

            GuiControls.Hint(Loc.T(
                "Connects to the VRCast website (coffin299.github.io) only to read the latest version number",
                "最新のバージョン番号を読むためだけに VRCast の Web サイト（coffin299.github.io）へ接続します",
                "최신 버전 번호를 읽기 위해서만 VRCast 웹사이트(coffin299.github.io)에 접속합니다",
                "仅为读取最新版本号而连接 VRCast 网站（coffin299.github.io）",
                "僅為讀取最新版本號而連線 VRCast 網站（coffin299.github.io）"));
            GuiControls.Hint(UpdateStatus());

            // 新しいバージョンがあれば（通知しないことにしたものでも）ここから開ける
            if (_updates.IsUpdateAvailable)
            {
                UpdateDownloadButtons.Draw(_updates);
            }

            GuiControls.EndCard();
        }

        private string UpdateStatus()
        {
            // 確認の状態を表示言語で返す
            switch (_updates.State)
            {
                case UpdateChecker.CheckState.Checking:
                    return Loc.T("Checking...", "確認中...", "확인 중...", "正在检查...", "正在檢查...");
                case UpdateChecker.CheckState.Failed:
                    return Loc.T("Could not check (offline?)", "確認できませんでした（オフライン？）",
                        "확인하지 못했습니다 (오프라인?)", "无法检查（是否离线？）", "無法檢查（是否離線？）");
                case UpdateChecker.CheckState.Done:
                    return _updates.IsUpdateAvailable
                        ? Loc.T("New version", "新しいバージョン", "새 버전", "新版本", "新版本") + $": {_updates.LatestVersion}"
                        : Loc.T("You are using the latest version", "最新のバージョンです", "최신 버전입니다",
                            "已是最新版本", "已是最新版本");
                default:
                    return Loc.T("Not checked", "未確認", "확인 안 함", "未检查", "未檢查");
            }
        }

        private void DrawHelp()
        {
            GuiControls.BeginCard(Loc.T("Help", "ヘルプ", "도움말", "帮助", "說明"));
            GuiControls.Hint(Loc.T("Step-by-step guide and troubleshooting (opens in your browser)",
                "使い方の手順とトラブルシューティング（ブラウザで開きます）",
                "사용 방법과 문제 해결 (브라우저에서 열립니다)",
                "使用步骤与故障排除（在浏览器中打开）",
                "使用步驟與疑難排解（在瀏覽器中開啟）"));

            if (GUILayout.Button(Loc.T("Open help", "ヘルプを開く", "도움말 열기", "打开帮助", "開啟說明")))
            {
                HelpPage.Open();
            }

            GuiControls.EndCard();
        }

        private void DrawReset()
        {
            GuiControls.BeginCard(Loc.T("Reset", "リセット", "초기화", "重置", "重設"));
            GuiControls.Hint(Loc.T(
                "Restore every setting to its default (window size, recent avatars, each avatar's camera and " +
                "other avatars' light and pose are kept)",
                "全ての設定を初期状態に戻します（ウィンドウサイズ・最近使ったアバター・アバターごとのカメラと、" +
                "表示中以外のアバターに記憶したライト・待機ポーズは残ります）",
                "모든 설정을 초기 상태로 되돌립니다 (창 크기, 최근 사용한 아바타, 아바타별 카메라와 " +
                "표시 중이 아닌 아바타에 기억한 조명·대기 포즈는 유지됩니다)",
                "将所有设置恢复为默认值（窗口大小、最近使用的虚拟形象、各虚拟形象的相机，" +
                "以及当前未显示的虚拟形象记住的灯光和待机姿势会保留）",
                "將所有設定恢復為預設值（視窗大小、最近使用的虛擬形象、各虛擬形象的相機，" +
                "以及目前未顯示的虛擬形象記住的燈光和待機姿勢會保留）"));

            // 1 段階目: リセットを押すと確認を出す
            if (!_confirmingReset)
            {
                if (GuiControls.DangerButton(Loc.T("Reset all settings", "全ての設定をリセット", "모든 설정 초기화",
                        "重置所有设置", "重設所有設定")))
                {
                    _confirmingReset = true;
                    _resetDone = false;
                }

                // 直前のリセット結果
                if (_resetDone)
                {
                    GuiControls.Hint(Loc.T("All settings were reset.", "全ての設定をリセットしました。",
                        "모든 설정을 초기화했습니다.", "已重置所有设置。", "已重設所有設定。"));
                }

                GuiControls.EndCard();
                return;
            }

            // 2 段階目: 本当にリセットするか確認
            GUILayout.Label(Loc.T("Are you sure? This cannot be undone.", "本当にリセットしますか？元に戻せません。",
                "정말 초기화하시겠습니까? 되돌릴 수 없습니다.", "确定要重置吗？此操作无法撤销。", "確定要重設嗎？此操作無法復原。"));
            GUILayout.BeginHorizontal();
            if (GuiControls.DangerButton(Loc.T("Yes, reset", "リセットする", "초기화하기", "确定重置", "確定重設")))
            {
                _resetAll();
                _confirmingReset = false;
                _resetDone = true;
            }

            if (GUILayout.Button(Loc.T("Cancel", "キャンセル", "취소", "取消", "取消")))
            {
                _confirmingReset = false;
            }

            GUILayout.EndHorizontal();
            GuiControls.EndCard();
        }

        private void DrawAbout()
        {
            GuiControls.BeginCard(Loc.T("About", "このアプリについて", "이 앱에 대하여", "关于本应用", "關於本應用程式"));
            GuiControls.Hint($"VRCast {Application.version}");
            GuiControls.Hint(Loc.T(
                "Tab key: show / hide this panel (the background is transparent in OBS while hidden)",
                "Tab キー: このパネルの表示 / 非表示（隠している間は OBS で背景も透過）",
                "Tab 키: 이 패널 표시 / 숨기기 (숨긴 동안에는 OBS에서 배경도 투명)",
                "Tab 键：显示 / 隐藏此面板（隐藏期间 OBS 中背景也会透明）",
                "Tab 鍵：顯示 / 隱藏此面板（隱藏期間 OBS 中背景也會透明）"));
            GuiControls.Hint(Loc.T("Settings are saved when the app closes", "設定は終了時に保存されます",
                "설정은 종료할 때 저장됩니다", "设置会在退出时保存", "設定會在結束時儲存"));
            GuiControls.EndCard();
        }
    }
}
