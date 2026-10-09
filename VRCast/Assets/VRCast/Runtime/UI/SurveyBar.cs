using UnityEngine;

namespace VRCast.UI
{
    /// <summary>
    /// パネル下部に常に表示するアンケート欄（次に対応してほしい機能などの募集）。
    /// ほかのカードと同じ見た目で、GitHub の Issue か Google フォームへ誘導する。すぐ下に公式 Discord のボタンを出す。
    /// </summary>
    public class SurveyBar
    {
        // 回答先（GitHub の Issue / Google フォーム）
        private const string GitHubIssueUrl = "https://github.com/coffin299/VRCast/issues/11";
        private const string GoogleFormUrl = "https://forms.gle/L8z1P6SXZ23SnwWa8";

        // 公式 Discord サーバーの招待
        private const string DiscordUrl = "https://discord.gg/vM5RH52HdF";

        public void Draw()
        {
            UiTheme theme = UiTheme.Current;
            GUILayout.BeginVertical(theme.SurveyCard);
            GUILayout.Label(Loc.T("★ Survey", "★ アンケート", "★ 설문", "★ 问卷", "★ 問卷"), theme.SurveyBadge);
            GUILayout.Label(Loc.T(
                "We're collecting ideas for what VRCast should support next. Feel free to write anything via the links below.",
                "VRCast で次に対応してほしい機能などを募集しています。下のリンクから自由にいろいろ書いてくださると幸いです。",
                "VRCast에서 다음에 지원했으면 하는 기능 등을 모집하고 있습니다. 아래 링크에서 자유롭게 적어 주시면 감사하겠습니다.",
                "我们正在征集希望 VRCast 接下来支持的功能等建议。欢迎通过下方链接自由填写。",
                "我們正在徵集希望 VRCast 接下來支援的功能等建議。歡迎透過下方連結自由填寫。"), theme.SurveyText);

            // 回答先を横に並べる（幅が足りなければボタンが縮む）
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Write on GitHub Issues", "GitHub の Issue に書く", "GitHub Issue에 쓰기",
                    "在 GitHub Issue 中填写", "在 GitHub Issue 中填寫"), GUI.skin.button, GuiControls.Shrinkable))
            {
                Application.OpenURL(GitHubIssueUrl);
            }

            if (GUILayout.Button(Loc.T("Answer on Google Forms", "Google フォームで答える", "Google 폼으로 답하기",
                    "通过 Google 表单填写", "透過 Google 表單填寫"), GUI.skin.button, GuiControls.Shrinkable))
            {
                Application.OpenURL(GoogleFormUrl);
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            // アンケート欄のすぐ下に公式 Discord への招待
            if (GUILayout.Button(Loc.T("Join the official Discord", "公式 Discord に参加", "공식 Discord 참여",
                    "加入官方 Discord", "加入官方 Discord"), GuiControls.Shrinkable))
            {
                Application.OpenURL(DiscordUrl);
            }
        }
    }
}
