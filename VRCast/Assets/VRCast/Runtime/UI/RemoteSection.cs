using System.Collections.Generic;
using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Remote;

namespace VRCast.UI
{
    /// <summary>
    /// OSC / HTTP タブ（外部操作の ON/OFF・ポート・状態と、コマンドの一覧）。
    /// 表情のコマンドは表示中のアバターの表情から自動で並べ、行ごとにコピーできる。
    /// </summary>
    public class RemoteSection
    {
        // コマンド行の種類の列の幅と、コピーボタンの幅
        private const float KindWidth = 50f;
        private const float CopyButtonWidth = 80f;

        // 「コピーしました」を出しておく秒数
        private const float CopiedSeconds = 1.5f;

        private readonly AvatarComponentCache _avatar;
        private readonly RemoteControl _remote;
        private readonly AppSettings _settings;

        // 入力途中のポート番号
        private string _oscPortInput;
        private string _httpPortInput;

        // 表情のコマンドに toggle を付けて表示するか（同じ表情をもう一度送ると自動検出へ戻る。保存しない表示の切替）
        private bool _toggle = true;

        // 最後にコピーした文字列と、「コピーしました」を消す時刻
        private string _copied;
        private float _copiedUntil;

        public RemoteSection(AvatarSession session, RemoteControl remote, AppSettings settings)
        {
            _avatar = new AvatarComponentCache(session);
            _remote = remote;
            _settings = settings;
            SyncFromSettings();
        }

        /// <summary>
        /// 設定値を直接書き換えた後（全設定のリセット等）に、入力途中の値を設定に合わせ直す。
        /// </summary>
        public void SyncFromSettings()
        {
            _oscPortInput = _settings.remoteOscPort.ToString();
            _httpPortInput = _settings.remoteHttpPort.ToString();
        }

        public void Draw()
        {
            _avatar.Refresh();
            DrawGeneral();
            DrawCommonCommands();
            DrawExpressionCommands();
        }

        private void DrawGeneral()
        {
            GuiControls.BeginCard(Loc.T("External control (OSC / HTTP)", "外部から操作（OSC / HTTP）", "외부 조작 (OSC / HTTP)",
                "外部操作（OSC / HTTP）", "外部操作（OSC / HTTP）"));
            _settings.remoteControlEnabled = GUILayout.Toggle(
                _settings.remoteControlEnabled,
                Loc.T("Allow expression control from Stream Deck, OSC apps, etc.",
                    "Stream Deck や OSC アプリなどから表情を操作できるようにする",
                    "Stream Deck·OSC 앱 등에서 표정을 조작할 수 있게 하기",
                    "允许从 Stream Deck、OSC 应用等操作表情",
                    "允許從 Stream Deck、OSC 應用程式等操作表情"));

            // 待ち受けポート（範囲内の数値になったときだけ反映し、待ち受けが開き直す）と状態
            _settings.remoteOscPort = GuiControls.PortField(
                Loc.T("OSC port (UDP)", "OSC ポート（UDP）", "OSC 포트 (UDP)", "OSC 端口（UDP）", "OSC 連接埠（UDP）"),
                ref _oscPortInput, _settings.remoteOscPort, AppSettings.MinTrackingPort, AppSettings.MaxTrackingPort);
            if (_settings.remoteControlEnabled && _remote != null)
            {
                GuiControls.Hint(_remote.OscStatus);
            }

            _settings.remoteHttpPort = GuiControls.PortField(
                Loc.T("HTTP port", "HTTP ポート", "HTTP 포트", "HTTP 端口", "HTTP 連接埠"),
                ref _httpPortInput, _settings.remoteHttpPort, AppSettings.MinTrackingPort, AppSettings.MaxTrackingPort);
            if (_settings.remoteControlEnabled && _remote != null)
            {
                GuiControls.Hint(_remote.HttpStatus);
            }

            // 接続できる範囲と安全上の扱い
            GuiControls.Hint(Loc.T(
                "Only this PC can connect. HTTP requests sent from web pages in a browser are refused (typing the URL in the address bar works).",
                "この PC からだけ接続できます。ブラウザで開いた Web ページからの HTTP の送信は拒否します（アドレス欄に URL を直接入力すると使えます）。",
                "이 PC에서만 연결할 수 있습니다. 브라우저의 웹 페이지에서 보낸 HTTP 요청은 거부합니다 (주소창에 URL을 직접 입력하면 사용할 수 있습니다).",
                "仅限本机连接。拒绝浏览器中网页发出的 HTTP 请求（在地址栏直接输入 URL 可以使用）。",
                "僅限本機連線。拒絕瀏覽器中網頁發出的 HTTP 請求（在網址列直接輸入 URL 可以使用）。"));

            // 無効のままでも一覧は見られるが、送っても反応しないことを示す
            if (!_settings.remoteControlEnabled)
            {
                GuiControls.Hint(Loc.T("Off: the commands below do nothing until you turn this on.",
                    "OFF のため、下のコマンドを送っても反応しません。",
                    "꺼져 있어 아래 명령을 보내도 반응하지 않습니다.",
                    "已关闭，发送下方命令不会有反应。",
                    "已關閉，傳送下方命令不會有反應。"));
            }

            GuiControls.EndCard();
        }

