using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRCast.AvatarFormat;
using static VRCast.Converter.Editor.ExporterLoc;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// アバターを選んで .vrcaster を書き出す Editor ウィンドウ。
    /// </summary>
    public class AvatarExporterWindow : EditorWindow
    {
        // 次回の保存ダイアログ初期フォルダを保持する EditorPrefs キー
        private const string LastDirectoryKey = "VRCast.Converter.LastExportDirectory";

        // ダイアログ・進捗バーのタイトル（製品名なので訳さない）
        private const string Title = "VRCast Exporter";

        // アプリアイコン（VRCastIcon.png）の GUID。UPM（Packages/）と unitypackage（Assets/）で置き場所が違うため GUID で探す
        private const string IconGuid = "f0bfda6d028343bab19380553d3cfe26";

        // ヘッダーに表示するアイコンの大きさ（画像は高 DPI 用に 2 倍の 128px）
        private const float HeaderIconSize = 64f;

        // ヘッダーの製品名の文字サイズ
        private const int TitleFontSize = 18;

        [SerializeField]
        private GameObject _avatar;

        private Texture2D _icon;
        private GUIStyle _titleStyle;

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

        private void OnGUI()
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

            using (new EditorGUI.DisabledScope(error != null))
            {
                if (GUILayout.Button(T("Export...", "書き出す...", "내보내기...", "导出...", "匯出...")))
                {
                    ExportWithDialog();
                }
            }
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
                AvatarExporter.Report report = AvatarExporter.Export(_avatar, path);

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

            return
                $"{L("Exported", "出力先", "출력 위치", "输出位置", "輸出位置")}: {report.OutputPath}\n" +
                $"{L("Size", "サイズ", "크기", "大小", "大小")}: {report.Manifest.bundleSize / (1024f * 1024f):F1} MB\n" +
                $"Humanoid: {report.IsHumanoid}\n" +
                $"{L("NDMF (Modular Avatar) applied", "NDMF（Modular Avatar）の適用", "NDMF(Modular Avatar) 적용",
                    "已应用 NDMF（Modular Avatar）", "已套用 NDMF（Modular Avatar）")}: {report.NdmfApplied}\n" +
                $"{L("Outfits / accessories attached by VRCast", "VRCast が付け替えた衣装・小物",
                    "VRCast가 다시 붙인 의상·소품", "VRCast 重新挂接的服装和小物",
                    "VRCast 重新掛接的服裝和小物")}: {report.ModularAvatarFallbackFixes}\n" +
                $"{L("Baked FX default clips", "焼き込んだ FX 初期状態のクリップ", "반영한 FX 초기 상태 클립",
                    "已烘焙的 FX 初始状态剪辑", "已烘焙的 FX 初始狀態剪輯")}: {report.BakedFxClips}\n" +
                $"{L("Expressions", "表情", "표정", "表情", "表情")}: {report.ExpressionCount}\n" +
                $"{L("Lip sync", "リップシンク", "립싱크", "口型同步", "口型同步")}: {report.LipSyncMode}, " +
                $"{L("blink", "まばたき", "눈 깜빡임", "眨眼", "眨眼")}: {report.HasBlink}, " +
                $"{L("wink", "ウインク", "윙크", "眨单眼", "眨單眼")}: {report.HasWink}\n" +
                $"PhysBones: {report.PhysBoneCount}\n" +
                $"Constraints: {report.ConstraintCount}\n" +
                $"{L("Removed components", "取り除いたコンポーネント", "제거한 컴포넌트", "已移除的组件",
                    "已移除的元件")}: {report.Strip.RemovedComponents}, " +
                $"{L("missing scripts", "Missing Script", "Missing Script", "Missing Script",
                    "Missing Script")}: {report.Strip.RemovedMissingScripts}, " +
                $"{L("EditorOnly objects", "EditorOnly オブジェクト", "EditorOnly 오브젝트", "EditorOnly 对象",
                    "EditorOnly 物件")}: {report.Strip.RemovedEditorOnlyObjects}";
        }
    }
}
