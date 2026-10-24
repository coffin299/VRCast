using UnityEngine;
using VRCast.Core;

namespace VRCast.Rendering
{
    /// <summary>
    /// アンチエイリアス（MSAA / FXAA）と高解像度描画。両者は独立して併用できる。
    /// 仮想カメラ等の OnRenderImage でも効くよう、必要なら一時 RT へ描いてから渡す。
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class CameraAntiAliasing : MonoBehaviour
    {
        private AppSettings _settings;
        private Material _fxaaMaterial;
        private RenderTexture _sceneRt;
        private UnityEngine.Camera _camera;

        public void Initialize(AppSettings settings)
        {
            _settings = settings;
            _camera = GetComponent<UnityEngine.Camera>();
            Apply();
        }

        /// <summary>
        /// 設定を品質・一時 RT・コンポーネントの有効状態へ反映する。
        /// </summary>
        public void Apply()
        {
            if (_settings == null)
            {
                return;
            }

            AntiAliasingMode mode = EffectiveMode();
            float scale = RenderScaleOf(_settings);
            bool needsRt = mode == AntiAliasingMode.Msaa || scale > 1.001f;
            bool needsFxaa = mode == AntiAliasingMode.Fxaa;
            enabled = needsRt || needsFxaa;

            if (mode == AntiAliasingMode.Msaa)
            {
                QualitySettings.antiAliasing = MsaaSamples();
            }
            else
            {
                QualitySettings.antiAliasing = 0;
            }

            if (_camera != null)
            {
                _camera.allowMSAA = mode == AntiAliasingMode.Msaa;
            }

            if (!enabled)
            {
                ReleaseSceneRt();
            }
            else if (needsFxaa)
            {
                EnsureMaterial();
            }
        }

        private void OnDisable()
        {
            if (_camera != null)
            {
                _camera.targetTexture = null;
            }

            ReleaseSceneRt();
        }

        private void OnDestroy()
        {
            if (_fxaaMaterial != null)
            {
                Destroy(_fxaaMaterial);
                _fxaaMaterial = null;
            }

            ReleaseSceneRt();
        }

        private void OnPreCull()
        {
            if (_settings == null || _camera == null)
            {
                return;
            }

            AntiAliasingMode mode = EffectiveMode();
            float scale = RenderScaleOf(_settings);
            bool needsRt = mode == AntiAliasingMode.Msaa || scale > 1.001f;
            if (!needsRt)
            {
                _camera.targetTexture = null;
                return;
            }

            int samples = mode == AntiAliasingMode.Msaa ? MsaaSamples() : 1;
            int width = Mathf.Max(1, Mathf.RoundToInt(Screen.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(Screen.height * scale));
            EnsureSceneRt(width, height, samples);
            _camera.targetTexture = _sceneRt;
        }

        private void OnPostRender()
        {
            if (_camera != null)
            {
                _camera.targetTexture = null;
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            // MSAA resolve・高解像度の縮小のあと、必要なら FXAA（α 保持）
            if (EffectiveMode() == AntiAliasingMode.Fxaa && EnsureMaterial())
            {
                Graphics.Blit(source, destination, _fxaaMaterial, 0);
                return;
            }

            Graphics.Blit(source, destination);
        }

        /// <summary>
        /// アバター描画の内部解像度倍率（高解像度描画オフなら 1）。
        /// </summary>
        public static float RenderScaleOf(AppSettings settings)
        {
            if (settings == null)
            {
                return 1f;
            }

            switch (settings.supersample)
            {
                case AntiAliasingQuality.x2: return 1.25f;
                case AntiAliasingQuality.x4: return 1.5f;
                case AntiAliasingQuality.x8: return 2f;
                default: return 1f;
            }
        }

        /// <summary>
        /// アバター描画の内部解像度（ウィンドウサイズ × 倍率）。
        /// </summary>
        public static void RenderSizeOf(AppSettings settings, out int width, out int height)
        {
            float scale = RenderScaleOf(settings);
            width = Mathf.Max(1, Mathf.RoundToInt(Screen.width * scale));
            height = Mathf.Max(1, Mathf.RoundToInt(Screen.height * scale));
        }

        private static AntiAliasingMode EffectiveModeOf(AppSettings settings)
        {
            AntiAliasingMode mode = settings.antiAliasingMode;
            if (mode == AntiAliasingMode.Unspecified || mode == AntiAliasingMode.Supersample)
            {
                return AntiAliasingMode.Msaa;
            }

            return mode;
        }

        private AntiAliasingMode EffectiveMode()
        {
            return _settings == null ? AntiAliasingMode.Off : EffectiveModeOf(_settings);
        }

        private int MsaaSamples()
        {
            return MsaaSamplesOf(_settings);
        }

        /// <summary>
        /// 今の設定の MSAA サンプル数。MSAA 以外は 1。
        /// </summary>
        public static int MsaaSamplesOf(AppSettings settings)
        {
            if (settings == null || EffectiveModeOf(settings) != AntiAliasingMode.Msaa)
            {
                return 1;
            }

            int samples = (int)settings.antiAliasing;
            return samples == 2 || samples == 4 || samples == 8 ? samples : 4;
        }

        /// <summary>
        /// 描いた絵を出力先へ移す。MSAA はここで解消し、高解像度は縮め、FXAA ならそれを掛ける。
        /// </summary>
        public static void BlitProcessed(AppSettings settings, RenderTexture source, RenderTexture destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            if (settings != null && EffectiveModeOf(settings) == AntiAliasingMode.Fxaa && EnsureSharedFxaa())
            {
                Graphics.Blit(source, destination, _sharedFxaa, 0);
                return;
            }

            Graphics.Blit(source, destination);
        }

        private static Material _sharedFxaa;

        private static bool EnsureSharedFxaa()
        {
            if (_sharedFxaa != null)
            {
                return true;
            }

            Shader shader = Shader.Find("Hidden/VRCast/FXAA");
            if (shader == null)
            {
                return false;
            }

            _sharedFxaa = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        private void EnsureSceneRt(int width, int height, int samples)
        {
            int aa = Mathf.Max(1, samples);
            if (_sceneRt != null && _sceneRt.width == width && _sceneRt.height == height
                && _sceneRt.antiAliasing == aa)
            {
                return;
            }

            ReleaseSceneRt();
            _sceneRt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "VRCast AntiAliasing",
                antiAliasing = aa,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _sceneRt.Create();
        }

        private void ReleaseSceneRt()
        {
            if (_sceneRt == null)
            {
                return;
            }

            _sceneRt.Release();
            Destroy(_sceneRt);
            _sceneRt = null;
        }

        private bool EnsureMaterial()
        {
            if (_fxaaMaterial != null)
            {
                return true;
            }

            Shader shader = Shader.Find("Hidden/VRCast/FXAA");
            if (shader == null)
            {
                return false;
            }

            _fxaaMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }
    }
}
