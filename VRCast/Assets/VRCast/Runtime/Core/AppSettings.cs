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

        public int version = CurrentVersion;
        public int windowWidth = 1280;
        public int windowHeight = 720;
        public string lastAvatarPath = string.Empty;

        // 背景（透過時は alpha 0 で塗りつぶし、非透過時はこの色を不透明で使う）
        public bool transparentBackground;
        public Color backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f);

        // ディレクショナルライト
        public float lightIntensity = 1f;
        public float lightYaw = -30f;
        public float lightPitch = 50f;

        // 待機ポーズ（0 = T ポーズのまま、1 = 腕を下ろし切る / 肘を曲げ切る）
        public float poseArmDown = 1f;
        public float poseElbowBend = 0.3f;

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
        }
    }
}
