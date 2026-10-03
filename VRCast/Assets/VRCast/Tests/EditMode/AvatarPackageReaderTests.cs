using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Avatars;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace VRCast.Tests
{
    /// <summary>
    /// AvatarPackageReader の検証処理（信頼できない入力の拒否）を確認する。
    /// 実際の AssetBundle は使わず、ダミーバイト列で ZIP 構造とハッシュ検証のみを扱う。
    /// </summary>
    public class AvatarPackageReaderTests
    {
        // ダミー bundle の中身
        private static readonly byte[] DummyBundle = { 1, 2, 3, 4, 5, 6, 7, 8 };

        private string _directory;
        private string _cacheRoot;

        [SetUp]
        public void SetUp()
        {
            // テストごとに独立した一時ディレクトリを使う
            _directory = Path.Combine(Path.GetTempPath(), "VRCastPkgTests_" + Guid.NewGuid().ToString("N"));
            _cacheRoot = Path.Combine(_directory, "cache");
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            // 一時ディレクトリを後始末
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void Extract_ValidPackage_ExtractsBundle()
        {
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle);

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            // 展開された bundle が元のバイト列と一致すること
            Assert.That(File.ReadAllBytes(package.BundlePath), Is.EqualTo(DummyBundle));
            Assert.That(package.Manifest.name, Is.EqualTo("TestAvatar"));
        }

        [Test]
        public void Extract_SecondTime_ReusesCache()
        {
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle);

            // 2 回展開しても同じキャッシュパスになること
            string first = AvatarPackageReader.Extract(path, _cacheRoot).BundlePath;
            string second = AvatarPackageReader.Extract(path, _cacheRoot).BundlePath;
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void Extract_HashMismatch_Throws()
        {
            // manifest のハッシュと異なる中身を格納
            AvatarManifest manifest = CreateManifest(DummyBundle);
            byte[] tampered = { 8, 7, 6, 5, 4, 3, 2, 1 };
            string path = WritePackage(manifest, tampered);

            Assert.Throws<AvatarPackageException>(() => AvatarPackageReader.Extract(path, _cacheRoot));
        }

        [Test]
        public void Extract_MissingManifest_Throws()
        {
            string path = WritePackage(null, DummyBundle);

            Assert.Throws<AvatarPackageException>(() => AvatarPackageReader.Extract(path, _cacheRoot));
        }

        [Test]
        public void Extract_UnsupportedFormatVersion_Throws()
        {
            AvatarManifest manifest = CreateManifest(DummyBundle);
            manifest.formatVersion = AvatarPackageLayout.FormatVersion + 1;
            string path = WritePackage(manifest, DummyBundle);

            Assert.Throws<AvatarPackageException>(() => AvatarPackageReader.Extract(path, _cacheRoot));
        }

        [Test]
        public void Extract_TraversalEntry_Throws()
        {
            // 正常なパッケージに上位参照エントリを追加
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle, "../evil.txt");

            Assert.Throws<AvatarPackageException>(() => AvatarPackageReader.Extract(path, _cacheRoot));
        }

        [Test]
        public void Extract_NotZip_Throws()
        {
            string path = Path.Combine(_directory, "broken" + AvatarPackageLayout.Extension);
            File.WriteAllText(path, "not a zip");

            Assert.Throws<AvatarPackageException>(() => AvatarPackageReader.Extract(path, _cacheRoot));
        }

        [TestCase("manifest.json", true)]
        [TestCase("avatar.bundle", true)]
        [TestCase("metadata/", true)]
        [TestCase("metadata/physbones.json", true)]
        [TestCase("metadata/sub/x.json", false)]
        [TestCase("metadata/x.txt", false)]
        [TestCase("../manifest.json", false)]
        [TestCase("C:/evil.json", false)]
        [TestCase("metadata\\x.json", false)]
        [TestCase("other.dll", false)]
        public void IsAllowedEntryName_Cases(string name, bool expected)
        {
            Assert.That(AvatarPackageReader.IsAllowedEntryName(name), Is.EqualTo(expected));
        }

        private static AvatarManifest CreateManifest(byte[] bundle)
        {
            // bundle の内容から正しいハッシュとサイズを設定
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

        private string WritePackage(AvatarManifest manifest, byte[] bundle, string extraEntry = null)
        {
            string path = Path.Combine(_directory, "test" + AvatarPackageLayout.Extension);
            using (FileStream stream = File.Create(path))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                // manifest（null の場合は省略）
                if (manifest != null)
                {
                    WriteEntry(zip, AvatarPackageLayout.ManifestEntry, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(manifest)));
                }

                // bundle 本体
                WriteEntry(zip, AvatarPackageLayout.BundleEntry, bundle);

                // 追加の不正エントリ
                if (extraEntry != null)
                {
                    WriteEntry(zip, extraEntry, new byte[] { 0 });
                }
            }

            return path;
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
