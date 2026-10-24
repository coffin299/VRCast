using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

        // 「非アクティブのオブジェクトも書き出す」の選択を保持する EditorPrefs キー
        private const string IncludeInactiveObjectsKey = "VRCast.Converter.IncludeInactiveObjects";

        // 「アバターと合わない衣装の姿勢を直す」の選択を保持する EditorPrefs キー
        private const string FixOutfitPosesKey = "VRCast.Converter.FixOutfitPoses";

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

        // 左の設定の列の幅（ウィンドウの幅に対する割合と上下限）と列の間隔
        private const float LeftPaneRatio = 0.42f;
        private const float LeftPaneMinWidth = 300f;
        private const float LeftPaneMaxWidth = 520f;
        private const float PaneGap = 4f;
        private static readonly Color SeparatorColor = new Color(0f, 0f, 0f, 0.3f);
        private static readonly Color PlaceholderColor = new Color(0f, 0f, 0f, 0.12f);

        // プレビューの高さ（下の 2 列の高さに対する割合と下限）
        private const float PreviewHeightRatio = 0.65f;
        private const float MinPreviewHeight = 260f;

        // 上の段（アバターの指定・書き出すボタン・進み具合）の高さと、ボタンの幅、言語の選択欄の幅
        private const float TopBarHeight = 24f;
        private const float TopButtonWidth = 140f;
        private const float LanguagePopupWidth = 110f;

        // 新しく開くときのウィンドウの大きさと、最小の大きさ（左右 2 列が収まる幅）
        private static readonly Vector2 DefaultWindowSize = new Vector2(1000f, 720f);
        private static readonly Vector2 MinWindowSize = new Vector2(640f, 480f);

        [SerializeField]
        private GameObject _avatar;

        private Texture2D _icon;
        private GUIStyle _titleStyle;
        private Vector2 _scroll;
        private Vector2 _resultScroll;
        private float _columnsHeight = MinPreviewHeight / PreviewHeightRatio;
        private GUIStyle _placeholderStyle;

        // 「書き出される内容について」を開いているか
        [SerializeField]
        private bool _showAbout;

        // 追加の表情クリップの指定と、その指定を読み込んだアバター（アバターが変わったら読み直す）
        private List<Object> _extraEntries = new List<Object>();
        private GameObject _extraOwner;
        private bool _extraLoaded;

        // 指定を展開したクリップと判定結果（指定・プロジェクトが変わったときだけ作り直す。null = 未作成）
        private List<KeyValuePair<AnimationClip, ClipCheck>> _extraChecks;

        // 直前のドロップの結果の表示と、その種類（null = なし）
        private string _dropNotice;
        private MessageType _dropNoticeType;

        // 書き出しの結果（プレビュー・記録）
        [SerializeField]
        private ExportResultView _result = new ExportResultView();

        // 進めている書き出しと、その記録の保存先（書き出し中でなければ null）
        private ExportJob _job;
        private string _jobLogPath;

        // 今の工程の名前を描き終えたか（描いてから次の工程へ進め、重い工程の間も何をしているかが見えるようにする）
        private bool _stepPainted;

        [MenuItem("VRCast/Avatar Exporter")]
        private static void Open()
        {
            // 既存ウィンドウがあれば再利用（大きさはそのまま）。新しく開くときは 2 列で収まる大きさにする
            bool opened = HasOpenInstances<AvatarExporterWindow>();
            var window = GetWindow<AvatarExporterWindow>(Title);
            if (!opened && !window.docked && window.position.width < MinWindowSize.x)
            {
                Rect rect = window.position;
                window.position = new Rect(rect.x, rect.y, DefaultWindowSize.x, DefaultWindowSize.y);
            }
        }

        private void OnEnable()
        {
            // アイコンが見つからなくても書き出しは使えるよう、無ければ表示しないだけにする
            string iconPath = AssetDatabase.GUIDToAssetPath(IconGuid);
            _icon = string.IsNullOrEmpty(iconPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            titleContent = new GUIContent(Title, _icon);

            // 右にプレビューを並べるため、2 列で収まる幅より狭くさせない
            minSize = MinWindowSize;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;

            // 閉じたら書き出しを中断し（複製と一時ファイルは片付く）、プレビューを破棄する
            if (_job != null)
            {
                _job.Dispose();
                _job = null;
            }

            _result.Release();
        }

        private void OnEditorUpdate()
        {
            // 書き出し中は 1 回に 1 工程ずつ進め、合間に進み具合を描く
            if (_job != null)
            {
                if (_stepPainted)
                {
                    _stepPainted = false;
                    if (!_job.MoveNext())
                    {
                        FinishExport();
                    }
                }

                Repaint();
                return;
            }

            // プレビューで腕を動かしている間は描き直し続ける
            if (_result.IsAnimating)
            {
                Repaint();
            }
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
            // 上: 見出し・アバターの指定・書き出すボタン・進み具合（スクロールさせず常に見える）
            DrawHeader();
            DrawTopBar();
            EditorGUILayout.Space(2f);
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true)), SeparatorColor);
            EditorGUILayout.Space(2f);

            // 下: 左に設定、右に書き出しの結果。どちらも残りの高さに収め、はみ出す分は列ごとにスクロールする
            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                float leftWidth = Mathf.Clamp(position.width * LeftPaneRatio, LeftPaneMinWidth, LeftPaneMaxWidth);
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(leftWidth), GUILayout.ExpandHeight(true)))
                {
                    _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
                    DrawSettings();
                    EditorGUILayout.EndScrollView();
                }

                GUILayout.Space(PaneGap);
                Rect separator = GUILayoutUtility.GetRect(1f, 1f, GUILayout.Width(1f), GUILayout.ExpandHeight(true));
                EditorGUI.DrawRect(separator, SeparatorColor);
                GUILayout.Space(PaneGap);

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
                {
                    DrawResultPane();
                }
            }

            // 下の 2 列に使える高さ（プレビューの大きさの目安）。変わったら描き直す
            if (Event.current.type == EventType.Repaint)
            {
                float height = GUILayoutUtility.GetLastRect().height;
                if (Mathf.Abs(height - _columnsHeight) > 1f)
                {
                    _columnsHeight = height;
                    Repaint();
                }
            }
        }

        private void DrawTopBar()
        {
            // 書き出し中はアバターを変えられないようにする（進み具合と中断のボタンだけ操作できる）
            using (new EditorGUI.DisabledScope(_job != null))
            using (new EditorGUILayout.HorizontalScope())
            {
                // シーン上のオブジェクトと Prefab の両方を受け付ける
                _avatar = (GameObject)EditorGUILayout.ObjectField(
                    T("Avatar Root", "アバターのルート", "아바타 루트", "虚拟形象根对象", "虛擬形象根物件"),
                    _avatar, typeof(GameObject), true, GUILayout.Height(TopBarHeight));

                using (new EditorGUI.DisabledScope(AvatarExporter.Validate(_avatar) != null))
                {
                    if (GUILayout.Button(T("Export...", "書き出す...", "내보내기...", "导出...", "匯出..."),
                            GUILayout.Width(TopButtonWidth), GUILayout.Height(TopBarHeight)))
                    {
                        ExportWithDialog();
                        // ダイアログ中にレイアウトが崩れるため、この回の描画は打ち切る（EndLayoutGroup のエラーを防ぐ）
                        GUIUtility.ExitGUI();
                    }
                }
            }

            // 検証結果を表示し、問題があれば書き出しを無効化（書き出し中は一時フォルダがあるため出さない）
            string error = _job == null ? AvatarExporter.Validate(_avatar) : null;
            if (error != null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Warning);
            }

            // Android（Quest）向けのままだと砂嵐になりやすいので、Windows へ切り替えるボタンを出す
            if (_job == null && !AvatarExporter.IsRequiredBuildTargetActive)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(T(
                            "Switch build target to Windows...",
                            "ビルドターゲットを Windows に切り替える...",
                            "빌드 타겟을 Windows로 전환...",
                            "将构建目标切换到 Windows...",
                            "將建置目標切換到 Windows..."), GUILayout.Height(TopBarHeight)))
                    {
                        bool ok = EditorUtility.DisplayDialog(
                            T("Switch build target", "ビルドターゲットの切り替え", "빌드 타겟 전환", "切换构建目标",
                                "切換建置目標"),
                            T(
                                "Unity will switch to PC, Mac & Linux Standalone (Windows) and may reimport assets. "
                                + "This can take a while. Export again after it finishes.",
                                "Build Settings を PC, Mac & Linux Standalone（Windows）に切り替えます。"
                                + "アセットの再インポートが走り、時間がかかることがあります。完了してから書き出してください。",
                                "Build Settings를 PC, Mac & Linux Standalone(Windows)으로 바꿉니다. "
                                + "에셋 재임포트가 진행되며 시간이 걸릴 수 있습니다. 끝난 뒤 다시 내보내세요.",
                                "将把 Build Settings 切换到 PC, Mac & Linux Standalone（Windows）。"
                                + "可能会重新导入资源并花费一些时间。完成后请再导出。",
                                "會將 Build Settings 切換到 PC, Mac & Linux Standalone（Windows）。"
                                + "可能會重新匯入資產並花費一些時間。完成後請再匯出。"),
                            T("Switch", "切り替える", "전환", "切换", "切換"),
                            T("Cancel", "キャンセル", "취소", "取消", "取消"));
                        if (ok && !AvatarExporter.TrySwitchToRequiredBuildTarget())
                        {
                            EditorUtility.DisplayDialog(
                                T("Switch failed", "切り替えに失敗", "전환 실패", "切换失败", "切換失敗"),
                                T(
                                    "Could not switch the build target. Change it manually in File > Build Settings.",
                                    "ビルドターゲットを切り替えられませんでした。File > Build Settings から手動で変えてください。",
                                    "빌드 타겟을 바꿀 수 없습니다. File > Build Settings에서 수동으로 변경하세요.",
                                    "无法切换构建目标。请在 File > Build Settings 中手动更改。",
                                    "無法切換建置目標。請在 File > Build Settings 中手動變更。"),
                                "OK");
                        }

                        GUIUtility.ExitGUI();
                    }
                }
            }

            DrawProgress();
        }

        private void DrawPlaceholder(string message)
        {
            if (_placeholderStyle == null)
            {
                _placeholderStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { alignment = TextAnchor.MiddleCenter };
            }

            Rect area = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(area, PlaceholderColor);
            GUI.Label(area, message, _placeholderStyle);
        }

        private static void DrawResultTitle()
        {
            EditorGUILayout.LabelField(T("Export result", "書き出しの結果", "내보내기 결과", "导出结果", "匯出結果"),
                EditorStyles.boldLabel);
        }

        private void DrawResultPane()
        {
            DrawResultTitle();

            // 書き出し中は上の進み具合を見てもらう
            if (_job != null)
            {
                DrawPlaceholder(T("Exporting... See the progress above.", "書き出しています... 進み具合は上に出ています。",
                    "내보내는 중... 진행 상황은 위에 표시됩니다.", "正在导出... 进度显示在上方。", "正在匯出... 進度顯示在上方。"));
                return;
            }

            // まだ書き出していなければ、ここに何が出るかを案内する
            if (!_result.HasResult)
            {
                DrawPlaceholder(T(
                    "No avatar to preview.\nAfter exporting, the exported avatar is previewed here together with the export log.",
                    "プレビュー対象のアバターがありません。\n書き出すと、ここに書き出したアバターのプレビューと書き出しの記録が出ます。",
                    "미리볼 아바타가 없습니다.\n내보내면 여기에 내보낸 아바타의 미리보기와 내보내기 기록이 나옵니다.",
                    "没有可预览的虚拟形象。\n导出后，这里会显示导出的虚拟形象预览和导出记录。",
                    "沒有可預覽的虛擬形象。\n匯出後，這裡會顯示匯出的虛擬形象預覽和匯出記錄。"));
                return;
            }

            // 結果・プレビュー・記録をまとめてスクロールする（プレビューは見えている高さの大部分を使う）
            _resultScroll = EditorGUILayout.BeginScrollView(_resultScroll, GUILayout.ExpandHeight(true));
            _result.DrawStatus();
            _result.DrawPreview(Repaint, Mathf.Max(MinPreviewHeight, _columnsHeight * PreviewHeightRatio));
            _result.DrawDetails();
            EditorGUILayout.EndScrollView();
        }

        private void DrawSettings()
        {
            // 書き出し中は設定を変えられないようにする（進み具合と中断のボタンだけ操作できる）
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && _job == null;

            // 長い説明は折りたたんでおく
            _showAbout = EditorGUILayout.Foldout(_showAbout, T("What is exported", "書き出される内容について",
                "내보내는 내용에 대해", "关于导出的内容", "關於匯出的內容"), true);
            if (_showAbout)
            {
                DrawAbout();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(T("Options", "書き出しの設定", "내보내기 설정", "导出设置", "匯出設定"),
                EditorStyles.boldLabel);
            DrawBlendShapeOption();
            DrawInactiveObjectsOption();
            DrawOutfitPoseOption();
            DrawExtraExpressions();

            GUI.enabled = enabled;
        }

        private static void DrawAbout()
        {
            EditorGUILayout.HelpBox(
                T(
                    "Only Unity built-in components (Transform, Animator, Renderers, MeshFilter, Particle System) are exported. " +
                    "VRChat components, scripts and the Animator Controller are removed from the exported copy. " +
                    "The FX layer's default state (toggles etc.) is baked into the copy before removal. " +
                    "Inactive objects are handled as chosen in \"Also export inactive objects\" below. " +
                    "Avatar tools such as VRCFury and Modular Avatar are applied to the copy first, " +
                    "the same way as when uploading to VRChat. " +
                    "Outfits and accessories set up with MA Merge Armature / Bone Proxy that were not merged " +
                    "are attached to the avatar's bones by VRCast so that they follow the avatar.",
                    "書き出されるのは Unity 標準のコンポーネント（Transform・Animator・Renderer・MeshFilter・Particle System）だけです。" +
                    "VRChat のコンポーネント・スクリプト・Animator Controller は書き出し用の複製から取り除かれます" +
                    "（元のアバターは変更しません）。取り除く前に、FX レイヤーの初期状態（トグルなど）を複製に焼き込みます。" +
                    "非アクティブなオブジェクトの扱いは、下の「非アクティブのオブジェクトも書き出す」で選べます。" +
                    "VRCFury・Modular Avatar などの改変は、VRChat へのアップロード時と同じく先に複製へ適用します。" +
                    "MA Merge Armature / Bone Proxy で付けた衣装・小物が統合されなかった場合は、" +
                    "VRCast がアバターのボーンに付け替えて体に追従させます。",
                    "Unity 기본 컴포넌트(Transform, Animator, Renderer, MeshFilter, Particle System)만 내보냅니다. " +
                    "VRChat 컴포넌트, 스크립트, Animator Controller는 내보내기용 복제본에서 제거됩니다" +
                    "(원본 아바타는 변경하지 않습니다). 제거하기 전에 FX 레이어의 초기 상태(토글 등)를 복제본에 반영합니다. " +
                    "비활성 오브젝트의 처리는 아래의 '비활성 오브젝트도 내보내기'에서 고를 수 있습니다. " +
                    "VRCFury, Modular Avatar 등의 개조는 VRChat 업로드 시와 같이 먼저 복제본에 적용합니다. " +
                    "MA Merge Armature / Bone Proxy로 붙인 의상·소품이 통합되지 않은 경우에는 " +
                    "VRCast가 아바타의 본에 다시 붙여 몸을 따라가게 합니다.",
                    "只导出 Unity 内置组件（Transform、Animator、Renderer、MeshFilter、Particle System）。" +
                    "VRChat 组件、脚本和 Animator Controller 会从导出用的副本中移除（不会修改原始虚拟形象）。" +
                    "移除前会把 FX 层的初始状态（开关等）烘焙到副本中。" +
                    "未激活对象的处理方式可在下方的“也导出未激活的对象”中选择。" +
                    "VRCFury、Modular Avatar 等改造会像上传到 VRChat 时一样先应用到副本。" +
                    "用 MA Merge Armature / Bone Proxy 添加的服装和小物如果未被合并，" +
                    "VRCast 会把它们重新挂到虚拟形象的骨骼上并跟随身体。",
                    "只匯出 Unity 內建元件（Transform、Animator、Renderer、MeshFilter、Particle System）。" +
                    "VRChat 元件、腳本和 Animator Controller 會從匯出用的副本中移除（不會修改原始虛擬形象）。" +
                    "移除前會把 FX 層的初始狀態（開關等）烘焙到副本中。" +
                    "未啟用物件的處理方式可在下方的「也匯出未啟用的物件」中選擇。" +
                    "VRCFury、Modular Avatar 等改造會像上傳到 VRChat 時一樣先套用到副本。" +
                    "用 MA Merge Armature / Bone Proxy 新增的服裝和小物如果未被合併，" +
                    "VRCast 會把它們重新掛到虛擬形象的骨骼上並跟隨身體。"),
                MessageType.Info);
        }

        private void DrawProgress()
        {
            if (_job == null)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect bar = GUILayoutUtility.GetRect(0f, TopBarHeight, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(bar, _job.Progress, DescribeStep(_job.Current));
                if (Event.current.type == EventType.Repaint)
                {
                    _stepPainted = true;
                }

                if (GUILayout.Button(T("Cancel", "中断", "중단", "中止", "中止"),
                        GUILayout.Width(TopButtonWidth), GUILayout.Height(TopBarHeight)))
                {
                    CancelExport();
                    GUIUtility.ExitGUI();
                }
            }

            if (_job.Current == ExportJob.Step.BuildingBundle)
            {
                EditorGUILayout.HelpBox(T("Unity shows its own progress window while building the avatar data.",
                    "アバターのデータを作る間は、Unity の進捗画面が出ます。",
                    "아바타 데이터를 만드는 동안에는 Unity의 진행 화면이 나옵니다.",
                    "生成虚拟形象数据期间会显示 Unity 的进度窗口。", "產生虛擬形象資料期間會顯示 Unity 的進度視窗。"),
                    MessageType.None);
            }
        }

        private static string DescribeStep(ExportJob.Step step)
        {
            switch (step)
            {
                case ExportJob.Step.Preparing:
                    return T("Copying the avatar...", "アバターを複製しています...", "아바타를 복제하는 중...",
                        "正在复制虚拟形象...", "正在複製虛擬形象...");
                case ExportJob.Step.ApplyingTools:
                    return T("Applying avatar tools (VRCFury / Modular Avatar)...",
                        "改変ツール（VRCFury・Modular Avatar）を適用しています...",
                        "개조 도구(VRCFury·Modular Avatar)를 적용하는 중...",
                        "正在应用改造工具（VRCFury / Modular Avatar）...", "正在套用改造工具（VRCFury / Modular Avatar）...");
                case ExportJob.Step.ReadingAvatar:
                    return T("Reading outfits, lip sync and PhysBones...", "衣装・リップシンク・揺れものを読み込んでいます...",
                        "의상·립싱크·흔들림을 읽는 중...", "正在读取服装、口型同步和摇晃物...", "正在讀取服裝、口型同步和搖晃物...");
                case ExportJob.Step.ReadingExpressions:
                    return T("Reading expressions...", "表情を読み込んでいます...", "표정을 읽는 중...", "正在读取表情...",
                        "正在讀取表情...");
                case ExportJob.Step.Cleaning:
                    return T("Removing components that are not exported...", "書き出さないコンポーネントを取り除いています...",
                        "내보내지 않는 컴포넌트를 제거하는 중...", "正在移除不导出的组件...", "正在移除不匯出的元件...");
                case ExportJob.Step.SavingPrefab:
                    return T("Preparing meshes and animations...", "メッシュとアニメーションを準備しています...",
                        "메시와 애니메이션을 준비하는 중...", "正在准备网格和动画...", "正在準備網格和動畫...");
                case ExportJob.Step.BuildingBundle:
                    return T("Building the avatar data (this can take a while)...", "アバターのデータを作っています（時間がかかります）...",
                        "아바타 데이터를 만드는 중 (시간이 걸립니다)...", "正在生成虚拟形象数据（需要一些时间）...",
                        "正在產生虛擬形象資料（需要一些時間）...");
                default:
                    return T("Writing the file...", "ファイルに書き出しています...", "파일로 내보내는 중...", "正在写入文件...",
                        "正在寫入檔案...");
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

        private static void DrawInactiveObjectsOption()
        {
            // 既定は書き出す（アプリの「オブジェクト」タブで後から ON にできるように）
            bool include = EditorPrefs.GetBool(IncludeInactiveObjectsKey, true);
            bool changed = EditorGUILayout.ToggleLeft(
                T("Also export inactive objects", "非アクティブのオブジェクトも書き出す",
                    "비활성 오브젝트도 내보내기", "也导出未激活的对象", "也匯出未啟用的物件"),
                include);
            if (changed != include)
            {
                EditorPrefs.SetBool(IncludeInactiveObjectsKey, changed);
            }

            // ON / OFF それぞれの挙動を補足
            EditorGUILayout.HelpBox(
                T(
                    "On: objects turned off in the scene are exported, kept turned off, and can be turned on " +
                    "in the app's Objects tab (the file gets larger). " +
                    "Off: they are not exported, except particles, which are kept turned off.",
                    "ON: シーンで OFF にしたオブジェクトも OFF のまま書き出し、アプリの「オブジェクト」タブで ON にできます" +
                    "（ファイルは大きくなります）。" +
                    "OFF: 書き出しません（パーティクルだけは OFF のまま書き出します）。",
                    "ON: 씬에서 꺼 둔 오브젝트도 꺼진 상태로 내보내 앱의 '오브젝트' 탭에서 켤 수 있습니다 " +
                    "(파일이 커집니다). " +
                    "OFF: 내보내지 않습니다 (파티클만 꺼진 상태로 내보냅니다).",
                    "开启：在场景中关闭的对象也以关闭状态导出，可在应用的“对象”标签页中开启（文件会变大）。" +
                    "关闭：不导出（只有粒子会以关闭状态导出）。",
                    "開啟：在場景中關閉的物件也以關閉狀態匯出，可在應用程式的「物件」分頁中開啟（檔案會變大）。" +
                    "關閉：不匯出（只有粒子會以關閉狀態匯出）。"),
                MessageType.None);
        }

        private static void DrawOutfitPoseOption()
        {
            // 既定は OFF（衣装のボーンを動かすため、明示的に選んだときだけ）
            bool fix = EditorPrefs.GetBool(FixOutfitPosesKey, false);
            bool changed = EditorGUILayout.ToggleLeft(
                T("Fix outfit poses that do not match the avatar", "アバターと合わない衣装の姿勢を直す",
                    "아바타와 맞지 않는 의상 자세 바로잡기", "修正与虚拟形象不匹配的服装姿势", "修正與虛擬形象不符的服裝姿勢"),
                fix);
            if (changed != fix)
            {
                EditorPrefs.SetBool(FixOutfitPosesKey, changed);
            }

            EditorGUILayout.HelpBox(
                T(
                    "For outfits whose sleeves or legs float away from the body because a T-pose outfit was put on an " +
                    "A-pose avatar (or the other way round). The outfit's arm and leg bones are turned to match the avatar " +
                    "in the exported copy only. Check the result in the preview after exporting.",
                    "T ポーズの衣装を A ポーズのアバターに着せた（またはその逆の）ために、袖や脚が体から浮いてしまう衣装向けです。" +
                    "書き出し用の複製の上でだけ、衣装の腕・脚のボーンをアバターの向きに合わせます。書き出し後のプレビューで確かめてください。",
                    "T 포즈 의상을 A 포즈 아바타에 입혀서(또는 그 반대) 소매나 다리가 몸에서 뜨는 의상용입니다. " +
                    "내보내기용 복제본에서만 의상의 팔·다리 본을 아바타의 방향에 맞춥니다. 내보낸 후 미리보기로 확인하세요.",
                    "适用于把 T 姿势的服装穿到 A 姿势的虚拟形象上（或相反）导致袖子或腿部离开身体的服装。" +
                    "只在导出用的副本上把服装的手臂和腿部骨骼对齐到虚拟形象的方向。导出后请在预览中确认。",
                    "適用於把 T 姿勢的服裝穿到 A 姿勢的虛擬形象上（或相反）導致袖子或腿部離開身體的服裝。" +
                    "只在匯出用的副本上把服裝的手臂和腿部骨骼對齊到虛擬形象的方向。匯出後請在預覽中確認。"),
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
                usable += ExpressionExtractor.CanImport(check.Value) ? 1 : 0;
            }

            EditorGUILayout.LabelField(
                T("Expressions to add", "追加される表情", "추가될 표정", "将追加的表情", "將追加的表情")
                + $": {usable} / {_extraChecks.Count}",
                EditorStyles.miniBoldLabel);

            // クリップごとに取り込めるか（取り込めない理由・BlendShape 部分だけを取り込む理由）を表示。クリックでプロジェクト上の場所を示す
            foreach (KeyValuePair<AnimationClip, ClipCheck> check in _extraChecks)
            {
                string mark = ExpressionExtractor.CanImport(check.Value) ? "✓" : "✗";
                string reason = check.Value == ClipCheck.Expression ? string.Empty : " — " + DescribeCheck(check.Value);
                if (GUILayout.Button($"{mark} {check.Key.name}{reason}", EditorStyles.miniLabel))
                {
                    EditorGUIUtility.PingObject(check.Key);
                }
            }

            // 名前が FX の表情と重なると番号付きになることを補足
            EditorGUILayout.HelpBox(
                T(
                    "Clips that are also in FaceEmo or the FX layer are added only once (with the FaceEmo name). " +
                    "If the name matches another expression, a number is added, such as \"Name (2)\".",
                    "FaceEmo・FX レイヤーにもあるクリップは 1 回だけ入ります（FaceEmo の名前になります）。名前がほかの表情と重なる場合は「名前 (2)」のように番号が付きます。",
                    "FaceEmo·FX 레이어에도 있는 클립은 한 번만 들어갑니다 (FaceEmo의 이름이 됩니다). 이름이 다른 표정과 겹치면 「이름 (2)」처럼 번호가 붙습니다.",
                    "同时在 FaceEmo 或 FX 层中的剪辑只会加入一次（使用 FaceEmo 的名称）。名称与其他表情重复时会加上编号，例如“名称 (2)”。",
                    "同時在 FaceEmo 或 FX 層中的剪輯只會加入一次（使用 FaceEmo 的名稱）。名稱與其他表情重複時會加上編號，例如「名稱 (2)」。"),
                MessageType.None);
        }

        private static string DescribeCheck(ClipCheck check)
        {
            // 取り込めない理由、または取り込むときの補足の文言
            switch (check)
            {
                case ClipCheck.HasOtherCurves:
                    return T("also moves other things (only the blend shapes are added)",
                        "ブレンドシェイプ以外も動かしています（ブレンドシェイプの部分だけ入ります）",
                        "블렌드셰이프 이외의 것도 움직입니다 (블렌드셰이프 부분만 들어갑니다)",
                        "还驱动了 BlendShape 以外的内容（只加入 BlendShape 部分）",
                        "還驅動了 BlendShape 以外的內容（只加入 BlendShape 部分）");
                case ClipCheck.NoCurves:
                    return T("moves no blend shapes", "ブレンドシェイプを動かしていません", "블렌드셰이프를 움직이지 않습니다",
                        "没有驱动 BlendShape", "沒有驅動 BlendShape");
                case ClipCheck.TooManyCurves:
                    return T("too many blend shapes (only the first ones are added)",
                        "ブレンドシェイプが多すぎます（先頭から上限までが入ります）",
                        "블렌드셰이프가 너무 많습니다 (앞에서부터 상한까지 들어갑니다)",
                        "BlendShape 过多（从开头起加入到上限为止）", "BlendShape 過多（從開頭起加入到上限為止）");
                case ClipCheck.AllZero:
                    return T("all values are 0 (added as a reset expression)", "値がすべて 0 です（戻す用の表情として入ります）",
                        "값이 모두 0입니다 (되돌리기용 표정으로 들어갑니다)", "所有值均为 0（作为复位用表情加入）",
                        "所有值均為 0（作為復位用表情加入）");
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

                // 表示言語の切り替えは右上に置く（選んだ言語は EditorPrefs に保存し、次回以降も使う）
                using (new EditorGUILayout.VerticalScope(GUILayout.Height(HeaderIconSize)))
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(_job != null))
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(T("Language", "言語", "언어", "语言", "語言"));
                        Selected = (Language)EditorGUILayout.Popup((int)Selected, LanguageLabels(),
                            GUILayout.Width(LanguagePopupWidth));
                    }

                    GUILayout.FlexibleSpace();
                }
            }

            EditorGUILayout.Space(2f);
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

            // 書き出しの記録（このウィンドウの下に出し、書き出したファイルの横にも保存する）
            string outputPath = path.EndsWith(AvatarPackageLayout.Extension, StringComparison.OrdinalIgnoreCase)
                ? path
                : path + AvatarPackageLayout.Extension;
            _jobLogPath = Path.ChangeExtension(outputPath, ".log.txt");

            // 前の結果のプレビューを片付けてから、1 工程ずつ進める書き出しを始める（進めるのは OnEditorUpdate）
            _result.Clear();
            _job = AvatarExporter.Start(
                _avatar, outputPath, EditorPrefs.GetBool(KeepSceneBlendShapesKey, true),
                ExtraExpressionClips.Collect(_extraEntries),
                EditorPrefs.GetBool(IncludeInactiveObjectsKey, true), new ExportLog(),
                EditorPrefs.GetBool(FixOutfitPosesKey, false));
            _stepPainted = false;
            Repaint();
        }

        private void FinishExport()
        {
            ExportJob job = _job;
            _job = null;
            ExportLog log = job.Log;
            if (job.Error == null)
            {
                // Console と記録のファイルの概要は問い合わせ時に読みやすいよう英語固定、ウィンドウは表示言語
                AvatarExporter.Report report = job.Report;
                string englishSummary = BuildSummary(report, true);
                Debug.Log("[VRCast][Exporter] " + englishSummary.Replace("\n", " / "));
                _result.Set(report.OutputPath, BuildSummary(report, false), log,
                    log.Save(_jobLogPath, ExportLog.UseJapanese, LogHeader(englishSummary)), false);
            }
            else
            {
                // 失敗理由を記録して表示（スタックトレースは Console へ）
                Debug.LogException(job.Error);
                log.Add(ExportLog.Level.Error, "Export failed: " + job.Error.Message,
                    "書き出しに失敗しました: " + job.Error.Message);
                _result.Set(null, null, log, log.Save(_jobLogPath, ExportLog.UseJapanese, LogHeader(null)), true);
            }

            job.Dispose();
        }

        private void CancelExport()
        {
            // 中断しても複製と一時ファイルは片付く。途中までの記録は残す
            ExportJob job = _job;
            _job = null;
            job.Dispose();
            job.Log.Add(ExportLog.Level.Warning, "The export was cancelled.", "書き出しを中断しました。");
            _result.Set(null, null, job.Log, job.Log.Save(_jobLogPath, ExportLog.UseJapanese, LogHeader(null)), true);
            Repaint();
        }

        private string LogHeader(string englishSummary)
        {
            // 問い合わせ時に状況が分かるよう、書き出した日時・アバター・Unity の版を先頭に置く
            string header = $"VRCast Exporter log\n" +
                $"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"Avatar: {(_avatar != null ? _avatar.name : "-")}\n" +
                $"Unity: {Application.unityVersion}";
            return string.IsNullOrEmpty(englishSummary) ? header : header + "\n\n" + englishSummary;
        }

        private static string UnresolvedSamples(AvatarExporter.Report report)
        {
            // スクリーンショットで原因が分かるよう、先頭の数件だけ「  - 表情名 (パス / BlendShape [原因])」で並べる
            const int maxSamples = 3;
            var builder = new StringBuilder();
            for (int i = 0; report.UnresolvedDetails != null && i < report.UnresolvedDetails.Count && i < maxSamples; i++)
            {
                builder.Append("  - ").Append(report.UnresolvedDetails[i]).Append('\n');
            }

            return builder.ToString();
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
            string preprocess = L("Avatar tools (VRCFury / Modular Avatar)", "改変ツール（VRCFury・Modular Avatar）の適用",
                "개조 도구(VRCFury·Modular Avatar) 적용", "已应用改造工具（VRCFury / Modular Avatar）",
                "已套用改造工具（VRCFury / Modular Avatar）");
            // 適用した前処理の種類を表示用の文言にする
            string preprocessMode = report.Preprocess == AvatarPreprocessor.Mode.VrcSdk
                ? L("all (VRChat SDK upload processing)", "すべて（VRChat SDK のアップロード時の処理）",
                    "전체(VRChat SDK 업로드 시 처리)", "全部（VRChat SDK 上传时的处理）", "全部（VRChat SDK 上傳時的處理）")
                : report.Preprocess == AvatarPreprocessor.Mode.NdmfOnly
                    ? L("NDMF only", "NDMF のみ", "NDMF만", "仅 NDMF", "僅 NDMF")
                    : L("none", "なし", "없음", "无", "無");
            string attached = L("Outfits / accessories attached by VRCast", "VRCast が付け替えた衣装・小物",
                "VRCast가 다시 붙인 의상·소품", "VRCast 重新挂接的服装和小物", "VRCast 重新掛接的服裝和小物");
            string baked = L("Baked FX default clips", "焼き込んだ FX 初期状態のクリップ", "반영한 FX 초기 상태 클립",
                "已烘焙的 FX 初始状态剪辑", "已烘焙的 FX 初始狀態剪輯");
            string sceneBlendShapes = L("Kept scene blend shapes", "シーンのブレンドシェイプを優先",
                "씬의 블렌드셰이프 우선", "优先场景 BlendShape", "優先場景 BlendShape");
            string expressions = L("Expressions", "表情", "표정", "表情", "表情");
            string extraExpressions = L("added clips", "追加したクリップ", "추가한 클립", "追加的剪辑", "追加的剪輯");
            // 追加指定のうち FaceEmo・FX で取り込み済みだった数（あるときだけ）
            string extraIncluded = report.ExtraExpressionIncluded > 0
                ? " " + L("already in FaceEmo / FX", "FaceEmo・FX で取り込み済み", "FaceEmo·FX에서 이미 포함",
                    "已由 FaceEmo / FX 加入", "已由 FaceEmo / FX 加入") + $" {report.ExtraExpressionIncluded}"
                : string.Empty;
            // アプリの設定で表示したときだけ一覧に出る表情（FX の小物の切り替え等から BlendShape だけを取り出したもの）
            string hiddenExpressions = L("hidden by default", "既定で非表示", "기본 숨김", "默认隐藏", "預設隱藏");
            // FaceEmo の設定があったときだけ件数を出す（取り込まなかった表情があればその数も）
            string faceEmoSkipped = report.FaceEmoSkipped > 0
                ? " " + L("skipped", "見送り", "제외", "跳过", "略過") + $" {report.FaceEmoSkipped}"
                : string.Empty;
            string faceEmo = report.FaceEmoLaunchers > 0 || report.FaceEmoMenuClips > 0
                ? $"FaceEmo: {report.FaceEmoExpressionCount} / {report.FaceEmoClips}{faceEmoSkipped}" +
                  $" ({L("from menu", "メニューから", "메뉴에서", "来自菜单", "來自選單")} {report.FaceEmoMenuClips}), "
                : string.Empty;

            // シーンに FaceEmo が複数あり、このアバター用のものを決められなかった（読めていない）ことを知らせる
            string faceEmoNotFound = report.FaceEmoLaunchers == 0 && report.FaceEmoOtherLaunchers > 0
                ? L("FaceEmo: the scene has FaceEmo for other avatars and none could be matched to this one, " +
                    "so it was not read. Open FaceEmo and check the target avatar, " +
                    "or keep only this avatar's FaceEmo in the scene.",
                    "FaceEmo: シーンに別のアバター用の FaceEmo があり、このアバター用を決められないため読み込んでいません。" +
                    "FaceEmo を開いて対象のアバターを確認するか、シーンにこのアバターの FaceEmo だけを置いてください。",
                    "FaceEmo: 씬에 다른 아바타용 FaceEmo가 있어 이 아바타용을 정할 수 없어 읽지 않았습니다. " +
                    "FaceEmo를 열어 대상 아바타를 확인하거나 씬에 이 아바타의 FaceEmo만 두세요.",
                    "FaceEmo：场景中有其他虚拟形象的 FaceEmo，无法确定此虚拟形象使用哪一个，因此未读取。" +
                    "请打开 FaceEmo 确认目标虚拟形象，或在场景中只保留此虚拟形象的 FaceEmo。",
                    "FaceEmo：場景中有其他虛擬形象的 FaceEmo，無法確定此虛擬形象使用哪一個，因此未讀取。" +
                    "請開啟 FaceEmo 確認目標虛擬形象，或在場景中只保留此虛擬形象的 FaceEmo。") + "\n"
                : string.Empty;

            // メッシュが見つからずアプリの一覧に出ない表情があれば数を知らせる（名前は Console に出す）
            string unresolved = report.UnresolvedExpressions > 0
                ? string.Format(L(
                    "⚠ {0} expressions will not appear in the app because their meshes were not found (names are in the log).",
                    "⚠ {0} 個の表情は動かすメッシュが見つからないため、アプリの表情一覧に出ません（名前は記録に出ています）。",
                    "⚠ 표정 {0}개는 움직일 메시를 찾지 못해 앱의 표정 목록에 나오지 않습니다 (이름은 기록에 있습니다).",
                    "⚠ {0} 个表情因找不到要驱动的网格，不会出现在应用的表情列表中（名称见记录）。",
                    "⚠ {0} 個表情因找不到要驅動的網格，不會出現在應用程式的表情列表中（名稱見記錄）。"),
                    report.UnresolvedExpressions) + "\n" + UnresolvedSamples(report)
                : string.Empty;
            string particles = L("Particle systems", "パーティクル", "파티클", "粒子系统", "粒子系統");
            string lipSync = L("Lip sync", "リップシンク", "립싱크", "口型同步", "口型同步");
            string blink = L("blink", "まばたき", "눈 깜빡임", "眨眼", "眨眼");
            string wink = L("wink", "ウインク", "윙크", "眨单眼", "眨單眼");
            string removed = L("Removed components", "取り除いたコンポーネント", "제거한 컴포넌트", "已移除的组件",
                "已移除的元件");
            string missing = L("missing scripts", "Missing Script", "Missing Script", "Missing Script",
                "Missing Script");
            string editorOnly = L("EditorOnly objects", "EditorOnly オブジェクト", "EditorOnly 오브젝트", "EditorOnly 对象",
                "EditorOnly 物件");
            string inactive = report.IncludedInactiveObjects
                ? L("Inactive objects (exported off)", "非アクティブのオブジェクト（OFF で書き出し）",
                    "비활성 오브젝트 (꺼진 상태로 내보냄)", "未激活的对象（以关闭状态导出）", "未啟用的物件（以關閉狀態匯出）")
                : L("Inactive objects (removed)", "非アクティブのオブジェクト（取り除いた数）",
                    "비활성 오브젝트 (제거함)", "未激活的对象（已移除）", "未啟用的物件（已移除）");
            int inactiveCount = report.IncludedInactiveObjects ? report.InactiveObjects : report.RemovedInactiveObjects;
            string fxToggles = L("Objects toggled by FX", "FX で切り替えるオブジェクト", "FX로 전환하는 오브젝트",
                "由 FX 切换的对象", "由 FX 切換的物件");
            string propAnimators = L("Prop animators", "小物の Animator", "소품 Animator", "小物件的 Animator",
                "小物件的 Animator");
            string fxMotions = L("prop motions from FX", "FX の小物の動き", "FX의 소품 움직임", "FX 中的小物件动作",
                "FX 中的小物件動作");
            // コントローラーを書き出せず再生されない小物の Animator（あるときだけ。パスは Console に出す）
            string droppedAnimators = report.DroppedPropAnimators > 0
                ? " " + L("not playable", "再生できない", "재생 불가", "无法播放", "無法播放") +
                  $" {report.DroppedPropAnimators}"
                : string.Empty;

            return
                $"{exported}: {report.OutputPath}\n" +
                $"{size}: {report.Manifest.bundleSize / (1024f * 1024f):F1} MB\n" +
                $"Humanoid: {report.IsHumanoid}\n" +
                $"{preprocess}: {preprocessMode}" +
                (report.OptimizerBlendShapeSettingsDisabled > 0 ? " (AAO BlendShape optimization: off)" : string.Empty) +
                "\n" +
                $"{attached}: {report.ModularAvatarFallbackFixes}\n" +
                $"{baked}: {report.BakedFxClips}\n" +
                $"{sceneBlendShapes}: {report.KeptSceneBlendShapes}\n" +
                $"{expressions}: {report.ExpressionCount} " +
                $"({faceEmo}{extraExpressions}: {report.ExtraExpressionCount} / {report.ExtraExpressionClips}{extraIncluded}, " +
                $"{hiddenExpressions}: {report.HiddenExpressionCount})\n" +
                faceEmoNotFound +
                unresolved +
                $"{lipSync}: {report.LipSyncMode}, {blink}: {report.HasBlink}, {wink}: {report.HasWink}\n" +
                $"PhysBones: {report.PhysBoneCount}\n" +
                $"Constraints: {report.ConstraintCount}\n" +
                $"Blendshape Sync (MA): {report.BlendShapeSyncCount}\n" +
                $"{particles}: {report.ParticleSystemCount}\n" +
                $"{propAnimators}: {report.PropAnimators}{droppedAnimators}, {fxMotions}: {report.FxMotionClips}\n" +
                $"{L("Standing clips", "立ちポーズ", "서 있는 포즈", "站立姿势", "站立姿勢")}: {report.StandClips}\n" +
                $"{inactive}: {inactiveCount}, {fxToggles}: {report.FxToggleCount}\n" +
                $"{removed}: {report.Strip.RemovedComponents}, " +
                $"{missing}: {report.Strip.RemovedMissingScripts}, " +
                $"{editorOnly}: {report.Strip.RemovedEditorOnlyObjects}" +
                (report.ShaderVariants > 0
                    ? "\n" + L("Shader variants", "シェーダーバリアント", "셰이더 배리언트", "着色器变体", "著色器變體")
                      + $": {report.ShaderVariants} ({report.ShaderVariantShaders} "
                      + L("shaders", "種", "종", "种", "種") + ")"
                    : string.Empty);
        }
    }
}
