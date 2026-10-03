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
            GuiControls.BeginCard(Loc.T("Welcome to VRCast", "VRCast へようこそ"));
            GuiControls.Hint(Loc.T(
                "Follow these steps to show your avatar in OBS. Use the tabs on the left for detailed settings.",
                "次の手順でアバターを OBS に映せます。細かい設定は左のタブで変更できます。"));

            // ヘルプ（Web ページ）
            if (GUILayout.Button(Loc.T("Open help (browser)", "ヘルプを開く（ブラウザ）")))
            {
                HelpPage.Open();
            }

            GuiControls.EndCard();
        }

        private void DrawLoadStep()
        {
            GuiControls.BeginStep(1, Loc.T("Load your avatar", "アバターを読み込む"), _session.Current != null);
            GuiControls.Hint(Loc.T(
                "Drag and drop a .vrcaster file onto this window, or choose one with the button below.",
                ".vrcaster ファイルをこのウィンドウにドラッグ＆ドロップするか、下のボタンで選んでください。"));
            GuiControls.Hint(Loc.T(
                "To make a .vrcaster file, export your avatar in Unity with the VRCast converter (see help).",
                ".vrcaster ファイルは、Unity でアバターを VRCast 変換ツールから書き出して作ります（ヘルプ参照）。"));

            GUILayout.BeginHorizontal();

            // ファイル選択（読込中は無効）
            GUI.enabled = !_session.IsLoading;
            if (GUILayout.Button(Loc.T("Choose file...", "ファイルを選ぶ...")))
            {
                _avatarSection.Browse();
            }

            GUI.enabled = true;

            // 詳細（再読み込み・エラー内容）は Avatar タブ
            if (GUILayout.Button(Loc.T("Avatar tab", "アバタータブへ")))
            {
                _open(StartLink.Avatar);
            }

            GUILayout.EndHorizontal();

            // 読込状態
            if (_session.IsLoading)
            {
                GUILayout.Label(Loc.T("Loading...", "読み込み中..."));
            }
            else if (_avatarSection.HasError)
            {
                GUILayout.Label(Loc.T("Could not load. See the Avatar tab for details.",
                    "読み込めませんでした。詳しくはアバタータブを見てください。"));
            }
            else if (_session.Current != null)
            {
                GuiControls.Hint(Loc.T("Loaded: ", "読み込み済み: ") + _session.Current.Manifest.name);
            }

            GuiControls.EndCard();
        }

        private void DrawTransparentStep()
        {
            bool transparent = _rendering.TransparentBackground;
            GuiControls.BeginStep(2, Loc.T("Make the background transparent", "背景を透過にする"), transparent);
            GuiControls.Hint(Loc.T(
                "The window keeps showing the background color, but OBS sees only the avatar.",
                "この画面では背景色のまま見えますが、OBS にはアバターだけが映ります。"));

            // 透過の切り替え
            if (GUILayout.Button(transparent
                    ? Loc.T("Turn transparency off", "透過をやめる")
                    : Loc.T("Make transparent", "透過にする")))
            {
                _rendering.TransparentBackground = !transparent;
            }

            GuiControls.EndCard();
        }

        private void DrawObsStep()
        {
            GuiControls.BeginStep(3, Loc.T("Add VRCast to OBS", "OBS に取り込む"), null);
            GUILayout.Label(Loc.T(
                "1) In OBS, click + under Sources and choose \"Game Capture\".",
                "1) OBS の「ソース」の + から「ゲームキャプチャ」を追加します。"));
            GUILayout.Label(Loc.T(
                "2) Mode: \"Capture specific window\", Window: \"[VRCast.exe]: VRCast\".",
                "2) モードは「特定のウィンドウをキャプチャ」、ウィンドウは「[VRCast.exe]: VRCast」を選びます。"));
            GUILayout.Label(Loc.T(
                "3) Check \"Allow Transparency\".",
                "3)「透過を許可」にチェックを入れます。"));
            GuiControls.Hint(Loc.T(
                "Window Capture cannot do transparency. Use Game Capture.",
                "「ウィンドウキャプチャ」では透過できません。「ゲームキャプチャ」を使ってください。"));
            GuiControls.EndCard();
        }

        private void DrawHideStep()
        {
            GuiControls.BeginStep(4, Loc.T("Hide this panel", "このパネルを隠す"), null);
            GuiControls.Hint(Loc.T(
                "This panel also appears in OBS. Press the Tab key to hide it and make the whole window transparent "
                + "(press again to show).",
                "このパネルは OBS にも映ります。Tab キーでパネルを隠し、背景を含めてウィンドウ全体を透過します"
                + "（もう一度押すと表示）。"));
            GuiControls.EndCard();
        }

        private void DrawMore()
        {
            GuiControls.BeginCard(Loc.T("More", "さらに"));

            // Discord / Zoom 等は仮想カメラ（使用中なら表示）
            string camera = _virtualCamera.Enabled ? Loc.T(" (on)", "（使用中）") : string.Empty;
            GuiControls.Hint(Loc.T("Use in Discord / Zoom as a webcam", "Discord / Zoom で Web カメラとして使う") + camera);
            if (GUILayout.Button(Loc.T("Virtual camera (Output tab)", "仮想カメラ（出力タブ）")))
            {
                _open(StartLink.Output);
            }

            GuiControls.Hint(Loc.T("Move with your webcam / lip sync with your mic",
                "Web カメラで動かす / マイクで口パクする"));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Tracking tab", "トラッキングタブ")))
            {
                _open(StartLink.Tracking);
            }

            if (GUILayout.Button(Loc.T("Face tab", "顔タブ")))
            {
                _open(StartLink.Face);
            }

            GUILayout.EndHorizontal();
            GuiControls.EndCard();
        }
    }
}
