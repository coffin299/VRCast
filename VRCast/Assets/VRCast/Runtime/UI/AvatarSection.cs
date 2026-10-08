using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Platform;

namespace VRCast.UI
{
    /// <summary>
    /// Avatar タブ（.vrcaster / .vrm のドロップ・ファイル選択・パス入力による読み込み、再読み込み・解除、
    /// 最近使ったアバターへの切り替えと、表示中アバターの情報）。
    /// 読み込む前のファイル確認の失敗は表示言語に合わせて表示する。
    /// </summary>
    public class AvatarSection
    {
        // 読み込む前の確認で見つかった問題
        private enum InputError
        {
            None,
            Empty,
            Unsupported,
            NotFound,
        }

        // 参照ボタンの幅
        private const float BrowseWidth = 90f;

        // 最近使ったアバターの「一覧から外す」ボタンの幅
        private const float ForgetWidth = 28f;

        // 最近使ったアバターのファイルの有無を調べ直す間隔（秒。OnGUI は 1 フレームに複数回呼ばれるため毎回は調べない）
        private const float ExistsCheckInterval = 2f;

        private readonly AvatarSession _session;
        private readonly AppSettings _settings;
        private string _pathInput;

        // 直前の確認結果と、対象のファイル名（表示時に言語へ合わせて文言を作る）
        private InputError _inputError;
        private string _inputName = string.Empty;

        // ファイルが見つからない最近使ったアバターのパスと、最後に調べた時刻
        private readonly HashSet<string> _missingRecent = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        private float _existsCheckedAt = float.NegativeInfinity;

        // 「×」で一覧から外すアバター（レイアウト計算と描画で項目数がずれないよう、次のレイアウト計算時に消す）
        private string _pendingForget;

        public AvatarSection(AvatarSession session, AppSettings settings, string initialPath)
        {
            _session = session;
            _settings = settings;
            _pathInput = initialPath ?? string.Empty;
        }

        public void Draw()
        {
            DrawLoader();
            DrawRecent();
            DrawInfo();
        }

        /// <summary>
        /// ドロップされたパスから最初の .vrcaster / .vrm を読み込む。対応ファイルが無ければエラーを表示して false。
        /// </summary>
        public bool LoadDropped(IReadOnlyList<string> paths)
        {
            // 読み込めるファイルが無ければ先頭のファイル名でエラー表示
            string package = AvatarFiles.FindSupported(paths);
            if (package == null)
            {
                _inputError = InputError.Unsupported;
                _inputName = paths != null && paths.Count > 0 ? FileNameOf(paths[0]) : string.Empty;
                return false;
            }

            // 入力欄にも反映して読み込む
            _pathInput = package;
            return TryLoad(package);
        }

