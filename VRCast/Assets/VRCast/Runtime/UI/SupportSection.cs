using UnityEngine;

namespace VRCast.UI
{
    /// <summary>
    /// サポートタブ。操作の質問と不具合の報告先として、Discord のリンクを欄の中に出す。
    /// </summary>
    public sealed class SupportSection
    {
        // サポート用 Discord（同じタブの下に出す公式 Discord の招待とは別）
        private const string InviteCode = "zyf99zZNUx";
        private const string InviteUrl = "https://discord.gg/" + InviteCode;

        public void Draw()
        {
            UiTheme theme = UiTheme.Current;
            GuiControls.BeginCard(Loc.T("Support", "サポート", "지원", "支持", "支援"));
            GuiControls.Hint(Loc.T(
                "If you're not sure how to do something, it isn't working the way you expected, or you want to report a bug, this is the place. Questions about how to use VRCast are welcome too.",
                "操作がわからないとき、思ったとおりに動かないとき、不具合を報告したいときは、こちらへどうぞ。使い方の質問も、そのまま書いてください。",
                "조작이 헷갈리거나, 생각한 대로 움직이지 않거나, 버그를 알리고 싶을 때는 여기로 와 주세요. 사용 방법 질문도 그대로 남겨 주세요.",
                "如果不知道怎么操作、没有按预期动作，或想报告问题，请来这里。使用方法的问题也可以直接写下来。",
                "如果不知道怎麼操作、沒有按預期動作，或想回報問題，請來這裡。使用方法的問題也可以直接寫下來。"));
            GuiControls.Hint(Loc.T(
                "Join from the link below. In the Discord app, you can also choose \"Join a server\" and paste the link or the invite code.",
                "参加は下のリンクから開いてください。Discord アプリの「サーバーに参加」に、リンクか招待コードを貼っても開けます。",
                "참여는 아래 링크에서 열어 주세요. Discord 앱의 \"서버 참가하기\"에 링크나 초대 코드를 붙여 넣어도 열 수 있습니다.",
                "请从下面的链接加入。也可以在 Discord 应用的“加入服务器”中粘贴链接或邀请码。",
                "請從下面的連結加入。也可以在 Discord 應用程式的「加入伺服器」中貼上連結或邀請碼。"));

            // リンクと招待コードは、欄の中でコピーできるように出す
            GUILayout.BeginHorizontal();
            GUILayout.Label(InviteUrl, theme != null ? theme.Title : GUI.skin.label, GuiControls.Shrinkable);
            GuiControls.CopyButton(InviteUrl);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(InviteCode, theme != null ? theme.Hint : GUI.skin.label, GuiControls.Shrinkable);
            GuiControls.CopyButton(InviteCode);
            GUILayout.EndHorizontal();

            if (GUILayout.Button(Loc.T("Open Discord", "Discord を開く", "Discord 열기", "打开 Discord", "開啟 Discord"),
                    theme != null ? theme.AccentButton : GUI.skin.button, GuiControls.Shrinkable))
            {
                Application.OpenURL(InviteUrl);
            }

            GuiControls.EndCard();
        }
    }
}
