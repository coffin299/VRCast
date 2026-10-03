using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Animations;
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
            public int BakedFxClips;
            public int ExpressionCount;
            public string LipSyncMode;
            public bool HasBlink;
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

                // VRChat アバターなら FX コントローラーを取得（無ければ null）
                Component descriptor = VrcDescriptorReader.FindDescriptor(source);
                AnimatorController fx = descriptor != null ? VrcDescriptorReader.GetFxController(descriptor) : null;

                // VRChat 上の初期状態に近づけるため、除去前に FX の既定状態を焼き込む
                if (fx != null)
                {
                    report.BakedFxClips = FxDefaultStateBaker.Bake(
                        clone, fx, VrcDescriptorReader.GetExpressionParameterDefaults(descriptor));
                }

                // FX から表情プリセットを抽出
                ExpressionSet expressions = fx != null ? ExpressionExtractor.Extract(fx) : new ExpressionSet();
                report.ExpressionCount = expressions.presets.Length;

                // リップシンク・まぶた設定（パスは元アバター基準＝複製と同じ階層）
                AvatarDescriptorData descriptorData = descriptor != null
                    ? VrcDescriptorReader.GetDescriptorData(descriptor, source.transform)
                    : new AvatarDescriptorData();
                report.LipSyncMode = descriptorData.lipSync.mode;
                report.HasBlink = descriptorData.eyelids.blinkBlendShape.Length > 0;

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

                // 中身のある metadata だけを Runtime と同じ規則で検証して同梱
                var metadata = new Dictionary<string, IMetadata>();
                if (expressions.presets.Length > 0)
                {
                    metadata[AvatarPackageLayout.ExpressionsEntry] = expressions;
                }

                if (report.LipSyncMode != LipSyncData.ModeNone || report.HasBlink)
                {
                    metadata[AvatarPackageLayout.DescriptorEntry] = descriptorData;
                }

                foreach (KeyValuePair<string, IMetadata> entry in metadata)
                {
                    string metadataError = entry.Value.Validate();
                    if (metadataError != null)
                    {
                        throw new InvalidOperationException($"Invalid {entry.Key}: {metadataError}");
                    }
                }

                // ZIP にまとめて出力
                WritePackage(outputPath, report.Manifest, bundlePath, metadata);
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

        private static void WritePackage(
            string outputPath, AvatarManifest manifest, string bundlePath, IReadOnlyDictionary<string, IMetadata> metadata)
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
                WriteTextEntry(zip, AvatarPackageLayout.ManifestEntry, JsonUtility.ToJson(manifest, true));

                // 任意の metadata（JsonUtility.ToJson はインターフェース経由でも実行時の型でシリアライズする）
                foreach (KeyValuePair<string, IMetadata> entry in metadata)
                {
                    WriteTextEntry(zip, entry.Key, JsonUtility.ToJson(entry.Value, true));
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

        private static void WriteTextEntry(ZipArchive zip, string entryName, string text)
        {
            // テキストエントリも bundle と同じく無圧縮で格納
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(entry.Open()))
            {
                writer.Write(text);
            }
        }
    }
}
