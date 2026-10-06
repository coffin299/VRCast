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
        public void AvatarCamera_SaveThenLoad_RoundTripsPose()
        {
            var saved = new AppSettings();
            var pose = new CameraPose
            {
                target = new Vector3(0.1f, 1.3f, -0.2f), distance = 1.8f, yaw = 160f, pitch = 10f, fieldOfView = 25f,
            };
            saved.SetAvatarCamera("C:/Avatars/A.vrcaster", pose);
            _store.Save(saved);

            // 再読込しても同じ視点が、パスの大文字・小文字に関係なく取り出せること
            AppSettings loaded = _store.Load();
            Assert.That(loaded.TryGetAvatarCamera("c:/avatars/a.VRCASTER", out CameraPose result), Is.True);
            Assert.That(result.SameAs(pose), Is.True);
            Assert.That(loaded.TryGetAvatarCamera("C:/Avatars/B.vrcaster", out _), Is.False);
        }

        [Test]
        public void AvatarCamera_OverLimit_DropsLeastRecentlyUsed()
        {
            var settings = new AppSettings();
            var pose = new CameraPose { distance = 2f, fieldOfView = 30f };

            // 上限ちょうどまで記録し、最初のものを使い直してから 1 件追加する
            for (int i = 0; i < AppSettings.MaxAvatarCameras; i++)
            {
                settings.SetAvatarCamera($"C:/Avatars/{i}.vrcaster", pose);
            }

            settings.SetAvatarCamera("C:/Avatars/0.vrcaster", pose);
            settings.SetAvatarCamera("C:/Avatars/new.vrcaster", pose);

            // 件数は上限のまま、使い直した 0 は残り、最も長く使っていない 1 が捨てられること
            Assert.That(settings.avatarCameras.Count, Is.EqualTo(AppSettings.MaxAvatarCameras));
            Assert.That(settings.TryGetAvatarCamera("C:/Avatars/0.vrcaster", out _), Is.True);
            Assert.That(settings.TryGetAvatarCamera("C:/Avatars/1.vrcaster", out _), Is.False);
        }

        [Test]
        public void AvatarCamera_InvalidPose_IsNotRecorded()
        {
            var settings = new AppSettings();

            // 壊れた値（NaN）や空のパスは記録しないこと
            settings.SetAvatarCamera("C:/Avatars/A.vrcaster", new CameraPose { distance = float.NaN });
            settings.SetAvatarCamera(string.Empty, new CameraPose { distance = 2f });
            Assert.That(settings.avatarCameras, Is.Empty);
        }

        [Test]
        public void AvatarLook_SaveThenLoad_RoundTripsAndKeepsCamera()
        {
            var saved = new AppSettings();
            var pose = new CameraPose { distance = 2f, fieldOfView = 30f };
            var look = new AvatarLook
            {
                lightIntensity = 2f, lightYaw = 10f, lightPitch = 20f, lightTemperature = 5000f, ambientIntensity = 1.5f,
                avatarBrightness = 2f, poseArmDown = 0.5f, poseElbowBend = 0.3f, avatarYaw = 15f,
            };
            saved.SetAvatarCamera("C:/Avatars/A.vrcaster", pose);
            saved.SetAvatarLook("C:/Avatars/A.vrcaster", look);
            saved.SetAvatarCamera("C:/Avatars/A.vrcaster", new CameraPose { distance = 3f, fieldOfView = 30f });
            _store.Save(saved);

            // 視点を記録し直しても見た目は残り、再読込後も同じ値で取り出せること
            AppSettings loaded = _store.Load();
            Assert.That(loaded.TryGetAvatarLook("c:/avatars/a.vrcaster", out AvatarLook result), Is.True);
            Assert.That(result.SameAs(look), Is.True);
            Assert.That(loaded.TryGetAvatarCamera("C:/Avatars/A.vrcaster", out CameraPose camera), Is.True);
            Assert.That(camera.distance, Is.EqualTo(3f));
        }

        [Test]
        public void AvatarLook_WithoutCamera_IsNotRecorded()
        {
            var settings = new AppSettings();

            // 視点の記録が無いアバター・壊れた値は見た目を記録しないこと
            settings.SetAvatarLook("C:/Avatars/A.vrcaster", AvatarLook.From(settings));
            settings.SetAvatarCamera("C:/Avatars/B.vrcaster", new CameraPose { distance = 2f });
            settings.SetAvatarLook("C:/Avatars/B.vrcaster", new AvatarLook { lightIntensity = float.NaN });
            Assert.That(settings.TryGetAvatarLook("C:/Avatars/A.vrcaster", out _), Is.False);
            Assert.That(settings.TryGetAvatarLook("C:/Avatars/B.vrcaster", out _), Is.False);
        }

        [Test]
        public void AvatarLook_ApplyTo_ClampsValues()
        {
            var settings = new AppSettings();

            // 範囲外の値は書き戻すときに補正されること
            new AvatarLook { lightIntensity = 100f, avatarBrightness = 0f, poseArmDown = 2f }.ApplyTo(settings);
            Assert.That(settings.lightIntensity, Is.EqualTo(AppSettings.MaxLightIntensity));
            Assert.That(settings.avatarBrightness, Is.EqualTo(AppSettings.MinAvatarBrightness));
            Assert.That(settings.poseArmDown, Is.EqualTo(1f));
        }

        [Test]
        public void RecentAvatars_ReturnsNewestFirstUpToLimit()
        {
            var settings = new AppSettings();
            var pose = new CameraPose { distance = 2f };

            // 上限より多く記録し、最初のものを使い直す
            for (int i = 0; i < AppSettings.MaxRecentAvatars + 2; i++)
            {
                settings.SetAvatarCamera($"C:/Avatars/{i}.vrcaster", pose);
            }

            settings.SetAvatarCamera("C:/Avatars/0.vrcaster", pose);

            // 新しい順に上限件数だけ返り、使い直した 0 が先頭になること
            var recent = settings.RecentAvatars();
            Assert.That(recent.Count, Is.EqualTo(AppSettings.MaxRecentAvatars));
            Assert.That(recent[0], Is.EqualTo("C:/Avatars/0.vrcaster"));
            Assert.That(recent[1], Is.EqualTo($"C:/Avatars/{AppSettings.MaxRecentAvatars + 1}.vrcaster"));
        }

        [Test]
        public void ForgetAvatar_RemovesFromRecentAndForgetsCamera()
        {
            var settings = new AppSettings();
            settings.SetAvatarCamera("C:/Avatars/A.vrcaster", new CameraPose { distance = 2f });
            settings.SetAvatarCamera("C:/Avatars/B.vrcaster", new CameraPose { distance = 2f });

            // 外したアバターは一覧にも視点の記録にも残らないこと
            settings.ForgetAvatar("c:/avatars/a.vrcaster");
            Assert.That(settings.RecentAvatars(), Is.EqualTo(new[] { "C:/Avatars/B.vrcaster" }));
            Assert.That(settings.TryGetAvatarCamera("C:/Avatars/A.vrcaster", out _), Is.False);
        }

        [Test]
        public void ResetToDefaults_KeepsAvatarCameras()
        {
            var settings = new AppSettings();
            settings.SetAvatarCamera("C:/Avatars/A.vrcaster", new CameraPose { distance = 2f, fieldOfView = 30f });

            // 全設定のリセット後もアバターごとのカメラは残ること
            settings.ResetToDefaults();
            Assert.That(settings.TryGetAvatarCamera("C:/Avatars/A.vrcaster", out _), Is.True);
        }

        [Test]
        public void Load_UnknownProcessPriority_FallsBackToNormal()
        {
            _store.Save(new AppSettings { processPriority = (ProcessPriority)99 });

            // 未知の優先度（リアルタイム等の手編集）は通常へ補正されること
            AppSettings settings = _store.Load();
            Assert.That(settings.processPriority, Is.EqualTo(ProcessPriority.Normal));
        }

        [Test]
        public void Load_UnknownGpuPreference_FallsBackToAuto()
        {
            _store.Save(new AppSettings { gpuPreference = (GpuPreference)99, gpuAdapter = null });

            // 未知の GPU の優先設定は自動へ、null の GPU 名は空文字へ補正されること
            AppSettings settings = _store.Load();
            Assert.That(settings.gpuPreference, Is.EqualTo(GpuPreference.Auto));
            Assert.That(settings.gpuAdapter, Is.Empty);
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
        public void Load_OutOfRangeAvatarBrightness_IsClamped()
        {
            _store.Save(new AppSettings { avatarBrightness = 0f });

            // 範囲外のアバターの明るさは下限へ丸められること
            AppSettings settings = _store.Load();
            Assert.That(settings.avatarBrightness, Is.EqualTo(AppSettings.MinAvatarBrightness));
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
