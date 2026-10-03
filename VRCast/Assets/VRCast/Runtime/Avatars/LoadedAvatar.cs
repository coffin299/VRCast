using System;
using UnityEngine;
using VRCast.AvatarFormat;
using Object = UnityEngine.Object;

namespace VRCast.Avatars
{
    /// <summary>
    /// シーンに生成済みのアバターと、その AssetBundle の組。Dispose で両方を解放する。
    /// </summary>
    public sealed class LoadedAvatar : IDisposable
    {
        private AssetBundle _bundle;

        public GameObject Instance { get; private set; }
        public Animator Animator { get; }
        public AvatarManifest Manifest { get; }
        public string SourcePath { get; }
        public int RendererCount { get; }

        public bool IsHumanoid => Animator != null && Animator.isHuman;

        public LoadedAvatar(GameObject instance, AssetBundle bundle, AvatarManifest manifest, string sourcePath)
        {
            Instance = instance;
            _bundle = bundle;
            Manifest = manifest;
            SourcePath = sourcePath;

            // ルートの Animator と描画対象数を記録
            Animator = instance.GetComponent<Animator>();
            RendererCount = instance.GetComponentsInChildren<Renderer>(true).Length;
        }

        /// <summary>
        /// 全 Renderer を包む境界。Renderer が無ければルート位置の点を返す。
        /// </summary>
        public Bounds CalculateBounds()
        {
            Renderer[] renderers = Instance.GetComponentsInChildren<Renderer>();

            // 描画対象が無い場合はルート位置を返す
            if (renderers.Length == 0)
            {
                return new Bounds(Instance.transform.position, Vector3.zero);
            }

            // 最初の Renderer を起点に順次拡張
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        public void Dispose()
        {
            // 生成したオブジェクトを破棄
            if (Instance != null)
            {
                Object.Destroy(Instance);
                Instance = null;
            }

            // bundle と読み込んだアセット（Mesh / Texture 等）を解放
            if (_bundle != null)
            {
                _bundle.Unload(true);
                _bundle = null;
            }
        }
    }
}
