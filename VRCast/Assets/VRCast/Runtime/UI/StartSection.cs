using System;
using UnityEngine;
using VRCast.Avatars;
using VRCast.Output;
using VRCast.Rendering;

namespace VRCast.UI
{
    /// <summary>
    /// Start タブから開く他のタブ。
    /// </summary>
    public enum StartLink
    {
        Avatar,
        Face,
        Tracking,
        Output,
    }

    /// <summary>
    /// Start タブ（はじめての人向けに、アバターの読み込みから OBS に映すまでを手順で案内する）。
    /// 状態が分かる手順は完了 / 未完了を表示し、その場で操作できるボタンを置く。
    /// </summary>
    public class StartSection
    {
        private readonly AvatarSession _session;
        private readonly AvatarSection _avatarSection;
        private readonly RenderingController _rendering;
        private readonly VirtualCameraOutput _virtualCamera;
        private readonly Action<StartLink> _open;

        /// <param name="open">他のタブを開く処理</param>
        public StartSection(
            AvatarSession session, AvatarSection avatarSection, RenderingController rendering,
            VirtualCameraOutput virtualCamera, Action<StartLink> open)
        {
            _session = session;
            _avatarSection = avatarSection;
            _rendering = rendering;
            _virtualCamera = virtualCamera;
            _open = open;
        }

        public void Draw()
        {
            DrawWelcome();
            DrawLoadStep();
            DrawTransparentStep();
            DrawObsStep();
            DrawHideStep();
            DrawMore();
        }

        private void DrawWelcome()
        {
            GuiControls.BeginCard(Loc.T("Welcome to VRCast", "VRCast へようこそ", "VRCast에 오신 것을 환영합니다",
                "欢迎使用 VRCast", "歡迎使用 VRCast"));
            GuiControls.Hint(Loc.T(
                "Follow these steps to show your avatar in OBS. Use the tabs on the left for detailed settings.",
                "次の手順でアバターを OBS に映せます。細かい設定は左のタブで変更できます。",
                "아래 단계를 따라 아바타를 OBS에 표시할 수 있습니다. 자세한 설정은 왼쪽 탭에서 변경하세요.",
                "按照以下步骤即可在 OBS 中显示虚拟形象。详细设置请在左侧的标签页中更改。",
                "依照以下步驟即可在 OBS 中顯示虛擬形象。詳細設定請在左側的分頁中變更。"));

            // ヘルプ（Web ページ）
            if (GUILayout.Button(Loc.T("Open help (browser)", "ヘルプを開く（ブラウザ）", "도움말 열기 (브라우저)",
                    "打开帮助（浏览器）", "開啟說明（瀏覽器）")))
            {
                HelpPage.Open();
            }

            GuiControls.EndCard();
        }

