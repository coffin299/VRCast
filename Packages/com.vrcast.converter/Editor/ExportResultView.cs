using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;
using VRCast.AvatarFormat;
using static VRCast.Converter.Editor.ExporterLoc;
using Object = UnityEngine.Object;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// 書き出しの結果の表示（エクスポーターのウィンドウの下に描く）。書き出した .vrcaster の中のアバターをプレビューし
    /// （腕を動かして体への追従を確かめられる）、書き出しの記録（日本語・英語）と概要を表示する。失敗したときは記録だけを出す。
    /// </summary>
    [Serializable]
    public sealed class ExportResultView
    {
        // プレビューの背景色
        private static readonly Color PreviewBackground = new Color(0.32f, 0.33f, 0.36f);

        // プレビューのカメラの画角と、ドラッグ・ホイールの感度
        private const float FieldOfView = 30f;
        private const float OrbitSpeed = 0.4f;
        private const float ZoomSpeed = 0.05f;

        // 「腕を動かす」の速さ（ラジアン / 秒）と、腕の上げ下げの範囲（Arm Down-Up のマッスル値）
        private const float SwingSpeed = 2f;
        private const float ArmDown = -0.8f;
        private const float ArmUp = 0.6f;

        // 書き出したファイルから取り出した AssetBundle を置くフォルダ（プロジェクトの Temp の下）
        private const string PreviewFolder = "Temp/VRCastPreview";

        [SerializeField] private bool _hasResult;
        [SerializeField] private string _outputPath;
        [SerializeField] private string _logPath;
        [SerializeField] private string _summary;
        [SerializeField] private bool _failed;
        [SerializeField] private ExportLog _log = new ExportLog();
        [SerializeField] private bool _showInfo;
        [SerializeField] private bool _showSummary;
        [SerializeField] private bool _swingArms;

        // プレビュー（スクリプトの再読み込みのたびに作り直す）
        [NonSerialized] private PreviewRenderUtility _preview;
        [NonSerialized] private AssetBundle _bundle;
        [NonSerialized] private string _bundleFile;
        [NonSerialized] private GameObject _instance;
        [NonSerialized] private string _previewError;
        [NonSerialized] private Bounds _bounds;
        [NonSerialized] private Vector2 _orbit;
        [NonSerialized] private float _distance;
        [NonSerialized] private HumanPoseHandler _poseHandler;
        [NonSerialized] private HumanPose _pose;
        [NonSerialized] private float[] _baseMuscles;
        [NonSerialized] private int[] _armMuscles;
        [NonSerialized] private ConstraintSet _constraintSet;
        [NonSerialized] private List<ConstraintEvaluator> _constraints;
        // Editor がプロジェクト側のシェーダーに差し替えたとき、プレビューだけではアプリの見た目を保証できない旨
        [NonSerialized] private string _shaderRemapNote;

        public bool HasResult => _hasResult;

        /// <summary>
        /// 腕を動かしている間は true（ウィンドウは描き直し続ける）。
        /// </summary>
        public bool IsAnimating => _swingArms && _poseHandler != null;

        /// <summary>
        /// 結果を設定する。failed なら outputPath は使わない（プレビューなし）。
        /// </summary>
        public void Set(string outputPath, string summary, ExportLog log, string logPath, bool failed)
        {
            Release();
            _hasResult = true;
            _outputPath = outputPath;
            _summary = summary;
            _log = log ?? new ExportLog();
            _logPath = logPath;
            _failed = failed;
        }

        public void Clear()
        {
            Release();
            _hasResult = false;
            _log = new ExportLog();
        }

        /// <summary>
        /// 書き出したファイルのプレビューを出せるか（失敗したときは出さない）。
        /// </summary>
        public bool HasPreview => _hasResult && !_failed;

        /// <summary>
        /// 書き出しの記録と詳細を描く。
        /// </summary>
        public void DrawDetails()
        {
            if (!_hasResult)
            {
                return;
            }

            DrawLog();
            DrawSummary();
        }

        /// <summary>
        /// 結果の一言と、ファイル・記録のボタンを描く。
        /// </summary>
        public void DrawStatus()
        {
            if (!_hasResult)
            {
                return;
            }

            int errors = _log.Count(ExportLog.Level.Error);
            int warnings = _log.Count(ExportLog.Level.Warning);
            string status = _failed
                ? T("Export failed. See the log below.", "書き出しに失敗しました。下の記録を確かめてください。",
                    "내보내기에 실패했습니다. 아래 기록을 확인하세요.", "导出失败。请查看下方的记录。", "匯出失敗。請查看下方的記錄。")
                : warnings + errors > 0
                    ? T("Exported with warnings. Check the preview and the log below.",
                        "書き出しましたが、注意があります。プレビューと下の記録を確かめてください。",
                        "내보냈지만 주의할 점이 있습니다. 미리보기와 아래 기록을 확인하세요.",
                        "已导出，但有需要注意的地方。请查看预览和下方的记录。",
                        "已匯出，但有需要注意的地方。請查看預覽和下方的記錄。")
                    : T("Exported. Check the preview to see how the avatar looks.",
                        "書き出しました。プレビューで見た目を確かめてください。",
                        "내보냈습니다. 미리보기로 모습을 확인하세요.", "已导出。请在预览中确认外观。", "已匯出。請在預覽中確認外觀。");
            MessageType type = _failed || errors > 0 ? MessageType.Error : warnings > 0 ? MessageType.Warning : MessageType.Info;
            EditorGUILayout.HelpBox(status, type);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_failed || string.IsNullOrEmpty(_outputPath) || !File.Exists(_outputPath)))
                {
                    if (GUILayout.Button(T("Show exported file", "書き出したファイルを表示", "내보낸 파일 표시",
                            "显示导出的文件", "顯示匯出的檔案")))
                    {
                        EditorUtility.RevealInFinder(_outputPath);
                    }
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_logPath) || !File.Exists(_logPath)))
                {
                    if (GUILayout.Button(T("Open log file", "記録のファイルを開く", "기록 파일 열기", "打开记录文件",
                            "開啟記錄檔案")))
                    {
                        EditorUtility.OpenWithDefaultApp(_logPath);
                    }
                }

                if (GUILayout.Button(T("Copy log", "記録をコピー", "기록 복사", "复制记录", "複製記錄")))
                {
                    EditorGUIUtility.systemCopyBuffer = _log.ToText(ExportLog.UseJapanese, _summary);
                }
            }
        }

        /// <summary>
        /// 書き出したファイルのプレビューを、指定の高さで描く。
        /// </summary>
        public void DrawPreview(Action repaint, float height)
        {
            if (!HasPreview)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(T("Preview (exported file)", "プレビュー（書き出したファイル）", "미리보기 (내보낸 파일)",
                "预览（导出的文件）", "預覽（匯出的檔案）"), EditorStyles.boldLabel);

            EnsurePreview();
            if (_instance == null)
            {
                EditorGUILayout.HelpBox(
                    T("The preview cannot be shown: ", "プレビューを表示できません: ", "미리보기를 표시할 수 없습니다: ",
                        "无法显示预览：", "無法顯示預覽：") + _previewError, MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_poseHandler == null))
                {
                    bool swing = GUILayout.Toggle(_swingArms, T("Move the arms to check that the outfit follows",
                        "腕を動かして衣装の追従を確かめる", "팔을 움직여 의상이 따라오는지 확인", "移动手臂确认服装是否跟随",
                        "移動手臂確認服裝是否跟隨"), "Button");
                    if (swing != _swingArms)
                    {
                        _swingArms = swing;
                        if (!swing)
                        {
                            ApplyArms(_baseMuscles);
                        }
                    }
                }

                if (GUILayout.Button(T("Reset view", "視点を戻す", "시점 초기화", "重置视角", "重設視角"), GUILayout.Width(120f)))
                {
                    ResetView();
                }
            }

            Rect rect = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
            HandlePreviewInput(rect, repaint);
            if (Event.current.type == EventType.Repaint)
            {
                RenderPreview(rect);
            }

            EditorGUILayout.LabelField(T("Drag to rotate, scroll to zoom.", "ドラッグで回転、ホイールで拡大縮小。",
                "드래그로 회전, 휠로 확대·축소.", "拖动旋转，滚轮缩放。", "拖曳旋轉，滾輪縮放。"), EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(_shaderRemapNote))
            {
                EditorGUILayout.HelpBox(_shaderRemapNote, MessageType.Info);
            }

            EditorGUILayout.HelpBox(T(
                "PhysBones (hair, skirts and other swaying parts) do not move in this preview; they stay in their resting shape. " +
                "They move in the VRCast app.",
                "揺れもの（PhysBone：髪・スカートなど）はこのプレビューでは動かず、止まったままの形で表示されます。VRCast のアプリでは動きます。",
                "흔들림(PhysBone: 머리카락, 치마 등)은 이 미리보기에서 움직이지 않고 멈춘 모양으로 표시됩니다. VRCast 앱에서는 움직입니다.",
                "摇晃物（PhysBone：头发、裙子等）在此预览中不会动，会以静止的形状显示。在 VRCast 应用中会动。",
                "搖晃物（PhysBone：頭髮、裙子等）在此預覽中不會動，會以靜止的形狀顯示。在 VRCast 應用程式中會動。"),
                MessageType.Info);
        }

        private void DrawLog()
        {
            EditorGUILayout.Space();
            bool japanese = ExportLog.UseJapanese;
            int infos = _log.Count(ExportLog.Level.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(japanese ? "記録" : "Log", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                _showInfo = GUILayout.Toggle(_showInfo,
                    (japanese ? "情報も表示" : "Show info") + $" ({infos})", EditorStyles.miniButton);
            }

            int shown = 0;
            foreach (ExportLog.Entry entry in _log.entries)
            {
                if (entry.level == ExportLog.Level.Info && !_showInfo)
                {
                    continue;
                }

                shown++;
                string message = (entry.external ? (japanese ? "（ほかのツール）" : "(other tool) ") : string.Empty)
                    + entry.Message(japanese)
                    + (string.IsNullOrEmpty(entry.detail) ? string.Empty : "\n" + entry.detail);
                EditorGUILayout.HelpBox(message, ToMessageType(entry.level));
            }

            if (shown == 0)
            {
                EditorGUILayout.LabelField(japanese ? "注意すべき点はありません。" : "Nothing to note.", EditorStyles.miniLabel);
            }
        }

        private void DrawSummary()
        {
            if (string.IsNullOrEmpty(_summary))
            {
                return;
            }

            EditorGUILayout.Space();
            _showSummary = EditorGUILayout.Foldout(_showSummary, T("Details", "詳細", "상세", "详细信息", "詳細資訊"), true);
            if (_showSummary)
            {
                EditorGUILayout.HelpBox(_summary, MessageType.None);
            }
        }

        private static MessageType ToMessageType(ExportLog.Level level)
        {
            switch (level)
            {
                case ExportLog.Level.Error:
                    return MessageType.Error;
                case ExportLog.Level.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }

        private void EnsurePreview()
        {
            // 作成済み、または作れなかった理由が分かっていれば作り直さない
            if (_instance != null || _previewError != null)
            {
                return;
            }

            try
            {
                LoadPreview();
            }
            catch (Exception e)
            {
                _previewError = e.Message;
                Release(false);
            }
        }

        private void LoadPreview()
        {
            if (string.IsNullOrEmpty(_outputPath) || !File.Exists(_outputPath))
            {
                throw new FileNotFoundException(T("The exported file was not found.", "書き出したファイルが見つかりません。",
                    "내보낸 파일을 찾을 수 없습니다.", "找不到导出的文件。", "找不到匯出的檔案。"));
            }

            // .vrcaster（ZIP）から AssetBundle を取り出す
            Directory.CreateDirectory(PreviewFolder);
            _bundleFile = Path.Combine(PreviewFolder, Guid.NewGuid().ToString("N") + ".bundle");
            using (FileStream stream = File.OpenRead(_outputPath))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                ZipArchiveEntry entry = zip.GetEntry(AvatarPackageLayout.BundleEntry)
                    ?? throw new InvalidDataException("avatar.bundle is missing.");
                using (Stream source = entry.Open())
                using (FileStream target = File.Create(_bundleFile))
                {
                    source.CopyTo(target);
                }

                _constraintSet = ReadConstraints(zip);
            }

            // 同じ名前の AssetBundle は同時に読めないため、前のプレビューの分を外してから読む
            foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (loaded.name == AvatarPackageLayout.BundleEntry)
                {
                    loaded.Unload(true);
                }
            }

            _bundle = AssetBundle.LoadFromFile(_bundleFile);
            if (_bundle == null)
            {
                throw new InvalidDataException(T("The avatar data could not be loaded in this Unity.",
                    "この Unity ではアバターのデータを読み込めませんでした。", "이 Unity에서는 아바타 데이터를 불러올 수 없습니다.",
                    "此 Unity 无法加载虚拟形象数据。", "此 Unity 無法載入虛擬形象資料。"));
            }

            var prefab = _bundle.LoadAsset<GameObject>(AvatarPackageLayout.PrefabAssetPath);
            if (prefab == null)
            {
                GameObject[] all = _bundle.LoadAllAssets<GameObject>();
                prefab = all.Length > 0 ? all[0] : null;
            }

            if (prefab == null)
            {
                throw new InvalidDataException("The avatar prefab is missing.");
            }

            _preview = new PreviewRenderUtility();
            _preview.camera.fieldOfView = FieldOfView;
            _preview.camera.nearClipPlane = 0.01f;
            _preview.camera.farClipPlane = 100f;
            _preview.camera.clearFlags = CameraClearFlags.SolidColor;
            _preview.camera.backgroundColor = PreviewBackground;
            _preview.lights[0].intensity = 1.1f;
            _preview.lights[0].transform.rotation = Quaternion.Euler(30f, 150f, 0f);
            _preview.lights[1].intensity = 0.6f;
            _preview.ambientColor = new Color(0.45f, 0.45f, 0.45f);

            _instance = Object.Instantiate(prefab);
            _instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _preview.AddSingleGO(_instance);

            // 画面外判定で消えないよう、常に更新する
            foreach (SkinnedMeshRenderer skinned in _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skinned.updateWhenOffscreen = true;
            }

            // 衣装の骨などを腕へ追従させる Constraint（アプリと同じく、骨格を動かすものは除いて評価する）
            _constraints = ConstraintEvaluator.CreateAll(
                _instance.transform, _instance.GetComponent<Animator>(), _constraintSet, out _);
            SetUpPose();
            EvaluateConstraints();
            _bounds = CalculateBounds(_instance);
            ResetView();
            _shaderRemapNote = DetectProjectShaderRemap(_instance);
        }

        /// <summary>
        /// Editor が AssetBundle のシェーダーをプロジェクト内の同名シェーダーへ差し替えていないか調べる。
        /// 差し替わっているとプレビューは綺麗でも、アプリでは bundle 内の（削られた）バリアントで砂嵐やマゼンタになり得る。
        /// </summary>
        private static string DetectProjectShaderRemap(GameObject root)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] shared = renderer.sharedMaterials;
                if (shared == null)
                {
                    continue;
                }

                foreach (Material material in shared)
                {
                    if (material == null || material.shader == null)
                    {
                        continue;
                    }

                    string path = AssetDatabase.GetAssetPath(material.shader);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    // プロジェクト／パッケージ由来なら Editor が差し替えた可能性が高い
                    if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(material.shader.name);
                    }
                }
            }

            if (names.Count == 0)
            {
                return null;
            }

            string list = string.Join(", ", names);
            return T(
                "This preview may use shaders from your Unity project (" + list + "), not only those inside the exported file. "
                + "The VRCast app always uses the shaders in the file. If the app shows noise or pink while this preview looks fine, "
                + "re-export with the latest converter (shader variants are included so the app keeps the look).",
                "このプレビューは、書き出したファイル内だけでなく、Unity プロジェクト側のシェーダー（" + list + "）に差し替わって表示されていることがあります。"
                + "VRCast のアプリはファイル内のシェーダーだけを使います。プレビューは普通なのにアプリだけ砂嵐やマゼンタになるときは、"
                + "最新の書き出しツールで書き出し直してください（シェーダーバリアントを同梱し、アプリでも見た目が保たれるようにしています）。",
                "이 미리보기는 내보낸 파일만이 아니라 Unity 프로젝트의 셰이더(" + list + ")로 바뀌어 표시될 수 있습니다. "
                + "VRCast 앱은 파일 안의 셰이더만 씁니다. 미리보기는 정상인데 앱만 노이즈나 마젠타로 보이면 "
                + "최신 내보내기 도구로 다시 내보내세요(셰이더 배리언트를 포함해 앱에서도 모습이 유지되도록 합니다).",
                "此预览可能使用 Unity 项目中的着色器（" + list + "），而不只是导出文件内的。VRCast 应用始终使用文件内的着色器。"
                + "若预览正常而应用出现噪点或品红，请用最新导出工具重新导出（已包含着色器变体以在应用中保持外观）。",
                "此預覽可能使用 Unity 專案中的著色器（" + list + "），而不只是匯出檔案內的。VRCast 應用程式一律使用檔案內的著色器。"
                + "若預覽正常而應用出現雜訊或洋紅，請用最新匯出工具重新匯出（已包含著色器變體以在應用中保持外觀）。");
        }

        private static ConstraintSet ReadConstraints(ZipArchive zip)
        {
            // 無い・読めない・内容が不正ならアプリと同じく空として扱う
            ZipArchiveEntry entry = zip.GetEntry(AvatarPackageLayout.ConstraintsEntry);
            if (entry == null)
            {
                return new ConstraintSet();
            }

            try
            {
                using (var reader = new StreamReader(entry.Open()))
                {
                    var set = JsonUtility.FromJson<ConstraintSet>(reader.ReadToEnd());
                    return set != null && set.Validate() == null ? set : new ConstraintSet();
                }
            }
            catch (Exception)
            {
                return new ConstraintSet();
            }
        }

        private void EvaluateConstraints()
        {
            if (_constraints == null)
            {
                return;
            }

            foreach (ConstraintEvaluator evaluator in _constraints)
            {
                evaluator.Evaluate();
            }
        }

        private void SetUpPose()
        {
            // Humanoid なら腕を動かせるようにする（アプリと同じく人型のマッスルで動かす）
            var animator = _instance.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                return;
            }

            _poseHandler = new HumanPoseHandler(animator.avatar, _instance.transform);
            _poseHandler.GetHumanPose(ref _pose);
            _baseMuscles = (float[])_pose.muscles.Clone();
            _armMuscles = new[]
            {
                Array.IndexOf(HumanTrait.MuscleName, "Left Arm Down-Up"),
                Array.IndexOf(HumanTrait.MuscleName, "Right Arm Down-Up"),
            };
        }

        private void ApplyArms(float[] muscles)
        {
            if (_poseHandler == null || muscles == null)
            {
                return;
            }

            // 体の位置・向きは最初のまま、マッスルだけを書き換える
            Array.Copy(muscles, _pose.muscles, muscles.Length);
            _poseHandler.SetHumanPose(ref _pose);
            EvaluateConstraints();
        }

        private void RenderPreview(Rect rect)
        {
            if (IsAnimating)
            {
                float t = (Mathf.Sin((float)EditorApplication.timeSinceStartup * SwingSpeed) + 1f) * 0.5f;
                var muscles = (float[])_baseMuscles.Clone();
                foreach (int index in _armMuscles)
                {
                    if (index >= 0)
                    {
                        muscles[index] = Mathf.Lerp(ArmDown, ArmUp, t);
                    }
                }

                ApplyArms(muscles);
            }

            Quaternion rotation = Quaternion.Euler(_orbit.y, _orbit.x, 0f);
            _preview.camera.transform.SetPositionAndRotation(
                _bounds.center - rotation * Vector3.forward * _distance, rotation);

            _preview.BeginPreview(rect, GUIStyle.none);
            _preview.camera.Render();
            Texture texture = _preview.EndPreview();
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }

        private void HandlePreviewInput(Rect rect, Action repaint)
        {
            Event current = Event.current;
            if (!rect.Contains(current.mousePosition))
            {
                return;
            }

            if (current.type == EventType.MouseDrag)
            {
                _orbit.x += current.delta.x * OrbitSpeed;
                _orbit.y = Mathf.Clamp(_orbit.y + current.delta.y * OrbitSpeed, -89f, 89f);
                current.Use();
                repaint();
            }
            else if (current.type == EventType.ScrollWheel)
            {
                _distance = Mathf.Max(0.1f, _distance * (1f + current.delta.y * ZoomSpeed));
                current.Use();
                repaint();
            }
        }

        private void ResetView()
        {
            // 正面（アバターの顔側）から全身が収まる距離
            _orbit = new Vector2(180f, 5f);
            float radius = Mathf.Max(_bounds.extents.magnitude, 0.1f);
            _distance = radius / Mathf.Sin(FieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            bool found = false;
            var bounds = new Bounds(new Vector3(0f, 0.8f, 0f), Vector3.one);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (renderer is ParticleSystemRenderer || !renderer.enabled)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        /// <summary>
        /// プレビューを破棄する（結果の記録は残す）。ウィンドウを閉じる・書き出し直す前に呼ぶ。
        /// </summary>
        public void Release(bool clearError = true)
        {
            _poseHandler?.Dispose();
            _poseHandler = null;
            _swingArms = false;
            _constraints = null;
            _constraintSet = null;

            // プレビューのシーンに入れたアバターも一緒に破棄される
            _preview?.Cleanup();
            _preview = null;
            if (_instance != null)
            {
                Object.DestroyImmediate(_instance);
            }

            _instance = null;
            if (_bundle != null)
            {
                _bundle.Unload(true);
                _bundle = null;
            }

            if (!string.IsNullOrEmpty(_bundleFile))
            {
                FileUtil.DeleteFileOrDirectory(_bundleFile);
                _bundleFile = null;
            }

            if (clearError)
            {
                _previewError = null;
            }

            _shaderRemapNote = null;
        }
    }
}
