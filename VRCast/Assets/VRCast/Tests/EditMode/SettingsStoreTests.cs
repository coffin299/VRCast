using System.IO;
using NUnit.Framework;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// SettingsStore の読み書きとフォールバック動作を検証する。
    /// </summary>
    public class SettingsStoreTests
    {
        private string _directory;
        private SettingsStore _store;

        [SetUp]
        public void SetUp()
        {
            // テストごとに独立した一時ディレクトリを使う
            _directory = Path.Combine(Path.GetTempPath(), "VRCastTests_" + System.Guid.NewGuid().ToString("N"));
            _store = new SettingsStore(Path.Combine(_directory, SettingsStore.DefaultFileName));
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
        public void Load_MissingFile_ReturnsDefaults()
        {
            // ファイルが無い状態で読み込む
            AppSettings settings = _store.Load();

            // 既定値が返ること
            Assert.That(settings.windowWidth, Is.EqualTo(new AppSettings().windowWidth));
        }

        [Test]
        public void SaveThenLoad_RoundTripsValues()
        {
            // 既定値と異なる値を保存
            var saved = new AppSettings
            {
                windowWidth = 1920,
                windowHeight = 1080,
                backgroundColor = Color.green,
                lastAvatarPath = "C:/Avatars/Test.vrcaster",
            };
            Assert.That(_store.Save(saved), Is.True);

            // 再読込して同じ値であること
            AppSettings loaded = _store.Load();
            Assert.That(loaded.windowWidth, Is.EqualTo(1920));
            Assert.That(loaded.windowHeight, Is.EqualTo(1080));
            Assert.That(loaded.backgroundColor, Is.EqualTo(Color.green));
            Assert.That(loaded.lastAvatarPath, Is.EqualTo("C:/Avatars/Test.vrcaster"));
        }

        [Test]
        public void Load_CorruptFile_ReturnsDefaults()
        {
            // 壊れた JSON を書き込む
            Directory.CreateDirectory(_directory);
            File.WriteAllText(_store.FilePath, "{ not json");

            // 例外を出さず既定値が返ること
            AppSettings settings = _store.Load();
            Assert.That(settings.windowHeight, Is.EqualTo(new AppSettings().windowHeight));
        }

        [Test]
        public void Load_OutOfRangeSize_IsClamped()
        {
            // 下限未満のサイズを保存
            _store.Save(new AppSettings { windowWidth = 0, windowHeight = -5 });

            // 読込時に下限へ補正されること
            AppSettings settings = _store.Load();
            Assert.That(settings.windowWidth, Is.EqualTo(AppSettings.MinWindowSize));
            Assert.That(settings.windowHeight, Is.EqualTo(AppSettings.MinWindowSize));
        }
    }
}
