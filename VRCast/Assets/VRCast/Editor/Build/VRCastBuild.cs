using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRCast.Core;
using VRCast.Output;
using VRCast.Tracking;

namespace VRCast.Editor.Build
{
    /// <summary>
    /// VRCast の Windows スタンドアロンビルド。メニューと -executeMethod の両方から呼べる。
    /// </summary>
    public static class VRCastBuild
    {
        // 起動シーンのパス（無ければ生成する）
        private const string MainScenePath = "Assets/VRCast/Scenes/Main.unity";

        // 仮想カメラの送信プラグインのパス（Tools/UnityCapture/fetch.ps1 で配置）
        private const string VirtualCameraPluginPath = "Assets/Plugins/UnityCapture/x86_64/UnityCapturePlugin.dll";

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

            // 同梱トラッカーの有無を確認（無くてもビルドは続行し、トラッキングはパス指定が必要になる）
            WarnIfTrackerMissing();

            // 仮想カメラの同梱ファイルの有無を確認（無くてもビルドは続行し、仮想カメラは使えない）
            WarnIfVirtualCameraMissing();

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

        private static void WarnIfTrackerMissing()
        {
            // 入力元ごとに StreamingAssets/(フォルダ) 以下に実行ファイルがあれば同梱される
            foreach (TrackingSource source in (TrackingSource[])System.Enum.GetValues(typeof(TrackingSource)))
            {
                if (TrackerProcess.FindBundled(Application.streamingAssetsPath, source) == null)
                {
                    string executable = TrackerProcess.ExecutableOf(source);
                    Debug.LogWarning($"[VRCast][Build] {executable} not found in Assets/StreamingAssets/"
                        + $"{TrackerProcess.FolderOf(source)}. {source} tracking will require an {executable} path.");
                }
            }
        }

        private static void WarnIfVirtualCameraMissing()
        {
            // ドライバー（StreamingAssets）と送信プラグイン（Plugins）の両方が必要
            bool hasDriver = VirtualCameraInstaller.FindBundled(Application.streamingAssetsPath) != null;
            bool hasPlugin = File.Exists(VirtualCameraPluginPath);
            if (!hasDriver || !hasPlugin)
            {
                Debug.LogWarning("[VRCast][Build] UnityCapture files not found. Run Tools/UnityCapture/fetch.ps1 "
                    + "to enable the virtual camera output.");
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
