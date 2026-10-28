using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace VRCast.Core
{
    /// <summary>
    /// 設定プリセットの 1 カテゴリ（ID・アバター関連か・含める AppSettings のフィールド名）。
    /// </summary>
    public sealed class PresetCategory
    {
        public string Id { get; }

        // アバターごとに合わせ直す値か（読み書きのときにまとめて外せるようにする）
        public bool IsAvatarRelated { get; }

        public IReadOnlyList<string> Fields { get; }

        public PresetCategory(string id, bool isAvatarRelated, params string[] fields)
        {
            Id = id;
            IsAvatarRelated = isAvatarRelated;
            Fields = fields;
        }
    }

    /// <summary>
    /// 読み込みの失敗理由。
    /// </summary>
    public enum PresetError
    {
        None,
        TooLarge,
        NotPreset,
        Malformed,
        Empty,
    }

    /// <summary>
    /// 書き出すファイルの形式（YAML は手で編集しやすいので推奨）。
    /// </summary>
    public enum PresetFormat
    {
        Yaml,
        Json,
    }

    /// <summary>
    /// 解析済みのプリセット。適用するまで設定は変わらない。
    /// </summary>
    public sealed class PresetFile
    {
        // カテゴリ ID ごとの、そのカテゴリに属するキーと値（JSON のテキスト）
        private readonly Dictionary<string, List<KeyValuePair<string, string>>> _blocks;

        public int FormatVersion { get; }
        public string AppVersion { get; }
        public string CreatedAt { get; }

        // 入っているカテゴリ（既知のもののみ、SettingsPreset.Categories の並び）
        public IReadOnlyList<PresetCategory> Categories { get; }

        // 対応版より新しい形式で作られたか（読める項目だけを読む）
        public bool IsNewerFormat => FormatVersion > SettingsPreset.CurrentFormatVersion;

        internal PresetFile(int formatVersion, string appVersion, string createdAt,
            Dictionary<string, List<KeyValuePair<string, string>>> blocks, IReadOnlyList<PresetCategory> categories)
        {
            FormatVersion = formatVersion;
            AppVersion = appVersion;
            CreatedAt = createdAt;
            _blocks = blocks;
            Categories = categories;
        }

        internal IEnumerable<KeyValuePair<string, string>> MembersOf(string categoryId)
        {
            return _blocks.TryGetValue(categoryId, out List<KeyValuePair<string, string>> members)
                ? members
                : (IEnumerable<KeyValuePair<string, string>>)Array.Empty<KeyValuePair<string, string>>();
        }
    }

    /// <summary>
    /// 設定プリセット（.yaml / .json）の書き出し・解析・適用。中身は人が読めるテキストで、設定はカテゴリごとのブロックに分ける。
    /// YAML は JSON と同じ構造（SimpleYaml で相互に変換）で、読み込みは拡張子でなく中身で判定する（{ で始まれば JSON）。
    /// 将来の版との互換のため、知らないカテゴリ・キーは無視し、無いキーは現在の値のまま残す。
    /// アバターごとの記録（シェイプキーの上限・視点・表情のキー等）と PC 固有の値は含めない。
    /// </summary>
    public static class SettingsPreset
    {
        // 書き出す拡張子と、以前の版が書き出していた拡張子（中身は JSON と同じ形式なので読み込みだけ受け付ける）
        public const string YamlExtension = ".yaml";
        public const string JsonExtension = ".json";
        public const string LegacyExtension = ".vrcastpreset";

        // 読み込みで受け付ける拡張子（ダイアログの絞り込み・ドロップの振り分け）
        public static readonly string[] ImportExtensions = { YamlExtension, ".yml", JsonExtension, LegacyExtension };

        // 書き出すときの最初のファイル名（拡張子を除く）
        public const string DefaultFileStem = "VRCast-preset";

        // YAML の先頭に付ける説明（# の行はコメント。日本語、英語の順）
        private const string YamlHeader =
            "# VRCast 設定プリセット / VRCast settings preset\n" +
            "#\n" +
            "# 「#」で始まる行はコメントです。設定の行を消すと、読み込むときその設定は今の値のままになります。\n" +
            "# 文字列は引用符なしで書けます。数字や true / false に見える文字列は \"...\" で囲んでください。\n" +
            "# 字下げは空白で書いてください（タブは使えません）。\n" +
            "#\n" +
            "# Lines starting with \"#\" are comments. Delete a setting's line to keep its current value when loading.\n" +
            "# Text can be written without quotes. Put text that looks like a number or true / false in \"...\".\n" +
            "# Indent with spaces (tabs are not allowed).\n" +
            "\n";

        // YAML のカテゴリの前に付けるコメント（日本語 / 英語）
        private static readonly Dictionary<string, string> CategoryComments = new Dictionary<string, string>
        {
            ["performance"] = "動作最適化（軽量モード・優先度・CPU・GPU・トラッカーの動作） / Performance (low load, priority, CPU, GPU, tracker mode)",
            ["display"] = "カメラの固定 / Camera lock",
            ["graphics"] = "グラフィック（背景・アンチエイリアス・ライティング・保存した見た目・輪郭線） / Graphics (background, anti-aliasing, lighting, saved looks, outline)",
            ["motion"] = "待機モーション・揺れもの・小物のアニメーション / Idle motion, physics and prop animations",
            ["face"] = "まばたき・口パク / Blinking and lip sync",
            ["tracking"] = "トラッキング / Tracking",
            ["output"] = "出力（仮想カメラ・Spout2） / Output (virtual camera, Spout2)",
            ["shortcuts"] = "ショートカットキー・OSC / HTTP / Shortcut keys and OSC / HTTP",
            ["interface"] = "パネル（言語・大きさ・不透明度・テーマ・アップデート・ログ） / Panel (language, size, opacity, theme, updates, logs)",
            ["avatarLook"] = "アバター関連: ポーズ・向き / Avatar-related: pose and direction",
            ["expressionMapping"] = "アバター関連: 表情反映の割り当て・しきい値 / Avatar-related: expression mapping and thresholds",
        };

        // グラフィックタブのプリセットで読み込むカテゴリ（書き出しでは他カテゴリも選べる）
        public const string GraphicsCategoryId = "graphics";
        public const string GraphicsFileStem = "VRCast-graphics";

        // 形式名・形式のバージョン（キー名の変更など互換性のない変更をしたら上げ、読み込み時に読み替える）
        public const string FormatName = "VRCastPreset";
        public const int CurrentFormatVersion = 1;

        // 読み込むファイルの大きさの上限（バイト。設定だけなら数 KB）
        public const long MaxFileBytes = 1024 * 1024;

        // 一番外側のキー
        private const string FormatKey = "format";
        private const string FormatVersionKey = "formatVersion";
        private const string AppVersionKey = "appVersion";
        private const string CreatedAtKey = "createdAt";
        private const string CategoriesKey = "categories";

        // カテゴリの並び（書き出し・画面の表示順）。設定を追加したら必ずどれかのカテゴリか Excluded に入れる（テストで検査）
        public static readonly IReadOnlyList<PresetCategory> Categories = new[]
        {
            new PresetCategory("performance", false,
                nameof(AppSettings.lowLoadMode), nameof(AppSettings.vSyncMode), nameof(AppSettings.targetFrameRate),
                nameof(AppSettings.showPerformanceStats), nameof(AppSettings.trackerMode),
                nameof(AppSettings.trackingFps),
                nameof(AppSettings.processPriority), nameof(AppSettings.avoidCacheCcd), nameof(AppSettings.hybridCores),
                nameof(AppSettings.gpuPreference)),
            new PresetCategory("display", false,
                nameof(AppSettings.cameraLocked), nameof(AppSettings.cameraZoomInsteadOfEntering)),
            new PresetCategory("graphics", false,
                nameof(AppSettings.transparentBackground), nameof(AppSettings.backgroundColor),
                nameof(AppSettings.lightingSystem), nameof(AppSettings.antiAliasingMode),
                nameof(AppSettings.antiAliasing), nameof(AppSettings.supersample),
                nameof(AppSettings.lightIntensity), nameof(AppSettings.lightYaw), nameof(AppSettings.lightPitch),
                nameof(AppSettings.lightTemperature), nameof(AppSettings.ambientIntensity),
                nameof(AppSettings.fillIntensity), nameof(AppSettings.fillYaw), nameof(AppSettings.fillPitch),
                nameof(AppSettings.fillTemperature),
                nameof(AppSettings.rimIntensity), nameof(AppSettings.rimYaw), nameof(AppSettings.rimPitch),
                nameof(AppSettings.rimTemperature),
                nameof(AppSettings.iblIntensity), nameof(AppSettings.iblPreset),
                nameof(AppSettings.reflectionStrength),
                nameof(AppSettings.softShadows), nameof(AppSettings.shadowStrength), nameof(AppSettings.shadowSoftness),
                nameof(AppSettings.bloomEnabled), nameof(AppSettings.bloomIntensity), nameof(AppSettings.bloomThreshold),
                nameof(AppSettings.tonemapEnabled), nameof(AppSettings.tonemapStrength),
                nameof(AppSettings.colorGradeEnabled), nameof(AppSettings.exposure), nameof(AppSettings.contrast),
                nameof(AppSettings.saturation), nameof(AppSettings.colorTemperature), nameof(AppSettings.colorTint),
                nameof(AppSettings.ssaoEnabled), nameof(AppSettings.ssaoIntensity), nameof(AppSettings.ssaoRadius),
                nameof(AppSettings.screenRimEnabled), nameof(AppSettings.screenRimIntensity),
                nameof(AppSettings.screenRimPower), nameof(AppSettings.screenRimHue),
                nameof(AppSettings.sharpenEnabled), nameof(AppSettings.sharpenAmount),
                nameof(AppSettings.vignetteEnabled), nameof(AppSettings.vignetteIntensity),
                nameof(AppSettings.avatarBrightness), nameof(AppSettings.outlineWidth),
                nameof(AppSettings.lightingPresets)),
            new PresetCategory("motion", false,
                nameof(AppSettings.idleMotionEnabled), nameof(AppSettings.idleBreathing), nameof(AppSettings.idleSway),
                nameof(AppSettings.idleHeadMotion), nameof(AppSettings.idleMotionSpeed), nameof(AppSettings.physicsEnabled),
                nameof(AppSettings.propAnimationsEnabled)),
            new PresetCategory("face", false,
                nameof(AppSettings.autoBlink), nameof(AppSettings.blinkCloseSeconds), nameof(AppSettings.blinkHoldSeconds),
                nameof(AppSettings.blinkOpenSeconds), nameof(AppSettings.blinkIntervalMin),
                nameof(AppSettings.blinkIntervalMax),
                nameof(AppSettings.blinkPausedByExpression), nameof(AppSettings.lipSyncEnabled),
                nameof(AppSettings.micGain), nameof(AppSettings.micThreshold), nameof(AppSettings.lipSyncVowels),
                nameof(AppSettings.lipSyncVoiceScale), nameof(AppSettings.lipSyncPausedByExpression),
                nameof(AppSettings.trackingMouthPausedByExpression), nameof(AppSettings.showHiddenExpressions)),
            new PresetCategory("tracking", false,
                nameof(AppSettings.trackingEnabled), nameof(AppSettings.trackingStopWithoutFace),
                nameof(AppSettings.trackingSource), nameof(AppSettings.trackingMirror),
                nameof(AppSettings.trackingSwapFaceSides), nameof(AppSettings.trackingBodyLean),
                nameof(AppSettings.trackingBodyMotion), nameof(AppSettings.trackingGaze), nameof(AppSettings.trackingBlink),
                nameof(AppSettings.trackingEyeOpenOffset), nameof(AppSettings.trackingBlinkSync),
                nameof(AppSettings.trackingSmoothing),
                nameof(AppSettings.trackingHands), nameof(AppSettings.trackingArmMatchAvatar),
                nameof(AppSettings.trackingKeepHandsInFront), nameof(AppSettings.trackingTorso),
                nameof(AppSettings.trackingTorsoLockTwist), nameof(AppSettings.trackingLookAtCamera),
                nameof(AppSettings.trackingVmcBody), nameof(AppSettings.trackingVmcGroundFeet),
                nameof(AppSettings.trackingPlantFeet),
                nameof(AppSettings.trackingPerfectSync),
                nameof(AppSettings.trackingPerfectSyncAnyShape), nameof(AppSettings.trackingPerfectSyncNaming),
                nameof(AppSettings.trackingJawOpenDeadzone),
                nameof(AppSettings.trackingJawOpenGain), nameof(AppSettings.trackingPerfectSyncBoost),
                nameof(AppSettings.trackingBoostPucker), nameof(AppSettings.trackingBoostMouth),
                nameof(AppSettings.trackingBoostCheekPuff),
                nameof(AppSettings.trackingBoostLips), nameof(AppSettings.trackingCheekPuffStart),
                nameof(AppSettings.trackingCheekPuffFull),                 nameof(AppSettings.trackingExpressions),
                nameof(AppSettings.trackingExpressionsIgnoreLimits), nameof(AppSettings.trackingExpressionThreshold),
                nameof(AppSettings.trackingTiltCorrectionX), nameof(AppSettings.trackingTiltCorrectionY),
                nameof(AppSettings.trackingTiltCorrectionZ), nameof(AppSettings.trackingTiltCorrectionW)),
            new PresetCategory("output", false,
                nameof(AppSettings.virtualCameraEnabled), nameof(AppSettings.virtualCameraMediaFoundation),
                nameof(AppSettings.spoutEnabled)),
            new PresetCategory("shortcuts", false,
                nameof(AppSettings.resetHotkeys), nameof(AppSettings.resetHotkeysInBackground),
                nameof(AppSettings.expressionHotkeysInBackground), nameof(AppSettings.remoteControlEnabled),
                nameof(AppSettings.autoResetEnabled), nameof(AppSettings.autoResetIntervalMinutes),
                nameof(AppSettings.autoResetHead), nameof(AppSettings.autoResetGaze),
                nameof(AppSettings.autoResetExpression)),
            new PresetCategory("interface", false,
                nameof(AppSettings.uiLanguage), nameof(AppSettings.uiScale), nameof(AppSettings.uiOpacity),
                nameof(AppSettings.darkMode),
                nameof(AppSettings.checkForUpdates), nameof(AppSettings.detailedLogging)),
            new PresetCategory("avatarLook", true,
                nameof(AppSettings.poseArmDown),
                nameof(AppSettings.poseElbowBend), nameof(AppSettings.poseArmSpread),
                nameof(AppSettings.poseArmForwardLeft), nameof(AppSettings.poseArmForwardRight),
                nameof(AppSettings.poseArmTwistLeft), nameof(AppSettings.poseArmTwistRight), nameof(AppSettings.avatarYaw)),
            new PresetCategory("expressionMapping", true,
                nameof(AppSettings.expressionSmile), nameof(AppSettings.expressionSurprise),
                nameof(AppSettings.expressionAngry), nameof(AppSettings.expressionSad), nameof(AppSettings.expressionWink),
                nameof(AppSettings.expressionSquint), nameof(AppSettings.expressionPout),
                nameof(AppSettings.thresholdSmile), nameof(AppSettings.thresholdSurprise),
                nameof(AppSettings.thresholdAngry), nameof(AppSettings.thresholdSad), nameof(AppSettings.thresholdWink),
                nameof(AppSettings.thresholdSquint), nameof(AppSettings.thresholdPout)),
        };

        // プリセットに含めないフィールド（形式・ウィンドウ・アバターごとの記録と、PC ごとに違うデバイス名・パス・ポート、
        // 本人の顔に合わせた表情のキャリブレーション）
        public static readonly IReadOnlyCollection<string> Excluded = new HashSet<string>
        {
            nameof(AppSettings.version),
            nameof(AppSettings.windowWidth),
            nameof(AppSettings.windowHeight),
            nameof(AppSettings.lastAvatarPath),
            nameof(AppSettings.avatarCameras),
            nameof(AppSettings.microphoneDevice),
            nameof(AppSettings.trackerPath),
            nameof(AppSettings.screenshotFolder),
            nameof(AppSettings.trackerCamera),
            nameof(AppSettings.trackerCameraIndex),
            nameof(AppSettings.gpuAdapter),
            nameof(AppSettings.trackingPort),
            nameof(AppSettings.vmcPort),
            nameof(AppSettings.iFacialMocapAddress),
            nameof(AppSettings.remoteOscPort),
            nameof(AppSettings.remoteHttpPort),
            nameof(AppSettings.expressionCalibrations),
            nameof(AppSettings.mascotLlmUrl),
            nameof(AppSettings.mascotLlmModel),
            nameof(AppSettings.mascotSystemPrompt),
            nameof(AppSettings.mascotTtsEngine),
            nameof(AppSettings.mascotTtsUrl),
            nameof(AppSettings.mascotTtsSpeaker),
            nameof(AppSettings.mascotStandClip),
            nameof(AppSettings.mascotWindowX),
            nameof(AppSettings.mascotWindowY),
            nameof(AppSettings.mascotScale),
            nameof(AppSettings.mascotVoiceInput),
        };

        /// <summary>
        /// 書き出す形式の拡張子。
        /// </summary>
        public static string ExtensionOf(PresetFormat format)
        {
            return format == PresetFormat.Yaml ? YamlExtension : JsonExtension;
        }

        /// <summary>
        /// パスが読み込める拡張子（.yaml・.yml・.json・以前の .vrcastpreset）なら true。
        /// </summary>
        public static bool HasImportExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string extension = Path.GetExtension(path);
            return Array.Exists(ImportExtensions,
                candidate => string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// ID からカテゴリを探す（知らない ID は null）。
        /// </summary>
        public static PresetCategory Find(string id)
        {
            foreach (PresetCategory category in Categories)
            {
                if (category.Id == id)
                {
                    return category;
                }
            }

            return null;
        }

        /// <summary>
        /// フィールド名を所有するカテゴリ（除外・未知は null）。
        /// </summary>
        public static PresetCategory CategoryOfField(string fieldName)
        {
            foreach (PresetCategory category in Categories)
            {
                if (Contains(category.Fields, fieldName))
                {
                    return category;
                }
            }

            return null;
        }

        /// <summary>
        /// 選んだカテゴリの設定をプリセットのテキストにする（カテゴリは Categories の並びで書く）。
        /// </summary>
        public static string Export(AppSettings settings, ICollection<string> categoryIds, string appVersion,
            DateTimeOffset createdAt, PresetFormat format = PresetFormat.Json)
        {
            string json = ExportJson(settings, categoryIds, appVersion, createdAt);
            if (format == PresetFormat.Json)
            {
                return json;
            }

            // 書き出した JSON は必ず変換できる形
            SimpleYaml.TryFromJson(json, out string yaml);
            return YamlHeader + AddCategoryComments(yaml);
        }

        private static string AddCategoryComments(string yaml)
        {
            // categories の直下（字下げ 2 段）のカテゴリの行の前に、空行とカテゴリの説明を入れる
            var builder = new StringBuilder(yaml.Length + 1024);
            bool inCategories = false;
            foreach (string line in yaml.Split('\n'))
            {
                if (line == CategoriesKey + ":")
                {
                    inCategories = true;
                }
                else if (inCategories && line.Length > 3 && line[0] == ' ' && line[1] == ' ' && line[2] != ' '
                         && line.EndsWith(":", StringComparison.Ordinal)
                         && CategoryComments.TryGetValue(line.Substring(2, line.Length - 3), out string comment))
                {
                    builder.Append('\n').Append("  # ").Append(comment).Append('\n');
                }

                builder.Append(line).Append('\n');
            }

            // Split で増えた末尾の改行を 1 つに戻す
            return builder.ToString().TrimEnd('\n') + "\n";
        }

        private static string ExportJson(AppSettings settings, ICollection<string> categoryIds, string appVersion,
            DateTimeOffset createdAt)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            // 全設定を JSON にしてキーで引けるようにする（値の書式は JsonUtility に任せる）
            var values = new Dictionary<string, string>();
            JsonObjectText.TryParseObject(JsonUtility.ToJson(settings), out List<KeyValuePair<string, string>> all);
            foreach (KeyValuePair<string, string> member in all)
            {
                values[member.Key] = member.Value;
            }

            // 選んだカテゴリごとに、そのカテゴリのキーだけのブロックを作る
            var blocks = new List<KeyValuePair<string, string>>();
            foreach (PresetCategory category in Categories)
            {
                if (!categoryIds.Contains(category.Id))
                {
                    continue;
                }

                var members = new List<KeyValuePair<string, string>>();
                foreach (string field in category.Fields)
                {
                    if (values.TryGetValue(field, out string value))
                    {
                        members.Add(new KeyValuePair<string, string>(field, value));
                    }
                }

                blocks.Add(new KeyValuePair<string, string>(category.Id, JsonObjectText.Build(members, 2)));
            }

            // 見出し（形式・版・作成日時）とカテゴリのブロック
            var root = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(FormatKey, JsonObjectText.Quote(FormatName)),
                new KeyValuePair<string, string>(FormatVersionKey,
                    CurrentFormatVersion.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>(AppVersionKey, JsonObjectText.Quote(appVersion ?? string.Empty)),
                new KeyValuePair<string, string>(CreatedAtKey,
                    JsonObjectText.Quote(createdAt.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture))),
                new KeyValuePair<string, string>(CategoriesKey, JsonObjectText.Build(blocks, 1)),
            };
            return JsonObjectText.Build(root) + "\n";
        }

        /// <summary>
        /// プリセットのテキストを解析する（設定は変えない）。VRCast のプリセットでない・壊れている・反映できる項目が無いなら false。
        /// </summary>
        public static bool TryParse(string text, out PresetFile preset, out PresetError error)
        {
            preset = null;

            // 大きすぎるテキストは読まない（文字数で概算）
            if (text != null && text.Length > MaxFileBytes)
            {
                error = PresetError.TooLarge;
                return false;
            }

            // { で始まらなければ YAML として JSON に直す
            string json = text;
            if (text != null && !LooksLikeJson(text) && !SimpleYaml.TryToJson(text, out json))
            {
                error = PresetError.Malformed;
                return false;
            }

            // 一番外側がオブジェクトでなければ壊れている
            if (!JsonObjectText.TryParseObject(json, out List<KeyValuePair<string, string>> root))
            {
                error = PresetError.Malformed;
                return false;
            }

            // 見出しを読む（知らないキーは無視）
            string format = null;
            int formatVersion = 0;
            string appVersion = string.Empty;
            string createdAt = string.Empty;
            string categoriesText = null;
            foreach (KeyValuePair<string, string> member in root)
            {
                switch (member.Key)
                {
                    case FormatKey:
                        JsonObjectText.TryReadString(member.Value, out format);
                        break;
                    case FormatVersionKey:
                        int.TryParse(member.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out formatVersion);
                        break;
                    case AppVersionKey:
                        JsonObjectText.TryReadString(member.Value, out appVersion);
                        break;
                    case CreatedAtKey:
                        JsonObjectText.TryReadString(member.Value, out createdAt);
                        break;
                    case CategoriesKey:
                        categoriesText = member.Value;
                        break;
                }
            }

            // 形式名と版（1 以上）が無ければ VRCast のプリセットではない
            if (format != FormatName || formatVersion < 1)
            {
                error = PresetError.NotPreset;
                return false;
            }

            // カテゴリの一覧はオブジェクトでなければ壊れている
            if (categoriesText == null
                || !JsonObjectText.TryParseObject(categoriesText, out List<KeyValuePair<string, string>> blockTexts))
            {
                error = PresetError.Malformed;
                return false;
            }

            // 各キーを「いま所有しているカテゴリ」へ振り分ける（以前 display / avatarLook にあったグラフィック項目も graphics へ移す）
            var blocks = new Dictionary<string, List<KeyValuePair<string, string>>>();
            foreach (KeyValuePair<string, string> block in blockTexts)
            {
                // 中身を全部消したカテゴリ（YAML では値が null になる）は飛ばす
                if (block.Value.Trim() == "null")
                {
                    continue;
                }

                // ブロックが壊れていたらファイル全体を壊れたものとして扱う（一部だけ反映して食い違うのを避ける）
                if (!JsonObjectText.TryParseObject(block.Value, out List<KeyValuePair<string, string>> members))
                {
                    error = PresetError.Malformed;
                    return false;
                }

                foreach (KeyValuePair<string, string> member in members)
                {
                    PresetCategory owner = CategoryOfField(member.Key);
                    if (owner == null)
                    {
                        continue;
                    }

                    if (!blocks.TryGetValue(owner.Id, out List<KeyValuePair<string, string>> accepted))
                    {
                        accepted = new List<KeyValuePair<string, string>>();
                        blocks[owner.Id] = accepted;
                    }

                    // 同じキーが複数ブロックにあれば後勝ち（新旧の混在ファイル向け）
                    int existing = accepted.FindIndex(item => item.Key == member.Key);
                    if (existing >= 0)
                    {
                        accepted[existing] = member;
                    }
                    else
                    {
                        accepted.Add(member);
                    }
                }
            }

            // 入っているカテゴリを決まった並びで並べる
            var categories = new List<PresetCategory>();
            foreach (PresetCategory category in Categories)
            {
                if (blocks.ContainsKey(category.Id))
                {
                    categories.Add(category);
                }
            }

            // 反映できる項目が 1 つも無い
            if (categories.Count == 0)
            {
                error = PresetError.Empty;
                return false;
            }

            preset = new PresetFile(formatVersion, appVersion ?? string.Empty, createdAt ?? string.Empty, blocks,
                categories);
            error = PresetError.None;
            return true;
        }

        /// <summary>
        /// 選んだカテゴリの値を設定へ反映し、範囲外の値を補正する（各機能への反映は呼び出し側）。
        /// 値の型が合わない等で反映できなければ設定を変えずに false。
        /// </summary>
        public static bool Apply(PresetFile preset, ICollection<string> categoryIds, AppSettings target)
        {
            if (preset == null)
            {
                throw new ArgumentNullException(nameof(preset));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            // 選んだカテゴリのキーを 1 つのオブジェクトにまとめる
            var members = new List<KeyValuePair<string, string>>();
            foreach (PresetCategory category in preset.Categories)
            {
                if (categoryIds.Contains(category.Id))
                {
                    members.AddRange(preset.MembersOf(category.Id));
                }
            }

            // 選んだものが無ければ何もしない
            if (members.Count == 0)
            {
                return false;
            }

            string json = JsonObjectText.Build(members);
            try
            {
                // 先に複製へ試しに反映し、値が読めることを確かめる（失敗しても今の設定を壊さない）
                var trial = new AppSettings();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(target), trial);
                JsonUtility.FromJsonOverwrite(json, trial);
            }
            catch (ArgumentException)
            {
                return false;
            }

            // テーマの既定色のままの背景はテーマに合わせて切り替わるため、反映前の状態を覚えておく
            Color background = target.backgroundColor;
            bool followsTheme = background == AppSettings.DefaultBackgroundOf(target.darkMode);

            // 各機能が同じインスタンスを参照しているため、置き換えずに中身へ上書きする（含まれないキーは今の値のまま）
            JsonUtility.FromJsonOverwrite(json, target);
            target.Sanitize();

            // テーマだけが変わり背景色はプリセットに無かったなら、新しいテーマの既定色にそろえる
            if (followsTheme && target.backgroundColor == background)
            {
                target.backgroundColor = AppSettings.DefaultBackgroundOf(target.darkMode);
            }

            return true;
        }

        private static bool LooksLikeJson(string text)
        {
            // 空白・BOM を除いた最初の文字が { なら JSON
            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c) && c != '\uFEFF')
                {
                    return c == '{';
                }
            }

            return false;
        }

        private static bool Contains(IReadOnlyList<string> fields, string field)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i] == field)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
