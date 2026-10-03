using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// アバターを選んで .vrcaster を書き出す Editor ウィンドウ。
    /// </summary>
    public class AvatarExporterWindow : EditorWindow
    {
        // 次回の保存ダイアログ初期フォルダを保持する EditorPrefs キー
        private const string LastDirectoryKey = "VRCast.Converter.LastExportDirectory";

        [SerializeField]
        private GameObject _avatar;

        [MenuItem("VRCast/Avatar Exporter")]
        private static void Open()
        {
            // 既存ウィンドウがあれば再利用
            GetWindow<AvatarExporterWindow>("VRCast Exporter");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Export avatar to .vrcaster", EditorStyles.boldLabel);

            // シーン上のオブジェクトと Prefab の両方を受け付ける
            _avatar = (GameObject)EditorGUILayout.ObjectField("Avatar Root", _avatar, typeof(GameObject), true);

            // 検証結果を表示し、問題があれば書き出しを無効化
            string error = AvatarExporter.Validate(_avatar);
            if (error != null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "Only Unity built-in components (Transform, Animator, Renderers, MeshFilter) are exported. " +
                "VRChat components, scripts and the Animator Controller are removed from the exported copy. " +
                "The FX layer's default state (toggles etc.) is baked into the copy before removal.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(error != null))
            {
                if (GUILayout.Button("Export..."))
                {
                    ExportWithDialog();
                }
            }
        }

        private void ExportWithDialog()
        {
            // 保存先を選択（キャンセル時は空文字）
            string directory = EditorPrefs.GetString(LastDirectoryKey, string.Empty);
            string path = EditorUtility.SaveFilePanel(
                "Export VRCast Avatar", directory, _avatar.name, AvatarPackageLayout.Extension.TrimStart('.'));
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            // 次回用に保存先フォルダを記憶
            EditorPrefs.SetString(LastDirectoryKey, Path.GetDirectoryName(path));

            try
            {
                // 書き出し中は進捗バーを表示
                EditorUtility.DisplayProgressBar("VRCast Exporter", "Building avatar package...", 0.5f);
                AvatarExporter.Report report = AvatarExporter.Export(_avatar, path);

                // 結果をログとダイアログで通知
                string summary =
                    $"Exported: {report.OutputPath}\n" +
                    $"Size: {report.Manifest.bundleSize / (1024f * 1024f):F1} MB\n" +
                    $"Humanoid: {report.IsHumanoid}\n" +
                    $"Baked FX default clips: {report.BakedFxClips}\n" +
                    $"Expressions: {report.ExpressionCount}\n" +
                    $"Removed components: {report.Strip.RemovedComponents}, " +
                    $"missing scripts: {report.Strip.RemovedMissingScripts}, " +
                    $"EditorOnly objects: {report.Strip.RemovedEditorOnlyObjects}";
                Debug.Log("[VRCast][Exporter] " + summary.Replace("\n", " / "));
                EditorUtility.DisplayDialog("VRCast Exporter", summary, "OK");
            }
            catch (Exception e)
            {
                // 失敗理由を表示（スタックトレースは Console へ）
                Debug.LogException(e);
                EditorUtility.DisplayDialog("VRCast Exporter", "Export failed:\n" + e.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
