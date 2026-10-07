using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
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
        private const string MediaFoundationPluginPath = "Assets/Plugins/VRCastVirtualCamera/x86_64/VRCastVirtualCamera.dll";

        // Spout2 の送信プラグインのパス（Tools/Spout/fetch.ps1 で配置）
        private const string SpoutPluginPath = "Assets/Plugins/KlakSpout/x86_64/KlakSpout.dll";

        // アプリアイコン（exe・タスクバー・タイトルバー）。透過付きの正方形 PNG
        private const string AppIconPath = "Assets/VRCast/Branding/AppIcon.png";

        // プロジェクトルートからの出力先
        private const string WindowsOutputPath = "Builds/Windows/VRCast.exe";

        // persistentDataPath を決める会社名・製品名
        private const string CompanyName = "VRCast";
        private const string ProductName = "VRCast";

        // アプリのバージョン（CHANGELOG.txt と converter の package.json の version に合わせる）
        private const string AppVersion = "1.11.3";

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

            // Spout2 の送信プラグインの有無を確認（無くてもビルドは続行し、Spout2 出力は使えない）
            if (!File.Exists(SpoutPluginPath))
            {
                Debug.LogWarning("[VRCast][Build] KlakSpout.dll not found. Run Tools/Spout/fetch.ps1 to enable the Spout2 output.");
            }

            // 設定ファイルの保存先 (LocalLow/VRCast/VRCast) を固定する
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;

            // Application.version・配布 zip 名に使うバージョン
            PlayerSettings.bundleVersion = AppVersion;

            // OBS キャプチャ向け: ウィンドウ表示・サイズ変更可・非アクティブ時も描画継続
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = DefaultWidth;
            PlayerSettings.defaultScreenHeight = DefaultHeight;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.useFlipModelSwapchain = true;

            // パネル下部の動作状況で GPU の処理時間を取る（FrameTimingManager はこれが無いと値が 0）
            PlayerSettings.enableFrameTimingStats = true;

            // VRChat / VCC プロジェクトと同じ Linear にする（Gamma だとアバターの陰影が VRChat より暗くなる）
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // C# を C++ に変換して実行する IL2CPP にする（揺れもの・トラッキング等の CPU 負荷を下げ、Mono のランタイムを同梱しない）。
            // ビルドには Unity の「Windows Build Support (IL2CPP)」と Visual Studio の C++ ツールが必要。
            // ランタイムはリフレクションを使わないため、コードの削減は Low（使っていない部分だけ除く）で足りる
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Standalone, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Low);

            // アプリアイコンを設定
            ApplyAppIcon();

            // 前回の出力を消してから出す（Mono でビルドした出力が残っていると IL2CPP のビルドが拒否される）
            CleanOutputFolder();

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

        private static void CleanOutputFolder()
        {
            // 同梱トラッカーはビルド後に BundledTrackerCopier がコピーし直すため、フォルダごと消してよい
            string folder = Path.GetDirectoryName(WindowsOutputPath);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
                Debug.Log($"[VRCast][Build] Cleaned previous build: {folder}");
            }
        }

        private static void WarnIfTrackerMissing()
        {
            // 入力元ごとに Trackers/(フォルダ) 以下に実行ファイルがあればビルド後に同梱される（BundledTrackerCopier）
            foreach (TrackingSource source in (TrackingSource[])System.Enum.GetValues(typeof(TrackingSource)))
            {
                // 外部アプリから受信する入力元には同梱版が無い
                if (!TrackingSourceInfo.UsesBundledTracker(source))
                {
                    continue;
                }

                if (TrackerProcess.FindBundled(TrackerProcess.BundledRoot, source) == null)
                {
                    string executable = TrackerProcess.ExecutableOf(source);
                    Debug.LogWarning($"[VRCast][Build] {executable} not found in {TrackerProcess.EditorFolderName}/"
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

            // Windows 11 の方式（Media Foundation）の DLL
            if (!File.Exists(MediaFoundationPluginPath))
            {
                Debug.LogWarning("[VRCast][Build] VRCastVirtualCamera.dll not found. Run Tools/VirtualCamera/build.bat (Unity Editor closed) "
                    + "to enable the Windows 11 virtual camera method.");
            }
        }

        private static void ApplyAppIcon()
        {
            // アイコン画像が無ければ既定の Unity アイコンのままビルドする
            var importer = AssetImporter.GetAtPath(AppIconPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[VRCast][Build] App icon not found: {AppIconPath}");
                return;
            }

            // 縮小時に劣化・縁の黒ずみが出ないよう、無圧縮・ミップマップなし・透過を考慮した取り込みにする
            if (importer.textureCompression != TextureImporterCompression.Uncompressed
                || importer.mipmapEnabled || !importer.alphaIsTransparency)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            // 既定アイコン（全プラットフォーム共通。Windows は各サイズをここから生成する）に設定
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIconPath);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
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
