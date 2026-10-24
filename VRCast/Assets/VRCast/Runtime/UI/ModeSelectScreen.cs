using UnityEngine;
using VRCast.Core;
using VRCast.Mascot;

namespace VRCast.UI
{
    /// <summary>
    /// 起動時の画面。上に言語、中央に大きいモードボタン、一番下に説明。
    /// マスコットを選んでいる間もこの画面は残り、その下に設定が出る。
    /// </summary>
    public static class ModeSelectScreen
    {
        public static int Draw(AppSettings settings, UiTheme theme, float width, float height, bool mascot,
            MascotMode mode)
        {
            int pick = 0;
            GUILayout.BeginArea(new Rect(0f, 0f, width, height));
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.Space(18f);
            DrawLanguages(settings, theme);
            GUILayout.Space(mascot ? 8f : 0f);
            if (!mascot)
            {
                GUILayout.FlexibleSpace();
            }

            pick = DrawModes(theme, width, mascot);
            if (mascot)
            {
                GUILayout.Space(8f);
                mode.Draw(height);
            }
            else
            {
                GUILayout.FlexibleSpace();
            }

            DrawDescription(theme, width);
            GUILayout.Space(14f);
            GUILayout.EndVertical();
            GUILayout.EndArea();
            return pick;
        }

        private static void DrawLanguages(AppSettings settings, UiTheme theme)
        {
            string[] labels = Loc.LanguageLabels(Loc.T("Auto", "自動", "자동", "自动", "自動"));
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            for (int i = 0; i < labels.Length; i++)
            {
                bool selected = (int)settings.uiLanguage == i;
                if (GUILayout.Toggle(selected, labels[i], theme.OverflowButton, GUILayout.MinWidth(0f)) && !selected)
                {
                    settings.uiLanguage = (UiLanguage)i;
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static int DrawModes(UiTheme theme, float width, bool mascot)
        {
            float buttonWidth = Mathf.Clamp((width - 80f) * 0.42f, 180f, 360f);
            int pick = 0;
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Toggle(false, Loc.T("Broadcast\nStream the avatar", "配信モード\nアバターを配信",
                    "방송 모드\n아바타를 송출", "直播模式\n播出虚拟形象", "直播模式\n播出虛擬形象"),
                    theme.ModeButton, GUILayout.Width(buttonWidth), GUILayout.Height(108f)))
            {
                pick = 1;
            }

            GUILayout.Space(16f);
            if (GUILayout.Toggle(mascot, Loc.T("Mascot\nKeep on the desktop", "マスコットモード\nデスクトップに置く",
                    "마스코트 모드\n바탕화면에 두기", "吉祥物模式\n放在桌面上", "吉祥物模式\n放在桌面上"),
                    theme.FeatureModeButton, GUILayout.Width(buttonWidth), GUILayout.Height(108f)) && !mascot)
            {
                pick = 2;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return pick;
        }

        private static void DrawDescription(UiTheme theme, float width)
        {
            float panel = Mathf.Min(width - 36f, 820f);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(theme.Card, GUILayout.Width(panel));
            GUILayout.Label(Loc.T(
                "Broadcast is the usual VRCast screen: send the avatar to a virtual camera or Spout.",
                "配信モードは今までの VRCast です。アバターを仮想カメラや Spout に出して配信します。",
                "방송 모드는 지금까지의 VRCast입니다. 아바타를 가상 카메라나 Spout으로 내보내 송출합니다.",
                "直播模式就是原来的 VRCast。把虚拟形象送到虚拟摄像头或 Spout 进行播出。",
                "直播模式就是原來的 VRCast。把虛擬形象送到虛擬攝影機或 Spout 進行播出。"), theme.Hint);
            GUILayout.Label(Loc.T(
                "Mascot keeps this screen and shows the avatar in another window. It looks toward a nearby cursor, " +
                "talks with a local language model such as LM Studio, and speaks with COEIROINK (recommended) or VOICEVOX. " +
                "With COEIROINK you can train your own voice and use it here. Training is done in COEIROINK.",
                "マスコットモードではこの画面を残し、アバターを別のウィンドウに出します。近くのカーソルの方を向き、" +
                "LM Studio などのローカル言語モデルと会話します。返事は COEIROINK（推奨）か VOICEVOX が読み上げます。" +
                "COEIROINK なら自分の声を学習させて、その声で話させられます。学習は COEIROINK 側で行います。",
                "마스코트 모드는 이 화면을 남기고 아바타를 다른 창에 띄웁니다. 가까운 커서를 바라보고, " +
                "LM Studio 같은 로컬 언어 모델과 대화합니다. 대답은 COEIROINK(권장)나 VOICEVOX가 읽어 줍니다. " +
                "COEIROINK에서는 자신의 목소리를 학습시켜 그 목소리로 말하게 할 수 있습니다. 학습은 COEIROINK에서 합니다.",
                "吉祥物模式会留下这个画面，并把虚拟形象显示在另一个窗口。光标靠近时会看向光标，" +
                "并与 LM Studio 等本地语言模型对话。回答由 COEIROINK（推荐）或 VOICEVOX 朗读。" +
                "COEIROINK 可以训练自己的声音，再用那个声音说话。训练在 COEIROINK 里进行。",
                "吉祥物模式會留下這個畫面，並把虛擬形象顯示在另一個視窗。游標靠近時會看向游標，" +
                "並與 LM Studio 等本地語言模型對話。回答由 COEIROINK（推薦）或 VOICEVOX 朗讀。" +
                "COEIROINK 可以訓練自己的聲音，再用那個聲音說話。訓練在 COEIROINK 裡進行。"), theme.Hint);
            GUILayout.Label("VRCast v" + Application.version, theme.Hint);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }
}
