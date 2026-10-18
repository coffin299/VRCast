using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// アバターが使うマテリアルのシェーダー／キーワードから ShaderVariantCollection を作り、
    /// AssetBundle ビルド時にバリアントが削られないようにする。
    /// </summary>
    public static class ShaderVariantCollector
    {
        // 一時フォルダに保存するアセット名（Prefab と同じ bundle に入れる）
        public const string AssetFileName = "shader_variants.shadervariants";

        // Built-in RP でアプリ側のライティングが使うことが多いキーワード（マテリアルのキーワードに足して候補にする）
        // 全部の直積は爆発するので、よく使う組み合わせだけを重ねる
        private static readonly string[][] BuiltinOverlays =
        {
            Array.Empty<string>(),
            new[] { "DIRECTIONAL" },
            new[] { "DIRECTIONAL", "LIGHTPROBE_SH" },
            new[] { "DIRECTIONAL", "SHADOWS_SCREEN" },
            new[] { "DIRECTIONAL", "SHADOWS_SCREEN", "LIGHTPROBE_SH" },
            new[] { "DIRECTIONAL", "VERTEXLIGHT_ON" },
            new[] { "DIRECTIONAL", "LIGHTPROBE_SH", "VERTEXLIGHT_ON" },
            new[] { "POINT" },
            new[] { "POINT", "SHADOWS_CUBE" },
            new[] { "SPOT" },
            new[] { "SPOT", "SHADOWS_DEPTH" },
            new[] { "FOG_LINEAR" },
            new[] { "FOG_EXP" },
            new[] { "FOG_EXP2" },
            new[] { "DIRECTIONAL", "FOG_LINEAR" },
            new[] { "DIRECTIONAL", "LIGHTPROBE_SH", "FOG_LINEAR" },
            new[] { "DIRECTIONAL", "SHADOWS_SCREEN", "LIGHTPROBE_SH", "FOG_LINEAR" },
            new[] { "INSTANCING_ON" },
            new[] { "DIRECTIONAL", "INSTANCING_ON" },
            new[] { "DIRECTIONAL", "LIGHTPROBE_SH", "INSTANCING_ON" },
        };

        public struct Result
        {
            public string AssetPath;
            public string[] ShaderAssetPaths;
            public int Materials;
            public int Shaders;
            public int Variants;
        }

        /// <summary>
        /// root 以下の Renderer のマテリアルからコレクションを作り folder に保存する。
        /// シェーダーが無ければ AssetPath は null。
        /// </summary>
        public static Result Create(GameObject root, string folder)
        {
            var collection = new ShaderVariantCollection();
            var shaders = new HashSet<Shader>();
            var shaderPaths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            int materials = 0;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] shared = renderer.sharedMaterials;
                if (shared == null)
                {
                    continue;
                }

                foreach (Material material in shared)
                {
                    if (material == null || material.shader == null)
                    {
                        continue;
                    }

                    materials++;
                    shaders.Add(material.shader);
                    string shaderPath = AssetDatabase.GetAssetPath(material.shader);
                    if (!string.IsNullOrEmpty(shaderPath))
                    {
                        shaderPaths.Add(shaderPath);
                    }

                    AddMaterialVariants(collection, material);
                }
            }

            if (shaders.Count == 0)
            {
                return default;
            }

            string path = folder.TrimEnd('/', '\\') + "/" + AssetFileName;
            AssetDatabase.CreateAsset(collection, path);
            return new Result
            {
                AssetPath = path,
                ShaderAssetPaths = new List<string>(shaderPaths).ToArray(),
                Materials = materials,
                Shaders = shaders.Count,
                Variants = collection.variantCount,
            };
        }

        private static void AddMaterialVariants(ShaderVariantCollection collection, Material material)
        {
            Shader shader = material.shader;
            string[] baseKeywords = CollectKeywords(material);

            // マテリアルのキーワード＋ライティング／フォグ／インスタンスのよく使う重ね合わせを候補にする
            foreach (string[] overlay in BuiltinOverlays)
            {
                string[] keywords = MergeKeywords(baseKeywords, overlay);
                TryAddAllPasses(collection, shader, keywords);
            }

            // キーワード無しも残す（Pass によってはこちらだけが有効）
            if (baseKeywords.Length > 0)
            {
                TryAddAllPasses(collection, shader, Array.Empty<string>());
            }
        }

        private static string[] CollectKeywords(Material material)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            string[] legacy = material.shaderKeywords;
            if (legacy != null)
            {
                foreach (string keyword in legacy)
                {
                    if (!string.IsNullOrEmpty(keyword))
                    {
                        names.Add(keyword);
                    }
                }
            }

            // LocalKeyword（lilToon 等）も拾う。shaderKeywords と重複してもよい
            LocalKeyword[] enabled = material.enabledKeywords;
            if (enabled != null)
            {
                foreach (LocalKeyword keyword in enabled)
                {
                    if (!string.IsNullOrEmpty(keyword.name))
                    {
                        names.Add(keyword.name);
                    }
                }
            }

            if (material.enableInstancing)
            {
                names.Add("INSTANCING_ON");
            }

            if (names.Count == 0)
            {
                return Array.Empty<string>();
            }

            var result = new string[names.Count];
            names.CopyTo(result);
            return result;
        }

        private static string[] MergeKeywords(string[] baseKeywords, string[] overlay)
        {
            if (overlay == null || overlay.Length == 0)
            {
                return baseKeywords;
            }

            if (baseKeywords == null || baseKeywords.Length == 0)
            {
                return overlay;
            }

            var names = new SortedSet<string>(baseKeywords, StringComparer.Ordinal);
            foreach (string keyword in overlay)
            {
                names.Add(keyword);
            }

            var result = new string[names.Count];
            names.CopyTo(result);
            return result;
        }

        private static void TryAddAllPasses(ShaderVariantCollection collection, Shader shader, string[] keywords)
        {
            // 使わない Pass は ShaderVariant のコンストラクタが例外を投げるので握りつぶす
            foreach (PassType pass in Enum.GetValues(typeof(PassType)))
            {
                try
                {
                    var variant = new ShaderVariantCollection.ShaderVariant(shader, pass, keywords);
                    collection.Add(variant);
                }
                catch (ArgumentException)
                {
                    // この Pass × キーワードの組み合わせはシェーダーに無い
                }
            }
        }
    }
}
