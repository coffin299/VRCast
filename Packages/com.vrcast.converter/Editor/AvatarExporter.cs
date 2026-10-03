using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;
using VRCast.AvatarFormat;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using Object = UnityEngine.Object;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// アバター GameObject を .vrcaster へ書き出す。
    /// 元オブジェクトは変更せず、複製を除去処理して一時 Prefab → AssetBundle → ZIP の順に生成する。
    /// </summary>
    public static class AvatarExporter
    {
        // 一時 Prefab を置くフォルダ（PrefabAssetPath の親）
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__VRCastExport";

        // Runtime は Windows スタンドアロンのみ対象
        private const BuildTarget BundleTarget = BuildTarget.StandaloneWindows64;

        public struct Report
        {
            public string OutputPath;
            public AvatarManifest Manifest;
            public bool IsHumanoid;
            public ComponentStripper.Result Strip;
        }

        /// <summary>
        /// 書き出し前の検証。問題があればエラーメッセージ、無ければ null を返す。
        /// </summary>
        public static string Validate(GameObject source)
        {
            // 対象未指定
            if (source == null)
            {
                return "Avatar GameObject is not set.";
            }

            // ルートに Animator が無いと Humanoid として扱えない
            var animator = source.GetComponent<Animator>();
            if (animator == null || animator.avatar == null)
            {
                return "Root object must have an Animator with an Avatar.";
            }

            // 一時フォルダが既に存在する場合はユーザー資産を消さないよう中断
            if (AssetDatabase.IsValidFolder($"{TempFolderParent}/{TempFolderName}"))
            {
                return $"Temporary folder '{TempFolderParent}/{TempFolderName}' already exists. Remove it and retry.";
            }

            return null;
        }

        public static Report Export(GameObject source, string outputPath)
        {
            // 事前検証に失敗したら例外で中断
            string error = Validate(source);
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            // 出力先の拡張子を統一
            if (!outputPath.EndsWith(AvatarPackageLayout.Extension, StringComparison.OrdinalIgnoreCase))
            {
                outputPath += AvatarPackageLayout.Extension;
            }

            var report = new Report { OutputPath = outputPath };
            GameObject clone = null;
            string bundleDir = FileUtil.GetUniqueTempPathInProject();
            string tempFolder = $"{TempFolderParent}/{TempFolderName}";

            try
            {
                // 元オブジェクトを壊さないよう複製し、原点に配置
                clone = Object.Instantiate(source);
                clone.name = source.name;
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                // 許可リスト外のコンポーネント等を除去
                report.Strip = ComponentStripper.Strip(clone);
                report.IsHumanoid = clone.GetComponent<Animator>().isHuman;

                // AssetBundle 化のため一時 Prefab として保存
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
                PrefabUtility.SaveAsPrefabAsset(clone, AvatarPackageLayout.PrefabAssetPath, out bool saved);
                if (!saved)
                {
                    throw new InvalidOperationException("Failed to save temporary prefab.");
                }

                // Prefab と依存アセット（Mesh / Material / Shader / Texture）を bundle 化
                string bundlePath = BuildBundle(bundleDir);

                // manifest を生成して検証
                report.Manifest = new AvatarManifest
                {
                    name = source.name,
                    unityVersion = Application.unityVersion,
                    bundleSha256 = HashUtility.ComputeSha256Hex(bundlePath),
                    bundleSize = new FileInfo(bundlePath).Length,
                    createdAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                };
                string manifestError = report.Manifest.Validate();
                if (manifestError != null)
                {
                    throw new InvalidOperationException(manifestError);
                }

                // ZIP にまとめて出力
                WritePackage(outputPath, report.Manifest, bundlePath);
                return report;
            }
            finally
            {
                // 複製と一時ファイルは成否に関わらず必ず後始末
                if (clone != null)
                {
                    Object.DestroyImmediate(clone);
                }

                AssetDatabase.DeleteAsset(tempFolder);
                FileUtil.DeleteFileOrDirectory(bundleDir);
            }
        }

        private static string BuildBundle(string bundleDir)
        {
            // 出力先を作成
            Directory.CreateDirectory(bundleDir);

            // 一時 Prefab だけを明示指定して bundle を作る（プロジェクト内の bundle 名設定は無視される）
            var builds = new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = AvatarPackageLayout.BundleEntry,
                    assetNames = new[] { AvatarPackageLayout.PrefabAssetPath },
                },
            };

            // LZ4 圧縮で部分読込を可能にし、エラーは厳格に扱う
            const BuildAssetBundleOptions options =
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode;
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(bundleDir, builds, options, BundleTarget);
            if (manifest == null)
            {
                throw new InvalidOperationException("AssetBundle build failed. See Console for details.");
            }

            // 生成された bundle のパスを返す
            string bundlePath = Path.Combine(bundleDir, AvatarPackageLayout.BundleEntry);
            if (!File.Exists(bundlePath))
            {
                throw new FileNotFoundException("AssetBundle output not found.", bundlePath);
            }

            return bundlePath;
        }

        private static void WritePackage(string outputPath, AvatarManifest manifest, string bundlePath)
        {
            // 書き込み途中の破損ファイルを残さないよう一時ファイルへ書いてから置き換える
            string tempPath = outputPath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            using (FileStream stream = File.Create(tempPath))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                // bundle は LZ4 圧縮済みで、Runtime 側の Deflate 依存も避けるため無圧縮で格納
                ZipArchiveEntry manifestEntry = zip.CreateEntry(AvatarPackageLayout.ManifestEntry, CompressionLevel.NoCompression);
                using (var writer = new StreamWriter(manifestEntry.Open()))
                {
                    writer.Write(JsonUtility.ToJson(manifest, true));
                }

                // bundle 本体をコピー
                ZipArchiveEntry bundleEntry = zip.CreateEntry(AvatarPackageLayout.BundleEntry, CompressionLevel.NoCompression);
                using (Stream entryStream = bundleEntry.Open())
                using (FileStream bundleStream = File.OpenRead(bundlePath))
                {
                    bundleStream.CopyTo(entryStream);
                }
            }

            // 既存ファイルを置き換え
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            File.Move(tempPath, outputPath);
        }
    }
}
