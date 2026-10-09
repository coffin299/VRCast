using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// 設定プリセット（.vrcastpreset）の書き出し・解析・適用と、JSON のキー単位の切り分けを検証する。
    /// </summary>
    public class SettingsPresetTests
    {
        private static readonly DateTimeOffset CreatedAt = new DateTimeOffset(2026, 10, 10, 8, 30, 0, TimeSpan.FromHours(9));

        private static string[] AllIds => SettingsPreset.Categories.Select(category => category.Id).ToArray();

        private static string Export(AppSettings settings, params string[] ids)
        {
            return SettingsPreset.Export(settings, ids.Length > 0 ? ids : AllIds, "1.2.3", CreatedAt);
        }

        private static PresetFile Parse(string text)
        {
            Assert.That(SettingsPreset.TryParse(text, out PresetFile preset, out PresetError error), Is.True,
                $"parse failed: {error}");
            return preset;
        }

        private static AppSettings Changed()
        {
            // 既定値と違う値を各カテゴリに入れた設定
            return new AppSettings
            {
                lowLoadMode = false,
                trackerMode = TrackerMode.Eco,
                backgroundColor = Color.green,
                idleBreathing = 0.5f,
                micGain = 2f,
                trackingMirror = false,
                spoutEnabled = true,
                resetHotkeysInBackground = false,
                uiScale = 1.5f,
                lightIntensity = 2f,
                poseArmDown = 0.4f,
                expressionSmile = "Joy \"big\"",
                thresholdSmile = 0.5f,
                windowWidth = 999,
                microphoneDevice = "My Mic",
                trackingPort = 12000,
            };
        }

        [Test]
        public void EveryField_IsInExactlyOneCategoryOrExcluded()
        {
            // AppSettings の保存されるフィールドを全て集める
            var fields = typeof(AppSettings)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(field => !field.IsNotSerialized)
                .Select(field => field.Name)
                .ToList();

            // それぞれがちょうど 1 か所（いずれかのカテゴリか除外）に入っていること（設定の追加時に割り当て忘れを防ぐ）
            foreach (string field in fields)
            {
                int count = SettingsPreset.Categories.Count(category => category.Fields.Contains(field))
                    + (SettingsPreset.Excluded.Contains(field) ? 1 : 0);
                Assert.That(count, Is.EqualTo(1), $"{field} must be in exactly one preset category or Excluded");
            }

            // カテゴリ・除外に、存在しないフィールド名が無いこと
            foreach (string name in SettingsPreset.Categories.SelectMany(category => category.Fields)
                         .Concat(SettingsPreset.Excluded))
            {
                Assert.That(fields, Does.Contain(name));
            }
        }

        [Test]
        public void ExportThenApply_RoundTripsValuesAndKeepsExcluded()
        {
            string text = Export(Changed());

            // 既定の設定へ全カテゴリを反映する
            var target = new AppSettings { windowWidth = 640, microphoneDevice = "Other", trackingPort = 13000 };
            Assert.That(SettingsPreset.Apply(Parse(text), AllIds, target), Is.True);

            // 各カテゴリの値が戻ること
            Assert.That(target.lowLoadMode, Is.False);
            Assert.That(target.trackerMode, Is.EqualTo(TrackerMode.Eco));
            Assert.That(target.backgroundColor, Is.EqualTo(Color.green));
            Assert.That(target.idleBreathing, Is.EqualTo(0.5f));
            Assert.That(target.micGain, Is.EqualTo(2f));
            Assert.That(target.trackingMirror, Is.False);
            Assert.That(target.spoutEnabled, Is.True);
            Assert.That(target.resetHotkeysInBackground, Is.False);
            Assert.That(target.uiScale, Is.EqualTo(1.5f));
            Assert.That(target.lightIntensity, Is.EqualTo(2f));
            Assert.That(target.poseArmDown, Is.EqualTo(0.4f));
            Assert.That(target.expressionSmile, Is.EqualTo("Joy \"big\""));
            Assert.That(target.thresholdSmile, Is.EqualTo(0.5f));

            // 除外した値（ウィンドウ・PC 固有）は書き出されず、反映先の値のままであること
            Assert.That(text, Does.Not.Contain("windowWidth"));
            Assert.That(text, Does.Not.Contain("microphoneDevice"));
            Assert.That(text, Does.Not.Contain("avatarCameras"));
            Assert.That(target.windowWidth, Is.EqualTo(640));
            Assert.That(target.microphoneDevice, Is.EqualTo("Other"));
            Assert.That(target.trackingPort, Is.EqualTo(13000));
        }

        [Test]
        public void Export_WritesHeaderAndOnlySelectedCategories()
        {
            string text = Export(Changed(), "performance");
            PresetFile preset = Parse(text);

            // 見出しが読め、選んだカテゴリだけが入っていること
            Assert.That(preset.FormatVersion, Is.EqualTo(SettingsPreset.CurrentFormatVersion));
            Assert.That(preset.AppVersion, Is.EqualTo("1.2.3"));
            Assert.That(preset.CreatedAt, Is.EqualTo("2026-10-10T08:30:00+09:00"));
            Assert.That(preset.Categories.Select(category => category.Id), Is.EqualTo(new[] { "performance" }));
            Assert.That(text, Does.Not.Contain("lightIntensity"));
        }

        [Test]
        public void Apply_UnselectedCategories_KeepCurrentValues()
        {
            PresetFile preset = Parse(Export(Changed()));
            var target = new AppSettings();
            var defaults = new AppSettings();

            // 動作最適化だけを反映する
            Assert.That(SettingsPreset.Apply(preset, new[] { "performance" }, target), Is.True);

            // 選んだカテゴリは変わり、ほかは既定のままであること
            Assert.That(target.trackerMode, Is.EqualTo(TrackerMode.Eco));
            Assert.That(target.uiScale, Is.EqualTo(defaults.uiScale));
            Assert.That(target.lightIntensity, Is.EqualTo(defaults.lightIntensity));
        }

        [Test]
        public void Apply_WithoutAvatarRelated_KeepsLookAndExpressionMapping()
        {
            PresetFile preset = Parse(Export(Changed()));
            var target = new AppSettings();
            var defaults = new AppSettings();

            // アバター関連以外を全て反映する
            string[] ids = SettingsPreset.Categories.Where(category => !category.IsAvatarRelated)
                .Select(category => category.Id).ToArray();
            Assert.That(SettingsPreset.Apply(preset, ids, target), Is.True);

            // ライト・ポーズ・表情の割り当てとしきい値は変わらないこと
            Assert.That(target.lightIntensity, Is.EqualTo(defaults.lightIntensity));
            Assert.That(target.poseArmDown, Is.EqualTo(defaults.poseArmDown));
            Assert.That(target.expressionSmile, Is.EqualTo(defaults.expressionSmile));
            Assert.That(target.thresholdSmile, Is.EqualTo(defaults.thresholdSmile));
            Assert.That(target.uiScale, Is.EqualTo(1.5f));
        }

        [Test]
        public void Parse_HandEditedFile_IgnoresUnknownMissingAndForeignKeys()
        {
            // 手で編集したファイル: 知らないカテゴリ・知らないキー・別カテゴリのキー・除外のキー、項目を消したブロック
            const string text = @"{
                ""format"": ""VRCastPreset"",
                ""formatVersion"": 1,
                ""futureHeader"": { ""a"": [1, 2, ""}""] },
                ""categories"": {
                    ""performance"": {
                        ""trackerMode"": 0,
                        ""futureSetting"": true,
                        ""uiScale"": 2.0,
                        ""windowWidth"": 64
                    },
                    ""futureCategory"": { ""x"": 1 }
                }
            }";
            PresetFile preset = Parse(text);
            Assert.That(preset.Categories.Select(category => category.Id), Is.EqualTo(new[] { "performance" }));

            var target = new AppSettings { lowLoadMode = false, windowWidth = 800 };
            Assert.That(SettingsPreset.Apply(preset, AllIds, target), Is.True);

            // 書いてある項目だけが変わり、消した項目（lowLoadMode）は今の値のまま、ほかのカテゴリ・除外のキーは反映されないこと
            Assert.That(target.trackerMode, Is.EqualTo(TrackerMode.Smooth));
            Assert.That(target.lowLoadMode, Is.False);
            Assert.That(target.uiScale, Is.EqualTo(new AppSettings().uiScale));
            Assert.That(target.windowWidth, Is.EqualTo(800));
        }

        [Test]
        public void Parse_NewerFormat_IsReadableAndFlagged()
        {
            string text = Export(Changed(), "performance").Replace("\"formatVersion\": 1", "\"formatVersion\": 99");

            // 新しい形式でも読め、新しい形式であることがわかること
            PresetFile preset = Parse(text);
            Assert.That(preset.IsNewerFormat, Is.True);
        }

        [TestCase("", PresetError.Malformed)]
        [TestCase("{ not json", PresetError.Malformed)]
        [TestCase("[1, 2]", PresetError.Malformed)]
        [TestCase("{\"windowWidth\": 1}", PresetError.NotPreset)]
        [TestCase("{\"format\": \"Other\", \"formatVersion\": 1, \"categories\": {}}", PresetError.NotPreset)]
        [TestCase("{\"format\": \"VRCastPreset\", \"formatVersion\": 1}", PresetError.Malformed)]
        [TestCase("{\"format\": \"VRCastPreset\", \"formatVersion\": 1, \"categories\": {\"performance\": [1]}}",
            PresetError.Malformed)]
        [TestCase("{\"format\": \"VRCastPreset\", \"formatVersion\": 1, \"categories\": {\"unknown\": {\"a\": 1}}}",
            PresetError.Empty)]
        public void Parse_InvalidText_IsRejected(string text, PresetError expected)
        {
            Assert.That(SettingsPreset.TryParse(text, out PresetFile preset, out PresetError error), Is.False);
            Assert.That(preset, Is.Null);
            Assert.That(error, Is.EqualTo(expected));
        }

        [Test]
        public void Parse_TooLargeText_IsRejected()
        {
            string text = new string(' ', (int)SettingsPreset.MaxFileBytes + 1);
            Assert.That(SettingsPreset.TryParse(text, out _, out PresetError error), Is.False);
            Assert.That(error, Is.EqualTo(PresetError.TooLarge));
        }

        [Test]
        public void Apply_OutOfRangeValues_AreClamped()
        {
            const string text = "{\"format\": \"VRCastPreset\", \"formatVersion\": 1, \"categories\": " +
                "{\"interface\": {\"uiScale\": 100}, \"avatarLook\": {\"lightIntensity\": -5}}}";
            var target = new AppSettings();

            // 範囲外の値は補正されること
            Assert.That(SettingsPreset.Apply(Parse(text), AllIds, target), Is.True);
            Assert.That(target.uiScale, Is.EqualTo(AppSettings.MaxUiScale));
            Assert.That(target.lightIntensity, Is.EqualTo(0f));
        }

        [Test]
        public void Apply_ThemeOnly_DefaultBackgroundFollowsTheme()
        {
            const string text = "{\"format\": \"VRCastPreset\", \"formatVersion\": 1, \"categories\": " +
                "{\"interface\": {\"darkMode\": true}}}";
            var target = new AppSettings();

            // 既定色の背景は、テーマだけを読み込んだときに新しいテーマの既定色になること
            Assert.That(SettingsPreset.Apply(Parse(text), AllIds, target), Is.True);
            Assert.That(target.darkMode, Is.True);
            Assert.That(target.backgroundColor, Is.EqualTo(AppSettings.DarkBackgroundColor));
        }

        [Test]
        public void Apply_NothingSelected_ReturnsFalseAndKeepsSettings()
        {
            PresetFile preset = Parse(Export(Changed()));
            var target = new AppSettings();
            Assert.That(SettingsPreset.Apply(preset, Array.Empty<string>(), target), Is.False);
            Assert.That(target.uiScale, Is.EqualTo(new AppSettings().uiScale));
        }

        [Test]
        public void JsonObjectText_SplitsTopLevelWithEscapesAndNesting()
        {
            const string json = "{ \"a\" : \"x\\\"},{\" , \"b\": {\"c\": [1, {\"d\": \"]\"}]}, \"e\": -1.5e3, \"f\": null }";
            Assert.That(JsonObjectText.TryParseObject(json, out List<KeyValuePair<string, string>> members), Is.True);

            // 文字列内の括弧・エスケープや入れ子に惑わされず、一番外側の 4 件に分かれること
            Assert.That(members.Select(member => member.Key), Is.EqualTo(new[] { "a", "b", "e", "f" }));
            Assert.That(members[0].Value, Is.EqualTo("\"x\\\"},{\""));
            Assert.That(members[1].Value, Is.EqualTo("{\"c\": [1, {\"d\": \"]\"}]}"));
            Assert.That(members[2].Value, Is.EqualTo("-1.5e3"));
            Assert.That(JsonObjectText.TryReadString(members[0].Value, out string a), Is.True);
            Assert.That(a, Is.EqualTo("x\"},{"));
        }

        [TestCase("{\"a\": 1,}")]
        [TestCase("{\"a\": [1}")]
        [TestCase("{\"a\" 1}")]
        [TestCase("{\"a\": 1} trailing")]
        [TestCase("{\"a\": \"unterminated}")]
        public void JsonObjectText_Malformed_ReturnsFalse(string json)
        {
            Assert.That(JsonObjectText.TryParseObject(json, out _), Is.False);
        }

        [Test]
        public void JsonObjectText_BuildThenParse_RoundTrips()
        {
            var members = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("k\"1", "true"),
                new KeyValuePair<string, string>("k2", JsonObjectText.Quote("line\nbreak")),
            };

            // 結合したテキストを切り分けると元のキー・値に戻ること
            Assert.That(JsonObjectText.TryParseObject(JsonObjectText.Build(members, 1), out var parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(members));
            Assert.That(JsonObjectText.Build(new List<KeyValuePair<string, string>>()), Is.EqualTo("{}"));
        }
    }
}
