using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Avatars
{
    /// <summary>
    /// .vrcaster を信頼できない入力として検証し、bundle をキャッシュへ展開する。
    /// </summary>
    public static class AvatarPackageReader
    {
        // ログのカテゴリ名
        private const string LogCategory = "AvatarPackage";

        // パッケージ全体の上限サイズ（1 GiB）
        public const long MaxPackageBytes = 1L << 30;

        // manifest.json の上限サイズ（64 KiB）
        public const long MaxManifestBytes = 64 * 1024;

        // ZIP エントリ数の上限
        public const int MaxEntries = 256;

        // コピー用バッファサイズ
        private const int CopyBufferSize = 81920;

        /// <summary>
        /// パッケージを検証し、bundle を cacheRoot/sha256/ に展開して返す。
        /// </summary>
        public static AvatarPackage Extract(string packagePath, string cacheRoot)
        {
            // ファイルの存在とサイズを先に確認
            var info = new FileInfo(packagePath);
            if (!info.Exists)
            {
                throw new AvatarPackageException($"File not found: {packagePath}");
            }

            if (info.Length > MaxPackageBytes)
            {
                throw new AvatarPackageException($"Package is too large: {info.Length} bytes.");
            }

            try
            {
                using (FileStream stream = info.OpenRead())
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    // 全エントリ名を検証（パストラバーサル・未知ファイルを拒否）
                    ValidateEntries(zip);

                    // manifest を読み込んで検証
                    AvatarManifest manifest = ReadManifest(zip);

                    // bundle をキャッシュへ展開（ハッシュ検証込み）
                    string bundlePath = ExtractBundle(zip, manifest, cacheRoot);

                    // Unity バージョン差は読めない可能性があるため警告
                    if (manifest.unityVersion != Application.unityVersion)
                    {
                        VRCastLog.Warning(LogCategory,
                            $"Unity version mismatch: package {manifest.unityVersion}, runtime {Application.unityVersion}.");
                    }

                    return new AvatarPackage(manifest, info.FullName, bundlePath);
                }
            }
            catch (InvalidDataException e)
            {
                // ZIP として壊れている
                throw new AvatarPackageException("Not a valid package (corrupt ZIP).", e);
            }
        }

        private static void ValidateEntries(ZipArchive zip)
        {
            // エントリ数の上限確認
            if (zip.Entries.Count > MaxEntries)
            {
                throw new AvatarPackageException($"Too many entries: {zip.Entries.Count}.");
            }

            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                // 許可されたエントリ名以外は拒否
                if (!IsAllowedEntryName(entry.FullName))
                {
                    throw new AvatarPackageException($"Unexpected entry: {entry.FullName}");
                }
            }
        }

        /// <summary>
        /// 既知のエントリ名だけを許可する。区切りは '/' のみ、上位参照や絶対パスは不可。
        /// </summary>
        public static bool IsAllowedEntryName(string name)
        {
            // 空名、バックスラッシュ、ドライブ指定、上位参照は不可
            if (string.IsNullOrEmpty(name) || name.Contains("\\") || name.Contains(":") || name.Contains(".."))
            {
                return false;
            }

            // 必須エントリ
            if (name == AvatarPackageLayout.ManifestEntry || name == AvatarPackageLayout.BundleEntry)
            {
                return true;
            }

            // metadata ディレクトリ自体のエントリ
            if (name == AvatarPackageLayout.MetadataPrefix)
            {
                return true;
            }

            // metadata/<name>.json（サブディレクトリ不可）
            if (name.StartsWith(AvatarPackageLayout.MetadataPrefix, StringComparison.Ordinal))
            {
                string fileName = name.Substring(AvatarPackageLayout.MetadataPrefix.Length);
                return fileName.Length > 0 && !fileName.Contains("/") && fileName.EndsWith(".json", StringComparison.Ordinal);
            }

            return false;
        }

        private static AvatarManifest ReadManifest(ZipArchive zip)
        {
            // 必須エントリの存在確認
            ZipArchiveEntry entry = zip.GetEntry(AvatarPackageLayout.ManifestEntry);
            if (entry == null)
            {
                throw new AvatarPackageException("manifest.json is missing.");
            }

            // 巨大 manifest による負荷を防ぐ
            if (entry.Length > MaxManifestBytes)
            {
                throw new AvatarPackageException("manifest.json is too large.");
            }

            // JSON として読み込み
            string json;
            using (var reader = new StreamReader(entry.Open()))
            {
                json = reader.ReadToEnd();
            }

            AvatarManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<AvatarManifest>(json);
            }
            catch (ArgumentException e)
            {
                // JSON 構文エラー
                throw new AvatarPackageException("manifest.json is not valid JSON.", e);
            }

            // 空 JSON 等で null になった場合
            if (manifest == null)
            {
                throw new AvatarPackageException("manifest.json is empty.");
            }

            // 内容の検証
            string error = manifest.Validate();
            if (error != null)
            {
                throw new AvatarPackageException("Invalid manifest: " + error);
            }

            return manifest;
        }

        private static string ExtractBundle(ZipArchive zip, AvatarManifest manifest, string cacheRoot)
        {
            // 必須エントリの存在確認
            ZipArchiveEntry entry = zip.GetEntry(AvatarPackageLayout.BundleEntry);
            if (entry == null)
            {
                throw new AvatarPackageException("avatar.bundle is missing.");
            }

            // ZIP 上のサイズと manifest の申告が一致しなければ不正
            if (entry.Length != manifest.bundleSize)
            {
                throw new AvatarPackageException("avatar.bundle size does not match manifest.");
            }

            // 検証済みハッシュ（16 進のみ）をディレクトリ名に使う
            string directory = Path.Combine(cacheRoot, manifest.bundleSha256);
            string bundlePath = Path.Combine(directory, AvatarPackageLayout.BundleEntry);

            // 同じ内容が展開済みでハッシュも一致すれば再利用
            if (File.Exists(bundlePath) && HashUtility.ComputeSha256Hex(bundlePath) == manifest.bundleSha256)
            {
                return bundlePath;
            }

            // 一時ファイルへ上限付きでコピー（ヘッダ偽装による展開爆弾対策）
            Directory.CreateDirectory(directory);
            string tempPath = bundlePath + ".tmp";
            try
            {
                using (Stream source = entry.Open())
                using (FileStream target = File.Create(tempPath))
                {
                    CopyWithLimit(source, target, manifest.bundleSize);
                }
            }
            catch
            {
                // 途中で失敗した一時ファイルを残さない
                File.Delete(tempPath);
                throw;
            }

            // ハッシュ不一致なら破棄
            if (HashUtility.ComputeSha256Hex(tempPath) != manifest.bundleSha256)
            {
                File.Delete(tempPath);
                throw new AvatarPackageException("avatar.bundle hash does not match manifest.");
            }

            // 検証済みファイルを正式な名前へ
            if (File.Exists(bundlePath))
            {
                File.Delete(bundlePath);
            }

            File.Move(tempPath, bundlePath);
            return bundlePath;
        }

        private static void CopyWithLimit(Stream source, Stream target, long expectedBytes)
        {
            var buffer = new byte[CopyBufferSize];
            long total = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                // 申告サイズを超えたら中断
                total += read;
                if (total > expectedBytes)
                {
                    throw new AvatarPackageException("avatar.bundle is larger than declared.");
                }

                target.Write(buffer, 0, read);
            }

            // 申告サイズに満たない場合も不正
            if (total != expectedBytes)
            {
                throw new AvatarPackageException("avatar.bundle is truncated.");
            }
        }
    }
}
