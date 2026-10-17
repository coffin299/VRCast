using System;
using UnityEngine;

namespace VRCast.Core
{
    /// <summary>
    /// 利用者が名前を付けて保存した最新ライティングの見た目（ライト・影・IBL・ポスト一式）。
    /// </summary>
    [Serializable]
    public class LightingPresetEntry
    {
        public const int MaxNameLength = 40;

        public string name = string.Empty;

        public float lightIntensity;
        public float lightYaw;
        public float lightPitch;
        public float lightTemperature;
        public float ambientIntensity;
        public float avatarBrightness;

        public float fillIntensity;
        public float fillYaw;
        public float fillPitch;
        public float fillTemperature;

        public float rimIntensity;
        public float rimYaw;
        public float rimPitch;
        public float rimTemperature;

        public float iblIntensity;
        public IblPreset iblPreset;
        public float reflectionStrength;

        public bool softShadows;
        public float shadowStrength;
        public float shadowSoftness;

        public bool bloomEnabled;
        public float bloomIntensity;
        public float bloomThreshold;
        public bool tonemapEnabled;
        public float tonemapStrength;

        public bool colorGradeEnabled;
        public float exposure;
        public float contrast;
        public float saturation;
        public float colorTemperature;
        public float colorTint;

        public bool ssaoEnabled;
        public float ssaoIntensity;
        public float ssaoRadius;

        public bool screenRimEnabled;
        public float screenRimIntensity;
        public float screenRimPower;
        public float screenRimHue;

        public bool sharpenEnabled;
        public float sharpenAmount;
        public bool vignetteEnabled;
        public float vignetteIntensity;

        /// <summary>
        /// 名前があり、数値が有限なら true。
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return false;
                }