        private void DrawLoadStep()
        {
            GuiControls.BeginStep(1, Loc.T("Load your avatar", "アバターを読み込む", "아바타 불러오기",
                "加载虚拟形象", "載入虛擬形象"), _session.Current != null);
            GuiControls.Hint(Loc.T(
                "Drag and drop a .vrcaster file onto this window, or choose one with the button below.",
                ".vrcaster ファイルをこのウィンドウにドラッグ＆ドロップするか、下のボタンで選んでください。",
                ".vrcaster 파일을 이 창에 드래그 앤 드롭하거나 아래 버튼으로 선택하세요.",
                "将 .vrcaster 文件拖放到此窗口，或使用下方按钮选择。",
                "將 .vrcaster 檔案拖放到此視窗，或使用下方按鈕選擇。"));
            GuiControls.Hint(Loc.T(
                "To make a .vrcaster file, export your avatar in Unity with the VRCast converter (see help).",
                ".vrcaster ファイルは、Unity でアバターを VRCast 変換ツールから書き出して作ります（ヘルプ参照）。",
                ".vrcaster 파일은 Unity에서 VRCast 변환 도구로 아바타를 내보내 만듭니다 (도움말 참조).",
                ".vrcaster 文件需在 Unity 中使用 VRCast 转换工具导出虚拟形象来制作（参见帮助）。",
                ".vrcaster 檔案需在 Unity 中使用 VRCast 轉換工具匯出虛擬形象來製作（請參閱說明）。"));

            GUILayout.BeginHorizontal();

            // ファイル選択（読込中は無効）
            GUI.enabled = !_session.IsLoading;
            if (GUILayout.Button(Loc.T("Choose file...", "ファイルを選ぶ...", "파일 선택...", "选择文件...", "選擇檔案...")))
            {
                _avatarSection.Browse();
            }

            GUI.enabled = true;

            // 詳細（再読み込み・エラー内容）は Avatar タブ
            if (GUILayout.Button(Loc.T("Avatar tab", "アバタータブへ", "아바타 탭으로", "前往虚拟形象标签页", "前往虛擬形象分頁")))
            {
                _open(StartLink.Avatar);
            }

            GUILayout.EndHorizontal();

            // 読込状態
            if (_session.IsLoading)
            {
                GUILayout.Label(Loc.T("Loading...", "読み込み中...", "불러오는 중...", "加载中...", "載入中..."));
            }
            else if (_avatarSection.HasError)
            {
                GUILayout.Label(Loc.T("Could not load. See the Avatar tab for details.",
                    "読み込めませんでした。詳しくはアバタータブを見てください。",
                    "불러오지 못했습니다. 자세한 내용은 아바타 탭을 확인하세요.",
                    "加载失败。详情请查看虚拟形象标签页。",
                    "載入失敗。詳細請查看虛擬形象分頁。"));
            }
            else if (_session.Current != null)
            {
                GuiControls.Hint(Loc.T("Loaded: ", "読み込み済み: ", "불러옴: ", "已加载：", "已載入：")
                    + _session.Current.Manifest.name);
            }

            GuiControls.EndCard();
        }

        private void DrawTransparentStep()
        {
            bool transparent = _rendering.TransparentBackground;
            GuiControls.BeginStep(2, Loc.T("Make the background transparent", "背景を透過にする", "배경을 투명하게 하기",
                "将背景设为透明", "將背景設為透明"), transparent);
            GuiControls.Hint(Loc.T(
                "The window keeps showing the background color, but OBS sees only the avatar.",
                "この画面では背景色のまま見えますが、OBS にはアバターだけが映ります。",
                "이 창에서는 배경색이 그대로 보이지만, OBS에는 아바타만 표시됩니다.",
                "此窗口中仍显示背景色，但 OBS 中只会显示虚拟形象。",
                "此視窗中仍顯示背景色，但 OBS 中只會顯示虛擬形象。"));

            // 透過の切り替え
            if (GUILayout.Button(transparent
                    ? Loc.T("Turn transparency off", "透過をやめる", "투명 끄기", "关闭透明", "關閉透明")
                    : Loc.T("Make transparent", "透過にする", "투명하게 하기", "设为透明", "設為透明")))
            {
                _rendering.TransparentBackground = !transparent;
            }

            GuiControls.EndCard();
        }

        private void DrawObsStep()
        {
            GuiControls.BeginStep(3, Loc.T("Add VRCast to OBS", "OBS に取り込む", "OBS에 추가하기",
                "添加到 OBS", "加入 OBS"), null);
            GUILayout.Label(Loc.T(
                "1) In OBS, click + under Sources and choose \"Game Capture\".",
                "1) OBS の「ソース」の + から「ゲームキャプチャ」を追加します。",
                "1) OBS의 \"소스\" 아래 + 를 눌러 \"게임 캡처\"를 추가합니다.",
                "1) 在 OBS 的“来源”中点击 +，添加“游戏采集”。",
                "1) 在 OBS 的「來源」中點擊 +，新增「遊戲擷取」。"));
            GUILayout.Label(Loc.T(
                "2) Mode: \"Capture specific window\", Window: \"[VRCast.exe]: VRCast\".",
                "2) モードは「特定のウィンドウをキャプチャ」、ウィンドウは「[VRCast.exe]: VRCast」を選びます。",
                "2) 모드는 \"특정 창 캡처\", 창은 \"[VRCast.exe]: VRCast\"를 선택합니다.",
                "2) 模式选择“采集特定窗口”，窗口选择“[VRCast.exe]: VRCast”。",
                "2) 模式選擇「擷取特定視窗」，視窗選擇「[VRCast.exe]: VRCast」。"));
            GUILayout.Label(Loc.T(
                "3) Check \"Allow Transparency\".",
                "3)「透過を許可」にチェックを入れます。",
                "3) \"투명도 허용\"에 체크합니다.",
                "3) 勾选“允许透明”。",
                "3) 勾選「允許透明」。"));
            GuiControls.Hint(Loc.T(
                "Window Capture cannot do transparency. Use Game Capture.",
                "「ウィンドウキャプチャ」では透過できません。「ゲームキャプチャ」を使ってください。",
                "\"윈도우 캡처\"로는 투명하게 할 수 없습니다. \"게임 캡처\"를 사용하세요.",
                "“窗口采集”无法透明，请使用“游戏采集”。",
                "「視窗擷取」無法透明，請使用「遊戲擷取」。"));
            GuiControls.EndCard();
        }

