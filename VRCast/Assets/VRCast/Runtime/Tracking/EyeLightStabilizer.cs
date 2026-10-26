using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCast.Tracking
{
    /// <summary>
    /// 目ボーンを回すと、平らな瞳の法線が傾いてトゥーンの陰影（グラデーションの端）が一番濃い色や一番薄い色になる。
    /// 瞳だけのマテリアル（lilToon などの _LightDirectionOverride）について、視線の回転と同じだけライト方向を回し、
    /// 正面を見ているときと同じ陰影のまま瞳だけ動かす。頭の回転による明るさの変化はそのまま残す。
    /// </summary>
    public sealed class EyeLightStabilizer
    {
        // lilToon / OpenLit。xyz が方向、w が 0 以外ならオブジェクトの向きに追従（オブジェクト空間）
        private static readonly int OverrideId = Shader.PropertyToID("_LightDirectionOverride");

        // 線形色空間での OpenLit の輝度（シェーダーの OpenLitLuminance と同じ重み）
        private static readonly Vector3 LinearLuminance = new Vector3(0.0396819152f, 0.458021790f, 0.00609653955f);
        private static readonly Vector3 GammaLuminance = new Vector3(0.22f, 0.707f, 0.071f);

        // 頂点の過半数が目ボーンに乗っているサブメッシュを瞳とみなす
        private const float EyeWeight = 0.5f;

        private readonly List<Slot> _slots = new List<Slot>();
        private Light[] _lights = System.Array.Empty<Light>();

        // 読めないメッシュの警告は、アバターを読み直しても一度だけ出す
        private static bool _loggedWeightFailure;

        /// <summary>
        /// アバターの瞳マテリアルを探し直す。目ボーンが無ければ何もしない。前のアバターへ書いた値は戻す。
        /// </summary>
        public void Bind(Transform root, Transform leftEye, Transform rightEye)
        {
            Restore();
            _slots.Clear();
            if (root == null || (leftEye == null && rightEye == null))
            {
                return;
            }

            var eyeMaterials = new HashSet<Material>();
            var blocked = new HashSet<Material>();
            var host = new Dictionary<Material, Transform>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                bool underEye = IsUnder(renderer.transform, leftEye) || IsUnder(renderer.transform, rightEye);
                var skinned = renderer as SkinnedMeshRenderer;
                if (!underEye && skinned != null && skinned.sharedMesh != null)
                {
                    ClassifySkinned(skinned, leftEye, rightEye, eyeMaterials, blocked, host);
                    continue;
                }

                // 目ボーンの子は瞳。それ以外（顔・体）に付いているマテリアルは後で除外する
                Material[] materials = renderer.sharedMaterials;
                HashSet<Material> target = underEye ? eyeMaterials : blocked;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null)
                    {
                        continue;
                    }

                    target.Add(material);
                    if (underEye && !host.ContainsKey(material))
                    {
                        host.Add(material, renderer.transform);
                    }
                }
            }

            foreach (Material material in eyeMaterials)
            {
                // 顔と共有しているマテリアルを回すと顔の陰影まで動く
                if (material == null || blocked.Contains(material) || !material.HasProperty(OverrideId))
                {
                    continue;
                }

                host.TryGetValue(material, out Transform transform);
                _slots.Add(new Slot(material, transform, material.GetVector(OverrideId)));
            }
        }

        /// <summary>
        /// 視線の回転（頭の向き headFrame 基準の look）に合わせてライト方向をずらす。
        /// 回転が無いときはマテリアルの元の値へ戻す。
        /// </summary>
        public void Apply(Quaternion headFrame, Quaternion look)
        {
            if (_slots.Count == 0)
            {
                return;
            }

            Quaternion delta = headFrame * look * Quaternion.Inverse(headFrame);
            if (Quaternion.Angle(delta, Quaternion.identity) < 0.05f)
            {
                Restore();
                return;
            }

            if (!TryMainLight(out Vector3 direction, out float luminance))
            {
                Restore();
                return;
            }

            Vector3 mainDir = direction * luminance;
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot.Material == null)
                {
                    continue;
                }

                Vector3 custom = ToWorld(slot.Original, slot.Space);
                Vector3 shifted = Compensate(custom, delta, mainDir);
                Write(slot, shifted);
            }
        }

        /// <summary>
        /// 書き換えたライト方向を、アバター読み込み時の値へ戻す。
        /// </summary>
        public void Restore()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot.Material != null)
                {
                    slot.Material.SetVector(OverrideId, slot.Original);
                }
            }
        }

        /// <summary>
        /// メインライトへ足している方向 custom を、eyeDelta だけ回したあとも同じ陰影になる値にする。
        /// 環境光の向きは足さない（正面では元の値のままになる）。
        /// </summary>
        public static Vector3 Compensate(Vector3 customWorld, Quaternion eyeDelta, Vector3 mainDir)
        {
            // シェーダーは normalize(main + custom)。main + custom' = eyeDelta * (main + custom) なら
            // 回った法線との内積が、回す前と同じになる
            return eyeDelta * (mainDir + customWorld) - mainDir;
        }

        private static Vector3 ToWorld(Vector4 original, Transform space)
        {
            // w が 0 以外のときはオブジェクト空間（シェーダーがオブジェクトの回転を掛ける）
            if (original.w == 0f || space == null)
            {
                return original;
            }

            return space.TransformDirection(original);
        }

        private static void Write(Slot slot, Vector3 world)
        {
            Vector3 stored = slot.Original.w != 0f && slot.Space != null
                ? slot.Space.InverseTransformDirection(world)
                : world;
            slot.Material.SetVector(OverrideId, new Vector4(stored.x, stored.y, stored.z, slot.Original.w));
        }

        private bool TryMainLight(out Vector3 direction, out float luminance)
        {
            EnsureLights();
            Light main = null;
            float best = -1f;
            for (int i = 0; i < _lights.Length; i++)
            {
                Light light = _lights[i];
                if (light == null || !light.enabled || light.type != LightType.Directional)
                {
                    continue;
                }

                // ベースパスのメインライトは最も明るいディレクショナルライト
                float score = LightLuminance(light);
                if (score > best)
                {
                    best = score;
                    main = light;
                }
            }

            if (main == null)
            {
                direction = Vector3.forward;
                luminance = 0f;
                return false;
            }

            // _WorldSpaceLightPos0 はライトが来る方向（光線の向きの逆）
            direction = -main.transform.forward;
            luminance = best;
            return luminance > 1e-4f;
        }

        private void EnsureLights()
        {
            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] == null)
                {
                    _lights = System.Array.Empty<Light>();
                    break;
                }
            }

            if (_lights.Length == 0)
            {
                _lights = UnityEngine.Object.FindObjectsOfType<Light>();
            }
        }

        private static float LightLuminance(Light light)
        {
            // シェーダーへ渡る _LightColor0（リニア空間ではリニア色 × 強度）
            Color color = QualitySettings.activeColorSpace == ColorSpace.Linear ? light.color.linear : light.color;
            color *= light.intensity;
            Vector3 weight = QualitySettings.activeColorSpace == ColorSpace.Linear ? LinearLuminance : GammaLuminance;
            return color.r * weight.x + color.g * weight.y + color.b * weight.z;
        }

        private static void ClassifySkinned(
            SkinnedMeshRenderer renderer, Transform leftEye, Transform rightEye,
            HashSet<Material> eyeMaterials, HashSet<Material> blocked, Dictionary<Material, Transform> host)
        {
            Mesh mesh = renderer.sharedMesh;
            Transform[] bones = renderer.bones;
            int left = IndexOf(bones, leftEye);
            int right = IndexOf(bones, rightEye);
            Material[] materials = renderer.sharedMaterials;
            if (left < 0 && right < 0)
            {
                AddAll(materials, blocked, null, null);
                return;
            }

            // ボーンが目だけなら、ウェイトを読めなくても瞳のメッシュ
            if (OnlyEyeBones(bones, leftEye, rightEye))
            {
                AddAll(materials, eyeMaterials, host, renderer.transform);
                return;
            }

            for (int submesh = 0; submesh < mesh.subMeshCount && submesh < materials.Length; submesh++)
            {
                Material material = materials[submesh];
                if (material == null)
                {
                    continue;
                }

                if (IsEyeSubmesh(mesh, submesh, left, right))
                {
                    eyeMaterials.Add(material);
                    if (!host.ContainsKey(material))
                    {
                        host.Add(material, renderer.transform);
                    }
                }
                else
                {
                    blocked.Add(material);
                }
            }
        }

        private static void AddAll(Material[] materials, HashSet<Material> target, Dictionary<Material, Transform> host, Transform space)
        {
            if (materials == null)
            {
                return;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null)
                {
                    continue;
                }

                target.Add(materials[i]);
                if (host != null && !host.ContainsKey(materials[i]))
                {
                    host.Add(materials[i], space);
                }
            }
        }

        private static bool IsEyeSubmesh(Mesh mesh, int submesh, int leftBone, int rightBone)
        {
            // 読めないメッシュでアバターのセットアップ自体を止めない
            try
            {
                return IsEyeSubmeshUnchecked(mesh, submesh, leftBone, rightBone);
            }
            catch (System.Exception exception)
            {
                if (!_loggedWeightFailure)
                {
                    _loggedWeightFailure = true;
                    VRCast.Core.VRCastLog.Warning("Tracking", "Eye mesh weights could not be read: " + exception.Message);
                }

                return false;
            }
        }

        private static bool IsEyeSubmeshUnchecked(Mesh mesh, int submesh, int leftBone, int rightBone)
        {
            // ボーンウェイトは Mesh 本体から読む（MeshData の取得は Unity 2022.3 には無い）
            Unity.Collections.NativeArray<byte> bonesPerVertex = mesh.GetBonesPerVertex();
            Unity.Collections.NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
            Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(mesh);
            try
            {
                if (bonesPerVertex.Length == 0)
                {
                    return false;
                }

                Mesh.MeshData meshData = data[0];
                var offsets = new int[bonesPerVertex.Length];
                int cursor = 0;
                for (int vertex = 0; vertex < bonesPerVertex.Length; vertex++)
                {
                    offsets[vertex] = cursor;
                    cursor += bonesPerVertex[vertex];
                }

                SubMeshDescriptor descriptor = meshData.GetSubMesh(submesh);
                int eye = 0;
                int total = 0;
                if (meshData.indexFormat == IndexFormat.UInt32)
                {
                    Unity.Collections.NativeArray<int> indices = meshData.GetIndexData<int>();
                    CountEye(indices, descriptor, offsets, bonesPerVertex, weights, leftBone, rightBone, ref eye, ref total);
                }
                else
                {
                    Unity.Collections.NativeArray<ushort> indices = meshData.GetIndexData<ushort>();
                    CountEye(indices, descriptor, offsets, bonesPerVertex, weights, leftBone, rightBone, ref eye, ref total);
                }

                // 参照の過半数が目ボーンなら、そのサブメッシュは瞳
                return total > 0 && eye * 2 >= total;
            }
            finally
            {
                data.Dispose();
                bonesPerVertex.Dispose();
                weights.Dispose();
            }
        }

        private static void CountEye<T>(
            Unity.Collections.NativeArray<T> indices, SubMeshDescriptor descriptor, int[] offsets,
            Unity.Collections.NativeArray<byte> bonesPerVertex, Unity.Collections.NativeArray<BoneWeight1> weights,
            int leftBone, int rightBone, ref int eye, ref int total)
            where T : struct
        {
            int end = Mathf.Min(indices.Length, descriptor.indexStart + descriptor.indexCount);
            for (int i = descriptor.indexStart; i < end; i++)
            {
                int vertex = IndexAt(indices, i);
                if ((uint)vertex >= (uint)offsets.Length)
                {
                    continue;
                }

                total++;
                if (WeightedToEye(vertex, offsets, bonesPerVertex, weights, leftBone, rightBone))
                {
                    eye++;
                }
            }
        }

        private static int IndexAt<T>(Unity.Collections.NativeArray<T> indices, int index) where T : struct
        {
            // ushort と int のインデックスバッファを同じループで読む
            object value = indices[index];
            return value is ushort unsigned ? unsigned : (int)value;
        }

        private static bool OnlyEyeBones(Transform[] bones, Transform leftEye, Transform rightEye)
        {
            bool any = false;
            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = bones[i];
                if (bone == null)
                {
                    continue;
                }

                any = true;
                if (bone != leftEye && bone != rightEye)
                {
                    return false;
                }
            }

            return any;
        }

        private static bool WeightedToEye(
            int vertex, int[] offsets, Unity.Collections.NativeArray<byte> bonesPerVertex,
            Unity.Collections.NativeArray<BoneWeight1> weights, int leftBone, int rightBone)
        {
            int start = offsets[vertex];
            int count = bonesPerVertex[vertex];
            int end = Mathf.Min(weights.Length, start + count);
            for (int i = start; i < end; i++)
            {
                BoneWeight1 weight = weights[i];
                if (weight.weight > EyeWeight && (weight.boneIndex == leftBone || weight.boneIndex == rightBone))
                {
                    return true;
                }
            }

            return false;
        }

        private static int IndexOf(Transform[] bones, Transform bone)
        {
            if (bones == null || bone == null)
            {
                return -1;
            }

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == bone)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsUnder(Transform transform, Transform bone)
        {
            if (bone == null)
            {
                return false;
            }

            for (Transform cursor = transform; cursor != null; cursor = cursor.parent)
            {
                if (cursor == bone)
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct Slot
        {
            public readonly Material Material;
            public readonly Transform Space;
            public readonly Vector4 Original;

            public Slot(Material material, Transform space, Vector4 original)
            {
                Material = material;
                Space = space;
                Original = original;
            }
        }
    }
}
