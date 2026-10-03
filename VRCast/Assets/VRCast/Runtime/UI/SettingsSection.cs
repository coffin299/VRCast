using UnityEngine;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// Settings タブ（表示言語、UI の大きさ、バージョン情報）。
    /// </summary>
    public class SettingsSection
    {
        // UI の大きさのプリセット（スライダーだとドラッグ中にパネルが伸縮して操作しにくいためボタンで選ぶ）
        private static readonly float[] ScalePresets = { 0.75f, 1f, 1.25f, 1.5f, 2f };

        private readonly AppSettings _settings;

        public SettingsSection(AppSettings settings)
        {
            _settings = settings;
        }

        public void Draw()
        {
            DrawLanguage();
            DrawScale();
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
