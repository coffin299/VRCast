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
            GuiControls.BeginCard(Loc.T("Language", "言語"));

            // Auto は OS の言語（日本語以外は英語）
            string[] labels = { Loc.T("Auto (OS)", "自動（OS に合わせる）"), "English", "日本語" };
            _settings.uiLanguage = (UiLanguage)GuiControls.EnumSelector(
                Loc.T("Display language", "表示言語"), labels, (int)_settings.uiLanguage);

            GuiControls.EndCard();
        }

        private void DrawScale()
        {
            GuiControls.BeginCard(Loc.T("UI size", "UI の大きさ"));
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
            GuiControls.BeginCard(Loc.T("Help", "ヘルプ"));
            GuiControls.Hint(Loc.T("Step-by-step guide and troubleshooting (opens in your browser)",
                "使い方の手順とトラブルシューティング（ブラウザで開きます）"));

            if (GUILayout.Button(Loc.T("Open help", "ヘルプを開く")))
            {
                HelpPage.Open();
            }

            GuiControls.EndCard();
        }

        private void DrawReset()
        {
            GuiControls.BeginCard(Loc.T("Reset", "リセット"));
            GuiControls.Hint(Loc.T(
                "Restore every setting to its default (window size and the last avatar are kept)",
                "全ての設定を初期状態に戻します（ウィンドウサイズと最後に開いたアバターは残ります）"));

            // 1 段階目: リセットを押すと確認を出す
            if (!_confirmingReset)
            {
                if (GuiControls.DangerButton(Loc.T("Reset all settings", "全ての設定をリセット")))
                {
                    _confirmingReset = true;
                    _resetDone = false;
                }

                // 直前のリセット結果
                if (_resetDone)
                {
                    GuiControls.Hint(Loc.T("All settings were reset.", "全ての設定をリセットしました。"));
                }

                GuiControls.EndCard();
                return;
            }

            // 2 段階目: 本当にリセットするか確認
            GUILayout.Label(Loc.T("Are you sure? This cannot be undone.", "本当にリセットしますか？元に戻せません。"));
            GUILayout.BeginHorizontal();
            if (GuiControls.DangerButton(Loc.T("Yes, reset", "リセットする")))
            {
                _resetAll();
                _confirmingReset = false;
                _resetDone = true;
            }

            if (GUILayout.Button(Loc.T("Cancel", "キャンセル")))
            {
                _confirmingReset = false;
            }

            GUILayout.EndHorizontal();
            GuiControls.EndCard();
        }

        private void DrawAbout()
        {
            GuiControls.BeginCard(Loc.T("About", "このアプリについて"));
            GuiControls.Hint($"VRCast {Application.version}");
            GuiControls.Hint(Loc.T("Tab key: show / hide this panel", "Tab キー: このパネルの表示 / 非表示"));
            GuiControls.Hint(Loc.T("Settings are saved when the app closes", "設定は終了時に保存されます"));
            GuiControls.EndCard();
        }
    }
}
