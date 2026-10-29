using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Tracking;

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
        public const float MaxLightIntensity = 16f;

        // 環境光（全方向から当たる明るさ）の上限と、太陽光の色温度（K）の範囲
        public const float MaxAmbientIntensity = 8f;
        public const float MinLightTemperature = 2000f;
        public const float MaxLightTemperature = 12000f;

        // アバターの明るさ（マテリアルの色の倍率）の範囲
        public const float MinAvatarBrightness = 0.05f;
        public const float MaxAvatarBrightness = 20f;

        // アバターの輪郭線の太さ（マテリアルの太さの倍率）の上限
        public const float MaxOutlineWidth = 6f;

        // 最新ライティング（IBL・影・ブルーム・トーンマップ・画面効果）の上限
        public const float MaxIblIntensity = 8f;
        public const float MaxReflectionStrength = 2f;
        // コンタクト影は 1 超でも効く（Unity の Soft Shadow は 1 で頭打ち）
        public const float MaxShadowStrength = 5f;
        public const float MaxShadowSoftness = 1f;
        public const float MaxBloomIntensity = 8f;
        public const float MaxBloomThreshold = 5f;
        public const float MaxTonemapStrength = 2f;
        public const float MinExposure = -3f;
        public const float MaxExposure = 3f;
        public const float MinContrast = 0.25f;
        public const float MaxContrast = 3f;
        public const float MaxSaturation = 3f;
        public const float MaxColorBalance = 2f;
        public const float MaxSsaoIntensity = 5f;
        public const float MaxSsaoRadius = 3f;
        public const float MaxScreenRimIntensity = 5f;
        public const float MaxScreenRimPower = 16f;
        public const float MaxSharpen = 2f;
        public const float MaxVignette = 2f;

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

        // VMC プロトコルの受信ポートの既定値
        public const int DefaultVmcPort = 39540;

        // iFacialMocap のポート（アプリ側で固定。送信開始の合図の宛先と、受信の待ち受けの両方に使う）
        public const int IFacialMocapPort = 49983;

        // 自動リセットの間隔（分）の既定値と範囲
        public const float DefaultAutoResetIntervalMinutes = 5f;
        public const float MinAutoResetIntervalMinutes = 1f;
        public const float MaxAutoResetIntervalMinutes = 60f;

        // 外部操作の待ち受けポートの既定値（範囲はトラッキングと同じ）
        public const int DefaultRemoteOscPort = 39570;
        public const int DefaultRemoteHttpPort = 39571;

        // 頭の位置に合わせた体の動き（傾き・移動）の強さの上限（0 = 動かさない）
        public const float MaxTrackingBodyLean = 3f;

        // 視線の強さの上限（0 = 目を動かさない）
        public const float MaxTrackingGaze = 2f;

        // 目の開きの補正の上限（開いているとみなす目の閉じ具合。1 にするとまばたきで閉じなくなる）
        public const float MaxTrackingEyeOpenOffset = 0.6f;

        // パーフェクトシンクの口・頬の強調の倍率の上限（1 = 強調しない）
        public const float MaxPerfectSyncBoost = 5f;

        // 自動まばたきの動きの既定値（秒）と範囲
        public const float DefaultBlinkCloseSeconds = 0.07f;
        public const float DefaultBlinkHoldSeconds = 0.05f;
        public const float DefaultBlinkOpenSeconds = 0.16f;
        public const float DefaultBlinkIntervalMin = 2f;
        public const float DefaultBlinkIntervalMax = 6f;
        public const float MinBlinkMoveSeconds = 0.02f;
        public const float MaxBlinkMoveSeconds = 0.5f;
        public const float MaxBlinkHoldSeconds = 1f;
        public const float MinBlinkInterval = 0.5f;
        public const float MaxBlinkInterval = 20f;

        // パーフェクトシンクの口の開きの倍率の範囲（1 = そのまま）と、閉じた口とみなす開きの上限
        public const float MinJawOpenGain = 0.2f;
        public const float MaxJawOpenGain = 3f;
        public const float MaxJawOpenDeadzone = 0.5f;

        // 頬のふくらみの推定で、頬の幅の広がり（%）として選べる上限と、出し始め〜最大の最小の間隔（%）
        public const float MaxCheekWidenPercent = 15f;
        public const float MinCheekWidenSpan = 0.5f;

        // 待機ポーズの腕の向きの微調整（度）: 開き（真下から外側）の既定と範囲、前後（+ = 前）とひねり（+ = 内側）の範囲
        public const float DefaultPoseArmSpread = 12f;
        public const float MaxPoseArmSpread = 60f;
        public const float MinPoseArmForward = -30f;
        public const float MaxPoseArmForward = 60f;
        public const float MaxPoseArmTwist = 90f;

        // 待機モーション（呼吸・体の揺れ・頭のゆらぎ）の強さの上限と、速さの倍率の範囲
        public const float MaxIdleMotionStrength = 2f;
        public const float MinIdleMotionSpeed = 0.5f;
        public const float MaxIdleMotionSpeed = 2f;

        // 表情に切り替わるしきい値の範囲。強さは 0〜1 なので、1 より大きい値には届かず切り替わらない
        public const float MinExpressionThreshold = 0.1f;
        public const float MaxExpressionThreshold = 3f;

        // 表情ごとのしきい値が未設定であることを表す値（範囲外の負の値）
        public const float UseCommonThreshold = -1f;

        // 操作パネルの拡大率の範囲
        public const float MinUiScale = 0.75f;
        public const float MaxUiScale = 2f;

        // 操作パネルの背景の不透明度の範囲（0 にすると枠も見えず操作できなくなるため下限を設ける）
        public const float MinUiOpacity = 0.2f;
        public const float MaxUiOpacity = 1f;

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

        // 操作パネル（パーフェクトシンク化の編集画面を含む）の背景の不透明度。文字・画像は薄くしない
        public float uiOpacity = 1f;

        // 操作パネルのダークモード（既定はライト = ベージュ。OS の設定には合わせない）
        public bool darkMode;

        // 起動時に Web サイトの version.json で新しいバージョンを確認するか
        public bool checkForUpdates = true;

        // 軽量モード（描画のフレームレートとトラッカーの処理回数を下げ、ゲーム・OBS と同時に使うときの負荷を減らす）。
        // 配信ではゲーム・OBS と併用することが多いため既定は ON
        public bool lowLoadMode = true;

        // 垂直同期（Off / On / Fast Sync。On のとき FPS 上限は効かない。Fast Sync は NVIDIA のみ）
        public VSyncMode vSyncMode = VSyncMode.Off;

        // 描画の FPS 上限（VSync OFF のとき。軽量モード ON ならこの値と 30 の小さい方）
        public const int MinTargetFrameRate = 15;
        public const int MaxTargetFrameRate = 400;
        public int targetFrameRate = 60;

        // パネルの一番下に動作状況（fps・CPU・GPU・トラッキング）を表示するか
        public bool showPerformanceStats = true;

        // 同梱の MediaPipe トラッカーの動作（ぬるぬる / なめらか / エコ）
        public TrackerMode trackerMode = TrackerMode.Fluid;

        // MediaPipe トラッカーのカメラ取得・推定の目標 FPS（--fps / --max-fps。エコの間引きとは別）
        public const int MinTrackingFps = 15;
        public const int MaxTrackingFps = 120;
        public int trackingFps = 30;

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

        // カメラを近づけてもアバターの体に入り込まない（手前で止め、それ以上は画角を狭めて拡大する）
        public bool cameraZoomInsteadOfEntering = true;

        // 仮想カメラ（VRCast Camera）への出力と、Windows 11 の方式（Media Foundation。カメラ名は VRCast Camera (MF)）で出すか
        public bool virtualCameraEnabled;
        public bool virtualCameraMediaFoundation;

        // Spout2 への出力（OBS 等へ GPU 上で映像を渡す）
        public bool spoutEnabled;

        // 背景透過のスクリーンショットの保存先フォルダ（空 = ピクチャ\VRCast）
        public string screenshotFolder = string.Empty;

        // ライティング系統（既定はレガシー＝2.x 相当。最新は三点・影・IBL・ポスト）
        public LightingSystem lightingSystem = LightingSystem.Legacy;

        // アンチエイリアス（方式と強さ。既定は MSAA 4x。ぼやけやすい FXAA は選んだときだけ）
        public AntiAliasingMode antiAliasingMode = AntiAliasingMode.Msaa;
        public AntiAliasingQuality antiAliasing = AntiAliasingQuality.x4;

        // 高解像度描画（Off = ウィンドウと同じ。AA と併用可。x2=125% / x4=150% / x8=200%）
        public AntiAliasingQuality supersample = AntiAliasingQuality.Off;

        // 太陽光 / Key（ディレクショナルライト）。向きはカメラ正面からの角度（0 = 正面から当たる）
        // レガシー（2.x）と同じ既定。最新ライティングの「既定」プリセットは RenderingController 側の値を使う
        public float lightIntensity = 1.2f;
        public float lightYaw = -30f;
        public float lightPitch = 40f;

        // 太陽光の色温度（6500K でほぼ白、低いほど暖色）と環境光の明るさ（レガシー = 2.x と同じ既定）
        public float lightTemperature = 6000f;
        public float ambientIntensity = 1.2f;

        // 最新ライティングの Fill / Rim / IBL 等（「影ver2」プリセット＝既定）
        public float fillIntensity = 0.1f;
        public float fillYaw = 145f;
        public float fillPitch = 10f;
        public float fillTemperature = 7200f;

        public float rimIntensity = 0.28f;
        public float rimYaw = 168f;
        public float rimPitch = -12f;
        public float rimTemperature = 6800f;

        public float iblIntensity = 0.5f;
        public IblPreset iblPreset = IblPreset.Studio;
        public float reflectionStrength = 0.14f;
        public bool softShadows = true;
        public float shadowStrength = 0.52f;
        public float shadowSoftness = 0.056f;
        public bool bloomEnabled = true;
        public float bloomIntensity = 0.012f;
        public float bloomThreshold = 1.95f;
        public bool tonemapEnabled = true;
        public float tonemapStrength = 0.5f;

        public bool colorGradeEnabled = true;
        public float exposure = -0.12f;
        public float contrast = 1.08f;
        public float saturation = 0.86f;
        public float colorTemperature = -0.04f; // -1 暖色〜 +1 寒色
        public float colorTint = 0.01f; // -1 緑〜 +1 マゼンタ

        public bool ssaoEnabled = true;
        public float ssaoIntensity = 1.98f;
        public float ssaoRadius = 0.15f;

        public bool screenRimEnabled = true;
        public float screenRimIntensity = 0.12f;
        public float screenRimPower = 3.5f;
        public float screenRimHue = 0.55f;

        public bool sharpenEnabled = true;
        public float sharpenAmount;
        public bool vignetteEnabled;
        public float vignetteIntensity = 0.22f;

        // アバターの明るさ（1 = マテリアルのまま。シェーダーの明るさ上限を超えて明るくする）
        public float avatarBrightness = 1f;

        // アバターの輪郭線の太さ（1 = マテリアルのまま、0 = 輪郭線なし）
        public float outlineWidth = 1f;

        // 待機ポーズ（0 = T ポーズのまま、1 = 腕を下ろし切る / 肘を曲げ切る）。既定は気を付け
        public float poseArmDown = 1f;
        public float poseElbowBend = 0f;

        // 下ろした腕の向きの微調整（度）。開きは両腕共通、前後・ひねりは左右別（左右はアバターから見た向き）
        public float poseArmSpread = DefaultPoseArmSpread;
        public float poseArmForwardLeft;
        public float poseArmForwardRight;
        public float poseArmTwistLeft;
        public float poseArmTwistRight;

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

        // 自動まばたきの動き（閉じる・閉じたまま・開く時間（秒））と、次のまばたきまでの間隔（秒、この範囲でランダム）
        public float blinkCloseSeconds = DefaultBlinkCloseSeconds;
        public float blinkHoldSeconds = DefaultBlinkHoldSeconds;
        public float blinkOpenSeconds = DefaultBlinkOpenSeconds;
        public float blinkIntervalMin = DefaultBlinkIntervalMin;
        public float blinkIntervalMax = DefaultBlinkIntervalMax;

        // 表情（ニュートラル以外）を出している間はまばたきしない（自動・トラッキングとも。表情の目の形を崩さないため）
        public bool blinkPausedByExpression;

        // 揺れもの（PhysBone 近似）
        public bool physicsEnabled = true;

        // 小物のアニメーション（小物の Animator と FX の初期状態の動き）を VRChat と同じく自動で再生する
        public bool propAnimationsEnabled = true;

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

        // 離席（ON の間は顔・手・体をアバターへ書かず待機の姿勢へ戻す。パネル下部のボタンとショートカットキーで切り替える）
        public bool trackingAway;

        // 顔が映っていない間は、手・体を含むすべてのトラッキングを止める（既定 OFF。カメラは起動したまま、顔が戻ると再開）
        public bool trackingStopWithoutFace;
        public TrackingSource trackingSource = TrackingSource.MediaPipe;
        public int trackingPort = DefaultTrackingPort;
        public bool trackingMirror = true;

        // 顔（まばたき・ウインク・パーフェクトシンク・視線）の左右だけを入れ替える（頭の傾き・体・腕と左右が合わないとき用）
        public bool trackingSwapFaceSides;

        // 外部アプリ（VMC プロトコル）の受信ポート（同梱トラッカーの trackingPort とは別。LAN から受信する）
        public int vmcPort = DefaultVmcPort;

        // 外部アプリ（VMC プロトコル）から全身（腰・脚・腕・指・体全体の位置）を受け取ってアバターを動かす
        public bool trackingVmcBody = true;

        // 全身の追従で、足の位置を送信側に合わせて床に付ける（脚の比率の違いで足が浮く・めり込むのを膝の曲げで抑える）
        public bool trackingVmcGroundFeet = true;

        // iFacialMocap を動かしている iPhone の IP アドレス（送信開始の合図の宛先。空なら未設定）
        public string iFacialMocapAddress = string.Empty;
        public float trackingBodyLean = 1f;
        public BodyMotion trackingBodyMotion = BodyMotion.Lean;
        public float trackingGaze = 1f;

        // まばたきをトラッキングする（OFF なら目の開閉は使わず自動まばたきに任せる）
        public bool trackingBlink = true;

        // 目の開きの補正（この閉じ具合までは開いた目とみなす。トラッキングで目が半開きになる人向け、0 = 補正なし）
        public float trackingEyeOpenOffset;

        // まばたきの左右をそろえる（ウインクにしない。横を向いたときだけも選べる）
        public BlinkSyncMode trackingBlinkSync = BlinkSyncMode.None;

        // 動きのなめらかさ（高いほど頭・腕・手・表情の揺れや段差を強く抑え、カメラより遅れる。既定は最低 = カメラへの追従を優先）
        public MotionSmoothing trackingSmoothing = MotionSmoothing.Lowest;

        // 腕・手（指）のトラッキング（MediaPipe のみ）
        public bool trackingHands = true;

        // 腕の位置をアバターの体格に合わせる（手首の位置を写して IK で届かせ、顔の近くの手は顔を基準にする。OFF なら腕の向きだけ合わせる）
        public bool trackingArmMatchAvatar = true;

        // 顔の前の手を奥へ入れない（ピースなどで指が顔に重なるとき、奥行きの推定がずれても顔の手前に残す。頭の横の手はそのまま。既定 ON）
        public bool trackingKeepHandsInFront = true;

        // 上半身の向き（両肩の線から背骨・胸のひねり・左右の傾き。腕・手が ON のときだけ。MediaPipe のみ）
        public bool trackingTorso = true;

        // 上半身のひねり（縦軸まわり）を正面に固定する（左右の傾きは肩の線のまま）
        public bool trackingTorsoLockTwist;

        // カメラ目線（トラッキングの視線の代わりに、目を画面（カメラ）へ向ける。トラッキングを切っていても効く）
        public bool trackingLookAtCamera;

        // 全身モード（足を床に固定したまま、腰も上半身の傾き・移動についていかせ、膝で吸収する）
        public bool trackingPlantFeet;

        // パーフェクトシンク（MediaPipe・VMC・iFacialMocap のみ。ARKit 名の BlendShape を持つアバターの顔を直接動かす）
        public bool trackingPerfectSync = true;

        // パーフェクトシンクを有効にする条件（false = ARKit 名が MinMatchedShapes 種類以上、true = 1 種類でもあれば）
        public bool trackingPerfectSyncAnyShape;

        // パーフェクトシンクの名前系統（Auto = 一致数で自動、ArKit / Unified で固定）
        public PerfectSyncNamingMode trackingPerfectSyncNaming = PerfectSyncNamingMode.Auto;

        // パーフェクトシンクの口の開き（jawOpen）: この開きまでは閉じた口とみなし、残りを倍率で抑える・強める
        public float trackingJawOpenDeadzone;
        public float trackingJawOpenGain = 1f;

        // パーフェクトシンク中だけ、検出が弱い口・頬の動きを倍率で強める（とがらせる・口の左右・頬・唇）
        public bool trackingPerfectSyncBoost = true;
        public float trackingBoostPucker = 2f;
        public float trackingBoostMouth = 1f;
        public float trackingBoostCheekPuff = 1.5f;
        public float trackingBoostLips = 1f;

        // 頬のふくらみの推定（MediaPipe のみ）: 頬の幅が真顔より何 % 広がったら出し始め、何 % で最大にするか
        public float trackingCheekPuffStart = 1.5f;
        public float trackingCheekPuffFull = 6f;

        // パーフェクトシンクの「見る」方向（eyeLook*）の上限（1 = 今まで通り、0 = その 8 種だけ動かさない）
        public float trackingPerfectSyncEyeLookLimit = 1f;

        // パーフェクトシンクのシェイプごとのしきい値（ArKitFace.BlendShapeNames と同じ順。0 = 補正なし、1 = 動かさない）
        public float[] trackingPerfectSyncThresholds = new float[ArKitFace.BlendShapeNames.Length];

        // 表情反映（MediaPipe・VMC・iFacialMocap のみ）。表情ごとの割り当ては表情プリセット名（空欄 = 自動、"<none>" = 割り当てなし）
        public bool trackingExpressions = true;

        // 表情反映で自動で出した表情には BlendShape の上限をかけない（手動の表情・まばたき・口パク・パーフェクトシンクは対象のまま）
        public bool trackingExpressionsIgnoreLimits = true;
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

        // 表情一覧に、既定で非表示の表情（FX の小物の切り替え等から BlendShape だけを取り出したもの）も出す
        public bool showHiddenExpressions;

        // リセット・カメラ目線・スクリーンショット・離席のショートカットキー（アプリ全体。既定はテンキー 1〜7）と、背面でも使うか
        public List<ResetHotkey> resetHotkeys = ResetHotkey.Defaults();
        public bool resetHotkeysInBackground = true;

        // 一定時間ごとの自動リセット（既定 OFF）。間隔（分）と、対象（顔の向き・視線は正面付近で静止したときに取り直す）
        public bool autoResetEnabled;
        public float autoResetIntervalMinutes = DefaultAutoResetIntervalMinutes;
        public bool autoResetHead = true;
        public bool autoResetGaze = true;
        public bool autoResetExpression = true;

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

        // 表情のキャリブレーション（入力元ごとに記録した真顔と各表情の顔の数値。登録した表情だけ判定に使う）
        public List<ExpressionCalibrationSample> expressionCalibrations = new List<ExpressionCalibrationSample>();

        // 利用者が名前を付けて保存した最新ライティング（グラフィックプリセットの書き出し・読み込みに含める）
        public const int MaxLightingPresets = 32;
        public List<LightingPresetEntry> lightingPresets = new List<LightingPresetEntry>();

        // VRCast から起動するトラッカー（実行ファイルのパス（空 = 同梱版）と、使うカメラのデバイス名と番号。
        // 番号は -1 = 未設定（名前だけで解決）。同名カメラが複数あるときに番号で区別する）
        public string trackerPath = string.Empty;
        public string trackerCamera = string.Empty;
        public int trackerCameraIndex = -1;

        // カメラの傾き補正（専用キャリブで保存するクォータニオン。identity = 未補正。頭・腕・生データに同じものを掛ける）
        public float trackingTiltCorrectionX;
        public float trackingTiltCorrectionY;
        public float trackingTiltCorrectionZ;
        public float trackingTiltCorrectionW = 1f;

        // デバッグログタブの詳細ログ（トラッキングの受信統計・状態の変化など。調査時だけ ON にする想定で既定は OFF）
        public bool detailedLogging;

        // マスコットモード（この PC のローカルサーバー。プリセットには入れない）
        public string mascotLlmUrl = "http://127.0.0.1:1234/v1";
        public string mascotLlmModel = string.Empty;
        public string mascotSystemPrompt = string.Empty;
        public int mascotTtsEngine;
        public string mascotTtsUrl = "http://127.0.0.1:50032";
        public int mascotTtsSpeaker;
        public int mascotStandClip = -1;
        public int mascotWindowX = -1;
        public int mascotWindowY = -1;
        // マスコットの表示倍率（1 = 480×700。ウィンドウごと拡縮する）
        public float mascotScale = 1f;

        // マスコットへマイクで話しかける（Windows の音声認識。配信のマイク設定とは別に、既定のマイクを使う）
        public bool mascotVoiceInput;

        /// <summary>
        /// 顔（まばたき・パーフェクトシンク・視線）に使う鏡像の向き。顔の左右の入れ替えが ON なら鏡像設定と逆。
        /// </summary>
        public bool FaceMirror => trackingMirror != trackingSwapFaceSides;

        /// <summary>
        /// アバターへトラッキングを書かないなら true。
        /// 配信中でないとき、離席中、または「顔が見つからないときはトラッキングしない」で顔が映っていないとき。
        /// 同梱トラッカーはここでは止めない（顔が戻ったことを知るため受信は続ける）。
        /// </summary>
        public bool ShouldSuspendTracking(bool broadcasting, bool facePresent)
        {
            return !broadcasting || trackingAway || (trackingStopWithoutFace && !facePresent);
        }

        /// <summary>
        /// カメラの傾き補正（専用キャリブ未実施・不正値なら identity）。
        /// </summary>
        public Quaternion TrackingTiltCorrection
        {
            get
            {
                var q = new Quaternion(
                    trackingTiltCorrectionX, trackingTiltCorrectionY, trackingTiltCorrectionZ, trackingTiltCorrectionW);
                // ゼロや非有限は未補正
                float mag = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
                if (mag < 1e-8f || !TiltCorrectionFinite(q))
                {
                    return Quaternion.identity;
                }

                return q.normalized;
            }
            set
            {
                Quaternion q = value;
                float mag = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
                if (mag < 1e-8f || !TiltCorrectionFinite(q))
                {
                    q = Quaternion.identity;
                }
                else
                {
                    q = q.normalized;
                }

                trackingTiltCorrectionX = q.x;
                trackingTiltCorrectionY = q.y;
                trackingTiltCorrectionZ = q.z;
                trackingTiltCorrectionW = q.w;
            }
        }

        /// <summary>
        /// 傾き補正が identity でなければ true（解除ボタンの表示用）。
        /// </summary>
        public bool HasTrackingTiltCorrection => Quaternion.Angle(TrackingTiltCorrection, Quaternion.identity) > 0.01f;

        private static bool TiltCorrectionFinite(Quaternion q)
        {
            return !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w)
                || float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));
        }

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

            // 既存の記録（見た目を含む）を末尾へ移し、無ければ今入れたものとして新しく作る
            AvatarEntry entry = TakeAvatarEntry(avatarPath)
                ?? new AvatarEntry { avatarPath = avatarPath, addedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
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
        /// アバターのまばたきに使う BlendShape の指定を返す（未記録なら自動。複製なので書き換えても記録は変わらない）。
        /// </summary>
        public BlinkShapeOverride GetBlinkShapes(string avatarPath)
        {
            int index = FindAvatarCamera(avatarPath);
            return index >= 0 && avatarCameras[index].blinkShapes != null
                ? avatarCameras[index].blinkShapes.Clone()
                : new BlinkShapeOverride();
        }

        /// <summary>
        /// アバターの自動まばたきに使う .anim のパス（未記録・未指定なら空）。
        /// </summary>
        public string GetBlinkAnimation(string avatarPath)
        {
            int index = FindAvatarCamera(avatarPath);
            return index >= 0 ? avatarCameras[index].blinkAnimation ?? string.Empty : string.Empty;
        }

        /// <summary>
        /// アバターの自動まばたきに使う .anim のパスを記録する（空で解除）。カメラの視点を記録済みのアバターだけが対象。
        /// </summary>
        public void SetBlinkAnimation(string avatarPath, string animationPath)
        {
            int index = FindAvatarCamera(avatarPath);
            if (index >= 0)
            {
                avatarCameras[index].blinkAnimation = animationPath ?? string.Empty;
            }
        }

        /// <summary>
        /// アバターのまばたきに使う BlendShape の指定を記録する。カメラの視点を記録済みのアバターだけが対象（読込時に記録される）。
        /// </summary>
        public void SetBlinkShapes(string avatarPath, BlinkShapeOverride blinkShapes)
        {
            // 記録の無いアバター・値の無いときは対象外（視点の無い記録を作らない）
            int index = FindAvatarCamera(avatarPath);
            if (index < 0 || blinkShapes == null)
            {
                return;
            }

            // 欠けを補った複製を持つ（呼び出し側と共有しない）
            BlinkShapeOverride copy = blinkShapes.Clone();
            copy.Sanitize();
            avatarCameras[index].blinkShapes = copy;
        }

        /// <summary>
        /// アバターのパーティクルの ON/OFF を返す（未記録なら空。一覧は複製）。
        /// </summary>
        public List<ParticleToggle> GetParticleToggles(string avatarPath)
        {
            int index = FindAvatarCamera(avatarPath);
            return index >= 0
                ? new List<ParticleToggle>(avatarCameras[index].particles)
                : new List<ParticleToggle>();
        }

        /// <summary>
        /// アバターのパーティクルの ON/OFF を記録する。カメラの視点を記録済みのアバターだけが対象（読込時に記録される）。
        /// </summary>
        public void SetParticleToggles(string avatarPath, List<ParticleToggle> toggles)
        {
            // 記録の無いアバターは対象外（視点の無い記録を作らない）
            int index = FindAvatarCamera(avatarPath);
            if (index < 0)
            {
                return;
            }

            // パスの無いものは除いて複製を持つ
            avatarCameras[index].particles = toggles.FindAll(toggle => toggle != null && toggle.path != null);
        }

        /// <summary>
        /// アバターの揺れものチェーンの ON/OFF を返す（未記録なら空。一覧は複製）。
        /// </summary>
        public List<PhysBoneToggle> GetPhysBoneToggles(string avatarPath)
        {
            int index = FindAvatarCamera(avatarPath);
            return index >= 0
                ? new List<PhysBoneToggle>(avatarCameras[index].physBones)
                : new List<PhysBoneToggle>();
        }

        /// <summary>
        /// アバターの揺れものチェーンの ON/OFF を記録する。カメラの視点を記録済みのアバターだけが対象（読込時に記録される）。
        /// </summary>
        public void SetPhysBoneToggles(string avatarPath, List<PhysBoneToggle> toggles)
        {
            // 記録の無いアバターは対象外（視点の無い記録を作らない）
            int index = FindAvatarCamera(avatarPath);
            if (index < 0)
            {
                return;
            }

            // パスの無いものは除いて複製を持つ
            avatarCameras[index].physBones = toggles.FindAll(toggle => toggle != null && toggle.path != null);
        }

        /// <summary>
        /// アバターのオブジェクトの表示の ON/OFF（切り替えたものだけ）を返す（未記録なら空。一覧は複製）。
        /// </summary>
        public List<ObjectToggle> GetObjectToggles(string avatarPath)
        {
            int index = FindAvatarCamera(avatarPath);
            return index >= 0
                ? new List<ObjectToggle>(avatarCameras[index].objects)
                : new List<ObjectToggle>();
        }

        /// <summary>
        /// アバターのオブジェクトの表示の ON/OFF を記録する。カメラの視点を記録済みのアバターだけが対象（読込時に記録される）。
        /// </summary>
        public void SetObjectToggles(string avatarPath, List<ObjectToggle> toggles)
        {
            // 記録の無いアバターは対象外（視点の無い記録を作らない）
            int index = FindAvatarCamera(avatarPath);
            if (index < 0)
            {
                return;
            }

            // ルート自身（空のパス）と壊れたものは除いて複製を持つ
            avatarCameras[index].objects = toggles.FindAll(IsValidObjectToggle);
        }

        private static bool IsValidObjectToggle(ObjectToggle toggle)
        {
            return toggle != null && !string.IsNullOrEmpty(toggle.path);
        }

        /// <summary>
        /// アバターの一覧に出す画像のファイル名を返す（サムネイル用フォルダ内の名前。未設定・未記録なら空文字）。
        /// </summary>
        public string GetAvatarThumbnail(string avatarPath)
        {
            int index = FindAvatarCamera(avatarPath);
            return index >= 0 ? avatarCameras[index].thumbnail ?? string.Empty : string.Empty;
        }

        /// <summary>
        /// アバターの一覧に出す画像のファイル名を記録する（空文字で解除）。記録の無いアバター・不正な名前は false。
        /// </summary>
        public bool SetAvatarThumbnail(string avatarPath, string fileName)
        {
            // 記録の無いアバター（視点の無い記録は作らない）と、フォルダの外を指しうる名前は受け付けない
            int index = FindAvatarCamera(avatarPath);
            fileName ??= string.Empty;
            if (index < 0 || (fileName.Length > 0 && !IsThumbnailFileName(fileName)))
            {
                return false;
            }

            avatarCameras[index].thumbnail = fileName;
            return true;
        }

        /// <summary>
        /// 記録中のアバターの画像のファイル名（未設定は含まない）。使われなくなった画像を消すときに使う。
        /// </summary>
        public HashSet<string> AvatarThumbnails()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AvatarEntry entry in avatarCameras)
            {
                if (!string.IsNullOrEmpty(entry.thumbnail))
                {
                    names.Add(entry.thumbnail);
                }
            }

            return names;
        }

        /// <summary>
        /// サムネイル用フォルダ内のファイル名として使えるなら true（区切り文字・「.」「..」を含む名前は不可）。
        /// </summary>
        public static bool IsThumbnailFileName(string fileName)
        {
            return !string.IsNullOrEmpty(fileName) && fileName != "." && fileName != ".."
                && fileName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;
        }

        /// <summary>
        /// リセットのショートカットキーを返す（未割り当ては None）。
        /// </summary>
        public KeyCombo GetResetHotkey(ResetAction action)
        {
            ResetHotkey hotkey = resetHotkeys?.Find(item => item != null && item.action == action);
            return hotkey != null ? hotkey.Combo : KeyCombo.None;
        }

        /// <summary>
        /// リセットにキーの組み合わせを割り当てる（None で解除）。同じ組み合わせを使っていたほかのリセットからは外す。
        /// </summary>
        public void SetResetHotkey(ResetAction action, KeyCombo combo)
        {
            // 使えないキーは割り当てない（未割り当ては解除として受け付ける）
            if (combo.IsAssigned && !VirtualKeys.IsAssignable(combo.VirtualKey))
            {
                return;
            }

            NormalizeResetHotkeys();
            foreach (ResetHotkey hotkey in resetHotkeys)
            {
                // 対象には書き込み、1 つの組み合わせで 1 つのリセットだけを行うようほかの同じ組み合わせは外す
                if (hotkey.action == action)
                {
                    hotkey.SetCombo(combo);
                }
                else if (combo.IsAssigned && hotkey.Combo.Equals(combo))
                {
                    hotkey.SetCombo(KeyCombo.None);
                }
            }
        }

        /// <summary>
        /// その組み合わせを割り当てたリセットを探す。見つかれば true（未割り当ての組み合わせは false）。
        /// </summary>
        public bool TryFindResetHotkey(KeyCombo combo, out ResetAction action)
        {
            action = ResetAction.Head;
            ResetHotkey hotkey = combo.IsAssigned
                ? resetHotkeys?.Find(item => item != null && item.Combo.Equals(combo))
                : null;
            if (hotkey != null)
            {
                action = hotkey.action;
            }

            return hotkey != null;
        }

        /// <summary>
        /// リセットのショートカットキーを既定（テンキー 1〜7）に戻す。
        /// </summary>
        public void ResetResetHotkeys()
        {
            resetHotkeys = ResetHotkey.Defaults();
        }

        private void NormalizeResetHotkeys()
        {
            // 壊れた値・未知の種類・重複を捨て、使えないキーは未割り当てにする
            var normalized = new List<ResetHotkey>(ResetHotkey.ActionCount);
            var used = new HashSet<KeyCombo>();
            for (int i = 0; i < ResetHotkey.ActionCount; i++)
            {
                var action = (ResetAction)i;
                ResetHotkey hotkey = resetHotkeys?.Find(item => item != null && item.action == action);

                // 記録の無い種類（旧版の設定・手編集）は既定のキーで補う
                if (hotkey == null)
                {
                    hotkey = new ResetHotkey { action = action };
                    hotkey.SetCombo(ResetHotkey.DefaultCombo(action));
                }

                // 使えないキーと、先の種類と同じ組み合わせは未割り当てにする
                KeyCombo combo = hotkey.Combo;
                if (combo.IsAssigned && (!VirtualKeys.IsAssignable(combo.VirtualKey) || !used.Add(combo)))
                {
                    hotkey.SetCombo(KeyCombo.None);
                }

                normalized.Add(hotkey);
            }

            resetHotkeys = normalized;
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
        /// 記録のあるすべてのアバター（一度読み込んだもの）のパスを新しい順に返す。
        /// </summary>
        public List<string> KnownAvatars()
        {
            var paths = new List<string>(avatarCameras.Count);
            for (int i = avatarCameras.Count - 1; i >= 0; i--)
            {
                paths.Add(avatarCameras[i].avatarPath);
            }

            return paths;
        }

        /// <summary>
        /// アバターを VRCast に入れた日時（UNIX 時刻の秒・UTC）を返す。記録が無い・不明なら 0。
        /// </summary>
        public long GetAvatarAddedAt(string avatarPath)
        {
            int index = FindAvatarCamera(avatarPath);
            return index >= 0 ? avatarCameras[index].addedAt : 0;
        }

        /// <summary>
        /// アバターを入れた日時を記録する（ドロップ・参照・パス入力で読み込んだとき。記録の無いアバターは初回の記録時に入る）。
        /// onlyIfUnknown なら日時が不明な記録だけに入れる（以前の版の記録の補完用）。
        /// </summary>
        public void SetAvatarAddedAt(string avatarPath, long unixSeconds, bool onlyIfUnknown = false)
        {
            // 記録の無いアバター・負の日時は対象外。並び（最近使った順）は変えない
            int index = FindAvatarCamera(avatarPath);
            if (index < 0 || unixSeconds < 0 || (onlyIfUnknown && avatarCameras[index].addedAt > 0))
            {
                return;
            }

            avatarCameras[index].addedAt = unixSeconds;
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
            // FromJsonOverwrite は既存のリストを使い回して空にするため、上書き前に切り離す
            avatarCameras = new List<AvatarEntry>();

            // 既定値で上書き
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new AppSettings()), this);

            // 退避した値を戻す
            windowWidth = width;
            windowHeight = height;
            lastAvatarPath = avatarPath;
            avatarCameras = cameras;
        }

        /// <summary>
        /// グラフィックタブの項目だけを既定値に戻す（背景色は今のテーマの既定。ポーズ等は触らない）。
        /// </summary>
        public void ResetGraphicsToDefaults()
        {
            var defaults = new AppSettings();
            transparentBackground = defaults.transparentBackground;
            backgroundColor = DefaultBackgroundOf(darkMode);
            lightingSystem = defaults.lightingSystem;
            antiAliasingMode = defaults.antiAliasingMode;
            antiAliasing = defaults.antiAliasing;
            supersample = defaults.supersample;
            lightIntensity = defaults.lightIntensity;
            lightYaw = defaults.lightYaw;
            lightPitch = defaults.lightPitch;
            lightTemperature = defaults.lightTemperature;
            ambientIntensity = defaults.ambientIntensity;
            fillIntensity = defaults.fillIntensity;
            fillYaw = defaults.fillYaw;
            fillPitch = defaults.fillPitch;
            fillTemperature = defaults.fillTemperature;
            rimIntensity = defaults.rimIntensity;
            rimYaw = defaults.rimYaw;
            rimPitch = defaults.rimPitch;
            rimTemperature = defaults.rimTemperature;
            iblIntensity = defaults.iblIntensity;
            iblPreset = defaults.iblPreset;
            softShadows = defaults.softShadows;
            shadowStrength = defaults.shadowStrength;
            shadowSoftness = defaults.shadowSoftness;
            bloomEnabled = defaults.bloomEnabled;
            bloomIntensity = defaults.bloomIntensity;
            bloomThreshold = defaults.bloomThreshold;
            tonemapEnabled = defaults.tonemapEnabled;
            tonemapStrength = defaults.tonemapStrength;
            reflectionStrength = defaults.reflectionStrength;
            colorGradeEnabled = defaults.colorGradeEnabled;
            exposure = defaults.exposure;
            contrast = defaults.contrast;
            saturation = defaults.saturation;
            colorTemperature = defaults.colorTemperature;
            colorTint = defaults.colorTint;
            ssaoEnabled = defaults.ssaoEnabled;
            ssaoIntensity = defaults.ssaoIntensity;
            ssaoRadius = defaults.ssaoRadius;
            screenRimEnabled = defaults.screenRimEnabled;
            screenRimIntensity = defaults.screenRimIntensity;
            screenRimPower = defaults.screenRimPower;
            screenRimHue = defaults.screenRimHue;
            sharpenEnabled = defaults.sharpenEnabled;
            sharpenAmount = defaults.sharpenAmount;
            vignetteEnabled = defaults.vignetteEnabled;
            vignetteIntensity = defaults.vignetteIntensity;
            avatarBrightness = defaults.avatarBrightness;
            outlineWidth = defaults.outlineWidth;
            Sanitize();
        }

        /// <summary>
        /// パーフェクトシンクのしきい値を、BlendShape の種類数にそろえて 0〜1 に収める。
        /// 足りない分は 0（補正なし）。壊れた値も 0 にする。
        /// </summary>
        public void EnsurePerfectSyncThresholds()
        {
            int count = ArKitFace.BlendShapeNames.Length;
            if (trackingPerfectSyncThresholds == null || trackingPerfectSyncThresholds.Length != count)
            {
                var next = new float[count];
                if (trackingPerfectSyncThresholds != null)
                {
                    Array.Copy(trackingPerfectSyncThresholds, next,
                        Math.Min(trackingPerfectSyncThresholds.Length, count));
                }

                trackingPerfectSyncThresholds = next;
            }

            for (int i = 0; i < trackingPerfectSyncThresholds.Length; i++)
            {
                float value = trackingPerfectSyncThresholds[i];
                trackingPerfectSyncThresholds[i] = float.IsNaN(value) ? 0f : Mathf.Clamp(value, 0f, 1f);
            }
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
                // 入れた日時は負なら不明扱い（以前の版の記録は 0 = 不明）
                entry.addedAt = Math.Max(0, entry.addedAt);
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

                // パーティクルの ON/OFF はパスの無いものを捨てる（旧版の設定には無いので空の一覧にする）
                entry.particles ??= new List<ParticleToggle>();
                entry.particles.RemoveAll(toggle => toggle == null || toggle.path == null);

                // 揺れものチェーンの ON/OFF はパスの無いものを捨てる（旧版の設定には無いので空の一覧にする）
                entry.physBones ??= new List<PhysBoneToggle>();
                entry.physBones.RemoveAll(toggle => toggle == null || toggle.path == null);

                // オブジェクトの ON/OFF はルート自身と壊れたものを捨てる（旧版の設定には無いので空の一覧にする）
                entry.objects ??= new List<ObjectToggle>();
                entry.objects.RemoveAll(toggle => !IsValidObjectToggle(toggle));

                // 一覧の画像はフォルダの外を指しうる名前（手編集）を未設定にする（旧版の設定には無いので空にする）
                entry.thumbnail = IsThumbnailFileName(entry.thumbnail) ? entry.thumbnail : string.Empty;

                // まばたきの BlendShape の指定は欠けを未指定で補う（旧版の設定には無いので自動のままになる）
                entry.blinkShapes ??= new BlinkShapeOverride();
                entry.blinkShapes.Sanitize();
                entry.blinkAnimation ??= string.Empty;
            }

            // リセットのショートカットキーは種類ごとに 1 つへそろえる（旧版の設定には無いので既定のキーになる）
            NormalizeResetHotkeys();

            // 未知の表示言語は OS 準拠へ
            if (!Enum.IsDefined(typeof(UiLanguage), uiLanguage))
            {
                uiLanguage = UiLanguage.Auto;
            }

            // パネルの拡大率は範囲内に制限
            uiScale = Mathf.Clamp(uiScale, MinUiScale, MaxUiScale);

            // パネルの背景の不透明度も範囲内に制限
            uiOpacity = Mathf.Clamp(uiOpacity, MinUiOpacity, MaxUiOpacity);

            // 描画の FPS 上限は範囲内に制限（0 以下の古い・壊れた値は既定 60）
            if (targetFrameRate < MinTargetFrameRate || targetFrameRate > MaxTargetFrameRate)
            {
                targetFrameRate = targetFrameRate <= 0 ? 60 : Mathf.Clamp(targetFrameRate, MinTargetFrameRate, MaxTargetFrameRate);
            }
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

            // 未知のトラッカーの動作は既定（ぬるぬる）へ
            if (!Enum.IsDefined(typeof(TrackerMode), trackerMode))
            {
                trackerMode = TrackerMode.Fluid;
            }

            // 未知のなめらかさは既定（最低）へ
            if (!Enum.IsDefined(typeof(MotionSmoothing), trackingSmoothing))
            {
                trackingSmoothing = MotionSmoothing.Lowest;
            }

            // 未知のパーフェクトシンクの名前系統は自動へ
            if (!Enum.IsDefined(typeof(PerfectSyncNamingMode), trackingPerfectSyncNaming))
            {
                trackingPerfectSyncNaming = PerfectSyncNamingMode.Auto;
            }

            // 未知の垂直同期は OFF へ
            if (!Enum.IsDefined(typeof(VSyncMode), vSyncMode))
            {
                vSyncMode = VSyncMode.Off;
            }

            // トラッキング FPS は範囲内に制限（0 以下は既定 30）
            if (trackingFps < MinTrackingFps || trackingFps > MaxTrackingFps)
            {
                trackingFps = trackingFps <= 0 ? 30 : Mathf.Clamp(trackingFps, MinTrackingFps, MaxTrackingFps);
            }

            // 未知の GPU の優先設定は Windows に任せる
            if (!Enum.IsDefined(typeof(GpuPreference), gpuPreference))
            {
                gpuPreference = GpuPreference.Auto;
            }

            // null の GPU 名は直接指定なしの空文字へ
            gpuAdapter ??= string.Empty;
            // スクリーンショットの保存先は前後の空白・" を除く（null は既定の保存先の空文字へ）
            screenshotFolder = PathUtility.NormalizeInput(screenshotFolder);
            // 未知のライティング系統はレガシーへ
            if (!Enum.IsDefined(typeof(LightingSystem), lightingSystem))
            {
                lightingSystem = LightingSystem.Legacy;
            }

            // 旧「方式 = 高解像度描画」は MSAA + 高解像度へ移す
            if (antiAliasingMode == AntiAliasingMode.Supersample)
            {
                supersample = antiAliasing == AntiAliasingQuality.Off ? AntiAliasingQuality.x4 : antiAliasing;
                antiAliasingMode = AntiAliasingMode.Msaa;
                antiAliasing = AntiAliasingQuality.x4;
            }

            // 未知／未設定のアンチエイリアス方式は MSAA へ（古い設定は品質だけあり方式が 0）
            if (!Enum.IsDefined(typeof(AntiAliasingMode), antiAliasingMode)
                || antiAliasingMode == AntiAliasingMode.Unspecified)
            {
                antiAliasingMode = antiAliasing == AntiAliasingQuality.Off
                    ? AntiAliasingMode.Off
                    : AntiAliasingMode.Msaa;
            }

            // 未知の強さは 4x へ（MSAA で Off になっていたら 4x）
            if (!Enum.IsDefined(typeof(AntiAliasingQuality), antiAliasing))
            {
                antiAliasing = AntiAliasingQuality.x4;
            }
            else if (antiAliasingMode == AntiAliasingMode.Msaa && antiAliasing == AntiAliasingQuality.Off)
            {
                antiAliasing = AntiAliasingQuality.x4;
            }

            // 高解像度描画の未知値はオフへ
            if (!Enum.IsDefined(typeof(AntiAliasingQuality), supersample))
            {
                supersample = AntiAliasingQuality.Off;
            }

            // 未知の IBL プリセットはスタジオへ
            if (!Enum.IsDefined(typeof(IblPreset), iblPreset))
            {
                iblPreset = IblPreset.Studio;
            }

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
            // Fill / Rim
            fillIntensity = Mathf.Clamp(fillIntensity, 0f, MaxLightIntensity);
            fillPitch = Mathf.Clamp(fillPitch, -90f, 90f);
            fillYaw = Mathf.Repeat(fillYaw + 180f, 360f) - 180f;
            fillTemperature = Mathf.Clamp(fillTemperature, MinLightTemperature, MaxLightTemperature);
            rimIntensity = Mathf.Clamp(rimIntensity, 0f, MaxLightIntensity);
            rimPitch = Mathf.Clamp(rimPitch, -90f, 90f);
            rimYaw = Mathf.Repeat(rimYaw + 180f, 360f) - 180f;
            rimTemperature = Mathf.Clamp(rimTemperature, MinLightTemperature, MaxLightTemperature);
            // IBL・影・ポスト
            iblIntensity = Mathf.Clamp(iblIntensity, 0f, MaxIblIntensity);
            reflectionStrength = Mathf.Clamp(reflectionStrength, 0f, MaxReflectionStrength);
            shadowStrength = Mathf.Clamp(shadowStrength, 0f, MaxShadowStrength);
            shadowSoftness = Mathf.Clamp(shadowSoftness, 0f, MaxShadowSoftness);
            bloomIntensity = Mathf.Clamp(bloomIntensity, 0f, MaxBloomIntensity);
            bloomThreshold = Mathf.Clamp(bloomThreshold, 0f, MaxBloomThreshold);
            tonemapStrength = Mathf.Clamp(tonemapStrength, 0f, MaxTonemapStrength);
            exposure = Mathf.Clamp(exposure, MinExposure, MaxExposure);
            contrast = Mathf.Clamp(contrast, MinContrast, MaxContrast);
            saturation = Mathf.Clamp(saturation, 0f, MaxSaturation);
            colorTemperature = Mathf.Clamp(colorTemperature, -MaxColorBalance, MaxColorBalance);
            colorTint = Mathf.Clamp(colorTint, -MaxColorBalance, MaxColorBalance);
            ssaoIntensity = Mathf.Clamp(ssaoIntensity, 0f, MaxSsaoIntensity);
            ssaoRadius = Mathf.Clamp(ssaoRadius, 0f, MaxSsaoRadius);
            screenRimIntensity = Mathf.Clamp(screenRimIntensity, 0f, MaxScreenRimIntensity);
            screenRimPower = Mathf.Clamp(screenRimPower, 0.5f, MaxScreenRimPower);
            screenRimHue = Mathf.Repeat(screenRimHue, 1f);
            sharpenAmount = Mathf.Clamp(sharpenAmount, 0f, MaxSharpen);
            vignetteIntensity = Mathf.Clamp(vignetteIntensity, 0f, MaxVignette);
            // アバターの明るさは範囲内に制限
            avatarBrightness = Mathf.Clamp(avatarBrightness, MinAvatarBrightness, MaxAvatarBrightness);
            // 輪郭線の太さは 0〜上限に制限
            outlineWidth = Mathf.Clamp(outlineWidth, 0f, MaxOutlineWidth);
            // ポーズの度合いは 0〜1 に制限
            poseArmDown = Mathf.Clamp01(poseArmDown);
            // 肘の曲げも同様
            poseElbowBend = Mathf.Clamp01(poseElbowBend);
            // 腕の向きの微調整は範囲内に制限
            poseArmSpread = Mathf.Clamp(poseArmSpread, 0f, MaxPoseArmSpread);
            poseArmForwardLeft = Mathf.Clamp(poseArmForwardLeft, MinPoseArmForward, MaxPoseArmForward);
            poseArmForwardRight = Mathf.Clamp(poseArmForwardRight, MinPoseArmForward, MaxPoseArmForward);
            poseArmTwistLeft = Mathf.Clamp(poseArmTwistLeft, -MaxPoseArmTwist, MaxPoseArmTwist);
            poseArmTwistRight = Mathf.Clamp(poseArmTwistRight, -MaxPoseArmTwist, MaxPoseArmTwist);
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
            // 自動リセットの間隔は範囲内へ（壊れた値は既定へ）
            autoResetIntervalMinutes = float.IsNaN(autoResetIntervalMinutes) || float.IsInfinity(autoResetIntervalMinutes)
                ? DefaultAutoResetIntervalMinutes
                : Mathf.Clamp(autoResetIntervalMinutes, MinAutoResetIntervalMinutes, MaxAutoResetIntervalMinutes);
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
            // 目の開きの補正も同様
            trackingEyeOpenOffset = Mathf.Clamp(trackingEyeOpenOffset, 0f, MaxTrackingEyeOpenOffset);
            // 口・頬の強調の倍率も同様（1 未満は弱めてしまうため 1 から）
            trackingBoostPucker = Mathf.Clamp(trackingBoostPucker, 1f, MaxPerfectSyncBoost);
            trackingBoostMouth = Mathf.Clamp(trackingBoostMouth, 1f, MaxPerfectSyncBoost);
            trackingBoostCheekPuff = Mathf.Clamp(trackingBoostCheekPuff, 1f, MaxPerfectSyncBoost);
            trackingBoostLips = Mathf.Clamp(trackingBoostLips, 1f, MaxPerfectSyncBoost);
            // 自動まばたきの動きも同様（壊れた値は既定値へ。間隔の最長は最短より短くしない）
            blinkCloseSeconds = ClampOrDefault(blinkCloseSeconds, MinBlinkMoveSeconds, MaxBlinkMoveSeconds,
                DefaultBlinkCloseSeconds);
            blinkHoldSeconds = ClampOrDefault(blinkHoldSeconds, 0f, MaxBlinkHoldSeconds, DefaultBlinkHoldSeconds);
            blinkOpenSeconds = ClampOrDefault(blinkOpenSeconds, MinBlinkMoveSeconds, MaxBlinkMoveSeconds,
                DefaultBlinkOpenSeconds);
            blinkIntervalMin = ClampOrDefault(blinkIntervalMin, MinBlinkInterval, MaxBlinkInterval,
                DefaultBlinkIntervalMin);
            blinkIntervalMax = Mathf.Max(blinkIntervalMin,
                ClampOrDefault(blinkIntervalMax, MinBlinkInterval, MaxBlinkInterval, DefaultBlinkIntervalMax));
            // 口の開きの補正も同様
            trackingJawOpenDeadzone = Mathf.Clamp(trackingJawOpenDeadzone, 0f, MaxJawOpenDeadzone);
            trackingJawOpenGain = Mathf.Clamp(trackingJawOpenGain, MinJawOpenGain, MaxJawOpenGain);
            // 見る方向の上限と、シェイプごとのしきい値も 0〜1（長さが違う・欠けている配列は 0 でそろえる）
            trackingPerfectSyncEyeLookLimit = float.IsNaN(trackingPerfectSyncEyeLookLimit)
                ? 1f
                : Mathf.Clamp(trackingPerfectSyncEyeLookLimit, 0f, 1f);
            EnsurePerfectSyncThresholds();
            // 頬のふくらみの推定は、出し始めより最大が必ず大きくなるよう間隔を空ける
            trackingCheekPuffStart = Mathf.Clamp(trackingCheekPuffStart, 0f, MaxCheekWidenPercent - MinCheekWidenSpan);
            trackingCheekPuffFull = Mathf.Clamp(trackingCheekPuffFull, trackingCheekPuffStart + MinCheekWidenSpan,
                MaxCheekWidenPercent);
            // 未知のまばたきのそろえ方はそろえない扱いへ
            if (!Enum.IsDefined(typeof(BlinkSyncMode), trackingBlinkSync))
            {
                trackingBlinkSync = BlinkSyncMode.None;
            }

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
            // カメラ番号は -1 未満を未設定へ（上限はトラッカー側の 255）
            if (trackerCameraIndex < -1 || trackerCameraIndex > 255)
            {
                trackerCameraIndex = -1;
            }

            // 傾き補正は不正値を identity へ
            TrackingTiltCorrection = TrackingTiltCorrection;
            // 表情のキャリブレーションは古い要素数を直し、壊れた記録を捨てる
            expressionCalibrations ??= new List<ExpressionCalibrationSample>();
            foreach (ExpressionCalibrationSample sample in expressionCalibrations)
            {
                sample?.MigrateValues();
            }

            expressionCalibrations.RemoveAll(sample => sample == null || !sample.IsValid);
            // 利用者のライティングプリセットも壊れたものを捨て、上限を超えた古いものから落とす
            lightingPresets ??= new List<LightingPresetEntry>();
            lightingPresets.RemoveAll(entry => entry == null || !entry.IsValid);
            foreach (LightingPresetEntry entry in lightingPresets)
            {
                entry.Sanitize();
            }

            while (lightingPresets.Count > MaxLightingPresets)
            {
                lightingPresets.RemoveAt(0);
            }

            // マスコットの大きさ。未設定や壊れた値は 100%。範囲は 25%〜300%
            if (float.IsNaN(mascotScale) || float.IsInfinity(mascotScale) || mascotScale <= 0f)
            {
                mascotScale = 1f;
            }
            else
            {
                mascotScale = Mathf.Clamp(mascotScale, 0.25f, 3f);
            }
        }

        /// <summary>
        /// 今のライティングを名前付きで保存する（同じ名前があれば上書き）。空の名前は無視。
        /// </summary>
        public bool SaveLightingPreset(string presetName)
        {
            LightingPresetEntry entry = LightingPresetEntry.Capture(this, presetName);
            if (!entry.IsValid)
            {
                return false;
            }

            lightingPresets ??= new List<LightingPresetEntry>();
            for (int i = 0; i < lightingPresets.Count; i++)
            {
                LightingPresetEntry existing = lightingPresets[i];
                if (existing != null
                    && string.Equals(existing.name, entry.name, StringComparison.OrdinalIgnoreCase))
                {
                    lightingPresets[i] = entry;
                    Sanitize();
                    return true;
                }
            }

            lightingPresets.Add(entry);
            Sanitize();
            return true;
        }

        /// <summary>
        /// 保存済みライティングを適用する（見つからなければ false）。
        /// </summary>
        public bool ApplyLightingPreset(int index)
        {
            if (lightingPresets == null || index < 0 || index >= lightingPresets.Count)
            {
                return false;
            }

            LightingPresetEntry entry = lightingPresets[index];
            if (entry == null || !entry.IsValid)
            {
                return false;
            }

            entry.ApplyTo(this);
            return true;
        }

        /// <summary>
        /// 保存済みライティングを削除する。
        /// </summary>
        public bool RemoveLightingPreset(int index)
        {
            if (lightingPresets == null || index < 0 || index >= lightingPresets.Count)
            {
                return false;
            }

            lightingPresets.RemoveAt(index);
            return true;
        }

        private static float ClampOrDefault(float value, float min, float max, float fallback)
        {
            // NaN・無限大は既定値、それ以外は範囲内へ
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
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
