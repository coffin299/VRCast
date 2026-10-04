using System;
using UnityEngine;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// Settings タブ（表示言語、UI の大きさ、ヘルプ、全設定のリセット（2 段階確認）、バージョン情報）。
    /// </summary>
    public class SettingsSection
    {
        // UI の大きさのプリセット（スライダーだとドラッグ中にパネルが伸縮して操作しにくいためボタンで選ぶ）
        private static readonly float[] ScalePresets = { 0.75f, 1f, 1.25f, 1.5f, 2f };

        private readonly AppSettings _settings;
        private readonly Action _resetAll;

        // リセットの確認中か、直前にリセットしたか
        private bool _confirmingReset;
        private bool _resetDone;

        /// <param name="resetAll">全設定を既定値に戻して各機能へ反映する処理</param>
        public SettingsSection(AppSettings settings, Action resetAll)
        {
            _settings = settings;
            _resetAll = resetAll;
        }

        public void Draw()
        {
            DrawLanguage();
            DrawScale();
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
                if (GUILayout.Toggle(selected, $"{preset * 100f:F0}%", GUI.skin.button) && !selected)
                {
                    _settings.uiScale = preset;
                }
            }

            GUILayout.EndHorizontal();
            GuiControls.EndCard();
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
