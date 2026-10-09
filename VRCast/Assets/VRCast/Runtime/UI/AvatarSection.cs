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
    /// 最近使ったアバターへの切り替え（画像付きのタイル）と、表示中アバターの情報）。
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
            ImageWithoutAvatar,
        }

        // 参照ボタンの幅
        private const float BrowseWidth = 90f;

        // 最近使ったアバターのタイルの幅（画像は正方形）と、タイルの間隔
        private const float TileWidth = 96f;
        private const float TileSpacing = 6f;

        // タイルの画像の内側の余白（ボタンの枠を見せる）
        private const float TileImageInset = 3f;

        // 最近使ったアバターのファイルの有無を調べ直す間隔（秒。OnGUI は 1 フレームに複数回呼ばれるため毎回は調べない）
        private const float ExistsCheckInterval = 2f;

        // ファイルが見つからないアバターの画像の濃さ
        private const float MissingImageAlpha = 0.4f;

        private readonly AvatarSession _session;
        private readonly AppSettings _settings;
        private readonly AvatarThumbnails _thumbnails;
        private string _pathInput;

        // 画像を設定できなかった理由（英語。成功したら消す）
        private string _thumbnailError;

        // タイルを並べられる幅（Repaint で測り、次の Layout で列数に反映する）と、1 行のタイルの数
        private float _measuredWidth;
        private int _columns = 1;

        // タイルのアバター名の文字（1 行で切り詰める。テーマの説明文の文字から作り、テーマが変わったら作り直す）
        private GUIStyle _tileNameStyle;
        private GUIStyle _tileNameBase;

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
            _thumbnails = new AvatarThumbnails(settings);
            _pathInput = initialPath ?? string.Empty;
        }

        public void Draw()
        {
            DrawLoader();
            DrawRecent();
            DrawInfo();
        }

        /// <summary>
        /// ドロップされたパスから最初の .vrcaster / .vrm を読み込む。アバターが無く画像があれば表示中のアバターの画像にする。
        /// どちらも無い・画像を設定できなければエラーを表示して false。
        /// </summary>
        public bool LoadDropped(IReadOnlyList<string> paths)
        {
            // 読み込めるファイルが無ければ、画像は表示中のアバターへ設定し、それ以外は先頭のファイル名でエラー表示
            string package = AvatarFiles.FindSupported(paths);
            string image = package == null ? AvatarThumbnails.FindImage(paths) : null;
            if (image != null)
            {
                return SetCurrentThumbnail(image);
            }

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
            // 前回「×」を押したアバターを、項目数が決まる前（レイアウト計算時）に消す（一覧の画像も消す）
            if (_pendingForget != null && Event.current.type == EventType.Layout)
            {
                _thumbnails.Forget(_pendingForget);
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
                "Click to switch. Camera, light and pose are remembered for each avatar. "
                + "\"Image\" sets a picture (PNG / JPG / GIF) for the list; you can also drop an image onto the window for the avatar being shown.",
                "クリックで切り替えます。カメラ・ライト・待機ポーズはアバターごとに記憶されます。"
                + "「画像」で一覧の画像（PNG / JPG / GIF）を設定できます。表示中のアバターには、画像をウィンドウにドロップしても設定できます。",
                "클릭하여 전환합니다. 카메라·조명·대기 포즈는 아바타마다 기억됩니다. "
                + "\"이미지\"로 목록의 이미지(PNG / JPG / GIF)를 설정할 수 있습니다. 표시 중인 아바타는 이미지를 창에 드롭해도 설정됩니다.",
                "点击即可切换。相机、灯光和待机姿势会按虚拟形象分别记住。"
                + "用“图片”可设置列表中的图片（PNG / JPG / GIF）。也可将图片拖放到窗口，设置给正在显示的虚拟形象。",
                "點擊即可切換。相機、燈光和待機姿勢會依虛擬形象分別記住。"
                + "用「圖片」可設定清單中的圖片（PNG / JPG / GIF）。也可將圖片拖放到視窗，設定給正在顯示的虛擬形象。"));

            DrawTiles(recent);

            // 画像を設定できなかったときの理由
            if (!string.IsNullOrEmpty(_thumbnailError))
            {
                GuiControls.Warning(Loc.T("Could not set the image", "画像を設定できませんでした", "이미지를 설정하지 못했습니다",
                    "无法设置图片", "無法設定圖片") + $" ({_thumbnailError})");
            }

            GuiControls.Hint(Loc.T(
                "× removes the avatar from this list and forgets its camera, light, pose and image.",
                "× で一覧から外します（記憶したカメラ・ライト・待機ポーズ・画像も消えます）。",
                "×로 목록에서 제거합니다 (기억한 카메라·조명·대기 포즈·이미지도 지워집니다).",
                "点击 × 从列表中移除（记住的相机、灯光、待机姿势和图片也会清除）。",
                "點擊 × 從清單中移除（記住的相機、燈光、待機姿勢和圖片也會清除）。"));
            GuiControls.EndCard();
        }

        private void DrawTiles(List<string> recent)
        {
            // 並べられる幅を測る（Layout と Repaint で配置を食い違わせないよう、列数は次の Layout で反映する）
            Rect area = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                _measuredWidth = area.width;
            }
            else if (Event.current.type == EventType.Layout && _measuredWidth > 0f)
            {
                _columns = Mathf.Max(1, Mathf.FloorToInt((_measuredWidth + TileSpacing) / (TileWidth + TileSpacing)));
            }

            RefreshMissing(recent);
            string current = _session.Current?.SourcePath;
            for (int i = 0; i < recent.Count; i++)
            {
                // 行の始まり
                if (i % _columns == 0)
                {
                    GUILayout.BeginHorizontal();
                }
                else
                {
                    GUILayout.Space(TileSpacing);
                }

                DrawTile(recent[i], string.Equals(recent[i], current, System.StringComparison.OrdinalIgnoreCase));

                // 行の終わり（列が埋まった・最後のタイル）
                if (i % _columns == _columns - 1 || i == recent.Count - 1)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.Space(TileSpacing);
                }
            }
        }

        private void DrawTile(string path, bool isCurrent)
        {
            UiTheme theme = UiTheme.Current;
            bool missing = _missingRecent.Contains(path);
            GUILayout.BeginVertical(GUILayout.Width(TileWidth));

            // 画像（無ければ「画像なし」）を押すと切り替える。表示中・ファイルが無いアバターは押せない
            Rect rect = GUILayoutUtility.GetRect(TileWidth, TileWidth, GUILayout.Width(TileWidth), GUILayout.Height(TileWidth));
            Texture2D image = _thumbnails.Get(path);
            string placeholder = _thumbnails.Has(path)
                ? string.Empty
                : Loc.T("No image", "画像なし", "이미지 없음", "无图片", "無圖片");
            GUI.enabled = !_session.IsLoading && !isCurrent && !missing;
            if (GUI.Button(rect, placeholder))
            {
                _pathInput = path;
                TryLoad(path);
            }

            GUI.enabled = true;

            // 画像は枠の内側に、縦横比を保って切り抜いて描く（見つからないアバターは薄くする）
            if (image != null && Event.current.type == EventType.Repaint)
            {
                Color color = GUI.color;
                GUI.color = missing ? new Color(color.r, color.g, color.b, color.a * MissingImageAlpha) : color;
                GUI.DrawTexture(new Rect(rect.x + TileImageInset, rect.y + TileImageInset,
                    rect.width - TileImageInset * 2f, rect.height - TileImageInset * 2f), image, ScaleMode.ScaleAndCrop);
                GUI.color = color;
            }

            // アバター名と、表示中・見つからないことの印（印が無くても行の高さをそろえる）
            GUILayout.Label(new GUIContent(Path.GetFileNameWithoutExtension(FileNameOf(path)), path), TileNameStyle(theme),
                GUILayout.Width(TileWidth));
            if (isCurrent)
            {
                GUILayout.Label(Loc.T("Showing", "表示中", "표시 중", "显示中", "顯示中"),
                    theme != null ? theme.Success : GUI.skin.label, GUILayout.Width(TileWidth));
            }
            else if (missing)
            {
                GUILayout.Label(Loc.T("Not found", "見つかりません", "찾을 수 없음", "找不到", "找不到"),
                    theme != null ? theme.WarningText : GUI.skin.label, GUILayout.Width(TileWidth));
            }
            else
            {
                GUILayout.Label(" ", TileNameStyle(theme), GUILayout.Width(TileWidth));
            }

            GUILayout.BeginHorizontal();

            // 一覧の画像を選ぶ
            GUI.enabled = FileDialog.IsSupported;
            if (GUILayout.Button(Loc.T("Image", "画像", "이미지", "图片", "圖片"), GuiControls.Shrinkable))
            {
                BrowseThumbnail(path);
            }

            // 一覧から外す（記憶したカメラ・ライト・画像等も消える）。表示中のアバターはすぐ記録し直されるため外せない
            GUI.enabled = !_session.IsLoading && !isCurrent;
            if (GUILayout.Button("×", GuiControls.Shrinkable))
            {
                _pendingForget = path;
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private GUIStyle TileNameStyle(UiTheme theme)
        {
            // 説明文の文字を 1 行・中央寄せ・はみ出しは切る形にしたもの（テーマが変わったら作り直す）
            GUIStyle source = theme != null ? theme.Hint : GUI.skin.label;
            if (_tileNameStyle == null || _tileNameBase != source)
            {
                _tileNameBase = source;
                _tileNameStyle = new GUIStyle(source)
                {
                    wordWrap = false,
                    clipping = TextClipping.Clip,
                    alignment = TextAnchor.MiddleCenter,
                };
            }

            return _tileNameStyle;
        }

        private void BrowseThumbnail(string avatarPath)
        {
            // 選ばれたら一覧の画像にする（キャンセル時は何もしない）。最初はアバターのフォルダを開く
            string image = FileDialog.OpenFile(
                Loc.T("Choose an image for the list", "一覧の画像を選ぶ", "목록 이미지 선택", "选择列表图片", "選擇清單圖片"),
                Loc.T("Image (PNG / JPG / GIF)", "画像（PNG / JPG / GIF）", "이미지 (PNG / JPG / GIF)", "图片（PNG / JPG / GIF）",
                    "圖片（PNG / JPG / GIF）"),
                AvatarThumbnails.Extensions, avatarPath);
            if (image != null)
            {
                _thumbnailError = _thumbnails.Set(avatarPath, image);
            }
        }

        private bool SetCurrentThumbnail(string image)
        {
            // 画像のドロップは表示中のアバターが対象
            string current = _session.Current?.SourcePath;
            if (string.IsNullOrEmpty(current))
            {
                _inputError = InputError.ImageWithoutAvatar;
                _inputName = FileNameOf(image);
                return false;
            }

            _inputError = InputError.None;
            _thumbnailError = _thumbnails.Set(current, image);
            return _thumbnailError == null;
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
                DrawThumbnailButtons(avatar.SourcePath);
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

        private void DrawThumbnailButtons(string avatarPath)
        {
            GUILayout.BeginHorizontal();

            // 最近使ったアバターの一覧に出す画像を選ぶ
            GUI.enabled = FileDialog.IsSupported;
            if (GUILayout.Button(Loc.T("Choose list image...", "一覧の画像を選ぶ...", "목록 이미지 선택...", "选择列表图片...",
                    "選擇清單圖片..."), GuiControls.Shrinkable))
            {
                BrowseThumbnail(avatarPath);
            }

            // 設定済みの画像を外す
            GUI.enabled = _thumbnails.Has(avatarPath);
            if (GUILayout.Button(Loc.T("Remove image", "画像を外す", "이미지 제거", "移除图片", "移除圖片"), GuiControls.Shrinkable))
            {
                _thumbnails.Clear(avatarPath);
                _thumbnailError = null;
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private string DescribeInputError()
        {
            // 確認エラーの文言（表示言語に合わせる）
            switch (_inputError)
            {
                case InputError.ImageWithoutAvatar:
                    return Loc.T($"Error: load an avatar before dropping an image ({_inputName}). The image is used for the avatar being shown.",
                        $"エラー: 画像（{_inputName}）は表示中のアバターに設定されます。先にアバターを読み込んでください。",
                        $"오류: 이미지({_inputName})는 표시 중인 아바타에 설정됩니다. 먼저 아바타를 불러오세요.",
                        $"错误：图片（{_inputName}）会设置给正在显示的虚拟形象。请先加载虚拟形象。",
                        $"錯誤：圖片（{_inputName}）會設定給正在顯示的虛擬形象。請先載入虛擬形象。");
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
