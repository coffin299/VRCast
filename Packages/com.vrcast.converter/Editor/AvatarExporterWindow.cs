using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRCast.AvatarFormat;
using static VRCast.Converter.Editor.ExporterLoc;
using Object = UnityEngine.Object;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// アバターを選んで .vrcaster を書き出す Editor ウィンドウ。
    /// </summary>
    public class AvatarExporterWindow : EditorWindow
    {
        // 次回の保存ダイアログ初期フォルダを保持する EditorPrefs キー
        private const string LastDirectoryKey = "VRCast.Converter.LastExportDirectory";

        // 「シーンの BlendShape の値を優先」の選択を保持する EditorPrefs キー
        private const string KeepSceneBlendShapesKey = "VRCast.Converter.KeepSceneBlendShapes";

        // ダイアログ・進捗バーのタイトル（製品名なので訳さない）
        private const string Title = "VRCast Exporter";

        // アプリアイコン（VRCastIcon.png）の GUID。UPM（Packages/）と unitypackage（Assets/）で置き場所が違うため GUID で探す
        private const string IconGuid = "f0bfda6d028343bab19380553d3cfe26";

        // ヘッダーに表示するアイコンの大きさ（画像は高 DPI 用に 2 倍の 128px）
        private const float HeaderIconSize = 64f;

        // ヘッダーの製品名の文字サイズ
        private const int TitleFontSize = 18;

        // 追加の表情クリップのドロップ欄の高さ
        private const float DropAreaHeight = 40f;

        // 一覧の行の削除ボタンの幅
        private const float RemoveButtonWidth = 24f;

        [SerializeField]
        private GameObject _avatar;

        private Texture2D _icon;
        private GUIStyle _titleStyle;
        private Vector2 _scroll;

        // 追加の表情クリップの指定と、その指定を読み込んだアバター（アバターが変わったら読み直す）
        private List<Object> _extraEntries = new List<Object>();
        private GameObject _extraOwner;
        private bool _extraLoaded;

        // 指定を展開したクリップと判定結果（指定・プロジェクトが変わったときだけ作り直す。null = 未作成）
        private List<KeyValuePair<AnimationClip, ClipCheck>> _extraChecks;

        // 直前のドロップの結果の表示と、その種類（null = なし）
        private string _dropNotice;
        private MessageType _dropNoticeType;

        [MenuItem("VRCast/Avatar Exporter")]
        private static void Open()
        {
            // 既存ウィンドウがあれば再利用
            GetWindow<AvatarExporterWindow>(Title);
        }

        private void OnEnable()
        {
            // アイコンが見つからなくても書き出しは使えるよう、無ければ表示しないだけにする
            string iconPath = AssetDatabase.GUIDToAssetPath(IconGuid);
            _icon = string.IsNullOrEmpty(iconPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            titleContent = new GUIContent(Title, _icon);
        }

        private void OnFocus()
        {
            // 別の画面でクリップを編集して戻ってきたときに判定し直す
            _extraChecks = null;
        }

        private void OnProjectChange()
        {
            // クリップ・フォルダの追加や削除を判定に反映する
            _extraChecks = null;
            Repaint();
        }

        private void OnGUI()
        {
            // 追加の表情クリップの欄でウィンドウより長くなるため、全体をスクロールできるようにする
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawContents();
            EditorGUILayout.EndScrollView();
        }

        private void DrawContents()
        {
            DrawHeader();

            // 表示言語の切り替え（選んだ言語は EditorPrefs に保存し、次回以降も使う）
            Selected = (Language)EditorGUILayout.Popup(
                T("Language", "言語", "언어", "语言", "語言"), (int)Selected, LanguageLabels());

            EditorGUILayout.Space();

            // シーン上のオブジェクトと Prefab の両方を受け付ける
            _avatar = (GameObject)EditorGUILayout.ObjectField(
                T("Avatar Root", "アバターのルート", "아바타 루트", "虚拟形象根对象", "虛擬形象根物件"),
                _avatar, typeof(GameObject), true);

            // 検証結果を表示し、問題があれば書き出しを無効化
            string error = AvatarExporter.Validate(_avatar);
            if (error != null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                T(
                    "Only Unity built-in components (Transform, Animator, Renderers, MeshFilter) are exported. " +
                    "VRChat components, scripts and the Animator Controller are removed from the exported copy. " +
                    "The FX layer's default state (toggles etc.) is baked into the copy before removal. " +
                    "If NDMF is installed, Modular Avatar and other NDMF tools are applied to the copy first. " +
                    "Outfits and accessories set up with MA Merge Armature / Bone Proxy that were not merged " +
                    "are attached to the avatar's bones by VRCast so that they follow the avatar.",
                    "書き出されるのは Unity 標準のコンポーネント（Transform・Animator・Renderer・MeshFilter）だけです。" +
                    "VRChat のコンポーネント・スクリプト・Animator Controller は書き出し用の複製から取り除かれます" +
                    "（元のアバターは変更しません）。取り除く前に、FX レイヤーの初期状態（トグルなど）を複製に焼き込みます。" +
                    "NDMF が入っていれば、Modular Avatar などの改変を先に複製へ適用します。" +
                    "MA Merge Armature / Bone Proxy で付けた衣装・小物が統合されなかった場合は、" +
                    "VRCast がアバターのボーンに付け替えて体に追従させます。",
                    "Unity 기본 컴포넌트(Transform, Animator, Renderer, MeshFilter)만 내보냅니다. " +
                    "VRChat 컴포넌트, 스크립트, Animator Controller는 내보내기용 복제본에서 제거됩니다" +
                    "(원본 아바타는 변경하지 않습니다). 제거하기 전에 FX 레이어의 초기 상태(토글 등)를 복제본에 반영합니다. " +
                    "NDMF가 설치되어 있으면 Modular Avatar 등의 개조를 먼저 복제본에 적용합니다. " +
                    "MA Merge Armature / Bone Proxy로 붙인 의상·소품이 통합되지 않은 경우에는 " +
                    "VRCast가 아바타의 본에 다시 붙여 몸을 따라가게 합니다.",
                    "只导出 Unity 内置组件（Transform、Animator、Renderer、MeshFilter）。" +
                    "VRChat 组件、脚本和 Animator Controller 会从导出用的副本中移除（不会修改原始虚拟形象）。" +
                    "移除前会把 FX 层的初始状态（开关等）烘焙到副本中。" +
                    "如果安装了 NDMF，会先把 Modular Avatar 等改造应用到副本。" +
                    "用 MA Merge Armature / Bone Proxy 添加的服装和小物如果未被合并，" +
                    "VRCast 会把它们重新挂到虚拟形象的骨骼上并跟随身体。",
                    "只匯出 Unity 內建元件（Transform、Animator、Renderer、MeshFilter）。" +
                    "VRChat 元件、腳本和 Animator Controller 會從匯出用的副本中移除（不會修改原始虛擬形象）。" +
                    "移除前會把 FX 層的初始狀態（開關等）烘焙到副本中。" +
                    "如果安裝了 NDMF，會先把 Modular Avatar 等改造套用到副本。" +
                    "用 MA Merge Armature / Bone Proxy 新增的服裝和小物如果未被合併，" +
                    "VRCast 會把它們重新掛到虛擬形象的骨骼上並跟隨身體。"),
                MessageType.Info);

            DrawBlendShapeOption();
            DrawExtraExpressions();

            using (new EditorGUI.DisabledScope(error != null))
            {
                if (GUILayout.Button(T("Export...", "書き出す...", "내보내기...", "导出...", "匯出...")))
                {
                    ExportWithDialog();
                }
            }
        }

        private static void DrawBlendShapeOption()
        {
            // 既定はシーンの値を優先（Unity 上で調整した体型・表情をそのまま書き出す）
            bool keep = EditorPrefs.GetBool(KeepSceneBlendShapesKey, true);
            bool changed = EditorGUILayout.ToggleLeft(
                T("Keep blend shape values from the scene", "シーンのブレンドシェイプの値を優先する",
                    "씬의 블렌드셰이프 값을 우선", "优先使用场景中的 BlendShape 值", "優先使用場景中的 BlendShape 值"),
                keep);
            if (changed != keep)
            {
                EditorPrefs.SetBool(KeepSceneBlendShapesKey, changed);
            }

            // OFF のときの挙動を補足（縮小用ブレンドシェイプなど FX で切り替える仕組み向け）
            EditorGUILayout.HelpBox(
                T(
                    "When off, blend shapes animated by the FX layer use their default-state values instead " +
                    "(useful when outfit toggles also drive shrink blend shapes).",
                    "OFF にすると、FX レイヤーで動かしているブレンドシェイプは初期状態の値で書き出します" +
                    "（衣装の切り替えに合わせて縮小用ブレンドシェイプも動かしている場合など）。",
                    "OFF로 하면 FX 레이어에서 움직이는 블렌드셰이프는 초기 상태의 값으로 내보냅니다" +
                    "(의상 전환에 맞춰 축소용 블렌드셰이프도 움직이는 경우 등).",
                    "关闭后，FX 层驱动的 BlendShape 会以初始状态的值导出（例如切换服装时同时驱动收缩用 BlendShape）。",
                    "關閉後，FX 層驅動的 BlendShape 會以初始狀態的值匯出（例如切換服裝時同時驅動收縮用 BlendShape）。"),
                MessageType.None);
        }

        private void DrawExtraExpressions()
        {
            // アバターが変わった・ウィンドウを開き直したときは、そのアバターの指定を読み直す
            if (!_extraLoaded || _extraOwner != _avatar)
            {
                // アバター未選択の間に追加したものは、選んだアバターの指定に足して保存する（捨てない）
                List<Object> pending = _extraLoaded && _extraOwner == null ? _extraEntries : null;
                _extraEntries = ExtraExpressionClips.Load(_avatar);
                if (pending != null && _avatar != null && pending.Count > 0)
                {
                    foreach (Object entry in pending)
                    {
                        if (!_extraEntries.Contains(entry))
                        {
                            _extraEntries.Add(entry);
                        }
                    }

                    ExtraExpressionClips.Save(_avatar, _extraEntries);
                }

                _extraOwner = _avatar;
                _extraLoaded = true;
                _extraChecks = null;
                _dropNotice = null;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                T("Additional expressions", "追加の表情", "추가 표정", "追加表情", "追加表情"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                T(
                    "Expression clips in the FX layer are exported automatically. Add animation clips that are not " +
                    "in the FX layer (or folders containing them) here to use them as expressions in VRCast. " +
                    "Only clips that move blend shapes alone are used. Saved per avatar.",
                    "FX レイヤーにある表情クリップは自動で書き出されます。FX に入っていないアニメーションクリップ" +
                    "（またはそれが入ったフォルダ）をここに追加すると、VRCast で表情として使えます。" +
                    "ブレンドシェイプだけを動かすクリップが対象です。指定はアバターごとに保存されます。",
                    "FX 레이어에 있는 표정 클립은 자동으로 내보냅니다. FX에 없는 애니메이션 클립" +
                    "(또는 그것이 든 폴더)을 여기에 추가하면 VRCast에서 표정으로 사용할 수 있습니다. " +
                    "블렌드셰이프만 움직이는 클립이 대상입니다. 아바타별로 저장됩니다.",
                    "FX 层中的表情剪辑会自动导出。在此添加不在 FX 中的动画剪辑（或包含它们的文件夹），" +
                    "即可在 VRCast 中作为表情使用。仅限只驱动 BlendShape 的剪辑。按虚拟形象分别保存。",
                    "FX 層中的表情剪輯會自動匯出。在此新增不在 FX 中的動畫剪輯（或包含它們的資料夾），" +
                    "即可在 VRCast 中作為表情使用。僅限只驅動 BlendShape 的剪輯。按虛擬形象分別儲存。"),
                MessageType.None);

            // アバター未選択でも追加できる（保存はアバターを選んだときに行う）
            if (_avatar == null)
            {
                EditorGUILayout.HelpBox(
                    T("No avatar selected. Added clips are saved when you select an avatar.",
                        "アバターが未選択です。追加したものはアバターを選んだときに保存されます。",
                        "아바타가 선택되지 않았습니다. 추가한 것은 아바타를 선택할 때 저장됩니다.",
                        "尚未选择虚拟形象。添加的内容会在选择虚拟形象时保存。",
                        "尚未選擇虛擬形象。新增的內容會在選擇虛擬形象時儲存。"),
                    MessageType.Info);
            }

            bool changed = DrawExtraEntryRows();
            changed |= HandleExtraDrop();

            // 変更があればすぐ保存し（アバター未選択なら保存しない）、判定を作り直す
            if (changed)
            {
                ExtraExpressionClips.Save(_avatar, _extraEntries);
                _extraChecks = null;
            }

            DrawExtraChecks();
        }

        private void DrawExtraChecks()
        {
            // 指定が無ければ表示しない
            if (_extraEntries.Count == 0)
            {
                return;
            }

            // 判定は重いため、作り直しが必要なときだけ行う（行の数が変わるのでレイアウト計算のときに限る）
            if (_extraChecks == null)
            {
                if (Event.current.type != EventType.Layout)
                {
                    Repaint();
                    return;
                }

                _extraChecks = new List<KeyValuePair<AnimationClip, ClipCheck>>();
                foreach (AnimationClip clip in ExtraExpressionClips.Collect(_extraEntries))
                {
                    _extraChecks.Add(new KeyValuePair<AnimationClip, ClipCheck>(clip, ExpressionExtractor.Check(clip)));
                }
            }

            // 取り込める数 / クリップの数
            int usable = 0;
            foreach (KeyValuePair<AnimationClip, ClipCheck> check in _extraChecks)
            {
                usable += check.Value == ClipCheck.Expression ? 1 : 0;
            }

            EditorGUILayout.LabelField(
                T("Expressions to add", "追加される表情", "추가될 표정", "将追加的表情", "將追加的表情")
                + $": {usable} / {_extraChecks.Count}",
                EditorStyles.miniBoldLabel);

            // クリップごとに取り込めるか（取り込めない理由）を表示。クリックでプロジェクト上の場所を示す
            foreach (KeyValuePair<AnimationClip, ClipCheck> check in _extraChecks)
            {
                string mark = check.Value == ClipCheck.Expression ? "✓" : "✗";
                string reason = check.Value == ClipCheck.Expression ? string.Empty : " — " + DescribeCheck(check.Value);
                if (GUILayout.Button($"{mark} {check.Key.name}{reason}", EditorStyles.miniLabel))
                {
                    EditorGUIUtility.PingObject(check.Key);
                }
            }

            // 名前が FX の表情と重なると番号付きになることを補足
            EditorGUILayout.HelpBox(
                T(
                    "Clips that are also in the FX layer are added only once. If the name matches another expression, " +
                    "a number is added, such as \"Name (2)\".",
                    "FX レイヤーにもあるクリップは 1 回だけ入ります。名前がほかの表情と重なる場合は「名前 (2)」のように番号が付きます。",
                    "FX 레이어에도 있는 클립은 한 번만 들어갑니다. 이름이 다른 표정과 겹치면 「이름 (2)」처럼 번호가 붙습니다.",
                    "同时在 FX 层中的剪辑只会加入一次。名称与其他表情重复时会加上编号，例如“名称 (2)”。",
                    "同時在 FX 層中的剪輯只會加入一次。名稱與其他表情重複時會加上編號，例如「名稱 (2)」。"),
                MessageType.None);
        }

        private static string DescribeCheck(ClipCheck check)
        {
            // 取り込めない理由の文言
            switch (check)
            {
                case ClipCheck.HasOtherCurves:
                    return T("also moves things other than blend shapes", "ブレンドシェイプ以外も動かしています",
                        "블렌드셰이프 이외의 것도 움직입니다", "还驱动了 BlendShape 以外的内容", "還驅動了 BlendShape 以外的內容");
                case ClipCheck.NoCurves:
                    return T("empty clip", "中身が空です", "빈 클립입니다", "剪辑为空", "剪輯為空");
                case ClipCheck.TooManyCurves:
                    return T("too many blend shapes", "ブレンドシェイプが多すぎます", "블렌드셰이프가 너무 많습니다",
                        "BlendShape 过多", "BlendShape 過多");
                case ClipCheck.AllZero:
                    return T("all values are 0 (reset clip)", "値がすべて 0 です（戻す用のクリップ）",
                        "값이 모두 0입니다 (되돌리기용 클립)", "所有值均为 0（复位用剪辑）", "所有值均為 0（復位用剪輯）");
                default:
                    return string.Empty;
            }
        }

        private bool DrawExtraEntryRows()
        {
            bool changed = false;
            int removeAt = -1;
            for (int i = 0; i < _extraEntries.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // 差し替えは使えるもの（クリップ・フォルダ）のときだけ受け付ける
                    Object picked = EditorGUILayout.ObjectField(_extraEntries[i], typeof(Object), false);
                    if (picked != _extraEntries[i] && ExtraExpressionClips.IsAccepted(picked))
                    {
                        _extraEntries[i] = picked;
                        changed = true;
                    }

                    // × で外す行を控える（描画中に一覧を変えると行の数が合わなくなる）
                    if (GUILayout.Button("×", GUILayout.Width(RemoveButtonWidth)))
                    {
                        removeAt = i;
                    }
                }
            }

            // 描画し終えてから一覧から外す
            if (removeAt >= 0)
            {
                _extraEntries.RemoveAt(removeAt);
                changed = true;
            }

            return changed;
        }

        private bool HandleExtraDrop()
        {
            // ドロップ欄（複数のクリップ・フォルダを一度に追加できる）
            Rect area = GUILayoutUtility.GetRect(0f, DropAreaHeight, GUILayout.ExpandWidth(true));
            GUI.Box(area, T("Drop animation clips or folders here", "ここにアニメーションクリップかフォルダをドロップ",
                "여기에 애니메이션 클립이나 폴더를 드롭", "将动画剪辑或文件夹拖放到此处", "將動畫剪輯或資料夾拖放到此處"),
                EditorStyles.helpBox);

            // 直前のドロップの結果（追加した件数、または使えなかった理由）
            if (_dropNotice != null)
            {
                EditorGUILayout.HelpBox(_dropNotice, _dropNoticeType);
            }

            // ドロップ欄の上でのドラッグ操作だけを扱う
            // Use() で種類が Used に変わるため、先に控えておく
            Event current = Event.current;
            EventType type = current.type;
            bool dragging = type == EventType.DragUpdated || type == EventType.DragPerform;
            if (!dragging || !area.Contains(current.mousePosition))
            {
                return false;
            }

            // 使えるものが含まれていればコピーのカーソルにする（エクスプローラーからのプロジェクト内のフォルダも可）
            List<Object> dropped = ExtraExpressionClips.FromDrag(
                DragAndDrop.objectReferences, DragAndDrop.paths, out bool outsideProject);
            DragAndDrop.visualMode = dropped.Count > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            current.Use();

            // 離したときだけ処理する
            if (type != EventType.DragPerform)
            {
                return false;
            }

            DragAndDrop.AcceptDrag();

            // 使えるもののうち、まだ一覧に無いものを追加する
            int added = 0;
            foreach (Object entry in dropped)
            {
                if (!_extraEntries.Contains(entry))
                {
                    _extraEntries.Add(entry);
                    added++;
                }
            }

            // 結果を次のドロップまで表示し、追加した行と判定がすぐ見えるよう描き直す
            SetDropNotice(added, dropped.Count, outsideProject);
            Repaint();
            return added > 0;
        }

        private void SetDropNotice(int added, int usable, bool outsideProject)
        {
            // プロジェクトの外のものがあれば、追加できたものがあっても警告を優先する
            if (outsideProject)
            {
                _dropNoticeType = MessageType.Warning;
                _dropNotice = T("Folders outside this Unity project cannot be used. Move them into the project (Assets) first.",
                    "この Unity プロジェクトの外にあるフォルダは使えません。先にプロジェクト（Assets）の中へ入れてください。",
                    "이 Unity 프로젝트 밖에 있는 폴더는 사용할 수 없습니다. 먼저 프로젝트 (Assets) 안으로 옮기세요.",
                    "无法使用此 Unity 项目之外的文件夹。请先将其移入项目（Assets）中。",
                    "無法使用此 Unity 專案之外的資料夾。請先將其移入專案（Assets）中。");
                return;
            }

            // クリップでもフォルダでもない
            if (usable == 0)
            {
                _dropNoticeType = MessageType.Warning;
                _dropNotice = T("Only animation clips (.anim) and folders can be dropped.",
                    "ドロップできるのはアニメーションクリップ（.anim）とフォルダだけです。",
                    "드롭할 수 있는 것은 애니메이션 클립 (.anim)과 폴더뿐입니다.",
                    "只能拖放动画剪辑（.anim）和文件夹。", "只能拖放動畫剪輯（.anim）和資料夾。");
                return;
            }

            // 追加した件数（全部が追加済みならその旨）
            _dropNoticeType = MessageType.Info;
            _dropNotice = added > 0
                ? T($"Added {added}. Check below whether each clip can be used.",
                    $"{added} 件追加しました。取り込めるかどうかは下の一覧で確認できます。",
                    $"{added}개 추가했습니다. 가져올 수 있는지는 아래 목록에서 확인할 수 있습니다.",
                    $"已添加 {added} 项。可在下方列表中确认能否导入。",
                    $"已新增 {added} 項。可在下方清單中確認能否匯入。")
                : T("Already added.", "すでに追加されています。", "이미 추가되어 있습니다.", "已经添加过了。", "已經新增過了。");
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // アイコンは読み込めたときだけ左端に置く
                if (_icon != null)
                {
                    Rect iconRect = GUILayoutUtility.GetRect(
                        HeaderIconSize, HeaderIconSize, GUILayout.Width(HeaderIconSize), GUILayout.Height(HeaderIconSize));
                    GUI.DrawTexture(iconRect, _icon, ScaleMode.ScaleToFit);
                }

                // 製品名と説明をアイコンの高さの中央にそろえる
                using (new EditorGUILayout.VerticalScope(GUILayout.Height(HeaderIconSize)))
                {
                    GUILayout.FlexibleSpace();
                    // EditorStyles は OnEnable 時点で未初期化のことがあるため、描画時に作る
                    if (_titleStyle == null)
                    {
                        _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = TitleFontSize };
                    }

                    EditorGUILayout.LabelField(Title, _titleStyle, GUILayout.Height(TitleFontSize + 6));
                    EditorGUILayout.LabelField(
                        T("Export avatar to .vrcaster", "アバターを .vrcaster に書き出す", "아바타를 .vrcaster로 내보내기",
                            "将虚拟形象导出为 .vrcaster", "將虛擬形象匯出為 .vrcaster"),
                        EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                }
            }

            EditorGUILayout.Space();
        }

        private void ExportWithDialog()
        {
            // 保存先を選択（キャンセル時は空文字）
            string directory = EditorPrefs.GetString(LastDirectoryKey, string.Empty);
            string path = EditorUtility.SaveFilePanel(
                T("Export VRCast Avatar", "VRCast アバターを書き出す", "VRCast 아바타 내보내기",
                    "导出 VRCast 虚拟形象", "匯出 VRCast 虛擬形象"),
                directory, _avatar.name, AvatarPackageLayout.Extension.TrimStart('.'));
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            // 次回用に保存先フォルダを記憶
            EditorPrefs.SetString(LastDirectoryKey, Path.GetDirectoryName(path));

            try
            {
                // 書き出し中は進捗バーを表示
                EditorUtility.DisplayProgressBar(
                    Title,
                    T("Building avatar package...", "アバターパッケージを作成中...", "아바타 패키지 생성 중...",
                        "正在生成虚拟形象包...", "正在產生虛擬形象包..."),
                    0.5f);
                AvatarExporter.Report report = AvatarExporter.Export(
                    _avatar, path, EditorPrefs.GetBool(KeepSceneBlendShapesKey, true),
                    ExtraExpressionClips.Collect(_extraEntries));

                // Console のログは問い合わせ時に読みやすいよう英語固定、ダイアログは表示言語
                Debug.Log("[VRCast][Exporter] " + BuildSummary(report, true).Replace("\n", " / "));
                EditorUtility.DisplayDialog(Title, BuildSummary(report, false), "OK");
            }
            catch (Exception e)
            {
                // 失敗理由を表示（スタックトレースは Console へ）
                Debug.LogException(e);
                EditorUtility.DisplayDialog(
                    Title,
                    T("Export failed:", "書き出しに失敗しました:", "내보내기에 실패했습니다:", "导出失败：", "匯出失敗：")
                        + "\n" + e.Message,
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static string BuildSummary(AvatarExporter.Report report, bool english)
        {
            // english なら英語固定、そうでなければ表示言語で項目名を選ぶ
            string L(string en, string ja, string ko, string zhHans, string zhHant)
            {
                return english ? en : T(en, ja, ko, zhHans, zhHant);
            }

            // 補間文字列の {} 内で改行できない（C# 9）ため、項目名は先に変数へ取り出す
            string exported = L("Exported", "出力先", "출력 위치", "输出位置", "輸出位置");
            string size = L("Size", "サイズ", "크기", "大小", "大小");
            string ndmf = L("NDMF (Modular Avatar) applied", "NDMF（Modular Avatar）の適用", "NDMF(Modular Avatar) 적용",
                "已应用 NDMF（Modular Avatar）", "已套用 NDMF（Modular Avatar）");
            string attached = L("Outfits / accessories attached by VRCast", "VRCast が付け替えた衣装・小物",
                "VRCast가 다시 붙인 의상·소품", "VRCast 重新挂接的服装和小物", "VRCast 重新掛接的服裝和小物");
            string baked = L("Baked FX default clips", "焼き込んだ FX 初期状態のクリップ", "반영한 FX 초기 상태 클립",
                "已烘焙的 FX 初始状态剪辑", "已烘焙的 FX 初始狀態剪輯");
            string sceneBlendShapes = L("Kept scene blend shapes", "シーンのブレンドシェイプを優先",
                "씬의 블렌드셰이프 우선", "优先场景 BlendShape", "優先場景 BlendShape");
            string expressions = L("Expressions", "表情", "표정", "表情", "表情");
            string extraExpressions = L("added clips", "追加したクリップ", "추가한 클립", "追加的剪辑", "追加的剪輯");
            // FaceEmo の設定があったときだけ件数を出す
            string faceEmo = report.FaceEmoClips > 0
                ? $"FaceEmo: {report.FaceEmoExpressionCount} / {report.FaceEmoClips}, "
                : string.Empty;
            string lipSync = L("Lip sync", "リップシンク", "립싱크", "口型同步", "口型同步");
            string blink = L("blink", "まばたき", "눈 깜빡임", "眨眼", "眨眼");
            string wink = L("wink", "ウインク", "윙크", "眨单眼", "眨單眼");
            string removed = L("Removed components", "取り除いたコンポーネント", "제거한 컴포넌트", "已移除的组件",
                "已移除的元件");
            string missing = L("missing scripts", "Missing Script", "Missing Script", "Missing Script",
                "Missing Script");
            string editorOnly = L("EditorOnly objects", "EditorOnly オブジェクト", "EditorOnly 오브젝트", "EditorOnly 对象",
                "EditorOnly 物件");
            string inactive = L("inactive objects", "非アクティブのオブジェクト", "비활성 오브젝트", "未激活的对象",
                "未啟用的物件");

            return
                $"{exported}: {report.OutputPath}\n" +
                $"{size}: {report.Manifest.bundleSize / (1024f * 1024f):F1} MB\n" +
                $"Humanoid: {report.IsHumanoid}\n" +
                $"{ndmf}: {report.NdmfApplied}\n" +
                $"{attached}: {report.ModularAvatarFallbackFixes}\n" +
                $"{baked}: {report.BakedFxClips}\n" +
                $"{sceneBlendShapes}: {report.KeptSceneBlendShapes}\n" +
                $"{expressions}: {report.ExpressionCount} " +
                $"({faceEmo}{extraExpressions}: {report.ExtraExpressionCount} / {report.ExtraExpressionClips})\n" +
                $"{lipSync}: {report.LipSyncMode}, {blink}: {report.HasBlink}, {wink}: {report.HasWink}\n" +
                $"PhysBones: {report.PhysBoneCount}\n" +
                $"Constraints: {report.ConstraintCount}\n" +
                $"Blendshape Sync (MA): {report.BlendShapeSyncCount}\n" +
                $"{removed}: {report.Strip.RemovedComponents}, " +
                $"{missing}: {report.Strip.RemovedMissingScripts}, " +
                $"{editorOnly}: {report.Strip.RemovedEditorOnlyObjects}, " +
                $"{inactive}: {report.RemovedInactiveObjects}";
        }
    }
}
