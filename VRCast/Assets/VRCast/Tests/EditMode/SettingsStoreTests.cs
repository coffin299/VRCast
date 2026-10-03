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

        [Test]
        public void Load_OutOfRangeLight_IsClamped()
        {
            // 範囲外のライト設定を保存
            _store.Save(new AppSettings { lightIntensity = 100f, lightPitch = 200f, lightYaw = 270f });

            // 読込時に強度・仰角は上限へ、方位角は -180〜180 へ補正されること
            AppSettings settings = _store.Load();
            Assert.That(settings.lightIntensity, Is.EqualTo(AppSettings.MaxLightIntensity));
            Assert.That(settings.lightPitch, Is.EqualTo(90f));
            Assert.That(settings.lightYaw, Is.EqualTo(-90f).Within(0.001f));
        }

        [Test]
        public void Load_OutOfRangePose_IsClamped()
        {
            _store.Save(new AppSettings { poseArmDown = 5f, poseElbowBend = -1f });

            // ポーズの度合いは 0〜1 に補正されること
            AppSettings settings = _store.Load();
            Assert.That(settings.poseArmDown, Is.EqualTo(1f));
            Assert.That(settings.poseElbowBend, Is.EqualTo(0f));
        }

        [Test]
        public void Load_OutOfRangeMicrophone_IsClamped()
        {
            _store.Save(new AppSettings { micGain = 100f, micThreshold = -1f, microphoneDevice = null });

            // 感度・しきい値は範囲内へ、デバイス名は空文字へ補正されること
            AppSettings settings = _store.Load();
            Assert.That(settings.micGain, Is.EqualTo(AppSettings.MaxMicGain));
            Assert.That(settings.micThreshold, Is.EqualTo(0f));
            Assert.That(settings.microphoneDevice, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Load_OutOfRangeTrackingPort_IsClamped()
        {
            _store.Save(new AppSettings { trackingPort = 80, trackingBodyLean = 10f, trackingGaze = -1f });

            // 特権ポートは下限へ、上半身の傾きの強さは上限へ、視線の強さは 0 へ補正されること
            AppSettings settings = _store.Load();
            Assert.That(settings.trackingPort, Is.EqualTo(AppSettings.MinTrackingPort));
            Assert.That(settings.trackingBodyLean, Is.EqualTo(AppSettings.MaxTrackingBodyLean));
            Assert.That(settings.trackingGaze, Is.EqualTo(0f));
        }

        [Test]
        public void Load_UnknownTrackingSource_FallsBackToMediaPipe()
        {
            _store.Save(new AppSettings { trackingSource = (TrackingSource)99 });

            // 未知の入力元は既定の MediaPipe へ補正されること
            AppSettings settings = _store.Load();
            Assert.That(settings.trackingSource, Is.EqualTo(TrackingSource.MediaPipe));
        }

        [Test]
        public void Load_UnknownBodyMotion_FallsBackToLean()
        {
            _store.Save(new AppSettings { trackingBodyMotion = (BodyMotion)99 });

            // 未知の体の動かし方は既定の傾きへ補正されること
            AppSettings settings = _store.Load();
            Assert.That(settings.trackingBodyMotion, Is.EqualTo(BodyMotion.Lean));
        }

        [Test]
        public void Load_UnknownUiLanguage_FallsBackToAuto()
        {
            _store.Save(new AppSettings { uiLanguage = (UiLanguage)99 });

            // 未知の表示言語は OS 準拠へ補正されること
            AppSettings settings = _store.Load();
            Assert.That(settings.uiLanguage, Is.EqualTo(UiLanguage.Auto));
        }

        [Test]
        public void ResetToDefaults_RestoresDefaultsButKeepsWindowAndAvatar()
        {
            var settings = new AppSettings
            {
                windowWidth = 1920,
                windowHeight = 1080,
                lastAvatarPath = "C:/Avatars/Test.vrcaster",
                transparentBackground = true,
                lightIntensity = 3f,
                uiLanguage = UiLanguage.English,
            };

            // 同じインスタンスのまま既定値へ戻り、ウィンドウサイズと最後のアバターは残ること
            settings.ResetToDefaults();
            var defaults = new AppSettings();
            Assert.That(settings.transparentBackground, Is.EqualTo(defaults.transparentBackground));
            Assert.That(settings.lightIntensity, Is.EqualTo(defaults.lightIntensity));
            Assert.That(settings.uiLanguage, Is.EqualTo(defaults.uiLanguage));
            Assert.That(settings.windowWidth, Is.EqualTo(1920));
            Assert.That(settings.windowHeight, Is.EqualTo(1080));
            Assert.That(settings.lastAvatarPath, Is.EqualTo("C:/Avatars/Test.vrcaster"));
        }

        [Test]
        public void Load_OutOfRangeLighting_IsClamped()
        {
            _store.Save(new AppSettings { lightTemperature = 100f, ambientIntensity = 9f });

            // 色温度は下限、環境光は上限へ丸められること
            AppSettings settings = _store.Load();
            Assert.That(settings.lightTemperature, Is.EqualTo(AppSettings.MinLightTemperature));
            Assert.That(settings.ambientIntensity, Is.EqualTo(AppSettings.MaxAmbientIntensity));
        }

        [Test]
        public void Load_OutOfRangeVoiceScale_IsClamped()
        {
            _store.Save(new AppSettings { lipSyncVoiceScale = 0f });

            // 範囲外の声の高さ補正は下限へ丸められること
            AppSettings settings = _store.Load();
            Assert.That(settings.lipSyncVoiceScale, Is.EqualTo(AppSettings.MinVoiceScale));
        }

        [Test]
        public void Load_OutOfRangeUiScale_IsClamped()
        {
            _store.Save(new AppSettings { uiScale = 10f });

            // 範囲外の UI 倍率は上限へ丸められること
            AppSettings settings = _store.Load();
            Assert.That(settings.uiScale, Is.EqualTo(AppSettings.MaxUiScale));
        }
    }
}
