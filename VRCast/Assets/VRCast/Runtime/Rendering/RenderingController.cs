using UnityEngine;
using VRCast.Core;

namespace VRCast.Rendering
{
    /// <summary>
    /// 背景（透過 / 単色）、解像度、ディレクショナルライトを設定値に従って適用する。
    /// 値の保持は AppSettings に委ね、変更は即座に反映する。
    /// </summary>
    public class RenderingController : MonoBehaviour
    {
        // 透過時の背景色（OBS ゲームキャプチャの「透過を許可」で alpha 0 が抜ける）
        private static readonly Color TransparentColor = new Color(0f, 0f, 0f, 0f);

        private UnityEngine.Camera _camera;
        private Light _light;
        private AppSettings _settings;

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

            ApplyBackground();
            ApplyLight();
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

        private void ApplyBackground()
        {
            // スカイボックスは使わず常に単色クリア（透過時は alpha 0）
            _camera.clearFlags = CameraClearFlags.SolidColor;
            Color opaque = _settings.backgroundColor;
            opaque.a = 1f;
            _camera.backgroundColor = _settings.transparentBackground ? TransparentColor : opaque;
        }

        private void ApplyLight()
        {
            // 強度と向きを反映
            _light.intensity = _settings.lightIntensity;
            _light.transform.rotation = Quaternion.Euler(_settings.lightPitch, _settings.lightYaw, 0f);
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
