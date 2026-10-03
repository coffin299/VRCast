using System;
using System.Collections;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;
using Object = UnityEngine.Object;

namespace VRCast.Avatars
{
    /// <summary>
    /// 展開済みパッケージの AssetBundle を非同期に読み込み、アバターを生成する。
    /// </summary>
    public static class AvatarLoader
    {
        // ログのカテゴリ名
        private const string LogCategory = "AvatarLoader";

        /// <summary>
        /// コルーチンとして実行する。成功時は onLoaded、失敗時は onError を呼ぶ。
        /// </summary>
        public static IEnumerator Load(
            AvatarPackage package, Transform parent, Action<LoadedAvatar> onLoaded, Action<string> onError)
        {
            // bundle を非同期で読み込む
            AssetBundleCreateRequest bundleRequest = AssetBundle.LoadFromFileAsync(package.BundlePath);
            yield return bundleRequest;
            AssetBundle bundle = bundleRequest.assetBundle;
            if (bundle == null)
            {
                onError("Failed to load AssetBundle (Unity version mismatch or corrupt bundle).");
                yield break;
            }

            // 既定パスの Prefab を読み込む
            AssetBundleRequest assetRequest = bundle.LoadAssetAsync<GameObject>(AvatarPackageLayout.PrefabAssetPath);
            yield return assetRequest;
            var prefab = assetRequest.asset as GameObject;
            if (prefab == null)
            {
                bundle.Unload(true);
                onError("Avatar prefab not found in bundle.");
                yield break;
            }

            // シーンへ生成し、manifest の名前を付ける
            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = package.Manifest.name;

            // 許可リスト外のコンポーネントを除去（Exporter を経由しない改変 bundle 対策）
            Sanitize(instance);

            // ルートモーションで勝手に移動しないよう無効化
            var animator = instance.GetComponent<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
            }

            var loaded = new LoadedAvatar(instance, bundle, package);

            // Humanoid でない場合はトラッキング等が使えないため警告
            if (!loaded.IsHumanoid)
            {
                VRCastLog.Warning(LogCategory, $"Avatar '{package.Manifest.name}' is not Humanoid.");
            }

            VRCastLog.Info(LogCategory, $"Loaded '{package.Manifest.name}' (renderers: {loaded.RendererCount}).");
            onLoaded(loaded);
        }

        private static void Sanitize(GameObject root)
        {
            int removed = 0;
            int missing = 0;
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                // Missing Script は Runtime では除去できないが、コードを持たないため数のみ記録
                if (component == null)
                {
                    missing++;
                    continue;
                }

                // 許可リスト外は破棄
                if (!AllowedComponents.IsAllowed(component))
                {
                    Object.Destroy(component);
                    removed++;
                }
            }

            // 想定外の内容があった場合のみ警告
            if (removed > 0 || missing > 0)
            {
                VRCastLog.Warning(LogCategory, $"Sanitized avatar: removed {removed} components, {missing} missing scripts.");
            }
        }
    }
}
