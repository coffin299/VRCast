using System;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.PerfectSync;
using Object = UnityEngine.Object;

namespace VRCast.Avatars
{
    /// <summary>
    /// シーンに生成済みのアバターと、その読み込み元（.vrcaster の AssetBundle または VRM）の組。
    /// Dispose でアバターと読み込んだアセットを解放する。
    /// </summary>
    public sealed class LoadedAvatar : IDisposable
    {
        // 骨格基準フレーミングの係数（身長比）
        private const float HeadTopScale = 2f;
        private const float DefaultHeadSize = 0.15f;
        private const float WidthRatio = 0.5f;
        private const float DepthRatio = 0.3f;

        // 顔の写真の範囲（頭の大きさに対する半分の高さの倍率。髪や首元が少し入る程度）と、骨格が無いときの全高に対する比
        private const float FaceFrameScale = 0.9f;
        private const float NonHumanoidFaceRatio = 0.12f;

        // .vrcaster の bundle（VRM は null。VRM のアセットはアバター本体の破棄と一緒に解放される）
        private AssetBundle _bundle;

        public GameObject Instance { get; private set; }
        public Animator Animator { get; }
        public string Name { get; }
        public string SourcePath { get; }
        public int RendererCount { get; }
        public ExpressionSet Expressions { get; }
        public AvatarDescriptorData Descriptor { get; }
        public PhysBoneSet PhysBones { get; }
        public ConstraintSet Constraints { get; }

        // BlendShape の同期（.vrcaster の Modular Avatar の Blendshape Sync。VRM は空）
        public BlendShapeSyncSet BlendShapeSync { get; private set; } = new BlendShapeSyncSet();

        // パーティクルをまとめて切り替える単位（.vrcaster の FX で表示 ON/OFF されるもの。VRM は空）
        public ParticleGroupSet ParticleGroups { get; private set; } = new ParticleGroupSet();

        // FX で表示 ON/OFF されるオブジェクト（.vrcaster のもの。VRM は空）
        public FxToggleSet FxToggles { get; private set; } = new FxToggleSet();

        // パーフェクトシンク化の設定と生成した形状（.vrcaster のパーフェクトシンク版のもの。それ以外は空）
        public PerfectSyncSet PerfectSync { get; private set; } = new PerfectSyncSet();
        public PerfectSyncShapeData PerfectSyncShapes { get; private set; } = new PerfectSyncShapeData();

        // 生成した形状を足したメッシュの差し替え（足していなければ null）。破棄時に複製のメッシュも破棄する
        public PerfectSyncMeshes PerfectSyncMeshes { get; set; }

        // .vrcaster の manifest（VRM は null）
        public AvatarManifest Manifest { get; }

        // VRM の情報（.vrcaster は null）
        public VrmAvatarInfo Vrm { get; }

        public bool IsHumanoid => Animator != null && Animator.isHuman;

        // 読み込み時に足元をルートへ動かしたなら true（原点をアバターへ戻すボタン用）
        public bool OriginAligned { get; set; }

        public LoadedAvatar(GameObject instance, AssetBundle bundle, AvatarPackage package)
            : this(instance, package.Manifest.name, package.SourcePath, package.Expressions, package.Descriptor,
                package.PhysBones, package.Constraints)
        {
            _bundle = bundle;
            Manifest = package.Manifest;
            BlendShapeSync = package.BlendShapeSync;
            ParticleGroups = package.ParticleGroups;
            FxToggles = package.FxToggles;
            PerfectSync = package.PerfectSync;
            PerfectSyncShapes = package.PerfectSyncShapes;
        }

        public LoadedAvatar(GameObject instance, string sourcePath, VrmAvatarData vrm)
            : this(instance, vrm.Info.name, sourcePath, vrm.Expressions, vrm.Descriptor, vrm.PhysBones, new ConstraintSet())
        {
            Vrm = vrm.Info;
        }

        private LoadedAvatar(
            GameObject instance, string name, string sourcePath, ExpressionSet expressions,
            AvatarDescriptorData descriptor, PhysBoneSet physBones, ConstraintSet constraints)
        {
            Instance = instance;
            Name = name;
            SourcePath = sourcePath;
            Expressions = expressions;
            Descriptor = descriptor;
            PhysBones = physBones;
            Constraints = constraints;

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

        /// <summary>
        /// 顔を写すときの中心と、写す範囲の半分の高さ（ワールド座標・メートル）。
        /// Humanoid は頭（目があれば両目の間）、それ以外は全 Renderer の上端付近を使う。
        /// </summary>
        public void CalculateFaceFrame(out Vector3 center, out float halfHeight)
        {
            Transform head = IsHumanoid ? Animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head != null)
            {
                // 頭の大きさは「首→頭」の長さの倍で近似（フレーミングと同じ）
                Transform neck = Animator.GetBoneTransform(HumanBodyBones.Neck);
                float headSize = neck != null
                    ? Vector3.Distance(neck.position, head.position) * HeadTopScale
                    : DefaultHeadSize;

                // 両目があればその間、無ければ頭のボーンから頭の大きさの半分ほど上を顔の中心にする
                Transform leftEye = Animator.GetBoneTransform(HumanBodyBones.LeftEye);
                Transform rightEye = Animator.GetBoneTransform(HumanBodyBones.RightEye);
                center = leftEye != null && rightEye != null
                    ? (leftEye.position + rightEye.position) * 0.5f
                    : head.position + Vector3.up * headSize * 0.5f;
                halfHeight = Mathf.Max(headSize, DefaultHeadSize) * FaceFrameScale;
                return;
            }

            // 骨格が無ければ、全体の上端から少し下を中心に、高さの一部を写す
            Bounds bounds = CalculateRendererBounds();
            float height = Mathf.Max(bounds.size.y, DefaultHeadSize);
            center = new Vector3(bounds.center.x, bounds.max.y - height * NonHumanoidFaceRatio, bounds.center.z);
            halfHeight = height * NonHumanoidFaceRatio;
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
            // パーフェクトシンクの形状を足した複製のメッシュ（アバターと一緒には破棄されない）
            PerfectSyncMeshes?.Restore(false);
            PerfectSyncMeshes = null;

            // 生成したオブジェクトを破棄（VRM はこれで読み込んだメッシュ・テクスチャ等も解放される）
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
