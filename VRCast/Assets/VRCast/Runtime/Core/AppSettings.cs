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

        // 頭の位置に合わせた体の動き（傾き・移動）の強さの上限（0 = 動かさない）
        public const float MaxTrackingBodyLean = 3f;

        // 視線の強さの上限（0 = 目を動かさない）
        public const float MaxTrackingGaze = 2f;

        // 表情に切り替わるしきい値の範囲（表情の強さ 0〜1 と同じ目盛り。小さいほど弱い表情でも反応する）
        public const float MinExpressionThreshold = 0.1f;
        public const float MaxExpressionThreshold = 0.8f;

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

        // 操作パネルのダークモード（既定はライト = ベージュ。OS の設定には合わせない）
        public bool darkMode;

        // 軽量モード（描画のフレームレートとトラッカーの処理回数を下げ、ゲーム・OBS と同時に使うときの負荷を減らす）
        public bool lowLoadMode;

        // 背景色（既定は目に優しいベージュ）。非透過時は不透明で使い、透過時は alpha 0 のまま色だけ塗る
        // （ウィンドウ上では色が見え、OBS のゲームキャプチャでは抜ける）
        public bool transparentBackground;
        public Color backgroundColor = new Color(0.90f, 0.86f, 0.78f, 1f);

        // 仮想カメラ（VRCast Camera）への出力
        public bool virtualCameraEnabled;

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

        // 表情反映（MediaPipe のみ）。表情ごとの割り当ては表情プリセット名（空欄 = 自動、"<none>" = 割り当てなし）
        public bool trackingExpressions = true;
        public float trackingExpressionThreshold = 0.3f;
        public string expressionSmile = string.Empty;
        public string expressionSurprise = string.Empty;
        public string expressionAngry = string.Empty;
        public string expressionSad = string.Empty;

        // VRCast から起動するトラッカー（実行ファイルのパス（空 = 同梱版）と、使うカメラのデバイス名）
        public string trackerPath = string.Empty;
        public string trackerCamera = string.Empty;

        /// <summary>
        /// 全ての設定を既定値に戻す（ウィンドウサイズと最後に開いたアバターは保持）。
        /// 各機能が同じインスタンスを参照しているため、置き換えずに中身を上書きする。
        /// </summary>
        public void ResetToDefaults()
        {
            // 保持する値を退避
            int width = windowWidth;
            int height = windowHeight;
            string avatarPath = lastAvatarPath;

            // 既定値で上書き
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new AppSettings()), this);

            // 退避した値を戻す
            windowWidth = width;
            windowHeight = height;
            lastAvatarPath = avatarPath;
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
            // null のパス・カメラ名は未設定扱いの空文字へ
            trackerPath ??= string.Empty;
            // カメラ名も同様
            trackerCamera ??= string.Empty;
        }
    }
}
