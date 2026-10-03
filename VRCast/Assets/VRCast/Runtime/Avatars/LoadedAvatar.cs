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
        // 骨格基準フレーミングの係数（身長比）
        private const float HeadTopScale = 2f;
        private const float DefaultHeadSize = 0.15f;
        private const float WidthRatio = 0.5f;
        private const float DepthRatio = 0.3f;

        private AssetBundle _bundle;

        public GameObject Instance { get; private set; }
        public Animator Animator { get; }
        public AvatarManifest Manifest { get; }
        public string SourcePath { get; }
        public int RendererCount { get; }
        public ExpressionSet Expressions { get; }
        public AvatarDescriptorData Descriptor { get; }

        public bool IsHumanoid => Animator != null && Animator.isHuman;

        public LoadedAvatar(GameObject instance, AssetBundle bundle, AvatarPackage package)
        {
            Instance = instance;
            _bundle = bundle;
            Manifest = package.Manifest;
            SourcePath = package.SourcePath;
            Expressions = package.Expressions;
            Descriptor = package.Descriptor;

            // ルートの Animator と描画対象数を記録
            Animator = instance.GetComponent<Animator>();
            RendererCount = instance.GetComponentsInChildren<Renderer>(true).Length;
        }

        /// <summary>
        /// カメラのフレーミング用の境界。Humanoid は骨格から本体だけを、それ以外は全 Renderer を包む。
        /// 小物やワールド固定オブジェクトで範囲が広がるのを避けるため骨格を優先する。
        /// </summary>
        public Bounds CalculateFramingBounds()
        {
            // 骨格から計算できなければ Renderer 基準
            return TryCalculateHumanoidBounds(out Bounds bounds) ? bounds : CalculateRendererBounds();
        }

        private bool TryCalculateHumanoidBounds(out Bounds bounds)
        {
            bounds = default;

            // Humanoid でなければ不可
            if (!IsHumanoid)
            {
                return false;
            }

            // 頭と腰は必須
            Transform head = Animator.GetBoneTransform(HumanBodyBones.Head);
            Transform hips = Animator.GetBoneTransform(HumanBodyBones.Hips);
            if (head == null || hips == null)
            {
                return false;
            }

            // 頭頂は「首→頭」の長さの倍で近似（首が無ければ固定値）
            Transform neck = Animator.GetBoneTransform(HumanBodyBones.Neck);
            float headSize = neck != null ? Vector3.Distance(neck.position, head.position) * HeadTopScale : DefaultHeadSize;
            float top = head.position.y + headSize;

            // 足元は両足とルートのうち最も低い位置
            float bottom = Instance.transform.position.y;
            bottom = LowerY(Animator.GetBoneTransform(HumanBodyBones.LeftFoot), bottom);
            bottom = LowerY(Animator.GetBoneTransform(HumanBodyBones.RightFoot), bottom);

            // 頭頂が足元より下になる異常な骨格は不可
            float height = top - bottom;
            if (height <= 0f)
            {
                return false;
            }

            // 腰の水平位置を中心に、身長比の幅・奥行きを持つ箱
            var center = new Vector3(hips.position.x, (top + bottom) * 0.5f, hips.position.z);
            bounds = new Bounds(center, new Vector3(height * WidthRatio, height, height * DepthRatio));
            return true;
        }

        private static float LowerY(Transform bone, float current)
        {
            // ボーンが無ければ現在値を維持
            return bone != null ? Mathf.Min(bone.position.y, current) : current;
        }

        /// <summary>
        /// 全 Renderer を包む境界。Renderer が無ければルート位置の点を返す。
        /// </summary>
        private Bounds CalculateRendererBounds()
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
