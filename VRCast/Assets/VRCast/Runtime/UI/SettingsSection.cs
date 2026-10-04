using System;
using UnityEngine;
using VRCast.Core;
using VRCast.Platform;
using VRCast.Rendering;

namespace VRCast.UI
{
    /// <summary>
    /// Settings タブ（表示言語、UI の大きさ、テーマ（ライト / ダーク）、軽量モード、アップデートの確認、ヘルプ、全設定のリセット（2 段階確認）、バージョン情報）。
    /// </summary>
    public class SettingsSection
    {
        // UI の大きさのプリセット（スライダーだとドラッグ中にパネルが伸縮して操作しにくいためボタンで選ぶ）
        private static readonly float[] ScalePresets = { 0.75f, 1f, 1.25f, 1.5f, 2f };

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

            GuiControls.EndCard();
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
                "Restore every setting to its default (window size and the last avatar are kept)",
                "全ての設定を初期状態に戻します（ウィンドウサイズと最後に開いたアバターは残ります）",
                "모든 설정을 초기 상태로 되돌립니다 (창 크기와 마지막으로 연 아바타는 유지됩니다)",
                "将所有设置恢复为默认值（窗口大小和上次打开的虚拟形象会保留）",
                "將所有設定恢復為預設值（視窗大小和上次開啟的虛擬形象會保留）"));

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
