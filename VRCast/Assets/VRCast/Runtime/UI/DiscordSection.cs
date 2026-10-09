using System;
using UnityEngine;
using VRCast.Platform;

namespace VRCast.UI
{
    /// <summary>
    /// Discord タブ（公式 Discord への参加ボタンと、Discord のウィジェットと同じ内容: サーバー名・オンライン人数・オンラインのメンバー）。
    /// 情報はタブを表示している間だけ取得する。
    /// </summary>
    public sealed class DiscordSection : IDisposable
    {
        // メンバーのアイコンの大きさと、状態の丸の欄の幅・丸の大きさ
        private const float AvatarSize = 24f;
        private const float StatusWidth = 16f;
        private const float StatusDotSize = 8f;

        // 状態の色（Discord と同じ: オンライン = 緑、退席中 = 黄、取り込み中 = 赤）
        private static readonly Color OnlineColor = new Color32(0x23, 0xA5, 0x5A, 0xFF);
        private static readonly Color IdleColor = new Color32(0xF0, 0xB2, 0x32, 0xFF);
        private static readonly Color DoNotDisturbColor = new Color32(0xF2, 0x3F, 0x43, 0xFF);

        private readonly DiscordWidget _widget = new DiscordWidget();

        // メンバーの行の文字（アイコンの高さの中央にそろえる）。テーマが作り直されたら作り直す
        private GUIStyle _rowStyle;
        private UiTheme _rowTheme;

        public void Draw()
        {
            // 取得の進行は Layout の時だけ反映する（Layout と Repaint で並ぶ行の数を食い違わせない）
            if (Event.current.type == EventType.Layout)
            {
                _widget.Poll();
            }

            DrawJoin();
            DrawWidget();
            DrawInviteCode();
        }

        public void Dispose()
        {
            _widget.Dispose();
        }

        private static void DrawJoin()
        {
            UiTheme theme = UiTheme.Current;
            GuiControls.BeginCard(Loc.T("Official Discord", "公式 Discord", "공식 Discord", "官方 Discord", "官方 Discord"));
            GuiControls.Hint(Loc.T(
                "The official VRCast community. Feel free to ask questions, request features or report bugs.",
                "VRCast の公式コミュニティです。質問・要望・不具合の報告などお気軽にどうぞ。",
                "VRCast 공식 커뮤니티입니다. 질문·요청·버그 신고 등 편하게 남겨 주세요.",
                "VRCast 官方社区。欢迎提问、提出需求或反馈问题。",
                "VRCast 官方社群。歡迎提問、提出需求或回報問題。"));

            // 招待を開く（目立つようアクセント色）
            if (GUILayout.Button(Loc.T("Join Discord", "Discord に参加", "Discord 참여", "加入 Discord", "加入 Discord"),
                    theme != null ? theme.AccentButton : GUI.skin.button, GuiControls.Shrinkable))
            {
                Application.OpenURL(DiscordWidget.InviteUrl);
            }

            GuiControls.EndCard();
        }

        private void DrawWidget()
        {
            // 見出しはサーバー名（未取得なら既定の名前）
            string title = string.IsNullOrEmpty(_widget.ServerName) ? "Discord" : _widget.ServerName;
            GuiControls.BeginCard(title);

            switch (_widget.State)
            {
                case DiscordWidget.FetchState.Done:
                    DrawMembers();
                    break;
                case DiscordWidget.FetchState.Failed:
                    GuiControls.Warning(Loc.T("Could not load the server info (offline?)",
                        "サーバーの情報を読み込めませんでした（オフライン等）",
                        "서버 정보를 불러오지 못했습니다 (오프라인 등)",
                        "无法加载服务器信息（可能处于离线状态）",
                        "無法載入伺服器資訊（可能處於離線狀態）"));
                    break;
                default:
                    GuiControls.Hint(Loc.T("Loading...", "読み込み中…", "불러오는 중…", "加载中…", "載入中…"));
                    break;
            }

            // すぐに取り直す（自動でも 1 分ごとに取り直す）
            if (GUILayout.Button(Loc.T("Refresh", "更新", "새로 고침", "刷新", "重新整理"), GuiControls.Shrinkable))
            {
                _widget.Refresh();
            }

            GuiControls.EndCard();
        }

        private static void DrawInviteCode()
        {
            UiTheme theme = UiTheme.Current;
            GuiControls.BeginCard(Loc.T("Invite code", "招待コード", "초대 코드", "邀请码", "邀請碼"));
            GuiControls.Hint(Loc.T(
                "In the Discord app, choose \"Join a server\" and enter this code or link.",
                "Discord アプリの「サーバーに参加」で、このコードかリンクを入力しても参加できます。",
                "Discord 앱의 \"서버 참가하기\"에서 이 코드나 링크를 입력해도 참여할 수 있습니다.",
                "也可以在 Discord 应用的“加入服务器”中输入此邀请码或链接加入。",
                "也可以在 Discord 應用程式的「加入伺服器」中輸入此邀請碼或連結加入。"));

            // コードとリンクをそれぞれコピーできる
            GUILayout.BeginHorizontal();
            GUILayout.Label(DiscordWidget.InviteCode, theme != null ? theme.Title : GUI.skin.label, GuiControls.Shrinkable);
            GuiControls.CopyButton(DiscordWidget.InviteCode);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(DiscordWidget.InviteUrl, theme != null ? theme.Hint : GUI.skin.label, GuiControls.Shrinkable);
            GuiControls.CopyButton(DiscordWidget.InviteUrl);
            GUILayout.EndHorizontal();
            GuiControls.EndCard();
        }

        private void DrawMembers()
        {
            // 行の文字のスタイルは今のスキン（テーマ）から作る
            if (_rowStyle == null || _rowTheme != UiTheme.Current)
            {
                _rowTheme = UiTheme.Current;
                _rowStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
            }

            GUIStyle label = _rowStyle;
            GuiControls.Hint(Loc.T("Online", "オンライン", "온라인", "在线", "線上") + $": {_widget.OnlineCount}");

            foreach (DiscordWidget.Member member in _widget.Members)
            {
                GUILayout.BeginHorizontal();

                // 丸いアイコン（取得前は場所だけ空けておく）と、状態の色の丸
                Rect icon = GUILayoutUtility.GetRect(AvatarSize, AvatarSize, GUILayout.Width(AvatarSize), GUILayout.Height(AvatarSize));
                Rect status = GUILayoutUtility.GetRect(StatusWidth, AvatarSize, GUILayout.Width(StatusWidth), GUILayout.Height(AvatarSize));
                if (Event.current.type == EventType.Repaint)
                {
                    Texture2D avatar = _widget.GetAvatar(member);
                    if (avatar != null)
                    {
                        GUI.DrawTexture(icon, avatar, ScaleMode.ScaleAndCrop, true, 0f, Color.white, 0f, AvatarSize / 2f);
                    }

                    // 行の高さの中央に置く
                    var dot = new Rect(status.center.x - StatusDotSize / 2f, status.center.y - StatusDotSize / 2f,
                        StatusDotSize, StatusDotSize);
                    GUI.DrawTexture(dot, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                        StatusColor(member.status), 0f, StatusDotSize / 2f);
                }

                GUILayout.Label(member.username, label, GuiControls.Shrinkable, GUILayout.Height(AvatarSize));
                GUILayout.EndHorizontal();
            }
        }

        private static Color StatusColor(string status)
        {
            // 不明な状態はオンライン扱い
            switch (status)
            {
                case "idle":
                    return IdleColor;
                case "dnd":
                    return DoNotDisturbColor;
                default:
                    return OnlineColor;
            }
        }
    }
}
