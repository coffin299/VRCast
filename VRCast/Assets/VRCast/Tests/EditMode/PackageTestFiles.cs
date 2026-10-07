using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using VRCast.AvatarFormat;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace VRCast.Tests
{
    /// <summary>
    /// テスト用の .vrcaster（ダミー bundle と manifest、任意の追加エントリ）を書く。
    /// </summary>
    public static class PackageTestFiles
    {
        // ダミー bundle の中身
        public static readonly byte[] DummyBundle = { 1, 2, 3, 4, 5, 6, 7, 8 };

        /// <summary>
        /// bundle の内容から正しいハッシュとサイズを設定した manifest。
        /// </summary>
        public static AvatarManifest CreateManifest(byte[] bundle)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return new AvatarManifest
                {
                    name = "TestAvatar",
                    unityVersion = Application.unityVersion,
                    bundleSha256 = BitConverter.ToString(sha.ComputeHash(bundle)).Replace("-", string.Empty).ToLowerInvariant(),
                    bundleSize = bundle.Length,
                };
            }
        }

        /// <summary>
        /// manifest（null なら省略）・bundle・追加エントリ（名前と文字列）を無圧縮で書く。
        /// </summary>
        public static void Write(string path, AvatarManifest manifest, byte[] bundle,
            IEnumerable<KeyValuePair<string, string>> extras = null)
        {
            using (FileStream stream = File.Create(path))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                // manifest（null の場合は省略）
                if (manifest != null)
                {
                    WriteEntry(zip, AvatarPackageLayout.ManifestEntry, Encoding.UTF8.GetBytes(JsonUtility.ToJson(manifest)));
                }

                // bundle 本体
                WriteEntry(zip, AvatarPackageLayout.BundleEntry, bundle);

                // 追加エントリ（不正名や metadata の検証用）
                if (extras == null)
                {
                    return;
                }

                foreach (KeyValuePair<string, string> extra in extras)
                {
                    WriteEntry(zip, extra.Key, Encoding.UTF8.GetBytes(extra.Value));
                }
            }
        }

        /// <summary>
        /// パッケージのエントリ名の一覧。
        /// </summary>
        public static List<string> EntryNames(string path)
        {
            var names = new List<string>();
            using (FileStream stream = File.OpenRead(path))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    names.Add(entry.FullName);
                }
            }

            return names;
        }

        /// <summary>
        /// パッケージのエントリの中身（無ければ null）。
        /// </summary>
        public static byte[] ReadEntry(string path, string name)
        {
            using (FileStream stream = File.OpenRead(path))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                ZipArchiveEntry entry = zip.GetEntry(name);
                if (entry == null)
                {
                    return null;
                }

                using (Stream source = entry.Open())
                using (var buffer = new MemoryStream())
                {
                    source.CopyTo(buffer);
                    return buffer.ToArray();
                }
            }
        }

        private static void WriteEntry(ZipArchive zip, string name, byte[] data)
        {
            // 無圧縮でエントリを書き込む（Exporter と同じ形式）
            using (Stream entry = zip.CreateEntry(name, CompressionLevel.NoCompression).Open())
            {
                entry.Write(data, 0, data.Length);
            }
        }
    }
}
