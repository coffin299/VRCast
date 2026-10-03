using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Platform;

namespace VRCast.UI
{
    /// <summary>
    /// Avatar タブ（.vrcaster のドロップ・ファイル選択・パス入力による読み込み、再読み込み・解除と、表示中アバターの情報）。
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

        private readonly AvatarSession _session;
        private string _pathInput;

        // 直前の確認結果と、対象のファイル名（表示時に言語へ合わせて文言を作る）
        private InputError _inputError;
        private string _inputName = string.Empty;

        public AvatarSection(AvatarSession session, string initialPath)
        {
            _session = session;
            _pathInput = initialPath ?? string.Empty;
        }

        public void Draw()
        {
            DrawLoader();
            DrawInfo();
        }

        /// <summary>
        /// ドロップされたパスから最初の .vrcaster を読み込む。対応ファイルが無ければエラーを表示して false。
        /// </summary>
        public bool LoadDropped(IReadOnlyList<string> paths)
        {
            // .vrcaster が無ければ先頭のファイル名でエラー表示
            string package = AvatarFiles.FindPackage(paths);
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
            GuiControls.BeginCard(Loc.T("Avatar file", "アバターファイル"));
            GuiControls.Hint(Loc.T(
                "Drop a .vrcaster file onto the window, choose one with Browse, or enter the path",
                ".vrcaster ファイルをウィンドウにドロップするか、参照で選ぶか、パスを入力してください"));

            GUILayout.BeginHorizontal();
            _pathInput = GUILayout.TextField(_pathInput);

            // ファイル選択（Windows のみ、読込中は無効）
            GUI.enabled = FileDialog.IsSupported && !_session.IsLoading;
            if (GUILayout.Button(Loc.T("Browse...", "参照..."), GUILayout.Width(BrowseWidth)))
            {
                Browse();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            // 読込中はボタンを無効化
            GUI.enabled = !_session.IsLoading;
            if (GUILayout.Button(Loc.T("Load", "読み込み")))
            {
                TryLoad(_pathInput);
            }

            // 表示中のアバターがある場合のみ有効
            GUI.enabled = !_session.IsLoading && _session.Current != null;
            if (GUILayout.Button(Loc.T("Reload", "再読み込み")))
            {
                _inputError = InputError.None;
                _session.Reload();
            }

            if (GUILayout.Button(Loc.T("Unload", "解除")))
            {
                _inputError = InputError.None;
                _session.Unload();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GuiControls.EndCard();
        }

        /// <summary>
        /// 直前の読み込みが失敗している（読み込む前の確認エラーを含む）なら true。
        /// </summary>
        public bool HasError => !_session.IsLoading && (_inputError != InputError.None || _session.LastError != null);

        /// <summary>
        /// ファイル選択ダイアログで .vrcaster を選ばせて読み込む（キャンセル時は何もしない）。
        /// </summary>
        public void Browse()
        {
            // 選ばれたら入力欄に反映して読み込む（キャンセル時は何もしない）
            string path = FileDialog.OpenFile(
                Loc.T("Open avatar", "アバターを開く"), Loc.T("VRCast avatar", "VRCast アバター"),
                AvatarPackageLayout.Extension, PathUtility.NormalizeInput(_pathInput));
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
            else if (!AvatarFiles.IsPackage(path))
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
            GuiControls.BeginCard(Loc.T("Status", "状態"));

            // 状態表示: 読込中 > 読み込む前の確認エラー > 読込エラー > アバター情報 の優先順
            if (_session.IsLoading)
            {
                GUILayout.Label(Loc.T("Loading...", "読み込み中..."));
            }
            else if (_inputError != InputError.None)
            {
                GUILayout.Label(DescribeInputError());
            }
            else if (_session.LastError != null)
            {
                GUILayout.Label(Loc.T("Error: ", "エラー: ") + _session.LastError);
            }
            else if (_session.Current != null)
            {
                LoadedAvatar avatar = _session.Current;
                GUILayout.Label(Loc.T("Name: ", "名前: ") + avatar.Manifest.name);
                string humanoid = avatar.IsHumanoid ? Loc.T("Yes", "はい") : Loc.T("No", "いいえ");
                GuiControls.Hint($"Humanoid: {humanoid}    {Loc.T("Renderers", "レンダラー")}: {avatar.RendererCount}");
                GuiControls.Hint(Loc.T("Built with Unity ", "書き出し Unity ") + avatar.Manifest.unityVersion);
            }
            else
            {
                GUILayout.Label(Loc.T("No avatar loaded.", "アバターが読み込まれていません。"));
            }

            GuiControls.EndCard();
        }

        private string DescribeInputError()
        {
            // 確認エラーの文言（表示言語に合わせる）
            switch (_inputError)
            {
                case InputError.Empty:
                    return Loc.T("Error: enter the path of a .vrcaster file.", "エラー: .vrcaster ファイルのパスを入力してください。");
                case InputError.NotFound:
                    return Loc.T($"Error: file not found ({_inputName}).", $"エラー: ファイルが見つかりません（{_inputName}）。");
                default:
                    return Loc.T(
                        $"Error: unsupported file ({_inputName}). Only .vrcaster files can be loaded.",
                        $"エラー: 対応していないファイルです（{_inputName}）。読み込めるのは .vrcaster ファイルのみです。");
            }
        }
    }
}
