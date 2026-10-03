using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRCast.Editor.Build
{
    /// <summary>
    /// VRCast の Windows スタンドアロンビルド。メニューと -executeMethod の両方から呼べる。
    /// </summary>
    public static class VRCastBuild
    {
        // 起動シーンのパス（無ければ生成する）
        private const string MainScenePath = "Assets/VRCast/Scenes/Main.unity";

        // プロジェクトルートからの出力先
        private const string WindowsOutputPath = "Builds/Windows/VRCast.exe";

        // persistentDataPath を決める会社名・製品名
        private const string CompanyName = "VRCast";
        private const string ProductName = "VRCast";

        // 初回起動時のウィンドウサイズ（以降は settings.json の値を使う）
        private const int DefaultWidth = 1280;
        private const int DefaultHeight = 720;

        [MenuItem("VRCast/Build/Windows x64")]
        public static void BuildWindows()
        {
            // 対話実行時は未保存シーンを破棄しないようユーザーに確認する
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[VRCast][Build] Build cancelled by user.");
                return;
            }

            // 起動シーンを用意してビルド対象に登録
            EnsureMainScene();

            // 設定ファイルの保存先 (LocalLow/VRCast/VRCast) を固定する
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;

            // OBS キャプチャ向け: ウィンドウ表示・サイズ変更可・非アクティブ時も描画継続
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = DefaultWidth;
            PlayerSettings.defaultScreenHeight = DefaultHeight;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;

            // ビルド設定を組み立てる
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainScenePath },
                locationPathName = WindowsOutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            // ビルドを実行して結果を確認
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            // 成功時は出力先とサイズをログに残す
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[VRCast][Build] Succeeded: {summary.outputPath} ({summary.totalSize} bytes)");
                return;
            }

            // 失敗時はエラー数を出力し、バッチモードでは終了コードで CI / 呼び出し側へ伝える
            Debug.LogError($"[VRCast][Build] Failed: {summary.result} (errors: {summary.totalErrors})");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }

        private static void EnsureMainScene()
        {
            // シーンが無ければ既定のカメラとライトだけを持つシーンを生成
            if (!File.Exists(MainScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MainScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, MainScenePath);
                Debug.Log($"[VRCast][Build] Created scene: {MainScenePath}");
            }

            // Build Settings に未登録なら先頭に追加する
            if (EditorBuildSettings.scenes.All(s => s.path != MainScenePath))
            {
                var scenes = EditorBuildSettings.scenes.ToList();
                scenes.Insert(0, new EditorBuildSettingsScene(MainScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }
    }
}
