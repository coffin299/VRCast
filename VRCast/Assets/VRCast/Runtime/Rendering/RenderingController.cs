using UnityEngine;
using UnityEngine.Rendering;
using VRCast.Core;

namespace VRCast.Rendering
{
    /// <summary>
    /// ライティングのプリセット（太陽光の強さ・色温度・向きと環境光の組）。
    /// </summary>
    public enum LightingPreset
    {
        Default,
        Sunny,
        Soft,
    }

    /// <summary>
    /// 背景（透過 / 単色）、解像度、太陽光（ディレクショナルライト）と環境光を設定値に従って適用する。
    /// 値の保持は AppSettings に委ね、変更は即座に反映する。
    /// </summary>
    public class RenderingController : MonoBehaviour
    {
        // 環境光の明るさ 1 のときの色（全方向から均一に当たる灰色）
        private const float AmbientBase = 0.5f;

        // カメラ正面（アバターの正面）から当てるときのワールドの向き。カメラは +Z 側から -Z を向く
        private const float FrontYaw = 180f;

        // 描画のフレームレートの上限（通常 / 軽量モード）。上限が無いと GPU を使い切ってゲーム・OBS を圧迫する
        public const int NormalFrameRate = 60;
        public const int LowLoadFrameRate = 30;

        private readonly AvatarMaterials _avatarMaterials = new AvatarMaterials();
        private UnityEngine.Camera _camera;
        private Light _light;
        private AppSettings _settings;
        private bool _forceTransparent;

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

        public float LightIntensity
        {
            get => _settings.lightIntensity;
            set
            {
                _settings.lightIntensity = Mathf.Clamp(value, 0f, AppSettings.MaxLightIntensity);
                ApplyLight();
            }
        }

        public float LightYaw
        {
            get => _settings.lightYaw;
            set
            {
                _settings.lightYaw = value;
                ApplyLight();
            }
        }

        public float LightPitch
        {
            get => _settings.lightPitch;
            set
            {
                _settings.lightPitch = Mathf.Clamp(value, -90f, 90f);
                ApplyLight();
            }
        }

        public float LightTemperature
        {
            get => _settings.lightTemperature;
            set
            {
                _settings.lightTemperature = Mathf.Clamp(
                    value, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
                ApplyLight();
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

        public bool LowLoadMode
        {
            get => _settings.lowLoadMode;
            set
            {
                _settings.lowLoadMode = value;
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

            // シーンのディレクショナルライトを使い、無ければ生成
            _light = FindDirectionalLight();
            if (_light == null)
            {
                _light = new GameObject("Directional Light").AddComponent<Light>();
                _light.type = LightType.Directional;
            }

            ApplyAll();
        }

        /// <summary>
        /// 設定値を直接書き換えた後（全設定のリセット等）に、フレームレート・背景・太陽光・環境光・アバターの明るさを反映し直す。
        /// </summary>
        public void ApplyAll()
        {
            ApplyFrameRate();
            ApplyBackground();
            ApplyLight();
            ApplyAmbient();
            _avatarMaterials.Apply(_settings.avatarBrightness);
        }

        /// <summary>
        /// 読み込んだアバターのマテリアルを登録し、保存済みの明るさを反映する。
        /// </summary>
        public void SetAvatar(GameObject instance)
        {
            _avatarMaterials.SetAvatar(instance);
            _avatarMaterials.Apply(_settings.avatarBrightness);
        }

        /// <summary>
        /// ライティングのプリセットを適用する（太陽光の強さ・色温度・向きと環境光）。
        /// </summary>
        public void ApplyPreset(LightingPreset preset)
        {
            // 既定値は AppSettings の初期値と同じ
            var defaults = new AppSettings();
            switch (preset)
            {
                case LightingPreset.Sunny:
                    // 晴れ: 強めの暖かい日差し + 明るめの環境光
                    SetLighting(1.8f, 5600f, defaults.lightYaw, 35f, 1.4f);
                    break;
                case LightingPreset.Soft:
                    // やわらか: 控えめな日差し + 強い環境光（影が薄く顔が明るい）
                    SetLighting(0.9f, 6800f, 0f, 30f, 2f);
                    break;
                default:
                    SetLighting(defaults.lightIntensity, defaults.lightTemperature, defaults.lightYaw,
                        defaults.lightPitch, defaults.ambientIntensity);
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

        private void SetLighting(float intensity, float temperature, float yaw, float pitch, float ambient)
        {
            // 値を範囲内で記録してまとめて反映
            _settings.lightIntensity = Mathf.Clamp(intensity, 0f, AppSettings.MaxLightIntensity);
            _settings.lightTemperature = Mathf.Clamp(
                temperature, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature);
            _settings.lightYaw = yaw;
            _settings.lightPitch = Mathf.Clamp(pitch, -90f, 90f);
            _settings.ambientIntensity = Mathf.Clamp(ambient, 0f, AppSettings.MaxAmbientIntensity);
            ApplyLight();
            ApplyAmbient();
        }

        private void ApplyFrameRate()
        {
            // 垂直同期に任せるとモニターのリフレッシュレート（144Hz 等）で描画するため、止めて上限を明示する
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = _settings.lowLoadMode ? LowLoadFrameRate : NormalFrameRate;
        }

        private void ApplyBackground()
        {
            // スカイボックスは使わず常に単色クリア
            _camera.clearFlags = CameraClearFlags.SolidColor;

            // 透過時は色だけ塗って alpha 0（ウィンドウは alpha を無視して色を表示、ゲームキャプチャは alpha で抜く）
            Color color = _settings.backgroundColor;
            color.a = _settings.transparentBackground || _forceTransparent ? 0f : 1f;
            _camera.backgroundColor = color;
        }

        private void ApplyLight()
        {
            // 強度・色温度・向きを反映
            _light.intensity = _settings.lightIntensity;
            _light.color = Mathf.CorrelatedColorTemperatureToRGB(_settings.lightTemperature);
            _light.transform.rotation = Quaternion.Euler(_settings.lightPitch, FrontYaw + _settings.lightYaw, 0f);
        }

        private void ApplyAmbient()
        {
            // ライティングデータを焼かないため、環境光は単色で直接与える（シェーダーが参照する SH も同じ色にする）
            Color ambient = new Color(AmbientBase, AmbientBase, AmbientBase) * _settings.ambientIntensity;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(ambient);
            RenderSettings.ambientProbe = probe;
        }

        private static Light FindDirectionalLight()
        {
            // 最初に見つかったディレクショナルライトを返す
            foreach (Light light in FindObjectsOfType<Light>())
            {
                if (light.type == LightType.Directional)
                {
                    return light;
                }
            }

            return null;
        }
    }
}
