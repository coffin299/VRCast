using System;
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

        // 頭の位置に合わせた体の動き（傾き・移動）の強さの上限（0 = 動かさない）
        public const float MaxTrackingBodyLean = 3f;

        // 視線の強さの上限（0 = 目を動かさない）
        public const float MaxTrackingGaze = 2f;

        // 操作パネルの拡大率の範囲
        public const float MinUiScale = 0.75f;
        public const float MaxUiScale = 2f;

        public int version = CurrentVersion;
        public int windowWidth = 1280;
        public int windowHeight = 720;
        public string lastAvatarPath = string.Empty;

        // 操作パネルの表示言語と拡大率
        public UiLanguage uiLanguage = UiLanguage.Auto;
        public float uiScale = 1f;

        // 背景（透過時は alpha 0 で塗りつぶし、非透過時はこの色を不透明で使う）
        public bool transparentBackground;
        public Color backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f);

        // 仮想カメラ（VRCast Camera）への出力
        public bool virtualCameraEnabled;

        // ディレクショナルライト
        public float lightIntensity = 1f;
        public float lightYaw = -30f;
        public float lightPitch = 50f;

        // 待機ポーズ（0 = T ポーズのまま、1 = 腕を下ろし切る / 肘を曲げ切る）。既定は気を付け
        public float poseArmDown = 1f;
        public float poseElbowBend = 0f;

        // アバターの向き（度、0 = カメラ正面）
        public float avatarYaw;

        // 自動まばたき
        public bool autoBlink = true;

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

        // トラッキング（入力元のトラッカーから UDP 受信。鏡像 = 本人の動きを鏡のように反映）
        public bool trackingEnabled;
        public TrackingSource trackingSource = TrackingSource.MediaPipe;
        public int trackingPort = DefaultTrackingPort;
        public bool trackingMirror = true;
        public float trackingBodyLean = 1f;
        public BodyMotion trackingBodyMotion = BodyMotion.Lean;
        public float trackingGaze = 1f;

        // 腕・手（指）のトラッキング（MediaPipe のみ）
        public bool trackingHands = true;

        // VRCast から起動するトラッカー（実行ファイルのパス（空 = 同梱版）と、使うカメラのデバイス名）
        public string trackerPath = string.Empty;
        public string trackerCamera = string.Empty;

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
            // 未知の表示言語は OS 準拠へ
            if (!Enum.IsDefined(typeof(UiLanguage), uiLanguage))
            {
                uiLanguage = UiLanguage.Auto;
            }

            // パネルの拡大率は範囲内に制限
            uiScale = Mathf.Clamp(uiScale, MinUiScale, MaxUiScale);
            // ライト強度は 0〜上限に制限
            lightIntensity = Mathf.Clamp(lightIntensity, 0f, MaxLightIntensity);
            // 仰角は真上〜真下の範囲に制限
            lightPitch = Mathf.Clamp(lightPitch, -90f, 90f);
            // 方位角は -180〜180 に正規化
            lightYaw = Mathf.Repeat(lightYaw + 180f, 360f) - 180f;
            // ポーズの度合いは 0〜1 に制限
            poseArmDown = Mathf.Clamp01(poseArmDown);
            // 肘の曲げも同様
            poseElbowBend = Mathf.Clamp01(poseElbowBend);
            // アバターの向きは -180〜180 に正規化
            avatarYaw = Mathf.Repeat(avatarYaw + 180f, 360f) - 180f;
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
            // 上半身の傾きの強さは 0〜上限に制限
            trackingBodyLean = Mathf.Clamp(trackingBodyLean, 0f, MaxTrackingBodyLean);
            // 未知の体の動かし方は既定の傾きへ
            if (!Enum.IsDefined(typeof(BodyMotion), trackingBodyMotion))
            {
                trackingBodyMotion = BodyMotion.Lean;
            }


            // 視線の強さも同様
            trackingGaze = Mathf.Clamp(trackingGaze, 0f, MaxTrackingGaze);
            // null のパス・カメラ名は未設定扱いの空文字へ
            trackerPath ??= string.Empty;
            // カメラ名も同様
            trackerCamera ??= string.Empty;
        }
    }
}
