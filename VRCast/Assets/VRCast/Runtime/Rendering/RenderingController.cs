using UnityEngine;
using UnityEngine.Rendering;
using VRCast.Core;

namespace VRCast.Rendering
{
    /// <summary>
    /// ライティングのプリセット。
    /// レガシー UI は Default / Sunny / Soft。最新 UI は Default / LegacyLike。
    /// </summary>
    public enum LightingPreset
    {
        Default = 0,
        Sunny = 1,
        Soft = 2,
        /// <summary>最新向け。2.x 以前に近い見た目（三点・IBL・ポストはほぼオフ）。</summary>
        LegacyLike = 3,
    }

    /// <summary>
    /// 背景（透過 / 単色）、解像度、ライティング（レガシーまたは最新）を設定値に従って適用する。
    /// 値の保持は AppSettings に委ね、変更は即座に反映する。
    /// </summary>
    public class RenderingController : MonoBehaviour
    {
        // 環境光の明るさ 1 のときの色（全方向から均一に当たる灰色）— レガシー用
        private const float AmbientBase = 0.5f;

        // カメラ正面（アバターの正面）から当てるときのワールドの向き。カメラは +Z 側から -Z を向く
        private const float FrontYaw = 180f;

        // 軽量モード時の描画 FPS の上限（設定の targetFrameRate より高いときはこちらに抑える）
        public const int LowLoadFrameRate = 30;

        // 表示・旧ドキュメント向けの通常の既定 FPS（設定の既定値と同じ）
        public const int NormalFrameRate = 60;

        // アバター向けの影距離（短くしてセルフシャドウの解像度を稼ぐ）
        private const float ModernShadowDistance = 12f;
        private const int ModernShadowResolution = 2048;

        private readonly AvatarMaterials _avatarMaterials = new AvatarMaterials();
        private UnityEngine.Camera _camera;
        private Light _keyLight;
        private Light _fillLight;
        private Light _rimLight;
        private CameraPostProcess _postProcess;
        private CameraAntiAliasing _antiAliasing;
        private AppSettings _settings;
        private bool _forceTransparent;
        private float _savedShadowDistance = 150f;

        /// <summary>
        /// 設定に関係なく背景を透過させる（パネルを隠している間）。保存はしない。
        /// </summary>
        public bool ForceTransparent
        {
            get => _forceTransparent;
            set
            {
                _forceTransparent = value;
                ApplyBackground();
            }
        }

        public bool TransparentBackground
        {
            get => _settings.transparentBackground;
            set
            {
                _settings.transparentBackground = value;
                ApplyBackground();
            }
        }

        public Color BackgroundColor
        {
            get => _settings.backgroundColor;
            set
            {
                // 単色背景は常に不透明で扱う
                value.a = 1f;
                _settings.backgroundColor = value;
                ApplyBackground();
            }
        }

        /// <summary>
        /// 操作パネルのダークモード。切り替え時、背景色が切り替え前のテーマの既定色のままなら新しいテーマの既定色にする
        /// （利用者が選んだ色は残す）。パネルの配色は MainPanel が設定を見て作り直す。
        /// </summary>
        public bool DarkMode
        {
            get => _settings.darkMode;
            set
            {
                // 変化が無ければ何もしない
                if (value == _settings.darkMode)
                {
                    return;
                }

                bool defaultBackground = _settings.backgroundColor == DefaultBackgroundColor;
                _settings.darkMode = value;
                if (defaultBackground)
                {
                    BackgroundColor = DefaultBackgroundColor;
                }
            }
        }

        /// <summary>
        /// 現在のテーマに合った背景色の既定値。
        /// </summary>
        public Color DefaultBackgroundColor => AppSettings.DefaultBackgroundOf(_settings.darkMode);

        public LightingSystem LightingSystem
        {
            get => _settings.lightingSystem;
            set
            {
                _settings.lightingSystem = value;
                ApplyAll();
            }
        }

