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

        public struct Result
        {
            public string AssetPath;
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
                Materials = materials,
                Shaders = shaders.Count,
                Variants = collection.variantCount,
            };
        }

        private static void AddMaterialVariants(ShaderVariantCollection collection, Material material)
        {
            Shader shader = material.shader;
            string[] keywords = material.shaderKeywords ?? Array.Empty<string>();

            // マテリアルのキーワード付きと、キーワード無しの両方を候補にする
            // （Pass によっては片方だけが有効）
            TryAddAllPasses(collection, shader, keywords);
            if (keywords.Length > 0)
            {
                TryAddAllPasses(collection, shader, Array.Empty<string>());
            }
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
