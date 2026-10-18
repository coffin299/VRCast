using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// AssetBundle 書き出しのあいだだけ、アバターが参照するテクスチャを PC 向け形式にする。
    /// <list type="bullet">
    /// <item>TextureImporter の Standalone に ASTC / ETC が載っている場合は Automatic に直す</item>
    /// <item>VRCQuestTools が Android アクティブ時に CompressTexture(ASTC) で焼いた .asset は、
    /// DXT5 に載せ替えてファイルを一時差し替えする（GUID / .meta は維持）</item>
    /// </list>
    /// 終わったら必ず元に戻す。
    /// </summary>
    internal sealed class StandaloneTextureFixer : IDisposable
    {
        private const string StandalonePlatform = "Standalone";
        private const string TempAssetFileName = "_vrcast_tex_pc_fix.asset";

        private readonly string _tempFolder;
        private readonly List<ImporterRestore> _importers = new List<ImporterRestore>();
        private readonly List<FileRestore> _files = new List<FileRestore>();
        private bool _disposed;

        public int FixedCount => _importers.Count + _files.Count;

        private struct ImporterRestore
        {
            public string Path;
            public bool HadOverride;
            public TextureImporterPlatformSettings Settings;
        }

        private struct FileRestore
        {
            public string Path;
            public byte[] Bytes;
        }

        /// <summary>
        /// root が参照するテクスチャを調べ、モバイル専用形式を直す。
        /// tempFolder は差し替え用の一時 .asset を置く書き出しフォルダ（例: Assets/__VRCastExport）。
        /// </summary>
        public static StandaloneTextureFixer Apply(GameObject root, string tempFolder)
        {
            var fixer = new StandaloneTextureFixer(tempFolder);
            if (root != null)
            {
                fixer.ApplyInternal(root);
            }

            return fixer;
        }

        private StandaloneTextureFixer(string tempFolder)
        {
            _tempFolder = string.IsNullOrEmpty(tempFolder)
                ? "Assets/__VRCastExport"
                : tempFolder.TrimEnd('/', '\\');
        }

        private void ApplyInternal(GameObject root)
        {
            var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var textures = new List<Texture>();
            foreach (Texture texture in CollectTextures(root))
            {
                if (texture == null)
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(texture);
                if (string.IsNullOrEmpty(path) || !paths.Add(path))
                {
                    continue;
                }

                textures.Add(texture);
            }

            // 1) インポーター経由（png 等）の Standalone 上書きを直す
            foreach (Texture texture in textures)
            {
                string path = AssetDatabase.GetAssetPath(texture);
                bool runtimeBad = IsMobileGpuFormat(texture);
                MaybeFixImporter(path, runtimeBad);
            }

            if (_importers.Count > 0)
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (ImporterRestore entry in _importers)
                    {
                        AssetDatabase.ImportAsset(entry.Path, ImportAssetOptions.ForceUpdate);
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                AssetDatabase.Refresh();
            }

            // 2) VRCQuestTools 等が CompressTexture(ASTC) で焼いた .asset はインポーターが無い／直らないので、
            //    中身を DXT5 に載せ替えてファイルだけ差し替える（.meta の GUID はそのまま）
            foreach (string path in paths)
            {
                var texture2D = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture2D == null || !IsMobileTextureFormat(texture2D.format))
                {
                    continue;
                }

                // インポーター付きで再インポート後も ASTC のままなら、焼き込みアセットとして扱う
                MaybeRewriteBakedTexture(texture2D, path);
            }

            if (_files.Count > 0)
            {
                AssetDatabase.Refresh();
            }
        }

        private void MaybeFixImporter(string path, bool force)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            TextureImporterPlatformSettings current = importer.GetPlatformTextureSettings(StandalonePlatform);
            bool badFormat = IsMobileImporterFormat(current.format);
            if (!force && !badFormat)
            {
                return;
            }

            if (!force && current.overridden && current.format == TextureImporterFormat.Automatic)
            {
                return;
            }

            var backup = new ImporterRestore
            {
                Path = path,
                HadOverride = current.overridden,
                Settings = CloneSettings(current),
            };

            var fixedSettings = CloneSettings(current);
            fixedSettings.name = StandalonePlatform;
            fixedSettings.overridden = true;
            fixedSettings.format = TextureImporterFormat.Automatic;
            if (fixedSettings.maxTextureSize <= 0)
            {
                fixedSettings.maxTextureSize = 2048;
            }

            importer.SetPlatformTextureSettings(fixedSettings);
            _importers.Add(backup);
        }

        private void MaybeRewriteBakedTexture(Texture2D source, string path)
        {
            string fullPath = ToFullPath(path);
            if (!File.Exists(fullPath))
            {
                return;
            }

            Texture2D rgba;
            try
            {
                rgba = BlitToRgba32(source);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[VRCast][Exporter] Failed to decompress mobile texture for PC export: "
                    + path + " (" + exception.Message + ")");
                return;
            }

            // VRCQuestTools と同様、Windows 向けは DXT5（アルファ付き）
            EditorUtility.CompressTexture(rgba, TextureFormat.DXT5, TextureCompressionQuality.Best);
            rgba.name = source.name;
            rgba.wrapMode = source.wrapMode;
            rgba.filterMode = source.filterMode;
            rgba.anisoLevel = source.anisoLevel;

            if (!AssetDatabase.IsValidFolder(_tempFolder))
            {
                Debug.LogWarning("[VRCast][Exporter] Temp folder missing; cannot rewrite texture: " + path);
                Object.DestroyImmediate(rgba);
                return;
            }

            string tmpPath = _tempFolder + "/" + TempAssetFileName;
            if (AssetDatabase.LoadAssetAtPath<Object>(tmpPath) != null)
            {
                AssetDatabase.DeleteAsset(tmpPath);
            }

            byte[] backup = File.ReadAllBytes(fullPath);
            try
            {
                AssetDatabase.CreateAsset(rgba, tmpPath);
                AssetDatabase.SaveAssets();
                File.Copy(ToFullPath(tmpPath), fullPath, true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[VRCast][Exporter] Failed to rewrite mobile texture for PC export: "
                    + path + " (" + exception.Message + ")");
                if (File.Exists(fullPath) && backup != null)
                {
                    File.WriteAllBytes(fullPath, backup);
                }

                if (AssetDatabase.LoadAssetAtPath<Object>(tmpPath) != null)
                {
                    AssetDatabase.DeleteAsset(tmpPath);
                }

                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Object>(tmpPath) != null)
            {
                AssetDatabase.DeleteAsset(tmpPath);
            }

            _files.Add(new FileRestore { Path = path, Bytes = backup });
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (FileRestore entry in _files)
            {
                try
                {
                    File.WriteAllBytes(ToFullPath(entry.Path), entry.Bytes);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[VRCast][Exporter] Failed to restore texture file: "
                        + entry.Path + " (" + exception.Message + ")");
                }
            }

            if (_importers.Count > 0)
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (ImporterRestore entry in _importers)
                    {
                        var importer = AssetImporter.GetAtPath(entry.Path) as TextureImporter;
                        if (importer == null)
                        {
                            continue;
                        }

                        if (entry.HadOverride)
                        {
                            importer.SetPlatformTextureSettings(entry.Settings);
                        }
                        else
                        {
                            importer.ClearPlatformTextureSettings(StandalonePlatform);
                        }
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
            }

            if (_files.Count > 0 || _importers.Count > 0)
            {
                AssetDatabase.Refresh();
            }
        }

        private static Texture2D BlitToRgba32(Texture2D source)
        {
            int width = Math.Max(1, source.width);
            int height = Math.Max(1, source.height);
            // 法線等は linear のことがあるが、砂嵐回避を優先して sRGB 読み出しで足りる場合が多い
            RenderTextureReadWrite readWrite = source.isDataSRGB
                ? RenderTextureReadWrite.sRGB
                : RenderTextureReadWrite.Linear;
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, readWrite);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(width, height, TextureFormat.RGBA32, true, !source.isDataSRGB);
                copy.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                copy.Apply(true, false);
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static string ToFullPath(string assetPath)
        {
            // Unity のカレントはプロジェクトルート
            return Path.GetFullPath(assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static IEnumerable<Texture> CollectTextures(GameObject root)
        {
            var set = new HashSet<Texture>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] shared = renderer.sharedMaterials;
                if (shared == null)
                {
                    continue;
                }

                foreach (Material material in shared)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    string[] names = material.GetTexturePropertyNames();
                    if (names == null)
                    {
                        continue;
                    }

                    foreach (string name in names)
                    {
                        Texture texture = material.GetTexture(name);
                        if (texture != null)
                        {
                            set.Add(texture);
                        }
                    }
                }
            }

            foreach (Object dependency in EditorUtility.CollectDependencies(new Object[] { root }))
            {
                if (dependency is Texture texture)
                {
                    set.Add(texture);
                }
            }

            return set;
        }

        private static bool IsMobileGpuFormat(Texture texture)
        {
            if (texture is Texture2D texture2D)
            {
                return IsMobileTextureFormat(texture2D.format);
            }

            if (texture is Cubemap cubemap)
            {
                return IsMobileTextureFormat(cubemap.format);
            }

            return false;
        }

        private static bool IsMobileTextureFormat(TextureFormat format)
        {
            string name = format.ToString();
            return name.IndexOf("ASTC", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ETC", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("PVRTC", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsMobileImporterFormat(TextureImporterFormat format)
        {
            string name = format.ToString();
            return name.IndexOf("ASTC", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ETC", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("PVRTC", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static TextureImporterPlatformSettings CloneSettings(TextureImporterPlatformSettings source)
        {
            return new TextureImporterPlatformSettings
            {
                name = source.name,
                overridden = source.overridden,
                maxTextureSize = source.maxTextureSize,
                resizeAlgorithm = source.resizeAlgorithm,
                format = source.format,
                textureCompression = source.textureCompression,
                compressionQuality = source.compressionQuality,
                crunchedCompression = source.crunchedCompression,
                allowsAlphaSplitting = source.allowsAlphaSplitting,
                androidETC2FallbackOverride = source.androidETC2FallbackOverride,
            };
        }
    }
}