        private void DrawLoader()
        {
            GuiControls.BeginCard(Loc.T("Avatar file", "アバターファイル", "아바타 파일", "虚拟形象文件", "虛擬形象檔案"));
            GuiControls.Hint(Loc.T(
                "Drop a .vrcaster or .vrm file onto the window, choose one with Browse, or enter the path",
                ".vrcaster または .vrm ファイルをウィンドウにドロップするか、参照で選ぶか、パスを入力してください",
                ".vrcaster 또는 .vrm 파일을 창에 드롭하거나, 찾아보기로 선택하거나, 경로를 입력하세요",
                "将 .vrcaster 或 .vrm 文件拖放到窗口，或通过“浏览”选择，或输入路径",
                "將 .vrcaster 或 .vrm 檔案拖放到視窗，或透過「瀏覽」選擇，或輸入路徑"));

            GUILayout.BeginHorizontal();
            _pathInput = GUILayout.TextField(_pathInput, GuiControls.Shrinkable);

            // ファイル選択（Windows のみ、読込中は無効）
            GUI.enabled = FileDialog.IsSupported && !_session.IsLoading;
            if (GUILayout.Button(Loc.T("Browse...", "参照...", "찾아보기...", "浏览...", "瀏覽..."), GUILayout.Width(BrowseWidth)))
            {
                Browse();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            // 読込中はボタンを無効化
            GUI.enabled = !_session.IsLoading;
            if (GUILayout.Button(Loc.T("Load", "読み込み", "불러오기", "加载", "載入"), GuiControls.Shrinkable))
            {
                TryLoad(_pathInput);
            }

            // 表示中のアバターがある場合のみ有効
            GUI.enabled = !_session.IsLoading && _session.Current != null;
            if (GUILayout.Button(Loc.T("Reload", "再読み込み", "다시 불러오기", "重新加载", "重新載入"), GuiControls.Shrinkable))
            {
                _inputError = InputError.None;
                _session.Reload();
            }

            if (GUILayout.Button(Loc.T("Unload", "解除", "해제", "卸载", "卸載"), GuiControls.Shrinkable))
            {
                _inputError = InputError.None;
                _session.Unload();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GuiControls.EndCard();
        }

        private void DrawRecent()
        {
            // 前回「×」を押したアバターを、項目数が決まる前（レイアウト計算時）に消す
            if (_pendingForget != null && Event.current.type == EventType.Layout)
            {
                _settings.ForgetAvatar(_pendingForget);
                _pendingForget = null;
            }

            // 一度も読み込んでいなければ表示しない
            List<string> recent = _settings.RecentAvatars();
            if (recent.Count == 0)
            {
                return;
            }

            GuiControls.BeginCard(Loc.T("Recent avatars", "最近使ったアバター", "최근 사용한 아바타", "最近使用的虚拟形象",
                "最近使用的虛擬形象"));
            GuiControls.Hint(Loc.T(
                "Click to switch. Camera, light and pose are remembered for each avatar.",
                "クリックで切り替えます。カメラ・ライト・待機ポーズはアバターごとに記憶されます。",
                "클릭하여 전환합니다. 카메라·조명·대기 포즈는 아바타마다 기억됩니다.",
                "点击即可切换。相机、灯光和待机姿势会按虚拟形象分别记住。",
                "點擊即可切換。相機、燈光和待機姿勢會依虛擬形象分別記住。"));

            RefreshMissing(recent);
            string current = _session.Current?.SourcePath;
            foreach (string path in recent)
            {
                GUILayout.BeginHorizontal();

                // 表示中のアバターには印を付け、ファイルが無いものは押せなくする
                bool isCurrent = string.Equals(path, current, System.StringComparison.OrdinalIgnoreCase);
                bool missing = _missingRecent.Contains(path);
                GUI.enabled = !_session.IsLoading && !isCurrent && !missing;
                if (GUILayout.Button(DescribeRecent(path, isCurrent, missing), GuiControls.Shrinkable))
                {
                    _pathInput = path;
                    TryLoad(path);
                }

                // 一覧から外す（記憶したカメラ・ライト等も消える）。表示中のアバターはすぐ記録し直されるため外せない
                GUI.enabled = !_session.IsLoading && !isCurrent;
                if (GUILayout.Button("×", GUILayout.Width(ForgetWidth)))
                {
                    _pendingForget = path;
                }

                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GuiControls.Hint(Loc.T(
                "× removes the avatar from this list and forgets its camera, light and pose.",
                "× で一覧から外します（記憶したカメラ・ライト・待機ポーズも消えます）。",
                "×로 목록에서 제거합니다 (기억한 카메라·조명·대기 포즈도 지워집니다).",
                "点击 × 从列表中移除（记住的相机、灯光和待机姿势也会清除）。",
                "點擊 × 從清單中移除（記住的相機、燈光和待機姿勢也會清除）。"));
            GuiControls.EndCard();
        }

        private void RefreshMissing(List<string> recent)
        {
            // 一定間隔でだけファイルの有無を調べ直す
            if (Time.unscaledTime - _existsCheckedAt < ExistsCheckInterval)
            {
                return;
            }

            _existsCheckedAt = Time.unscaledTime;
            _missingRecent.Clear();
            foreach (string path in recent)
            {
                // 移動・削除されたファイルを記録
                if (!File.Exists(path))
                {
                    _missingRecent.Add(path);
                }
            }
        }

        private static string DescribeRecent(string path, bool isCurrent, bool missing)
        {
            // 「名前（フォルダ名）」で同じ名前の別ファイルも見分けられるようにする
            string name = Path.GetFileNameWithoutExtension(FileNameOf(path));
            string folder = FileNameOf(Path.GetDirectoryName(path) ?? string.Empty);
            string label = string.IsNullOrEmpty(folder) ? name : $"{name}  ({folder})";

            // 表示中・見つからない場合は後ろに添える
            if (isCurrent)
            {
                return label + Loc.T(" - showing", " - 表示中", " - 표시 중", " - 显示中", " - 顯示中");
            }

            return missing
                ? label + Loc.T(" - not found", " - 見つかりません", " - 찾을 수 없음", " - 找不到", " - 找不到")
                : label;
        }

        /// <summary>
        /// 直前の読み込みが失敗している（読み込む前の確認エラーを含む）なら true。
        /// </summary>
        public bool HasError => !_session.IsLoading && (_inputError != InputError.None || _session.LastError != null);

        /// <summary>
        /// ファイル選択ダイアログで .vrcaster / .vrm を選ばせて読み込む（キャンセル時は何もしない）。
        /// </summary>
        public void Browse()
        {
            // 選ばれたら入力欄に反映して読み込む（キャンセル時は何もしない）
            string path = FileDialog.OpenFile(
                Loc.T("Open avatar", "アバターを開く", "아바타 열기", "打开虚拟形象", "開啟虛擬形象"),
                Loc.T("Avatar (VRCast / VRM)", "アバター（VRCast / VRM）", "아바타 (VRCast / VRM)", "虚拟形象（VRCast / VRM）",
                    "虛擬形象（VRCast / VRM）"),
                AvatarFiles.Extensions, PathUtility.NormalizeInput(_pathInput));
            if (path != null)
            {
                _pathInput = path;
                TryLoad(path);
            }
        }

        private bool TryLoad(string input)
        {
            // 読込中は受け付けない
            if (_session.IsLoading)
            {
                return false;
            }

            // 空・拡張子違い・存在しないファイルは読み込む前に知らせる
            string path = PathUtility.NormalizeInput(input);
            _inputName = FileNameOf(path);
            if (path.Length == 0)
            {
                _inputError = InputError.Empty;
            }
            else if (!AvatarFiles.IsSupported(path))
            {
                _inputError = InputError.Unsupported;
            }
            else if (!File.Exists(path))
            {
                _inputError = InputError.NotFound;
            }
            else
            {
                _inputError = InputError.None;
                _session.Load(path);
            }

            return _inputError == InputError.None;
        }

        private static string FileNameOf(string path)
        {
            // 入力欄の文字列は不正な文字を含みうるため、取り出せなければそのまま表示する
            try
            {
                string name = Path.GetFileName(path);
                return string.IsNullOrEmpty(name) ? path : name;
            }
            catch (System.ArgumentException)
            {
                return path;
            }
        }

        private void DrawInfo()
        {
            GuiControls.BeginCard(Loc.T("Status", "状態", "상태", "状态", "狀態"));

            // 状態表示: 読込中 > 読み込む前の確認エラー > 読込エラー > アバター情報 の優先順
            if (_session.IsLoading)
            {
                GUILayout.Label(Loc.T("Loading...", "読み込み中...", "불러오는 중...", "加载中...", "載入中..."));
            }
            else if (_inputError != InputError.None)
            {
                GUILayout.Label(DescribeInputError());
            }
            else if (_session.LastError != null)
            {
                GUILayout.Label(Loc.T("Error: ", "エラー: ", "오류: ", "错误：", "錯誤：") + _session.LastError);
            }
            else if (_session.Current != null)
            {
                LoadedAvatar avatar = _session.Current;
                GUILayout.Label(Loc.T("Name: ", "名前: ", "이름: ", "名称：", "名稱：") + avatar.Name);
                string humanoid = avatar.IsHumanoid
                    ? Loc.T("Yes", "はい", "예", "是", "是")
                    : Loc.T("No", "いいえ", "아니요", "否", "否");
                string renderers = Loc.T("Renderers", "レンダラー", "렌더러", "渲染器", "渲染器");
                GuiControls.Hint($"Humanoid: {humanoid}    {renderers}: {avatar.RendererCount}");
                DrawSource(avatar);
            }
            else
            {
                GUILayout.Label(Loc.T("No avatar loaded.", "アバターが読み込まれていません。", "불러온 아바타가 없습니다.",
                    "尚未加载虚拟形象。", "尚未載入虛擬形象。"));
            }

            GuiControls.EndCard();
        }

        private static void DrawSource(LoadedAvatar avatar)
        {
            // .vrcaster は書き出した Unity のバージョン
            if (avatar.Vrm == null)
            {
                GuiControls.Hint(Loc.T("Built with Unity ", "書き出し Unity ", "내보낸 Unity ", "导出 Unity ", "匯出 Unity ")
                    + avatar.Manifest.unityVersion);
                return;
            }

            // VRM は仕様バージョンと作者（作者が無ければ省く）
            string authors = string.IsNullOrEmpty(avatar.Vrm.authors)
                ? string.Empty
                : Loc.T("    Author: ", "    作者: ", "    제작자: ", "    作者：", "    作者：") + avatar.Vrm.authors;
            GuiControls.Hint("VRM " + avatar.Vrm.specVersion + authors);
        }

        private string DescribeInputError()
        {
            // 確認エラーの文言（表示言語に合わせる）
            switch (_inputError)
            {
                case InputError.Empty:
                    return Loc.T("Error: enter the path of a .vrcaster or .vrm file.",
                        "エラー: .vrcaster または .vrm ファイルのパスを入力してください。",
                        "오류: .vrcaster 또는 .vrm 파일의 경로를 입력하세요.",
                        "错误：请输入 .vrcaster 或 .vrm 文件的路径。",
                        "錯誤：請輸入 .vrcaster 或 .vrm 檔案的路徑。");
                case InputError.NotFound:
                    return Loc.T($"Error: file not found ({_inputName}).",
                        $"エラー: ファイルが見つかりません（{_inputName}）。",
                        $"오류: 파일을 찾을 수 없습니다 ({_inputName}).",
                        $"错误：找不到文件（{_inputName}）。",
                        $"錯誤：找不到檔案（{_inputName}）。");
                default:
                    return Loc.T(
                        $"Error: unsupported file ({_inputName}). Only .vrcaster and .vrm files can be loaded.",
                        $"エラー: 対応していないファイルです（{_inputName}）。読み込めるのは .vrcaster と .vrm ファイルのみです。",
                        $"오류: 지원하지 않는 파일입니다 ({_inputName}). .vrcaster와 .vrm 파일만 불러올 수 있습니다.",
                        $"错误：不支持的文件（{_inputName}）。只能加载 .vrcaster 和 .vrm 文件。",
                        $"錯誤：不支援的檔案（{_inputName}）。只能載入 .vrcaster 和 .vrm 檔案。");
            }
        }
    }
}
