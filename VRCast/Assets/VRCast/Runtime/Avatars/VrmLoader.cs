using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UniGLTF;
using UniVRM10;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;
using Object = UnityEngine.Object;
using VrmPreset = UniVRM10.ExpressionPreset;

namespace VRCast.Avatars
{
    /// <summary>
    /// VRM（0.x / 1.0）を UniVRM で読み込み、.vrcaster と同じ形（Unity 標準コンポーネント + metadata）のアバターにする。
    /// UniVRM 自身の揺れもの・表情・視線の処理は使わず、表情・まばたき・口の形・揺れものは metadata へ変換して VRCast の処理で動かす。
    /// </summary>
    public static class VrmLoader
    {
        // ログのカテゴリ名
        private const string LogCategory = "VrmLoader";

        // VRM ファイルの上限サイズ（.vrcaster と同じ 1 GiB。全体をメモリへ読み込むため）
        public const long MaxFileBytes = AvatarPackageReader.MaxPackageBytes;

        // 表情プリセットにする VRM の感情（まばたき・口の形・視線・ニュートラルは別扱い）と表示名
        private static readonly Dictionary<VrmPreset, string> EmotionNames = new Dictionary<VrmPreset, string>
        {
            { VrmPreset.happy, "Happy" },
            { VrmPreset.angry, "Angry" },
            { VrmPreset.sad, "Sad" },
            { VrmPreset.relaxed, "Relaxed" },
            { VrmPreset.surprised, "Surprised" },
        };

        /// <summary>
        /// コルーチンとして実行する。成功時は onLoaded、失敗時は onError を呼ぶ。
        /// </summary>
        public static IEnumerator Load(string path, Transform parent, Action<LoadedAvatar> onLoaded, Action<string> onError)
        {
            // ファイルの存在とサイズを先に確認
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                onError($"File not found: {path}");
                yield break;
            }

            if (file.Length > MaxFileBytes)
            {
                onError($"VRM file is too large: {file.Length} bytes.");
                yield break;
            }

            // 読み込み（VRM 0.x は UniVRM が 1.0 へ変換して読む）。完了までフレームを進める
            bool isVrm0 = false;
            Task<Vrm10Instance> task = LoadStoppedAsync(path, () => isVrm0 = true);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            // 失敗理由（UniVRM の例外）をそのまま表示する
            if (task.IsFaulted || task.IsCanceled || task.Result == null)
            {
                Exception error = task.Exception?.GetBaseException();
                VRCastLog.Error(LogCategory, $"Failed to load VRM: {error}");
                onError("Failed to load VRM (" + (error?.Message ?? "unknown error") + ").");
                yield break;
            }

            Vrm10Instance vrm = task.Result;
            GameObject instance = vrm.gameObject;

            // VRM の設定を metadata へ変換（UniVRM のコンポーネントを外す前に読む）
            VrmAvatarData data;
            try
            {
                data = Convert(vrm, Path.GetFileNameWithoutExtension(path), isVrm0);
            }
            catch (Exception e)
            {
                // 想定外のデータは読み込み失敗として扱い、生成済みのアバターを片付ける
                VRCastLog.Error(LogCategory, $"Failed to convert VRM: {e}");
                Object.Destroy(instance);
                onError("Failed to convert VRM (" + e.Message + ").");
                yield break;
            }

            // UniVRM の処理が動いていた場合に備えて読込時の姿勢へ戻し、Unity 標準以外のコンポーネントを外す
            RuntimeGltfInstance gltf = instance.GetComponent<RuntimeGltfInstance>();
            RestoreInitialPose(gltf);
            Strip(instance, vrm);

            // シーンへ配置し、VRM の名前を付ける
            instance.transform.SetParent(parent, false);
            instance.name = data.Info.name;

            // ルートモーションで勝手に移動しないよう無効化
            var animator = instance.GetComponent<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
            }

            // 配置が終わってから表示する
            gltf.ShowMeshes();