        private void DrawHideStep()
        {
            GuiControls.BeginStep(4, Loc.T("Hide this panel", "このパネルを隠す", "이 패널 숨기기",
                "隐藏此面板", "隱藏此面板"), null);
            GuiControls.Hint(Loc.T(
                "This panel also appears in OBS. Press the Tab key to hide it. While hidden, the background is "
                + "transparent in OBS Game Capture even if transparency is off (press again to show).",
                "このパネルは OBS にも映ります。Tab キーで隠せます。隠している間は透過が OFF でも"
                + " OBS のゲームキャプチャでは背景が抜けます（もう一度押すと表示）。",
                "이 패널은 OBS에도 표시됩니다. Tab 키로 숨길 수 있습니다. 숨겨진 동안에는 투명이 꺼져 있어도 "
                + "OBS 게임 캡처에서 배경이 투명해집니다 (다시 누르면 표시).",
                "此面板也会显示在 OBS 中。按 Tab 键可隐藏。隐藏期间即使透明为关闭，"
                + "OBS 游戏采集中的背景也会透明（再按一次显示）。",
                "此面板也會顯示在 OBS 中。按 Tab 鍵可隱藏。隱藏期間即使透明為關閉，"
                + "OBS 遊戲擷取中的背景也會透明（再按一次顯示）。"));
            GuiControls.EndCard();
        }

        private void DrawMore()
        {
            GuiControls.BeginCard(Loc.T("More", "さらに", "더 보기", "更多", "更多"));

            // Discord / Zoom 等は仮想カメラ（使用中なら表示）
            string camera = _virtualCamera.Enabled
                ? Loc.T(" (on)", "（使用中）", " (사용 중)", "（使用中）", "（使用中）")
                : string.Empty;
            GuiControls.Hint(Loc.T("Use in Discord / Zoom as a webcam", "Discord / Zoom で Web カメラとして使う",
                "Discord / Zoom에서 웹캠으로 사용", "在 Discord / Zoom 中作为摄像头使用",
                "在 Discord / Zoom 中作為網路攝影機使用") + camera);
            if (GUILayout.Button(Loc.T("Virtual camera (Output tab)", "仮想カメラ（出力タブ）", "가상 카메라 (출력 탭)",
                    "虚拟摄像头（输出标签页）", "虛擬攝影機（輸出分頁）")))
            {
                _open(StartLink.Output);
            }

            GuiControls.Hint(Loc.T("Move with your webcam / lip sync with your mic",
                "Web カメラで動かす / マイクで口パクする", "웹캠으로 움직이기 / 마이크로 립싱크",
                "用摄像头驱动 / 用麦克风对口型", "用網路攝影機驅動 / 用麥克風對嘴型"));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Tracking tab", "トラッキングタブ", "트래킹 탭", "追踪标签页", "追蹤分頁")))
            {
                _open(StartLink.Tracking);
            }

            if (GUILayout.Button(Loc.T("Face tab", "顔タブ", "얼굴 탭", "面部标签页", "臉部分頁")))
            {
                _open(StartLink.Face);
            }

            GUILayout.EndHorizontal();
            GuiControls.EndCard();
        }
    }
}
