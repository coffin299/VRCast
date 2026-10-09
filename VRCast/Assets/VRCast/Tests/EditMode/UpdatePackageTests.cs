using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using VRCast.Platform;

namespace VRCast.Tests
{
    /// <summary>
    /// 自動更新の配布 zip の検証（URL・SHA-256・エントリ名）と展開、アップデーターへの引数を確認する。
    /// </summary>
    public class UpdatePackageTests
    {
        // 正しい形の SHA-256（中身は問わない）
        private const string Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            // テストごとに独立した一時ディレクトリを使う
            _directory = Path.Combine(Path.GetTempPath(), "VRCastUpdateTests_" + Guid.NewGuid().ToString("N"));
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
        public void IsValid_GitHubReleaseWithHash_ReturnsTrue()
        {
            // GitHub Releases の URL・正の大きさ・64 桁の 16 進数がそろえば使えること
            string url = UpdatePackage.DownloadUrlPrefix + "v1.13.0/VRCast-1.13.0-win64.zip";
            Assert.That(UpdatePackage.IsValid(url, 1024, Sha256), Is.True);
            Assert.That(UpdatePackage.IsValid(url, 1024, Sha256.ToUpperInvariant()), Is.True);
        }

        [TestCase("https://example.com/VRCast.zip")]
        [TestCase("http://github.com/coffin299/VRCast/releases/download/v1/VRCast.zip")]
        [TestCase("https://github.com/coffin299/VRCast/releases/download/")]
        [TestCase("https://github.com/coffin299/VRCast/releases/download/../../other/x.zip")]
        [TestCase("https://github.com/coffin299/VRCast/releases/download/v1/VRCast.zip?x=1")]
        [TestCase("https://github.com/coffin299/VRCastEvil/releases/download/v1/VRCast.zip")]
        [TestCase("")]
        [TestCase(null)]
        public void IsAllowedUrl_OtherPlaces_ReturnsFalse(string url)
        {
            // 許可した場所以外・上位参照・クエリ付きは取りに行かないこと
            Assert.That(UpdatePackage.IsAllowedUrl(url), Is.False);
        }

        [TestCase(0L, Sha256)]
        [TestCase(-1L, Sha256)]
        [TestCase(1024L, "")]
        [TestCase(1024L, null)]
        [TestCase(1024L, "0123")]
        [TestCase(1024L, "g123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
        public void IsValid_BadSizeOrHash_ReturnsFalse(long size, string sha256)
        {
            // 大きさ・SHA-256 が確かめられない情報では自動更新しないこと
            string url = UpdatePackage.DownloadUrlPrefix + "v1.13.0/VRCast-1.13.0-win64.zip";
            Assert.That(UpdatePackage.IsValid(url, size, sha256), Is.False);
        }

        [Test]
        public void TryGetAppRelativePath_AppFile_ReturnsRelativePath()
        {
            // 本体フォルダ内のファイルは OS の区切りの相対パスになること
            Assert.That(UpdatePackage.TryGetAppRelativePath("VRCast/VRCast_Data/a.txt", out string relative), Is.True);
            Assert.That(relative, Is.EqualTo(Path.Combine("VRCast_Data", "a.txt")));
        }

        [TestCase("VRCast-Converter/VRCast-Converter-1.0.0.unitypackage")]
        [TestCase("VRCast/")]
        [TestCase("VRCast/VRCast_Data/")]
        [TestCase("Other/VRCast.exe")]
        public void TryGetAppRelativePath_OutsideAppOrFolder_ReturnsFalse(string entry)
        {
            // 書き出しツール・フォルダのエントリは展開しないこと
            Assert.That(UpdatePackage.TryGetAppRelativePath(entry, out _), Is.False);
        }

        [TestCase("VRCast/../evil.exe")]
        [TestCase("VRCast/VRCast_Data/../../evil.exe")]
        [TestCase("VRCast//evil.exe")]
        [TestCase("VRCast/C:/evil.exe")]
        [TestCase("VRCast/a\\..\\evil.exe")]
        public void TryGetAppRelativePath_ZipSlip_Throws(string entry)
        {
            // 展開先の外へ書く名前は拒否すること
            Assert.Throws<InvalidDataException>(() => UpdatePackage.TryGetAppRelativePath(entry, out _));
        }

        [Test]
        public void ExtractApp_ExtractsOnlyAppFolder()
        {
            // 本体の分だけを展開し、書き出しツールは展開しないこと
            string zip = WriteZip(("VRCast/VRCast.exe", "exe"), ("VRCast/VRCast_Data/a.txt", "a"),
                ("VRCast-Converter/x.unitypackage", "x"));
            string destination = Path.Combine(_directory, "out");

            int count = UpdatePackage.ExtractApp(zip, destination);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(File.ReadAllText(Path.Combine(destination, "VRCast_Data", "a.txt")), Is.EqualTo("a"));
            Assert.That(UpdatePackage.HasAppFiles(destination, "VRCast.exe"), Is.True);
            Assert.That(File.Exists(Path.Combine(destination, "x.unitypackage")), Is.False);
        }

        [Test]
        public void ExtractApp_RemovesPreviousFiles()
        {
            // 前回の展開の残りと混ざらないこと
            string destination = Path.Combine(_directory, "out");
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "stale.txt"), "old");
            string zip = WriteZip(("VRCast/VRCast.exe", "exe"));

            UpdatePackage.ExtractApp(zip, destination);

            Assert.That(File.Exists(Path.Combine(destination, "stale.txt")), Is.False);
        }

        [Test]
        public void HasAppFiles_MissingData_ReturnsFalse()
        {
            // exe だけで _Data が無ければ起動できないので使わないこと
            File.WriteAllText(Path.Combine(_directory, "VRCast.exe"), "exe");
            Assert.That(UpdatePackage.HasAppFiles(_directory, "VRCast.exe"), Is.False);
        }

        [Test]
        public void BuildUpdaterArguments_QuotesPaths()
        {
            // 空白を含むパスは引用符で囲み、アップデーターの引数の名前を付けること
            string arguments = UpdatePackage.BuildUpdaterArguments(1234, @"C:\a b\src", @"D:\VR Cast", "VRCast.exe",
                @"C:\log.txt", @"C:\result.txt");
            Assert.That(arguments, Is.EqualTo("--pid 1234 --source \"C:\\a b\\src\" --target \"D:\\VR Cast\" "
                + "--exe \"VRCast.exe\" --log \"C:\\log.txt\" --result \"C:\\result.txt\""));
        }

        [Test]
        public void Quote_TrailingBackslash_IsDoubled()
        {
            // 末尾の \ が閉じの " を打ち消さないこと（"C:\" → "C:\\"）
            Assert.That(UpdatePackage.Quote(@"C:\"), Is.EqualTo("\"C:\\\\\""));
        }

        private string WriteZip(params (string name, string text)[] entries)
        {
            // 指定のエントリだけを持つ zip を作る
            string path = Path.Combine(_directory, "package.zip");
            using (FileStream stream = File.Create(path))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach ((string name, string text) in entries)
                {
                    using (Stream entry = zip.CreateEntry(name).Open())
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(text);
                        entry.Write(bytes, 0, bytes.Length);
                    }
                }
            }

            return path;
        }
    }
}