        public bool IsModern => _settings.lightingSystem == LightingSystem.Modern;

        public AntiAliasingMode AntiAliasingMode
        {
            get => _settings.antiAliasingMode;
            set
            {
                _settings.antiAliasingMode = value;
                _antiAliasing?.Apply();
            }
        }

        public AntiAliasingQuality AntiAliasing
        {
            get => _settings.antiAliasing;
            set
            {
                _settings.antiAliasing = value;
                _antiAliasing?.Apply();
            }
        }

        public AntiAliasingQuality Supersample
        {
            get => _settings.supersample;
            set
            {
                _settings.supersample = value;
                _antiAliasing?.Apply();
            }
        }

        public float LightIntensity
        {
            get => _settings.lightIntensity;
            set
            {
                _settings.lightIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxLightIntensity);
                ApplyLights();
            }
        }

        public float LightYaw
        {
            get => _settings.lightYaw;
            set
            {
                _settings.lightYaw = value;
                ApplyLights();
            }
        }

        public float LightPitch
        {
            get => _settings.lightPitch;
            set
            {
                _settings.lightPitch = Mathf.Clamp(value, -90f, 90f);
                ApplyLights();
            }
        }

        public float LightTemperature
        {
            get => _settings.lightTemperature;
            set
            {
                _settings.lightTemperature = Mathf.Clamp(
                    value, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
                ApplyLights();
            }
        }

        public float AmbientIntensity
        {
            get => _settings.ambientIntensity;
            set
            {
                _settings.ambientIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxAmbientIntensity);
                ApplyAmbient();
            }
        }

