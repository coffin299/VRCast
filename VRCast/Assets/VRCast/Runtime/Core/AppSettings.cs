using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCast.Core
{
    /// <summary>
    /// settings.json に永続化するアプリ設定。JsonUtility でシリアライズする。
    /// フィールド追加は後方互換（欠けている項目は既定値）なので version は上げない。
    /// </summary>
    [Serializable]
    public class AppSettings
    {
        // 設定フォーマットのバージョン、互換性のない変更時に増やす
        public const int CurrentVersion = 1;

        // ウィンドウサイズの下限（px）
        public const int MinWindowSize = 64;

        // ライト強度の上限
        public const float MaxLightIntensity = 8f;

        // 環境光（全方向から当たる明るさ）の上限と、太陽光の色温度（K）の範囲
        public const float MaxAmbientIntensity = 3f;
        public const float MinLightTemperature = 2500f;
        public const float MaxLightTemperature = 10000f;

        // アバターの明るさ（マテリアルの色の倍率）の範囲
        public const float MinAvatarBrightness = 0.5f;
        public const float MaxAvatarBrightness = 10f;

        // マイク感度・しきい値の範囲
        public const float MinMicGain = 0.1f;
        public const float MaxMicGain = 10f;
        public const float MaxMicThreshold = 0.2f;

        // 母音判定の声の高さ補正（代表フォルマントに掛ける倍率）の範囲
        public const float MinVoiceScale = 0.8f;
        public const float MaxVoiceScale = 1.3f;

        // フェイストラッキング受信ポートの既定値（OpenSeeFace / VSeeFace と同じ）と範囲
        public const int DefaultTrackingPort = 11573;
        public const int MinTrackingPort = 1024;
        public const int MaxTrackingPort = 65535;

        // VMC プロトコルの受信ポートの既定値（VMC プロトコルの標準）
        public const int DefaultVmcPort = 39539;

        // iFacialMocap のポート（アプリ側で固定。送信開始の合図の宛先と、受信の待ち受けの両方に使う）
        public const int IFacialMocapPort = 49983;

        // 外部操作の待ち受けポートの既定値（範囲はトラッキングと同じ）
        public const int DefaultRemoteOscPort = 39570;
        public const int DefaultRemoteHttpPort = 39571;

        // 頭の位置に合わせた体の動き（傾き・移動）の強さの上限（0 = 動かさない）
        public const float MaxTrackingBodyLean = 3f;

        // 視線の強さの上限（0 = 目を動かさない）
        public const float MaxTrackingGaze = 2f;

        // 待機モーション（呼吸・体の揺れ・頭のゆらぎ）の強さの上限と、速さの倍率の範囲
        public const float MaxIdleMotionStrength = 2f;
        public const float MinIdleMotionSpeed = 0.5f;
        public const float MaxIdleMotionSpeed = 2f;

        // 表情に切り替わるしきい値の範囲（表情の強さ 0〜1 と同じ目盛り。小さいほど弱い表情でも反応する）
        public const float MinExpressionThreshold = 0.1f;
        public const float MaxExpressionThreshold = 0.8f;

        // 表情ごとのしきい値が未設定であることを表す値（範囲外の負の値）
        public const float UseCommonThreshold = -1f;

        // 操作パネルの拡大率の範囲
        public const float MinUiScale = 0.75f;
        public const float MaxUiScale = 2f;

        // カメラの視点・見た目を覚えておくアバターの数の上限（設定ファイルが際限なく大きくならないように）
        public const int MaxAvatarCameras = 50;

        // アバタータブに並べる最近使ったアバターの数
        public const int MaxRecentAvatars = 10;

        // テーマごとの背景色の既定値（ライト = 目に優しいベージュ、ダーク = パネルより少し明るい暗い茶系の灰色）
        public static readonly Color LightBackgroundColor = new Color(0.90f, 0.86f, 0.78f, 1f);
        public static readonly Color DarkBackgroundColor = new Color(0.17f, 0.16f, 0.15f, 1f);

        public int version = CurrentVersion;
        public int windowWidth = 1280;
        public int windowHeight = 720;
        public string lastAvatarPath = string.Empty;

        // アバターごとのカメラの視点と見た目（使った順で古いものが先頭。上限を超えたら最も長く使っていないものから捨てる）。
        // 最近使ったアバターの一覧もここから作る。名前は以前の設定ファイルとの互換のため据え置き
        public List<AvatarEntry> avatarCameras = new List<AvatarEntry>();

        // 操作パネルの表示言語と拡大率
        public UiLanguage uiLanguage = UiLanguage.Auto;
        public float uiScale = 1f;

        // 操作パネルのダークモード（既定はライト = ベージュ。OS の設定には合わせない）
        public bool darkMode;

        // 起動時に Web サイトの version.json で新しいバージョンを確認するか
        public bool checkForUpdates = true;

        // 軽量モード（描画のフレームレートとトラッカーの処理回数を下げ、ゲーム・OBS と同時に使うときの負荷を減らす）。
        // 配信ではゲーム・OBS と併用することが多いため既定は ON
        public bool lowLoadMode = true;

        // パネルの一番下に動作状況（fps・CPU・GPU・トラッキング）を表示するか
        public bool showPerformanceStats = true;

        // 同梱の MediaPipe トラッカーの動作（なめらか / エコ）
        public TrackerMode trackerMode = TrackerMode.Smooth;

        // VRCast 本体と同梱トラッカーのプロセスの優先度（両方に同じ値を使う）
        public ProcessPriority processPriority = ProcessPriority.Normal;

        // 2 CCD の X3D（片方だけ 3D V-Cache）で、VRCast 本体と同梱トラッカーをキャッシュの無い側のコアで動かす（ゲームとコアを取り合わない）
        public bool avoidCacheCcd = true;

        // P コアと E コアがある CPU で、VRCast 本体と同梱トラッカーに使わせるコア
        public HybridCoreSelection hybridCores = HybridCoreSelection.Auto;

        // 描画に使う GPU（次回起動から反映）。gpuAdapter に GPU 名があれば直接指定し、gpuPreference より優先する
        public GpuPreference gpuPreference = GpuPreference.Auto;
        public string gpuAdapter = string.Empty;

        // 背景色（既定はライトテーマのベージュ）。非透過時は不透明で使い、透過時は alpha 0 のまま色だけ塗る
        // （ウィンドウ上では色が見え、OBS のゲームキャプチャでは抜ける）
        public bool transparentBackground;
        public Color backgroundColor = LightBackgroundColor;

        // カメラの固定（マウスの回転・移動・ズームを無視する。画角スライダーとリセットは使える）
        public bool cameraLocked;

        // 仮想カメラ（VRCast Camera）への出力と、Windows 11 の方式（Media Foundation。カメラ名は VRCast Camera (MF)）で出すか
        public bool virtualCameraEnabled;
        public bool virtualCameraMediaFoundation;

        // Spout2 への出力（OBS 等へ GPU 上で映像を渡す）
        public bool spoutEnabled;

        // 太陽光（ディレクショナルライト）。向きはカメラ正面からの角度（0 = 正面から当たる）
        public float lightIntensity = 1.2f;
        public float lightYaw = -30f;
        public float lightPitch = 40f;

        // 太陽光の色温度（6500K でほぼ白、低いほど暖色）と環境光の明るさ
        public float lightTemperature = 6000f;
        public float ambientIntensity = 1.2f;

        // アバターの明るさ（1 = マテリアルのまま。シェーダーの明るさ上限を超えて明るくする）
        public float avatarBrightness = 1f;

        // 待機ポーズ（0 = T ポーズのまま、1 = 腕を下ろし切る / 肘を曲げ切る）。既定は気を付け
        public float poseArmDown = 1f;
        public float poseElbowBend = 0f;

        // アバターの向き（度、0 = カメラ正面）
        public float avatarYaw;

        // 待機モーション（強さ 0 = 動かさない、1 = 標準）と速さの倍率
        public bool idleMotionEnabled = true;
        public float idleBreathing = 1f;
        public float idleSway = 1f;
        public float idleHeadMotion = 1f;
        public float idleMotionSpeed = 1f;

        // 自動まばたき
        public bool autoBlink = true;

        // 表情（ニュートラル以外）を出している間はまばたきしない（自動・トラッキングとも。表情の目の形を崩さないため）
        public bool blinkPausedByExpression;

        // 揺れもの（PhysBone 近似）
        public bool physicsEnabled = true;

        // マイク音量によるリップシンク（デバイス名が空なら既定デバイス）
        public bool lipSyncEnabled = true;
        public string microphoneDevice = string.Empty;
        public float micGain = 1f;
        public float micThreshold = 0.01f;

        // 母音（あいうえお）に合わせた口の形（Viseme のアバターのみ。OFF なら音量で aa だけ）と声の高さ補正
        public bool lipSyncVowels = true;
        public float lipSyncVoiceScale = 1f;

        // 表情（ニュートラル以外）を出している間は口を動かさない（表情の口の形と重ねて崩さないため）。
        // マイクの口パクとトラッキングの口の開きは別々に止められる（両方 ON も可）
        public bool lipSyncPausedByExpression;
        public bool trackingMouthPausedByExpression;

        // トラッキング（入力元のトラッカーから UDP 受信。鏡像 = 本人の動きを鏡のように反映）
        public bool trackingEnabled;
        public TrackingSource trackingSource = TrackingSource.MediaPipe;
        public int trackingPort = DefaultTrackingPort;
        public bool trackingMirror = true;

        // 顔（まばたき・ウインク・パーフェクトシンク・視線）の左右だけを入れ替える（頭の傾き・体・腕と左右が合わないとき用）
        public bool trackingSwapFaceSides;

        // 外部アプリ（VMC プロトコル）の受信ポート（同梱トラッカーの trackingPort とは別。LAN から受信する）
        public int vmcPort = DefaultVmcPort;

        // iFacialMocap を動かしている iPhone の IP アドレス（送信開始の合図の宛先。空なら未設定）
        public string iFacialMocapAddress = string.Empty;
        public float trackingBodyLean = 1f;
        public BodyMotion trackingBodyMotion = BodyMotion.Lean;
        public float trackingGaze = 1f;

        // まばたきをトラッキングする（OFF なら目の開閉は使わず自動まばたきに任せる）
        public bool trackingBlink = true;

        // 腕・手（指）のトラッキング（MediaPipe のみ）
        public bool trackingHands = true;

        // 上半身の向き（両肩の線から背骨・胸のひねり・左右の傾き。腕・手が ON のときだけ。MediaPipe のみ）
        public bool trackingTorso = true;

        // パーフェクトシンク（MediaPipe・VMC・iFacialMocap のみ。ARKit 名の BlendShape を持つアバターの顔を直接動かす）
        public bool trackingPerfectSync = true;

        // パーフェクトシンクを有効にする条件（false = ARKit 名が MinMatchedShapes 種類以上、true = 1 種類でもあれば）
        public bool trackingPerfectSyncAnyShape;

        // 表情反映（MediaPipe・VMC・iFacialMocap のみ）。表情ごとの割り当ては表情プリセット名（空欄 = 自動、"<none>" = 割り当てなし）
        public bool trackingExpressions = true;
        public float trackingExpressionThreshold = 0.3f;
        public string expressionSmile = string.Empty;
        public string expressionSurprise = string.Empty;
        public string expressionAngry = string.Empty;
        public string expressionSad = string.Empty;
        public string expressionWink = string.Empty;
        public string expressionSquint = string.Empty;
        public string expressionPout = string.Empty;

        // 表情のショートカットキーを、VRCast のウィンドウが前面に無いときも使う（OBS などを操作中でも切り替えられる）
        public bool expressionHotkeysInBackground = true;

        // 外部（Stream Deck・OSC アプリ等）からの表情の操作。127.0.0.1 の OSC（UDP）と HTTP で待ち受ける（既定 OFF）
        public bool remoteControlEnabled;
        public int remoteOscPort = DefaultRemoteOscPort;
        public int remoteHttpPort = DefaultRemoteHttpPort;

        // 表情ごとのしきい値（UseCommonThreshold = 未設定で trackingExpressionThreshold を使う。旧版の共通値を引き継ぐため）
        public float thresholdSmile = UseCommonThreshold;
        public float thresholdSurprise = UseCommonThreshold;
        public float thresholdAngry = UseCommonThreshold;
        public float thresholdSad = UseCommonThreshold;
        public float thresholdWink = UseCommonThreshold;
        public float thresholdSquint = UseCommonThreshold;
        public float thresholdPout = UseCommonThreshold;

        // VRCast から起動するトラッカー（実行ファイルのパス（空 = 同梱版）と、使うカメラのデバイス名）
        public string trackerPath = string.Empty;
        public string trackerCamera = string.Empty;

        // デバッグログタブの詳細ログ（トラッキングの受信統計・状態の変化など。調査時だけ ON にする想定で既定は OFF）
        public bool detailedLogging;

        /// <summary>
        /// 顔（まばたき・パーフェクトシンク・視線）に使う鏡像の向き。顔の左右の入れ替えが ON なら鏡像設定と逆。
        /// </summary>
        public bool FaceMirror => trackingMirror != trackingSwapFaceSides;

        /// <summary>
        /// テーマに合った背景色の既定値を返す。
        /// </summary>
        public static Color DefaultBackgroundOf(bool dark)
        {
            return dark ? DarkBackgroundColor : LightBackgroundColor;
        }

        /// <summary>
        /// アバターのカメラの視点を探す（パスの大文字・小文字は区別しない）。無ければ false。
        /// </summary>
        public bool TryGetAvatarCamera(string avatarPath, out CameraPose pose)
        {
            int index = FindAvatarCamera(avatarPath);
            pose = index >= 0 ? avatarCameras[index].pose : default;
            return index >= 0;
        }

        /// <summary>
        /// アバターのカメラの視点を記録する（最新として末尾へ移し、上限を超えたら古いものから捨てる）。
        /// </summary>
        public void SetAvatarCamera(string avatarPath, CameraPose pose)
        {
            // パスが無い・壊れた値は記録しない
            if (string.IsNullOrEmpty(avatarPath) || !pose.IsFinite)
            {
                return;
            }

            // 既存の記録（見た目を含む）を末尾へ移し、無ければ新しく作る
            AvatarEntry entry = TakeAvatarEntry(avatarPath) ?? new AvatarEntry { avatarPath = avatarPath };
            entry.pose = pose;
            avatarCameras.Add(entry);
            TrimAvatarCameras();
        }

        /// <summary>
        /// アバターの見た目（ライト・待機ポーズ等）を探す。未記録なら false。
        /// </summary>
        public bool TryGetAvatarLook(string avatarPath, out AvatarLook look)
        {
            int index = FindAvatarCamera(avatarPath);
            bool found = index >= 0 && avatarCameras[index].hasLook;
            look = found ? avatarCameras[index].look : default;
            return found;
        }

        /// <summary>
        /// アバターの見た目を記録する。カメラの視点を記録済みのアバターだけが対象（先に SetAvatarCamera を呼ぶ）。
        /// </summary>
        public void SetAvatarLook(string avatarPath, AvatarLook look)
        {
            // 壊れた値は記録しない
            if (!look.IsFinite)
            {
                return;
            }

            // 記録の無いアバターは対象外（視点の無い記録を作らない）
            AvatarEntry entry = TakeAvatarEntry(avatarPath);
            if (entry == null)
            {
                return;
            }

            // 最新として末尾へ戻す
            entry.hasLook = true;
            entry.look = look;
            avatarCameras.Add(entry);
        }

        /// <summary>
        /// アバターの BlendShape の上限を返す（未記録なら空。一覧は複製なので増減しても記録は変わらない）。
        /// </summary>
        public List<BlendShapeLimit> GetBlendShapeLimits(string avatarPath)
        {
            int index = FindAvatarCamera(avatarPath);
            return index >= 0
                ? new List<BlendShapeLimit>(avatarCameras[index].blendShapeLimits)
                : new List<BlendShapeLimit>();
        }

        /// <summary>
        /// アバターの BlendShape の上限を記録する。カメラの視点を記録済みのアバターだけが対象（読込時に記録される）。
        /// </summary>
        public void SetBlendShapeLimits(string avatarPath, List<BlendShapeLimit> limits)
        {
            // 記録の無いアバターは対象外（視点の無い記録を作らない）
            int index = FindAvatarCamera(avatarPath);
            if (index < 0)
            {
                return;
            }

            // 壊れた値は除いて複製を持つ（呼び出し側の一覧と共有しない）
            avatarCameras[index].blendShapeLimits = limits.FindAll(limit => limit != null && limit.IsValid);
        }

        /// <summary>
        /// アバターの表情のショートカットキーを返す（未記録なら空。一覧は複製）。
        /// </summary>
        public List<ExpressionHotkey> GetExpressionHotkeys(string avatarPath)
        {
            int index = FindAvatarCamera(avatarPath);
            return index >= 0
                ? new List<ExpressionHotkey>(avatarCameras[index].expressionHotkeys)
                : new List<ExpressionHotkey>();
        }

        /// <summary>
        /// アバターの表情のショートカットキーを記録する。カメラの視点を記録済みのアバターだけが対象（読込時に記録される）。
        /// </summary>
        public void SetExpressionHotkeys(string avatarPath, List<ExpressionHotkey> hotkeys)
        {
            // 記録の無いアバターは対象外（視点の無い記録を作らない）
            int index = FindAvatarCamera(avatarPath);
            if (index < 0)
            {
                return;
            }

            // 壊れた値は除いて複製を持つ
            avatarCameras[index].expressionHotkeys = hotkeys.FindAll(hotkey => hotkey != null && hotkey.IsValid);
        }

        /// <summary>
        /// 最近使ったアバターのパスを新しい順に返す（最大 MaxRecentAvatars 件）。
        /// </summary>
        public List<string> RecentAvatars()
        {
            var paths = new List<string>(MaxRecentAvatars);
            // 末尾ほど新しいため後ろから取る
            for (int i = avatarCameras.Count - 1; i >= 0 && paths.Count < MaxRecentAvatars; i--)
            {
                paths.Add(avatarCameras[i].avatarPath);
            }

            return paths;
        }

        /// <summary>
        /// アバターの記録（カメラの視点・見た目）を消し、最近使ったアバターの一覧からも外す。
        /// </summary>
        public void ForgetAvatar(string avatarPath)
        {
            TakeAvatarEntry(avatarPath);
        }

        private AvatarEntry TakeAvatarEntry(string avatarPath)
        {
            // 見つかれば一覧から外して返す（呼び出し側が末尾へ戻す）
            int index = FindAvatarCamera(avatarPath);
            if (index < 0)
            {
                return null;
            }

            AvatarEntry entry = avatarCameras[index];
            avatarCameras.RemoveAt(index);
            return entry;
        }

        private void TrimAvatarCameras()
        {
            // 上限を超えた分を古い順（先頭）から捨てる
            if (avatarCameras.Count > MaxAvatarCameras)
            {
                avatarCameras.RemoveRange(0, avatarCameras.Count - MaxAvatarCameras);
            }
        }

        private int FindAvatarCamera(string avatarPath)
        {
            // パスが無ければ見つからない
            if (string.IsNullOrEmpty(avatarPath))
            {
                return -1;
            }

            // Windows のパスは大文字・小文字を区別しない
            return avatarCameras.FindIndex(
                entry => string.Equals(entry.avatarPath, avatarPath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 全ての設定を既定値に戻す（ウィンドウサイズ・最後に開いたアバター・アバターごとの記録は保持）。
        /// 各機能が同じインスタンスを参照しているため、置き換えずに中身を上書きする。
        /// </summary>
        public void ResetToDefaults()
        {
            // 保持する値を退避
            int width = windowWidth;
            int height = windowHeight;
            string avatarPath = lastAvatarPath;
            List<AvatarEntry> cameras = avatarCameras;

            // 既定値で上書き
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new AppSettings()), this);

            // 退避した値を戻す
            windowWidth = width;
            windowHeight = height;
            lastAvatarPath = avatarPath;
            avatarCameras = cameras;
        }

        /// <summary>
        /// 読み込んだ値を安全な範囲に補正する。
        /// </summary>
        public void Sanitize()
        {
            // 壊れた値や極端な値でウィンドウが消えないよう下限を設ける
            windowWidth = Mathf.Max(MinWindowSize, windowWidth);
            // 高さも同様に下限で補正
            windowHeight = Mathf.Max(MinWindowSize, windowHeight);
            // JSON に null が入っていた場合に備えて空文字へ正規化
            lastAvatarPath ??= string.Empty;
            // カメラの視点は、パスの無いもの・壊れた値を捨て、上限を超えた古いものも捨てる
            avatarCameras ??= new List<AvatarEntry>();
            avatarCameras.RemoveAll(entry => entry == null || string.IsNullOrEmpty(entry.avatarPath) || !entry.pose.IsFinite);
            TrimAvatarCameras();
            // 壊れた見た目は未記録扱いにする（視点は残す）
            foreach (AvatarEntry entry in avatarCameras)
            {
                entry.hasLook &= entry.look.IsFinite;
                // BlendShape の上限は壊れたものを捨て、範囲内に制限する（旧版の設定には無いので空の一覧にする）
                entry.blendShapeLimits ??= new List<BlendShapeLimit>();
                entry.blendShapeLimits.RemoveAll(limit => limit == null || !limit.IsValid);
                foreach (BlendShapeLimit limit in entry.blendShapeLimits)
                {
                    limit.path ??= string.Empty;
                    limit.max = Mathf.Clamp(limit.max, BlendShapeLimit.MinWeight, BlendShapeLimit.MaxWeight);
                }

                // 表情のショートカットキーは壊れたもの・使えないキーを捨てる（旧版の設定には無いので空の一覧にする）
                entry.expressionHotkeys ??= new List<ExpressionHotkey>();
                entry.expressionHotkeys.RemoveAll(hotkey => hotkey == null || !hotkey.IsValid);
            }

            // 未知の表示言語は OS 準拠へ
            if (!Enum.IsDefined(typeof(UiLanguage), uiLanguage))
            {
                uiLanguage = UiLanguage.Auto;
            }

            // パネルの拡大率は範囲内に制限
            uiScale = Mathf.Clamp(uiScale, MinUiScale, MaxUiScale);
            // 未知の優先度（リアルタイム等の手編集）は通常へ
            if (!Enum.IsDefined(typeof(ProcessPriority), processPriority))
            {
                processPriority = ProcessPriority.Normal;
            }

            // 未知のコアの選択は Windows に任せる
            if (!Enum.IsDefined(typeof(HybridCoreSelection), hybridCores))
            {
                hybridCores = HybridCoreSelection.Auto;
            }

            // 未知のトラッカーの動作はなめらかへ
            if (!Enum.IsDefined(typeof(TrackerMode), trackerMode))
            {
                trackerMode = TrackerMode.Smooth;
            }

            // 未知の GPU の優先設定は Windows に任せる
            if (!Enum.IsDefined(typeof(GpuPreference), gpuPreference))
            {
                gpuPreference = GpuPreference.Auto;
            }

            // null の GPU 名は直接指定なしの空文字へ
            gpuAdapter ??= string.Empty;
            // ライト強度は 0〜上限に制限
            lightIntensity = Mathf.Clamp(lightIntensity, 0f, MaxLightIntensity);
            // 仰角は真上〜真下の範囲に制限
            lightPitch = Mathf.Clamp(lightPitch, -90f, 90f);
            // 方位角は -180〜180 に正規化
            lightYaw = Mathf.Repeat(lightYaw + 180f, 360f) - 180f;
            // 色温度は範囲内に制限
            lightTemperature = Mathf.Clamp(lightTemperature, MinLightTemperature, MaxLightTemperature);
            // 環境光は 0〜上限に制限
            ambientIntensity = Mathf.Clamp(ambientIntensity, 0f, MaxAmbientIntensity);
            // アバターの明るさは範囲内に制限
            avatarBrightness = Mathf.Clamp(avatarBrightness, MinAvatarBrightness, MaxAvatarBrightness);
            // ポーズの度合いは 0〜1 に制限
            poseArmDown = Mathf.Clamp01(poseArmDown);
            // 肘の曲げも同様
            poseElbowBend = Mathf.Clamp01(poseElbowBend);
            // アバターの向きは -180〜180 に正規化
            avatarYaw = Mathf.Repeat(avatarYaw + 180f, 360f) - 180f;
            // 待機モーションの強さは 0〜上限に制限
            idleBreathing = Mathf.Clamp(idleBreathing, 0f, MaxIdleMotionStrength);
            // 体の揺れも同様
            idleSway = Mathf.Clamp(idleSway, 0f, MaxIdleMotionStrength);
            // 頭のゆらぎも同様
            idleHeadMotion = Mathf.Clamp(idleHeadMotion, 0f, MaxIdleMotionStrength);
            // 速さの倍率は範囲内に制限
            idleMotionSpeed = Mathf.Clamp(idleMotionSpeed, MinIdleMotionSpeed, MaxIdleMotionSpeed);
            // null のデバイス名は既定デバイス扱いの空文字へ
            microphoneDevice ??= string.Empty;
            // マイク感度は下限〜上限に制限
            micGain = Mathf.Clamp(micGain, MinMicGain, MaxMicGain);
            // しきい値は 0〜上限に制限
            micThreshold = Mathf.Clamp(micThreshold, 0f, MaxMicThreshold);
            // 声の高さ補正は範囲内に制限
            lipSyncVoiceScale = Mathf.Clamp(lipSyncVoiceScale, MinVoiceScale, MaxVoiceScale);
            // 未知の入力元（手編集・将来版の設定）は既定の MediaPipe へ
            if (!Enum.IsDefined(typeof(TrackingSource), trackingSource))
            {
                trackingSource = TrackingSource.MediaPipe;
            }

            // 受信ポートは特権ポートを避けた範囲に制限
            trackingPort = Mathf.Clamp(trackingPort, MinTrackingPort, MaxTrackingPort);
            // VMC の受信ポートも同じ範囲に制限
            vmcPort = Mathf.Clamp(vmcPort, MinTrackingPort, MaxTrackingPort);
            // iPhone の IP アドレスは前後の空白を除く（null は未設定の空文字へ）
            iFacialMocapAddress = (iFacialMocapAddress ?? string.Empty).Trim();
            // 外部操作のポートも同じ範囲に制限
            remoteOscPort = Mathf.Clamp(remoteOscPort, MinTrackingPort, MaxTrackingPort);
            remoteHttpPort = Mathf.Clamp(remoteHttpPort, MinTrackingPort, MaxTrackingPort);
            // 上半身の傾きの強さは 0〜上限に制限
            trackingBodyLean = Mathf.Clamp(trackingBodyLean, 0f, MaxTrackingBodyLean);
            // 未知の体の動かし方は既定の傾きへ
            if (!Enum.IsDefined(typeof(BodyMotion), trackingBodyMotion))
            {
                trackingBodyMotion = BodyMotion.Lean;
            }


            // 視線の強さも同様
            trackingGaze = Mathf.Clamp(trackingGaze, 0f, MaxTrackingGaze);
            // 表情のしきい値は範囲内に制限
            trackingExpressionThreshold = Mathf.Clamp(
                trackingExpressionThreshold, MinExpressionThreshold, MaxExpressionThreshold);
            // null の割り当ては自動扱いの空文字へ
            expressionSmile ??= string.Empty;
            // 驚きも同様
            expressionSurprise ??= string.Empty;
            // 怒りも同様
            expressionAngry ??= string.Empty;
            // 悲しみも同様
            expressionSad ??= string.Empty;
            // ウインクも同様
            expressionWink ??= string.Empty;
            // ジト目も同様
            expressionSquint ??= string.Empty;
            // ふくれっ面も同様
            expressionPout ??= string.Empty;
            // 表情ごとのしきい値は、未設定（負）のまま残し、設定済みなら範囲内に制限
            thresholdSmile = SanitizeThreshold(thresholdSmile);
            // 驚きも同様
            thresholdSurprise = SanitizeThreshold(thresholdSurprise);
            // 怒りも同様
            thresholdAngry = SanitizeThreshold(thresholdAngry);
            // 悲しみも同様
            thresholdSad = SanitizeThreshold(thresholdSad);
            // ウインクも同様
            thresholdWink = SanitizeThreshold(thresholdWink);
            // ジト目も同様
            thresholdSquint = SanitizeThreshold(thresholdSquint);
            // ふくれっ面も同様
            thresholdPout = SanitizeThreshold(thresholdPout);
            // null のパス・カメラ名は未設定扱いの空文字へ
            trackerPath ??= string.Empty;
            // カメラ名も同様
            trackerCamera ??= string.Empty;
        }

        /// <summary>
        /// 表情ごとのしきい値を補正する（未設定・壊れた値は未設定に、設定済みは範囲内に）。
        /// </summary>
        public static float SanitizeThreshold(float value)
        {
            // NaN・負の値は未設定扱い、それ以外は範囲内へ
            if (float.IsNaN(value) || value < 0f)
            {
                return UseCommonThreshold;
            }

            return Mathf.Clamp(value, MinExpressionThreshold, MaxExpressionThreshold);
        }
    }
}
