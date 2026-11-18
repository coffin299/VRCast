using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VRCast.Core;
using VRCast.Rendering;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 画像認識の 1 枚だけ、トゥーンと輪郭線を外して顔のテクスチャが残るなめらかな陰影で描く。
    /// MediaPipe は写真の顔で学習しているため、セル塗り・輪郭線・色のついたライトのままだと顔を見失いやすい。
    /// 画面に映しているアバターは変えず、Dispose でマテリアル・ライト・背景を戻す。
    /// </summary>
    public sealed class FaceRecognitionLook : IDisposable
    {
        // 認識用の正面ライト（少し上から。鼻と眉に写真らしい陰影が残る）
        private static readonly Quaternion LightTilt = Quaternion.Euler(16f, -10f, 0f);

        // 顔の色がテクスチャに無いときの肌色と、髪の輪郭が残る灰色の背景
        private static readonly Color SkinFallback = new Color(0.86f, 0.67f, 0.56f, 1f);
        private static readonly Color Backdrop = new Color(0.55f, 0.57f, 0.60f, 1f);
        private static readonly Color Ambient = new Color(0.40f, 0.38f, 0.36f, 1f);

        // 主色・アルベド（lilToon / MToon / Poiyomi / Standard）
        private static readonly string[] ColorProperties = { "_Color", "_BaseColor", "_MainColor" };
        private static readonly string[] TextureProperties = { "_MainTex", "_BaseMap", "_BaseColorMap" };

        private readonly List<RendererSwap> _renderers = new List<RendererSwap>();
        private readonly List<Material> _created = new List<Material>();
        private readonly List<LightState> _lights = new List<LightState>();

        private Camera _camera;
        private CameraClearFlags _clearFlags;
        private Color _background;
        private CameraPostProcess _postProcess;
        private bool _postProcessEnabled;
        private AmbientMode _ambientMode;
        private Color _ambientLight;
        private float _ambientIntensity;
        private float _reflectionIntensity;
        private GameObject _lightObject;
        private bool _sceneApplied;
        private bool _restored;

        /// <summary>
        /// 認識用の見た目にする。使い終わったら Dispose する。
        /// </summary>
        public static FaceRecognitionLook Apply(Camera camera, GameObject avatar)
        {
            var look = new FaceRecognitionLook();
            try
            {
                look.ApplyNow(camera, avatar);
                return look;
            }
            catch
            {
                // 途中で失敗しても、消したライトや差し替えたマテリアルを残さない
                look.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 差し替えたマテリアルと、ライト・背景・画面効果を元に戻す。
        /// </summary>
        public void Dispose()
        {
            if (_restored)
            {
                return;
            }

            _restored = true;
            foreach (RendererSwap swap in _renderers)
            {
                if (swap.Renderer != null)
                {
                    swap.Renderer.sharedMaterials = swap.Original;
                }
            }

            foreach (Material material in _created)
            {
                if (material != null)
                {
                    UnityEngine.Object.Destroy(material);
                }
            }

            foreach (LightState state in _lights)
            {
                if (state.Light == null)
                {
                    continue;
                }

                state.Light.enabled = state.Enabled;
                state.Light.intensity = state.Intensity;
                state.Light.color = state.Color;
                state.Light.shadows = state.Shadows;
                state.Light.transform.rotation = state.Rotation;
            }

            if (_lightObject != null)
            {
                UnityEngine.Object.Destroy(_lightObject);
            }

            if (!_sceneApplied)
            {
                return;
            }

            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientLight = _ambientLight;
            RenderSettings.ambientIntensity = _ambientIntensity;
            RenderSettings.reflectionIntensity = _reflectionIntensity;

            if (_camera != null)
            {
                _camera.clearFlags = _clearFlags;
                _camera.backgroundColor = _background;
            }

            if (_postProcess != null)
            {
                _postProcess.enabled = _postProcessEnabled;
            }
        }

        private void ApplyNow(Camera camera, GameObject avatar)
        {
            _camera = camera;
            ApplyLighting(camera);
            if (avatar != null)
            {
                ApplyMaterials(avatar);
            }
        }

        private void ApplyLighting(Camera camera)
        {
            // 今のライトは全部控え、認識中は正面からの白いライトだけにする
            foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                _lights.Add(new LightState(light));
                light.enabled = false;
            }

            _lightObject = new GameObject("VRCast Face Recognition Light")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            Light key = _lightObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = Color.white;
            key.intensity = 1.05f;
            key.shadows = LightShadows.None;
            key.transform.rotation = camera.transform.rotation * LightTilt;

            _ambientMode = RenderSettings.ambientMode;
            _ambientLight = RenderSettings.ambientLight;
            _ambientIntensity = RenderSettings.ambientIntensity;
            _reflectionIntensity = RenderSettings.reflectionIntensity;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Ambient;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.reflectionIntensity = 0f;

            // 背景色と色調補正は認識画像に焼かない（髪の輪郭が残る灰色にする）
            _clearFlags = camera.clearFlags;
            _background = camera.backgroundColor;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Backdrop;

            _postProcess = camera.GetComponent<CameraPostProcess>();
            if (_postProcess != null)
            {
                _postProcessEnabled = _postProcess.enabled;
                _postProcess.enabled = false;
            }

            _sceneApplied = true;
        }

        private void ApplyMaterials(GameObject avatar)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                VRCastLog.Warning("PerfectSync", "Standard shader was not found. Face recognition keeps the toon materials.");
                return;
            }

            // 映っているメッシュだけ差し替える（非表示の衣装は触らない）
            foreach (Renderer renderer in avatar.GetComponentsInChildren<Renderer>(false))
            {
                Material[] original = renderer.sharedMaterials;
                var replaced = new Material[original.Length];
                for (int i = 0; i < original.Length; i++)
                {
                    replaced[i] = CreateFaceMaterial(shader, original[i]);
                }

                _renderers.Add(new RendererSwap(renderer, original));
                renderer.sharedMaterials = replaced;
            }
        }

        private Material CreateFaceMaterial(Shader shader, Material source)
        {
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _created.Add(material);

            // 輪郭線だけのマテリアルは描かない（顔の縁の黒い線が認識を邪魔する）
            if (source != null && IsOutlineOnly(source))
            {
                material.SetFloat("_Mode", 1f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetFloat("_Cutoff", 2f);
                material.SetColor("_Color", Color.clear);
                material.renderQueue = 2450;
                return material;
            }

            CopyAlbedo(material, source);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", 0.12f);
            if (source != null && source.HasProperty("_Cull"))
            {
                material.SetInt("_Cull", source.GetInt("_Cull"));
            }

            ApplyAlpha(material, source);
            return material;
        }

        private static void CopyAlbedo(Material material, Material source)
        {
            Texture texture = null;
            string textureProperty = null;
            if (source != null)
            {
                foreach (string name in TextureProperties)
                {
                    if (!source.HasProperty(name))
                    {
                        continue;
                    }

                    texture = source.GetTexture(name);
                    if (texture != null)
                    {
                        textureProperty = name;
                        break;
                    }
                }
            }

            material.SetTexture("_MainTex", texture);
            if (textureProperty != null)
            {
                material.SetTextureScale("_MainTex", source.GetTextureScale(textureProperty));
                material.SetTextureOffset("_MainTex", source.GetTextureOffset(textureProperty));
            }

            // 明るさ倍率で 1 を超えた色は、色味を残したまま 1 までに戻す（白飛びした顔を渡さない）
            Color tint = SkinFallback;
            if (source != null)
            {
                foreach (string name in ColorProperties)
                {
                    if (!source.HasProperty(name))
                    {
                        continue;
                    }

                    tint = source.GetColor(name);
                    float max = Mathf.Max(tint.r, tint.g, tint.b);
                    if (max > 1f)
                    {
                        tint /= max;
                    }

                    if (texture == null && max < 0.15f)
                    {
                        tint = SkinFallback;
                    }

                    break;
                }
            }

            tint.a = source != null && source.HasProperty("_Color") ? source.GetColor("_Color").a : 1f;
            material.SetColor("_Color", tint);
        }

        private static void ApplyAlpha(Material material, Material source)
        {
            // まつ毛・髪の透けは残す（不透明にすると目の上に板が乗る）
            bool transparent = source != null && source.renderQueue >= 3000;
            bool cutout = source != null && !transparent && source.renderQueue >= 2450;
            if (transparent)
            {
                material.SetFloat("_Mode", 2f);
                material.EnableKeyword("_ALPHABLEND_ON");
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.renderQueue = source.renderQueue;
                return;
            }

            if (cutout)
            {
                material.SetFloat("_Mode", 1f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetInt("_ZWrite", 1);
                material.SetFloat("_Cutoff", source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0.5f);
                material.renderQueue = Mathf.Max(source.renderQueue, 2450);
                return;
            }

            material.SetFloat("_Mode", 0f);
            material.SetInt("_ZWrite", 1);
        }

        private static bool IsOutlineOnly(Material source)
        {
            string shaderName = source.shader != null ? source.shader.name : string.Empty;
            return shaderName.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0
                || source.name.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private readonly struct RendererSwap
        {
            public RendererSwap(Renderer renderer, Material[] original)
            {
                Renderer = renderer;
                Original = original;
            }

            public Renderer Renderer { get; }
            public Material[] Original { get; }
        }

        private readonly struct LightState
        {
            public LightState(Light light)
            {
                Light = light;
                Enabled = light.enabled;
                Intensity = light.intensity;
                Color = light.color;
                Shadows = light.shadows;
                Rotation = light.transform.rotation;
            }

            public Light Light { get; }
            public bool Enabled { get; }
            public float Intensity { get; }
            public Color Color { get; }
            public LightShadows Shadows { get; }
            public Quaternion Rotation { get; }
        }
    }
}