        public float FillIntensity
        {
            get => _settings.fillIntensity;
            set
            {
                _settings.fillIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxLightIntensity);
                ApplyLights();
            }
        }

        public float FillYaw
        {
            get => _settings.fillYaw;
            set
            {
                _settings.fillYaw = value;
                ApplyLights();
            }
        }

        public float FillPitch
        {
            get => _settings.fillPitch;
            set
            {
                _settings.fillPitch = Mathf.Clamp(value, -90f, 90f);
                ApplyLights();
            }
        }

        public float FillTemperature
        {
            get => _settings.fillTemperature;
            set
            {
                _settings.fillTemperature = Mathf.Clamp(
                    value, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
                ApplyLights();
            }
        }

        public float RimIntensity
        {
            get => _settings.rimIntensity;
            set
            {
                _settings.rimIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxLightIntensity);
                ApplyLights();
            }
        }

        public float RimYaw
        {
            get => _settings.rimYaw;
            set
            {
                _settings.rimYaw = value;
                ApplyLights();
            }
        }

        public float RimPitch
        {
            get => _settings.rimPitch;
            set
            {
                _settings.rimPitch = Mathf.Clamp(value, -90f, 90f);
                ApplyLights();
            }
        }

        public float RimTemperature
        {
            get => _settings.rimTemperature;
            set
            {
                _settings.rimTemperature = Mathf.Clamp(
                    value, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
                ApplyLights();
            }
        }

        public float IblIntensity
        {
            get => _settings.iblIntensity;
            set
            {
                _settings.iblIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxIblIntensity);
                ApplyAmbient();
            }
        }

        public IblPreset IblPreset
        {
            get => _settings.iblPreset;
            set
            {
                _settings.iblPreset = value;
                ApplyAmbient();
            }
        }

        public bool SoftShadows
        {
            get => _settings.softShadows;
            set
            {
                _settings.softShadows = value;
                ApplyLights();
                _postProcess?.SetActive(IsModern);
            }
        }

        public float ShadowStrength
        {
            get => _settings.shadowStrength;
            set
            {
                _settings.shadowStrength = Mathf.Clamp(value, 0f, AppSettings.MaxShadowStrength);
                ApplyLights();
            }
        }

        public float ShadowSoftness
        {
            get => _settings.shadowSoftness;
            set
            {
                _settings.shadowSoftness = Mathf.Clamp(value, 0f, AppSettings.MaxShadowSoftness);
                ApplyLights();
            }
        }

        public bool BloomEnabled
        {
            get => _settings.bloomEnabled;
            set
            {
                _settings.bloomEnabled = value;
                _postProcess?.SetActive(IsModern);
            }
        }

        public float BloomIntensity
        {
            get => _settings.bloomIntensity;
            set => _settings.bloomIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxBloomIntensity);
        }

        public float BloomThreshold
        {
            get => _settings.bloomThreshold;
            set => _settings.bloomThreshold = Mathf.Clamp(value, 0f, AppSettings.MaxBloomThreshold);
        }

        public bool TonemapEnabled
        {
            get => _settings.tonemapEnabled;
            set
            {
                _settings.tonemapEnabled = value;
                _postProcess?.SetActive(IsModern);
            }
        }

        public float TonemapStrength
        {
            get => _settings.tonemapStrength;
            set => _settings.tonemapStrength = Mathf.Clamp(value, 0f, AppSettings.MaxTonemapStrength);
        }

        public float ReflectionStrength
        {
            get => _settings.reflectionStrength;
            set
            {
                _settings.reflectionStrength = Mathf.Clamp(value, 0f, AppSettings.MaxReflectionStrength);
                ApplyAmbient();
            }
        }

        public bool ColorGradeEnabled
        {
            get => _settings.colorGradeEnabled;
            set => _settings.colorGradeEnabled = value;
        }

        public float Exposure
        {
            get => _settings.exposure;
            set => _settings.exposure = Mathf.Clamp(value, AppSettings.MinExposure, AppSettings.MaxExposure);
        }

        public float Contrast
        {
            get => _settings.contrast;
            set => _settings.contrast = Mathf.Clamp(value, AppSettings.MinContrast, AppSettings.MaxContrast);
        }

        public float Saturation
        {
            get => _settings.saturation;
            set => _settings.saturation = Mathf.Clamp(value, 0f, AppSettings.MaxSaturation);
        }

        public float ColorTemperatureBalance
        {
            get => _settings.colorTemperature;
            set => _settings.colorTemperature = Mathf.Clamp(
                value, -AppSettings.MaxColorBalance, AppSettings.MaxColorBalance);
        }

        public float ColorTint
        {
            get => _settings.colorTint;
            set => _settings.colorTint = Mathf.Clamp(
                value, -AppSettings.MaxColorBalance, AppSettings.MaxColorBalance);
        }

        public bool SsaoEnabled
        {
            get => _settings.ssaoEnabled;
            set
            {
                _settings.ssaoEnabled = value;
                _postProcess?.SetActive(IsModern);
            }
        }

        public float SsaoIntensity
        {
            get => _settings.ssaoIntensity;
            set => _settings.ssaoIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxSsaoIntensity);
        }

        public float SsaoRadius
        {
            get => _settings.ssaoRadius;
            set => _settings.ssaoRadius = Mathf.Clamp(value, 0f, AppSettings.MaxSsaoRadius);
        }

        public bool ScreenRimEnabled
        {
            get => _settings.screenRimEnabled;
            set
            {
                _settings.screenRimEnabled = value;
                _postProcess?.SetActive(IsModern);
            }
        }

        public float ScreenRimIntensity
        {
            get => _settings.screenRimIntensity;
            set => _settings.screenRimIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxScreenRimIntensity);
        }

        public float ScreenRimPower
        {
            get => _settings.screenRimPower;
            set => _settings.screenRimPower = Mathf.Clamp(value, 0.5f, AppSettings.MaxScreenRimPower);
        }

        public float ScreenRimHue
        {
            get => _settings.screenRimHue;
            set => _settings.screenRimHue = Mathf.Repeat(value, 1f);
        }

        public bool SharpenEnabled
        {
            get => _settings.sharpenEnabled;
            set => _settings.sharpenEnabled = value;
        }

        public float SharpenAmount
        {
            get => _settings.sharpenAmount;
            set => _settings.sharpenAmount = Mathf.Clamp(value, 0f, AppSettings.MaxSharpen);
        }

        public bool VignetteEnabled
        {
            get => _settings.vignetteEnabled;
            set => _settings.vignetteEnabled = value;
        }

        public float VignetteIntensity
        {
            get => _settings.vignetteIntensity;
            set => _settings.vignetteIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxVignette);
        }

        public float AvatarBrightness
        {
            get => _settings.avatarBrightness;
            set
            {
                _settings.avatarBrightness = Mathf.Clamp(
                    value, AppSettings.MinAvatarBrightness, AppSettings.MaxAvatarBrightness);
                _avatarMaterials.Apply(_settings.avatarBrightness);
            }
        }

        /// <summary>
        /// アバターの輪郭線の太さ（マテリアルの太さの倍率。1 = そのまま、0 = 輪郭線なし）。
        /// </summary>
        public float OutlineWidth
        {
            get => _settings.outlineWidth;
            set
            {
                _settings.outlineWidth = Mathf.Clamp(value, 0f, AppSettings.MaxOutlineWidth);
                _avatarMaterials.ApplyOutline(_settings.outlineWidth);
            }
        }

        /// <summary>
        /// 表示中のアバターに太さを変えられる輪郭線があるか。
        /// </summary>
        public bool HasAvatarOutline => _avatarMaterials.HasOutline;

        public bool LowLoadMode
        {
            get => _settings.lowLoadMode;
            set
            {
                _settings.lowLoadMode = value;
                ApplyFrameRate();
            }
        }

        /// <summary>
        /// 垂直同期の方式。On のときモニターのリフレッシュに合わせ、FPS 上限は使わない。
        /// Fast Sync は NVIDIA ドライバーへも書く（呼び出し側で NvidiaVerticalSync.Apply する）。
        /// </summary>
        public VSyncMode VSyncMode
        {
            get => _settings.vSyncMode;
            set
            {
                _settings.vSyncMode = value;
                ApplyFrameRate();
            }
        }

        /// <summary>
        /// 描画の FPS 上限（VSync OFF のとき。軽量モードならこれと LowLoadFrameRate の小さい方）。
        /// </summary>
        public int TargetFrameRate
        {
            get => _settings.targetFrameRate;
            set
            {
                _settings.targetFrameRate = Mathf.Clamp(value, AppSettings.MinTargetFrameRate, AppSettings.MaxTargetFrameRate);
                ApplyFrameRate();
            }
        }

        public int Width => Screen.width;
        public int Height => Screen.height;

        public void Initialize(UnityEngine.Camera targetCamera, AppSettings settings)
        {
            _camera = targetCamera;
            _settings = settings;

            // 配信中にフォーカスが外れても描画を止めない
            Application.runInBackground = true;

            _savedShadowDistance = QualitySettings.shadowDistance;
            // テクスチャの斜め面をはっきりさせる（明瞭さ）
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            EnsureLights();

            _antiAliasing = _camera.GetComponent<CameraAntiAliasing>();
            if (_antiAliasing == null)
            {
                _antiAliasing = _camera.gameObject.AddComponent<CameraAntiAliasing>();
            }

            _antiAliasing.Initialize(_settings);

            _postProcess = _camera.GetComponent<CameraPostProcess>();
            if (_postProcess == null)
            {
                _postProcess = _camera.gameObject.AddComponent<CameraPostProcess>();
            }

            _postProcess.Initialize(_settings);
            ApplyAll();
        }

        /// <summary>
        /// 設定値を直接書き換えた後（全設定のリセット等）に、フレームレート・背景・ライト・環境光・アバターの明るさ・輪郭線を反映し直す。
        /// </summary>
        public void ApplyAll()
        {
            ApplyFrameRate();
            ApplyBackground();
            ApplyLights();
            ApplyAmbient();
            ApplyAvatarMaterials();
            _antiAliasing?.Apply();
            _postProcess?.SetActive(IsModern);
        }

        /// <summary>
        /// 読み込んだアバターのマテリアルを登録し、保存済みの明るさと輪郭線の太さを反映する。
        /// </summary>
        public void SetAvatar(GameObject instance)
        {
            _avatarMaterials.SetAvatar(instance);
            ApplyAvatarMaterials();
        }

        private void ApplyAvatarMaterials()
        {
            // 明るさと輪郭線の太さはどちらも登録時の元の値に倍率を掛ける
            _avatarMaterials.Apply(_settings.avatarBrightness);
            _avatarMaterials.ApplyOutline(_settings.outlineWidth);
        }

        /// <summary>
        /// ライティングのプリセットを適用する。
        /// </summary>
        public void ApplyPreset(LightingPreset preset)
        {
            var defaults = new AppSettings();
            if (!IsModern)
            {
                switch (preset)
                {
                    case LightingPreset.Sunny:
                        SetLegacyLighting(1.8f, 5600f, defaults.lightYaw, 35f, 1.4f);
                        break;
                    case LightingPreset.Soft:
                        SetLegacyLighting(0.9f, 6800f, 0f, 30f, 2f);
                        break;
                    default:
                        SetLegacyLighting(defaults.lightIntensity, defaults.lightTemperature, defaults.lightYaw,
                            defaults.lightPitch, defaults.ambientIntensity);
                        break;
                }

                return;
            }

            switch (preset)
            {
                case LightingPreset.LegacyLike:
                    // 2.x と同じ太陽光・環境光だけ。Fill / Rim / IBL / ポストは足さない（白みの原因だった）
                    SetModernLighting(
                        keyIntensity: defaults.lightIntensity, keyTemp: defaults.lightTemperature,
                        keyYaw: defaults.lightYaw, keyPitch: defaults.lightPitch,
                        ambient: defaults.ambientIntensity,
                        fillIntensity: 0f, fillTemp: 6500f, fillYaw: 140f, fillPitch: 15f,
                        rimIntensity: 0f, rimTemp: 6500f, rimYaw: 160f, rimPitch: -5f,
                        ibl: 0f, IblPreset.Studio, softShadows: false, shadowStrength: 0.7f, shadowSoftness: 0.4f,
                        bloom: false, bloomIntensity: 0.02f, bloomThreshold: 1.85f, tonemap: false, tonemapStrength: 0.6f,
                        reflection: 0f);
                    _settings.avatarBrightness = 1f;
                    SetModernScreenFx(ssao: 0f, ssaoRadius: 0.38f, screenRim: 0f, rimPower: 4f, rimHue: 0.55f,
                        exposure: 0f, contrast: 1f, saturation: 1f, temperature: 0f, tint: 0f);
                    _settings.ssaoEnabled = false;
                    _settings.screenRimEnabled = false;
                    _settings.colorGradeEnabled = false;
                    _settings.sharpenEnabled = false;
                    _settings.vignetteEnabled = false;
                    break;
                default:
                    // 既定＝「影ver2」（利用者プリセット由来。Key／Ambient はレガシー共有既定と別）
                    SetModernLighting(
                        keyIntensity: 1.05f, keyTemp: 5800f, keyYaw: -22f, keyPitch: 34f,
                        ambient: 0.14f,
                        fillIntensity: defaults.fillIntensity, fillTemp: defaults.fillTemperature,
                        fillYaw: defaults.fillYaw, fillPitch: defaults.fillPitch,
                        rimIntensity: defaults.rimIntensity, rimTemp: defaults.rimTemperature,
                        rimYaw: defaults.rimYaw, rimPitch: defaults.rimPitch,
                        ibl: defaults.iblIntensity, defaults.iblPreset, defaults.softShadows,
                        defaults.shadowStrength, defaults.shadowSoftness, defaults.bloomEnabled, defaults.bloomIntensity,
                        defaults.bloomThreshold, defaults.tonemapEnabled, defaults.tonemapStrength,
                        defaults.reflectionStrength);
                    _settings.avatarBrightness = 1.23f;
                    SetModernScreenFx(
                        defaults.ssaoIntensity, defaults.ssaoRadius, defaults.screenRimIntensity,
                        defaults.screenRimPower, defaults.screenRimHue, defaults.exposure, defaults.contrast,
                        defaults.saturation, defaults.colorTemperature, defaults.colorTint);
                    _settings.sharpenEnabled = defaults.sharpenEnabled;
                    _settings.sharpenAmount = defaults.sharpenAmount;
                    _settings.vignetteEnabled = false;
                    break;
            }
        }

        /// <summary>
        /// ウィンドウ解像度を変更し、設定にも記録する。Editor では記録のみ。
        /// </summary>
        public void SetResolution(int width, int height)
        {
            // 下限で補正して記録
            _settings.windowWidth = Mathf.Max(AppSettings.MinWindowSize, width);
            _settings.windowHeight = Mathf.Max(AppSettings.MinWindowSize, height);

            // Editor の Game View は変更しない
            if (!Application.isEditor)
            {
                Screen.SetResolution(_settings.windowWidth, _settings.windowHeight, FullScreenMode.Windowed);
            }
        }

        private void SetLegacyLighting(float intensity, float temperature, float yaw, float pitch, float ambient)
        {
            _settings.lightIntensity = Mathf.Clamp(intensity, 0f, AppSettings.MaxLightIntensity);
            _settings.lightTemperature = Mathf.Clamp(
                temperature, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
            _settings.lightYaw = yaw;
            _settings.lightPitch = Mathf.Clamp(pitch, -90f, 90f);
            _settings.ambientIntensity = Mathf.Clamp(ambient, 0f, AppSettings.MaxAmbientIntensity);
            ApplyLights();
            ApplyAmbient();
        }

        private void SetModernLighting(
            float keyIntensity, float keyTemp, float keyYaw, float keyPitch, float ambient,
            float fillIntensity, float fillTemp, float fillYaw, float fillPitch,
            float rimIntensity, float rimTemp, float rimYaw, float rimPitch,
            float ibl, IblPreset iblPreset, bool softShadows, float shadowStrength, float shadowSoftness,
            bool bloom, float bloomIntensity, float bloomThreshold, bool tonemap, float tonemapStrength,
            float reflection)
        {
            _settings.lightIntensity = Mathf.Clamp(keyIntensity, 0f, AppSettings.MaxLightIntensity);
            _settings.lightTemperature = Mathf.Clamp(keyTemp, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
            _settings.lightYaw = keyYaw;
            _settings.lightPitch = Mathf.Clamp(keyPitch, -90f, 90f);
            _settings.ambientIntensity = Mathf.Clamp(ambient, 0f, AppSettings.MaxAmbientIntensity);
            _settings.fillIntensity = Mathf.Clamp(fillIntensity, 0f, AppSettings.MaxLightIntensity);
            _settings.fillTemperature = Mathf.Clamp(fillTemp, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
            _settings.fillYaw = fillYaw;
            _settings.fillPitch = Mathf.Clamp(fillPitch, -90f, 90f);
            _settings.rimIntensity = Mathf.Clamp(rimIntensity, 0f, AppSettings.MaxLightIntensity);
            _settings.rimTemperature = Mathf.Clamp(rimTemp, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
            _settings.rimYaw = rimYaw;
            _settings.rimPitch = Mathf.Clamp(rimPitch, -90f, 90f);
            _settings.iblIntensity = Mathf.Clamp(ibl, 0f, AppSettings.MaxIblIntensity);
            _settings.iblPreset = iblPreset;
            _settings.reflectionStrength = Mathf.Clamp(reflection, 0f, AppSettings.MaxReflectionStrength);
            _settings.softShadows = softShadows;
            _settings.shadowStrength = Mathf.Clamp(shadowStrength, 0f, AppSettings.MaxShadowStrength);
            _settings.shadowSoftness = Mathf.Clamp(shadowSoftness, 0f, AppSettings.MaxShadowSoftness);
            _settings.bloomEnabled = bloom;
            _settings.bloomIntensity = Mathf.Clamp(bloomIntensity, 0f, AppSettings.MaxBloomIntensity);
            _settings.bloomThreshold = Mathf.Clamp(bloomThreshold, 0f, AppSettings.MaxBloomThreshold);
            _settings.tonemapEnabled = tonemap;
            _settings.tonemapStrength = Mathf.Clamp(tonemapStrength, 0f, AppSettings.MaxTonemapStrength);
            ApplyLights();
            ApplyAmbient();
            _postProcess?.SetActive(true);
        }

        private void SetModernScreenFx(
            float ssao, float ssaoRadius, float screenRim, float rimPower, float rimHue,
            float exposure, float contrast, float saturation, float temperature, float tint)
        {
            _settings.ssaoEnabled = true;
            _settings.ssaoIntensity = Mathf.Clamp(ssao, 0f, AppSettings.MaxSsaoIntensity);
            _settings.ssaoRadius = Mathf.Clamp(ssaoRadius, 0f, AppSettings.MaxSsaoRadius);
            _settings.screenRimEnabled = true;
            _settings.screenRimIntensity = Mathf.Clamp(screenRim, 0f, AppSettings.MaxScreenRimIntensity);
            _settings.screenRimPower = Mathf.Clamp(rimPower, 0.5f, AppSettings.MaxScreenRimPower);
            _settings.screenRimHue = Mathf.Repeat(rimHue, 1f);
            _settings.colorGradeEnabled = true;
            _settings.exposure = Mathf.Clamp(exposure, AppSettings.MinExposure, AppSettings.MaxExposure);
            _settings.contrast = Mathf.Clamp(contrast, AppSettings.MinContrast, AppSettings.MaxContrast);
            _settings.saturation = Mathf.Clamp(saturation, 0f, AppSettings.MaxSaturation);
            _settings.colorTemperature = Mathf.Clamp(temperature, -AppSettings.MaxColorBalance, AppSettings.MaxColorBalance);
            _settings.colorTint = Mathf.Clamp(tint, -AppSettings.MaxColorBalance, AppSettings.MaxColorBalance);
            _postProcess?.SetActive(true);
        }

        private void ApplyFrameRate()
        {
            // 通常の垂直同期: モニターのリフレッシュに合わせる（ティアリング抑制。targetFrameRate は効かない）
            if (_settings.vSyncMode == VSyncMode.On)
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
                return;
            }

            // Off / Fast Sync / VRR: アプリは待たない（VRR・Fast Sync はドライバー／モニター側がリフレッシュを合わせる）
            QualitySettings.vSyncCount = 0;
            int rate = Mathf.Clamp(_settings.targetFrameRate, AppSettings.MinTargetFrameRate, AppSettings.MaxTargetFrameRate);
            if (_settings.lowLoadMode)
            {
                rate = Mathf.Min(rate, LowLoadFrameRate);
            }

            Application.targetFrameRate = rate;
        }

        private void ApplyBackground()
        {
            // スカイボックスは使わず常に単色クリア（IBL 用 skybox があっても空は描かない）
            _camera.clearFlags = CameraClearFlags.SolidColor;

            // 透過時は色だけ塗って alpha 0（ウィンドウは alpha を無視して色を表示、ゲームキャプチャは alpha で抜く）
            Color color = _settings.backgroundColor;
            color.a = _settings.transparentBackground || _forceTransparent ? 0f : 1f;
            _camera.backgroundColor = color;
        }

        private void EnsureLights()
        {
            _keyLight = FindOrCreateDirectional("Directional Light", FindDirectionalLight());
            _fillLight = FindOrCreateDirectional("VRCast Fill Light", FindNamedLight("VRCast Fill Light"));
            _rimLight = FindOrCreateDirectional("VRCast Rim Light", FindNamedLight("VRCast Rim Light"));
        }

        private void ApplyLights()
        {
            if (_keyLight == null)
            {
                EnsureLights();
            }

            ApplyDirectional(_keyLight, _settings.lightIntensity, _settings.lightTemperature,
                _settings.lightYaw, _settings.lightPitch);

            if (IsModern)
            {
                _fillLight.enabled = true;
                _rimLight.enabled = true;
                ApplyDirectional(_fillLight, _settings.fillIntensity, _settings.fillTemperature,
                    _settings.fillYaw, _settings.fillPitch);
                ApplyDirectional(_rimLight, _settings.rimIntensity, _settings.rimTemperature,
                    _settings.rimYaw, _settings.rimPitch);

                _fillLight.shadows = LightShadows.None;
                _rimLight.shadows = LightShadows.None;

                bool shadows = _settings.softShadows;
                _keyLight.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                _keyLight.shadowStrength = Mathf.Clamp01(_settings.shadowStrength);
                // Normal Bias を上げすぎると鼻など細かいセルフシャドウが消えるので上限を抑える
                _keyLight.shadowBias = Mathf.Lerp(0.02f, 0.05f, _settings.shadowSoftness);
                _keyLight.shadowNormalBias = Mathf.Lerp(0.08f, 0.22f, _settings.shadowSoftness);
                _keyLight.shadowCustomResolution = ModernShadowResolution;

                QualitySettings.shadows = shadows ? ShadowQuality.All : ShadowQuality.Disable;
                QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
                QualitySettings.shadowDistance = ModernShadowDistance;
                QualitySettings.shadowCascades = 2;

                // 面から光源へ向かう向き（forward の逆）をポストのコンタクトシャドウへ
                _postProcess?.SetKeyLightDirection(-_keyLight.transform.forward);
            }
            else
            {
                _fillLight.enabled = false;
                _rimLight.enabled = false;
                // レガシーは影を触らない（2.x と同じ）。最新から戻したあとに残るカスタム影設定だけ外す
                _keyLight.shadowCustomResolution = -1;
                QualitySettings.shadowDistance = _savedShadowDistance;
            }
        }

        private void ApplyAmbient()
        {
            if (IsModern)
            {
                StudioIbl.Apply(_settings.iblPreset, _settings.iblIntensity, _settings.ambientIntensity,
                    _settings.reflectionStrength);
            }
            else
            {
                StudioIbl.Clear();
                // ライティングデータを焼かないため、環境光は単色で直接与える（シェーダーが参照する SH も同じ色にする）
                Color ambient = new Color(AmbientBase, AmbientBase, AmbientBase) * _settings.ambientIntensity;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = ambient;
                var probe = new SphericalHarmonicsL2();
                probe.AddAmbientLight(ambient);
                RenderSettings.ambientProbe = probe;
            }
        }

        private static void ApplyDirectional(Light light, float intensity, float temperature, float yaw, float pitch)
        {
            light.intensity = intensity;
            light.color = Mathf.CorrelatedColorTemperatureToRGB(temperature);
            light.transform.rotation = Quaternion.Euler(pitch, FrontYaw + yaw, 0f);
        }

        private static Light FindOrCreateDirectional(string objectName, Light existing)
        {
            if (existing != null)
            {
                existing.type = LightType.Directional;
                return existing;
            }

            var light = new GameObject(objectName).AddComponent<Light>();
            light.type = LightType.Directional;
            return light;
        }

        private static Light FindDirectionalLight()
        {
            foreach (Light light in FindObjectsOfType<Light>())
            {
                if (light.type == LightType.Directional
                    && light.name != "VRCast Fill Light" && light.name != "VRCast Rim Light")
                {
                    return light;
                }
            }

            return null;
        }

        private static Light FindNamedLight(string objectName)
        {
            foreach (Light light in FindObjectsOfType<Light>())
            {
                if (light.name == objectName)
                {
                    return light;
                }
            }

            return null;
        }
    }
}