        private void DrawCommonCommands()
        {
            GuiControls.BeginCard(Loc.T("Common commands", "共通のコマンド", "공통 명령", "通用命令", "通用命令"));
            int port = _settings.remoteHttpPort;

            // 自動検出へ戻す
            GuiControls.SubHeading(Loc.T("Back to auto detection", "自動検出に戻す", "자동 감지로 복귀", "恢复自动检测", "恢復自動偵測"));
            DrawCommand("HTTP", RemoteCommandText.HttpPath(port, "/auto"));
            DrawCommand("OSC", "/vrcast/auto");

            // 状態（選択中の表情・固定中か・表情の一覧を JSON で返す）
            GuiControls.SubHeading(Loc.T("Status (JSON)", "状態（JSON）", "상태 (JSON)", "状态（JSON）", "狀態（JSON）"));
            DrawCommand("HTTP", RemoteCommandText.HttpPath(port, "/status"));
            GuiControls.EndCard();
        }

        private void DrawExpressionCommands()
        {
            GuiControls.BeginCard(Loc.T("Expression commands", "表情のコマンド", "표정 명령", "表情命令", "表情命令"));

            // 表情データが無ければ案内だけ
            var expressions = _avatar.Get<ExpressionController>();
            if (expressions == null || expressions.Names.Count == 0)
            {
                GuiControls.Hint(Loc.T("Load an avatar with expressions to list a command for each expression.",
                    "表情のあるアバターを読み込むと、表情ごとのコマンドが並びます。",
                    "표정이 있는 아바타를 불러오면 표정별 명령이 표시됩니다.",
                    "加载带表情的虚拟形象后，会列出每个表情的命令。",
                    "載入帶表情的虛擬形象後，會列出每個表情的命令。"));
                GuiControls.EndCard();
                return;
            }

            // toggle を付けるか（付けると、固定中の同じ表情をもう一度送ったときに自動検出へ戻る）
            _toggle = GUILayout.Toggle(_toggle, Loc.T(
                "Add toggle (sending the same expression again returns to auto detection)",
                "toggle を付ける（同じ表情をもう一度送ると自動検出に戻る）",
                "toggle 붙이기 (같은 표정을 다시 보내면 자동 감지로 복귀)",
                "加上 toggle（再次发送同一表情会恢复自动检测）",
                "加上 toggle（再次傳送同一表情會恢復自動偵測）"));
            GuiControls.Hint(Loc.T(
                "Commands use the expression name, so they keep working if the order changes. OSC apps send the address, plus the text in quotes as a string argument when shown.",
                "コマンドは表情の名前で指定するため、並び順が変わっても使えます。OSC アプリにはアドレスを、\"\" 付きの文字があればそれを文字列の引数として設定してください。",
                "명령은 표정 이름으로 지정하므로 순서가 바뀌어도 사용할 수 있습니다. OSC 앱에는 주소를, \"\" 안의 글자가 있으면 문자열 인수로 설정하세요.",
                "命令按表情名称指定，顺序改变后仍可使用。在 OSC 应用中设置地址；如有带引号的文字，请将其作为字符串参数。",
                "命令依表情名稱指定，順序改變後仍可使用。在 OSC 應用程式中設定位址；如有帶引號的文字，請將其作為字串參數。"));

            // 今の状態（選択中の表情と、固定中か）
            string current = expressions.Current >= 0
                ? expressions.Names[expressions.Current]
                : Loc.T("Neutral", "ニュートラル", "무표정", "无表情", "無表情");
            GuiControls.Status(expressions.IsManual
                ? Loc.T($"Now: {current} (locked)", $"現在: {current}（固定中）", $"현재: {current} (고정 중)", $"当前：{current}（已固定）", $"目前：{current}（已固定）")
                : Loc.T($"Now: {current}", $"現在: {current}", $"현재: {current}", $"当前：{current}", $"目前：{current}"), expressions.IsManual);

            // ニュートラル（番号 0）と、表情ごとのコマンド（番号は Pose タブの並び順）
            int port = _settings.remoteHttpPort;
            GuiControls.SubHeading("0. " + Loc.T("Neutral", "ニュートラル", "무표정", "无表情", "無表情"));
            DrawCommand("HTTP", RemoteCommandText.HttpNeutral(port, _toggle));
            DrawOsc(RemoteCommandText.OscNeutral(_toggle));

            IReadOnlyList<string> names = expressions.Names;
            for (int i = 0; i < names.Count; i++)
            {
                GuiControls.SubHeading($"{i + 1}. {names[i]}");
                DrawCommand("HTTP", RemoteCommandText.HttpExpression(port, names[i], _toggle));
                DrawOsc(RemoteCommandText.OscExpression(names[i], _toggle));
            }

            GuiControls.EndCard();
        }

        private void DrawOsc(RemoteCommandText.OscText osc)
        {
            // 表示は引数込み、コピーはアドレスだけ（OSC アプリではアドレスと引数を別々に入れるため）
            DrawCommand("OSC", RemoteCommandText.Describe(osc), osc.Address);
        }

        private void DrawCommand(string kind, string text, string copyText = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(kind, GUILayout.Width(KindWidth));

            // 長い URL も折り返して収める
            UiTheme theme = UiTheme.Current;
            GUILayout.Label(text, theme != null ? theme.Hint : GUI.skin.label, GuiControls.Shrinkable);

            // コピー（押した直後だけ「コピーしました」）
            string value = copyText ?? text;
            bool justCopied = _copied == value && Time.unscaledTime < _copiedUntil;
            if (GUILayout.Button(justCopied
                    ? Loc.T("Copied", "コピー済み", "복사됨", "已复制", "已複製")
                    : Loc.T("Copy", "コピー", "복사", "复制", "複製"), GUILayout.Width(CopyButtonWidth)))
            {
                GUIUtility.systemCopyBuffer = value;
                _copied = value;
                _copiedUntil = Time.unscaledTime + CopiedSeconds;
            }

            GUILayout.EndHorizontal();
        }
    }
}