                return IsFinite(lightIntensity) && IsFinite(lightYaw) && IsFinite(lightPitch)
                    && IsFinite(lightTemperature) && IsFinite(ambientIntensity) && IsFinite(avatarBrightness)
                    && IsFinite(fillIntensity) && IsFinite(fillYaw) && IsFinite(fillPitch)
                    && IsFinite(fillTemperature)
                    && IsFinite(rimIntensity) && IsFinite(rimYaw) && IsFinite(rimPitch)
                    && IsFinite(rimTemperature)
                    && IsFinite(iblIntensity) && IsFinite(reflectionStrength)
                    && IsFinite(shadowStrength) && IsFinite(shadowSoftness)
                    && IsFinite(bloomIntensity) && IsFinite(bloomThreshold) && IsFinite(tonemapStrength)
                    && IsFinite(exposure) && IsFinite(contrast) && IsFinite(saturation)
                    && IsFinite(colorTemperature) && IsFinite(colorTint)
                    && IsFinite(ssaoIntensity) && IsFinite(ssaoRadius)
                    && IsFinite(screenRimIntensity) && IsFinite(screenRimPower) && IsFinite(screenRimHue)
                    && IsFinite(sharpenAmount) && IsFinite(vignetteIntensity);
            }
        }

        /// <summary>
        /// 今の設定からスナップショットを作る（名前は前後の空白を除き、長さを制限する）。
        /// </summary>
        public static LightingPresetEntry Capture(AppSettings settings, string presetName)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var entry = new LightingPresetEntry
            {
                name = NormalizeName(presetName),
                lightIntensity = settings.lightIntensity,
                lightYaw = settings.lightYaw,
                lightPitch = settings.lightPitch,
                lightTemperature = settings.lightTemperature,
                ambientIntensity = settings.ambientIntensity,
                avatarBrightness = settings.avatarBrightness,
                fillIntensity = settings.fillIntensity,
                fillYaw = settings.fillYaw,
                fillPitch = settings.fillPitch,
                fillTemperature = settings.fillTemperature,
                rimIntensity = settings.rimIntensity,
                rimYaw = settings.rimYaw,
                rimPitch = settings.rimPitch,
                rimTemperature = settings.rimTemperature,
                iblIntensity = settings.iblIntensity,
                iblPreset = settings.iblPreset,
                reflectionStrength = settings.reflectionStrength,
                softShadows = settings.softShadows,
                shadowStrength = settings.shadowStrength,
                shadowSoftness = settings.shadowSoftness,
                bloomEnabled = settings.bloomEnabled,
                bloomIntensity = settings.bloomIntensity,
                bloomThreshold = settings.bloomThreshold,
                tonemapEnabled = settings.tonemapEnabled,
                tonemapStrength = settings.tonemapStrength,
                colorGradeEnabled = settings.colorGradeEnabled,
                exposure = settings.exposure,
                contrast = settings.contrast,
                saturation = settings.saturation,
                colorTemperature = settings.colorTemperature,
                colorTint = settings.colorTint,
                ssaoEnabled = settings.ssaoEnabled,
                ssaoIntensity = settings.ssaoIntensity,
                ssaoRadius = settings.ssaoRadius,
                screenRimEnabled = settings.screenRimEnabled,
                screenRimIntensity = settings.screenRimIntensity,
                screenRimPower = settings.screenRimPower,
                screenRimHue = settings.screenRimHue,
                sharpenEnabled = settings.sharpenEnabled,
                sharpenAmount = settings.sharpenAmount,
                vignetteEnabled = settings.vignetteEnabled,
                vignetteIntensity = settings.vignetteIntensity,
            };
            entry.Sanitize();
            return entry;
        }

        /// <summary>
        /// 設定へ書き戻す（範囲外は補正。反映は呼び出し側）。
        /// </summary>
        public void ApplyTo(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Sanitize();
            settings.lightIntensity = lightIntensity;
            settings.lightYaw = lightYaw;
            settings.lightPitch = lightPitch;
            settings.lightTemperature = lightTemperature;
            settings.ambientIntensity = ambientIntensity;
            settings.avatarBrightness = avatarBrightness;
            settings.fillIntensity = fillIntensity;
            settings.fillYaw = fillYaw;
            settings.fillPitch = fillPitch;
            settings.fillTemperature = fillTemperature;
            settings.rimIntensity = rimIntensity;
            settings.rimYaw = rimYaw;
            settings.rimPitch = rimPitch;
            settings.rimTemperature = rimTemperature;
            settings.iblIntensity = iblIntensity;
            settings.iblPreset = iblPreset;
            settings.reflectionStrength = reflectionStrength;
            settings.softShadows = softShadows;
            settings.shadowStrength = shadowStrength;
            settings.shadowSoftness = shadowSoftness;
            settings.bloomEnabled = bloomEnabled;
            settings.bloomIntensity = bloomIntensity;
            settings.bloomThreshold = bloomThreshold;
            settings.tonemapEnabled = tonemapEnabled;
            settings.tonemapStrength = tonemapStrength;
            settings.colorGradeEnabled = colorGradeEnabled;
            settings.exposure = exposure;
            settings.contrast = contrast;
            settings.saturation = saturation;
            settings.colorTemperature = colorTemperature;
            settings.colorTint = colorTint;
            settings.ssaoEnabled = ssaoEnabled;
            settings.ssaoIntensity = ssaoIntensity;
            settings.ssaoRadius = ssaoRadius;
            settings.screenRimEnabled = screenRimEnabled;
            settings.screenRimIntensity = screenRimIntensity;
            settings.screenRimPower = screenRimPower;
            settings.screenRimHue = screenRimHue;
            settings.sharpenEnabled = sharpenEnabled;
            settings.sharpenAmount = sharpenAmount;
            settings.vignetteEnabled = vignetteEnabled;
            settings.vignetteIntensity = vignetteIntensity;
            settings.Sanitize();
        }

        /// <summary>
        /// 名前と数値を安全な範囲に補正する。
        /// </summary>
        public void Sanitize()
        {
            name = NormalizeName(name);
            lightIntensity = Clamp(lightIntensity, 0f, AppSettings.MaxLightIntensity, 1.2f);
            lightYaw = Clamp(lightYaw, -360f, 360f, -30f);
            lightPitch = Clamp(lightPitch, -90f, 90f, 40f);
            lightTemperature = Clamp(lightTemperature, AppSettings.MinLightTemperature,
                AppSettings.MaxLightTemperature, 6000f);
            ambientIntensity = Clamp(ambientIntensity, 0f, AppSettings.MaxAmbientIntensity, 1.2f);
            avatarBrightness = Clamp(avatarBrightness, AppSettings.MinAvatarBrightness,
                AppSettings.MaxAvatarBrightness, 1f);
            fillIntensity = Clamp(fillIntensity, 0f, AppSettings.MaxLightIntensity, 0.1f);
            fillYaw = Clamp(fillYaw, -360f, 360f, 145f);
            fillPitch = Clamp(fillPitch, -90f, 90f, 10f);
            fillTemperature = Clamp(fillTemperature, AppSettings.MinLightTemperature,
                AppSettings.MaxLightTemperature, 7200f);
            rimIntensity = Clamp(rimIntensity, 0f, AppSettings.MaxLightIntensity, 0.28f);
            rimYaw = Clamp(rimYaw, -360f, 360f, 168f);
            rimPitch = Clamp(rimPitch, -90f, 90f, -12f);
            rimTemperature = Clamp(rimTemperature, AppSettings.MinLightTemperature,
                AppSettings.MaxLightTemperature, 6800f);
            iblIntensity = Clamp(iblIntensity, 0f, AppSettings.MaxIblIntensity, 0.5f);
            if (!Enum.IsDefined(typeof(IblPreset), iblPreset))
            {
                iblPreset = IblPreset.Studio;
            }

            reflectionStrength = Clamp(reflectionStrength, 0f, AppSettings.MaxReflectionStrength, 0.14f);
            shadowStrength = Clamp(shadowStrength, 0f, AppSettings.MaxShadowStrength, 0.52f);
            shadowSoftness = Clamp(shadowSoftness, 0f, AppSettings.MaxShadowSoftness, 0.056f);
            bloomIntensity = Clamp(bloomIntensity, 0f, AppSettings.MaxBloomIntensity, 0.012f);
            bloomThreshold = Clamp(bloomThreshold, 0f, AppSettings.MaxBloomThreshold, 1.95f);
            tonemapStrength = Clamp(tonemapStrength, 0f, AppSettings.MaxTonemapStrength, 0.5f);
            exposure = Clamp(exposure, AppSettings.MinExposure, AppSettings.MaxExposure, -0.12f);
            contrast = Clamp(contrast, AppSettings.MinContrast, AppSettings.MaxContrast, 1.08f);
            saturation = Clamp(saturation, 0f, AppSettings.MaxSaturation, 0.86f);
            colorTemperature = Clamp(colorTemperature, -AppSettings.MaxColorBalance, AppSettings.MaxColorBalance,
                -0.04f);
            colorTint = Clamp(colorTint, -AppSettings.MaxColorBalance, AppSettings.MaxColorBalance, 0.01f);
            ssaoIntensity = Clamp(ssaoIntensity, 0f, AppSettings.MaxSsaoIntensity, 1.98f);
            ssaoRadius = Clamp(ssaoRadius, 0f, AppSettings.MaxSsaoRadius, 0.15f);
            screenRimIntensity = Clamp(screenRimIntensity, 0f, AppSettings.MaxScreenRimIntensity, 0.12f);
            screenRimPower = Clamp(screenRimPower, 0.5f, AppSettings.MaxScreenRimPower, 3.5f);
            screenRimHue = Mathf.Repeat(IsFinite(screenRimHue) ? screenRimHue : 0.55f, 1f);
            sharpenAmount = Clamp(sharpenAmount, 0f, AppSettings.MaxSharpen, 0f);
            vignetteIntensity = Clamp(vignetteIntensity, 0f, AppSettings.MaxVignette, 0.22f);
        }

        public static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            return trimmed.Length <= MaxNameLength ? trimmed : trimmed.Substring(0, MaxNameLength);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float Clamp(float value, float min, float max, float fallback)
        {
            return IsFinite(value) ? Mathf.Clamp(value, min, max) : fallback;
        }
    }
}