            var loaded = new LoadedAvatar(instance, path, data);
            VRCastLog.Info(LogCategory, $"Loaded VRM {data.Info.specVersion} '{data.Info.name}' "
                + $"(renderers: {loaded.RendererCount}, expressions: {data.Expressions.presets.Length}, "
                + $"springs: {data.PhysBones.bones.Length}).");
            onLoaded(loaded);
        }

        private static async Task<Vrm10Instance> LoadStoppedAsync(string path, Action onVrm0)
        {
            // 操作用の骨（ControlRig）は作らず、全て揃ってから表示する
            Vrm10Instance vrm = await Vrm10.LoadPathAsync(
                path,
                canLoadVrm0X: true,
                controlRigGenerationOption: ControlRigGenerationOption.None,
                showMeshes: false,
                vrmMetaInformationCallback: (thumbnail, vrm10Meta, vrm0Meta) =>
                {
                    // VRM 0.x のときだけ 0.x の情報が届く
                    if (vrm0Meta != null)
                    {
                        onVrm0();
                    }
                });

            // UniVRM の揺れもの・表情・視線の処理を最初のフレームから止める（VRCast の処理と二重に動かさない）
            if (vrm != null)
            {
                vrm.UpdateType = Vrm10Instance.UpdateTypes.None;
                vrm.enabled = false;
            }

            return vrm;
        }

        private static VrmAvatarData Convert(Vrm10Instance vrm, string fallbackName, bool isVrm0)
        {
            Transform root = vrm.transform;
            var data = new VrmAvatarData();
            VRM10ObjectMeta meta = vrm.Vrm != null ? vrm.Vrm.Meta : null;

            // 表示用の情報（名前が無ければファイル名）
            data.Info.name = DisplayName(meta != null ? meta.Name : null, fallbackName);
            data.Info.specVersion = isVrm0 ? "0.x" : "1.0";
            data.Info.authors = meta != null && meta.Authors != null
                ? string.Join(", ", meta.Authors.Where(author => !string.IsNullOrWhiteSpace(author)))
                : string.Empty;

            // 表情・まばたき・口の形
            if (vrm.Vrm != null)
            {
                VRM10ObjectExpression expression = vrm.Vrm.Expression;
                data.Expressions = VrmMetadataBuilder.BuildExpressions(Emotions(root, expression));
                data.Descriptor.eyelids = VrmMetadataBuilder.BuildEyelids(
                    Bindings(root, expression.Blink), Bindings(root, expression.BlinkLeft),
                    Bindings(root, expression.BlinkRight));
                data.Descriptor.lipSync = VrmMetadataBuilder.BuildLipSync(
                    Bindings(root, expression.Aa), Bindings(root, expression.Ih), Bindings(root, expression.Ou),
                    Bindings(root, expression.Ee), Bindings(root, expression.Oh));
            }

            // 揺れもの
            var colliders = new List<VrmCollider>();
            List<VrmSpring> springs = Springs(vrm, colliders);
            data.PhysBones = VrmMetadataBuilder.BuildPhysBones(root, springs, colliders);

            // .vrcaster と同じ検証を通し、不正なものは空にする（警告のみで表示は続ける）
            data.Expressions = Validated(data.Expressions, "expressions");
            data.Descriptor = Validated(data.Descriptor, "descriptor");
            data.PhysBones = Validated(data.PhysBones, "springs");
            return data;
        }

        private static T Validated<T>(T metadata, string label) where T : IMetadata, new()
        {
            // 問題が無ければそのまま
            string error = metadata.Validate();
            if (error == null)
            {
                return metadata;
            }

            VRCastLog.Warning(LogCategory, $"Ignored invalid {label}: {error}");
            return new T();
        }

        private static string DisplayName(string name, string fallback)
        {
            // 空ならファイル名、長すぎれば manifest と同じ上限で切る
            string value = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
            value = string.IsNullOrWhiteSpace(value) ? "VRM" : value;
            return value.Length <= AvatarManifest.MaxNameLength ? value : value.Substring(0, AvatarManifest.MaxNameLength);
        }

        private static IEnumerable<VrmExpression> Emotions(Transform root, VRM10ObjectExpression expression)
        {
            foreach ((VrmPreset preset, VRM10Expression clip) in expression.Clips)
            {
                // 感情は表示名、独自の表情は VRM に書かれた名前
                string name;
                if (preset == VrmPreset.custom)
                {
                    name = clip.name;
                }
                else if (!EmotionNames.TryGetValue(preset, out name))
                {
                    continue;
                }

                yield return new VrmExpression { name = name, bindings = Bindings(root, clip) };
            }
        }

        private static List<VrmMorphBinding> Bindings(Transform root, VRM10Expression clip)
        {
            var result = new List<VrmMorphBinding>();

            // 未設定の表情は空
            if (clip == null || clip.MorphTargetBindings == null)
            {
                return result;
            }

            foreach (UniVRM10.MorphTargetBinding binding in clip.MorphTargetBindings)
            {
                // パスのメッシュと BlendShape 番号から名前を引く（見つからないものは除く）
                string path = binding.RelativePath ?? string.Empty;
                Transform node = path.Length == 0 ? root : root.Find(path);
                SkinnedMeshRenderer renderer = node != null ? node.GetComponent<SkinnedMeshRenderer>() : null;
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                if (mesh == null || binding.Index < 0 || binding.Index >= mesh.blendShapeCount)
                {
                    continue;
                }

                result.Add(new VrmMorphBinding(path, mesh.GetBlendShapeName(binding.Index), binding.Weight));
            }

            return result;
        }

        private static List<VrmSpring> Springs(Vrm10Instance vrm, List<VrmCollider> colliders)
        {
            var springs = new List<VrmSpring>();

            // コライダーは参照順に番号を振る（同じものは同じ番号）
            var indices = new Dictionary<VRM10SpringBoneCollider, int>();
            if (vrm.SpringBone == null || vrm.SpringBone.Springs == null)
            {
                return springs;
            }

            foreach (Vrm10InstanceSpringBone.Spring source in vrm.SpringBone.Springs)
            {
                if (source == null || source.Joints == null)
                {
                    continue;
                }

                // 関節（欠けたものは null のまま渡し、そこでチェーンを切る）
                var spring = new VrmSpring();
                foreach (VRM10SpringBoneJoint joint in source.Joints)
                {
                    spring.joints.Add(joint == null ? null : new VrmSpringJoint
                    {
                        transform = joint.transform,
                        stiffness = joint.m_stiffnessForce,
                        gravityPower = joint.m_gravityPower,
                        gravityDir = joint.m_gravityDir,
                        dragForce = joint.m_dragForce,
                        radius = joint.m_jointRadius,
                    });
                }

                // 当たるコライダー（グループを展開）
                foreach (VRM10SpringBoneColliderGroup group in source.ColliderGroups ?? new List<VRM10SpringBoneColliderGroup>())
                {
                    if (group == null || group.Colliders == null)
                    {
                        continue;
                    }

                    foreach (VRM10SpringBoneCollider collider in group.Colliders)
                    {
                        if (collider != null)
                        {
                            spring.colliders.Add(ColliderIndex(collider, indices, colliders));
                        }
                    }
                }

                springs.Add(spring);
            }

            return springs;
        }

        private static int ColliderIndex(
            VRM10SpringBoneCollider collider, Dictionary<VRM10SpringBoneCollider, int> indices, List<VrmCollider> colliders)
        {
            // 変換済みなら同じ番号
            if (indices.TryGetValue(collider, out int index))
            {
                return index;
            }

            // 形と内側判定（Inside 系は内側に閉じ込める）
            VrmColliderShape shape;
            bool inside;
            switch (collider.ColliderType)
            {
                case VRM10SpringBoneColliderTypes.Capsule:
                case VRM10SpringBoneColliderTypes.CapsuleInside:
                    shape = VrmColliderShape.Capsule;
                    inside = collider.ColliderType == VRM10SpringBoneColliderTypes.CapsuleInside;
                    break;
                case VRM10SpringBoneColliderTypes.Plane:
                    shape = VrmColliderShape.Plane;
                    inside = false;
                    break;
                default:
                    shape = VrmColliderShape.Sphere;
                    inside = collider.ColliderType == VRM10SpringBoneColliderTypes.SphereInside;
                    break;
            }

            index = colliders.Count;
            colliders.Add(new VrmCollider
            {
                transform = collider.transform,
                shape = shape,
                inside = inside,
                offset = collider.Offset,
                tail = collider.Tail,
                normal = collider.Normal,
                radius = collider.Radius,
            });
            indices[collider] = index;
            return index;
        }

        private static void RestoreInitialPose(RuntimeGltfInstance gltf)
        {
            // 読込直後の各ノードの姿勢（glTF に書かれた値）へ戻す
            foreach (var pair in gltf.InitialTransformStates)
            {
                if (pair.Key == null)
                {
                    continue;
                }

                pair.Key.localPosition = pair.Value.LocalPosition;
                pair.Key.localRotation = pair.Value.LocalRotation;
                pair.Key.localScale = pair.Value.LocalScale;
            }
        }

        private static void Strip(GameObject instance, Vrm10Instance vrm)
        {
            // 他のコンポーネントが依存する VRM 本体を先に外す
            Object.DestroyImmediate(vrm);

            // 後から付いたものほど依存する側なので、末尾から外す。
            // RuntimeGltfInstance は読み込んだメッシュ・テクスチャ等をアバターの破棄時に解放するため残す
            Component[] components = instance.GetComponentsInChildren<Component>(true);
            int removed = 0;
            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component component = components[i];
                if (component == null || AllowedComponents.IsAllowed(component) || component is RuntimeGltfInstance)
                {
                    continue;
                }

                Object.DestroyImmediate(component);
                removed++;
            }

            VRCastLog.Info(LogCategory, $"Removed {removed} UniVRM components.");
        }
    }
}
