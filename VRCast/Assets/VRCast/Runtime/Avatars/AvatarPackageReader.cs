using System;
using System.Collections.Generic;
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

                    // 任意の metadata を読み込み（不正なら空扱い）
                    var expressions = ReadMetadata<ExpressionSet>(zip, AvatarPackageLayout.ExpressionsEntry);
                    var descriptor = ReadMetadata<AvatarDescriptorData>(zip, AvatarPackageLayout.DescriptorEntry);
                    var physBones = ReadMetadata<PhysBoneSet>(zip, AvatarPackageLayout.PhysBonesEntry);
                    var constraints = ReadMetadata<ConstraintSet>(zip, AvatarPackageLayout.ConstraintsEntry);
                    PerfectSyncData perfectSync = ReadPerfectSync(zip);

                    // bundle をキャッシュへ展開（ハッシュ検証込み）
                    string bundlePath = ExtractBundle(zip, manifest, cacheRoot);

                    // 系列（2022.3 等）が違うと読めない可能性があるため警告（同じ系列内のパッチ版の差は互換）
                    if (!IsSameUnityLine(manifest.unityVersion, Application.unityVersion))
                    {
                        VRCastLog.Warning(LogCategory,
                            $"Unity version mismatch: package {manifest.unityVersion}, runtime {Application.unityVersion}.");
                    }

                    return new AvatarPackage(
                        manifest, info.FullName, bundlePath, expressions, descriptor, physBones, constraints, perfectSync);
                }
            }
            catch (InvalidDataException e)
            {
                // ZIP として壊れている
                throw new AvatarPackageException("Not a valid package (corrupt ZIP).", e);
            }
        }

        /// <summary>
        /// bundle を展開せずに、パッケージのパーフェクトシンクの形状だけを読む（別のパッケージから形状を取り込む用）。
        /// パッケージ自体の検証は Extract と同じで、不正なら AvatarPackageException。形状が無い・不正なら空。
        /// </summary>
        public static PerfectSyncData ReadPerfectSyncOnly(string packagePath)
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
                    // エントリ名と manifest は Extract と同じく検証する
                    ValidateEntries(zip);
                    ReadManifest(zip);
                    return ReadPerfectSync(zip);
                }
            }
            catch (InvalidDataException e)
            {
                // ZIP として壊れている
                throw new AvatarPackageException("Not a valid package (corrupt ZIP).", e);
            }
        }

        /// <summary>
        /// Unity のバージョンが同じ系列（"2022.3.22f1" と "2022.3.62f3" のように先頭 2 つの数字が同じ）なら true。
        /// </summary>
        public static bool IsSameUnityLine(string a, string b)
        {
            return string.Equals(UnityLineOf(a), UnityLineOf(b), StringComparison.Ordinal);
        }

        private static string UnityLineOf(string version)
        {
            // 「年.マイナー」までを取り出す（形式が違えば全体で比べる）
            string value = version ?? string.Empty;
            int first = value.IndexOf('.');
            int second = first >= 0 ? value.IndexOf('.', first + 1) : -1;
            return second > 0 ? value.Substring(0, second) : value;
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

            // 上限付きで JSON テキストを読み込み
            string json = ReadTextEntry(entry, MaxManifestBytes);

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

        private static T ReadMetadata<T>(ZipArchive zip, string entryName)
            where T : class, IMetadata, new()
        {
            // 任意エントリのため、無ければ空
            ZipArchiveEntry entry = zip.GetEntry(entryName);
            if (entry == null)
            {
                return new T();
            }

            try
            {
                // 上限付きで読み込み、JSON として解釈
                string json = ReadTextEntry(entry, AvatarPackageLayout.MaxMetadataBytes);
                var data = JsonUtility.FromJson<T>(json);

                // 空 JSON や内容不正はアバター表示を妨げないよう警告のみ
                string error = data == null ? "empty" : data.Validate();
                if (error != null)
                {
                    VRCastLog.Warning(LogCategory, $"Ignored invalid {entryName}: {error}");
                    return new T();
                }

                return data;
            }
            catch (Exception e) when (e is AvatarPackageException || e is ArgumentException)
            {
                // サイズ超過や JSON 構文エラーも警告のみ
                VRCastLog.Warning(LogCategory, $"Ignored unreadable {entryName}: {e.Message}");
                return new T();
            }
        }

        private static PerfectSyncData ReadPerfectSync(ZipArchive zip)
        {
            // 目次が無い・不正・形状が空なら空（不正の警告は ReadMetadata が出す）
            if (zip.GetEntry(AvatarPackageLayout.PerfectSyncEntry) == null)
            {
                return PerfectSyncData.Empty;
            }

            var set = ReadMetadata<PerfectSyncSet>(zip, AvatarPackageLayout.PerfectSyncEntry);
            if (set.IsEmpty)
            {
                return PerfectSyncData.Empty;
            }

            var shapes = new List<PerfectSyncShapeData>();
            foreach (string name in set.shapes)
            {
                // 目次にあって形状ファイルが無いものは飛ばす
                string entryName = PerfectSyncSet.ShapeEntryName(name);
                if (zip.GetEntry(entryName) == null)
                {
                    VRCastLog.Warning(LogCategory, $"Missing {entryName}.");
                    continue;
                }

                // 不正（名前が空になる）・目次と名前が違うものは飛ばす
                var shape = ReadMetadata<PerfectSyncShape>(zip, entryName);
                if (shape.name != name)
                {
                    continue;
                }

                // 差分を展開できた形状だけを使う
                PerfectSyncShapeData decoded = DecodeShape(set, shape, out string error);
                if (decoded == null)
                {
                    VRCastLog.Warning(LogCategory, $"Ignored invalid {entryName}: {error}");
                    continue;
                }

                shapes.Add(decoded);
            }

            return new PerfectSyncData(set.meshes, shapes);
        }

        private static PerfectSyncShapeData DecodeShape(PerfectSyncSet set, PerfectSyncShape shape, out string error)
        {
            var meshes = new List<PerfectSyncMeshDelta>();
            foreach (PerfectSyncShapeMesh mesh in shape.meshes)
            {
                // 目次に無いメッシュを指していれば不正
                if (mesh.mesh >= set.meshes.Length)
                {
                    error = "Mesh index is out of range.";
                    return null;
                }

                // 目次の頂点数を上限に展開
                if (!PerfectSyncCodec.TryDecode(mesh.indices, mesh.deltas, set.meshes[mesh.mesh].vertexCount,
                        out int[] indices, out Vector3[] deltas, out error))
                {
                    return null;
                }

                meshes.Add(new PerfectSyncMeshDelta(mesh.mesh, indices, deltas));
            }

            error = null;
            return new PerfectSyncShapeData(shape.name, meshes);
        }

        private static string ReadTextEntry(ZipArchiveEntry entry, long maxBytes)
        {
            // 申告サイズで先に弾く
            if (entry.Length > maxBytes)
            {
                throw new AvatarPackageException($"{entry.FullName} is too large.");
            }

            // 申告サイズ以上は読まない（ヘッダ偽装対策）
            using (Stream source = entry.Open())
            using (var buffer = new MemoryStream())
            {
                CopyWithLimit(source, buffer, entry.Length, entry.FullName);
                return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            }
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
                    CopyWithLimit(source, target, manifest.bundleSize, AvatarPackageLayout.BundleEntry);
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

        private static void CopyWithLimit(Stream source, Stream target, long expectedBytes, string entryName)
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
                    throw new AvatarPackageException($"{entryName} is larger than declared.");
                }

                target.Write(buffer, 0, read);
            }

            // 申告サイズに満たない場合も不正
            if (total != expectedBytes)
            {
                throw new AvatarPackageException($"{entryName} is truncated.");
            }
        }
    }
}
