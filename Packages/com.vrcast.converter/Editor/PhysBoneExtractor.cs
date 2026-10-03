using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// VRCPhysBone / VRCPhysBoneCollider をリフレクションで読み、PhysBoneSet へ変換する（SDK 非依存）。
    /// カーブ・グラブ・ポーズ・パラメーター連動などは対象外。
    /// </summary>
    public static class PhysBoneExtractor
    {
        // 対象コンポーネントの型名
        private const string PhysBoneTypeName = "VRCPhysBone";
        private const string ColliderTypeName = "VRCPhysBoneCollider";

        // カーブのサンプル数（チェーンの骨数より細かければ十分）
        private const int CurveSamples = 9;

        public static PhysBoneSet Extract(GameObject root)
        {
            Transform rootTransform = root.transform;

            // コライダーを先に変換し、コンポーネント → インデックスの対応を作る
            var colliders = new List<PhysBoneColliderData>();
            var colliderIndices = new Dictionary<Component, int>();
            foreach (Component component in ReflectionUtility.FindComponents(root, ColliderTypeName))
            {
                if (colliders.Count >= PhysBoneSet.MaxColliders)
                {
                    break;
                }

                colliderIndices[component] = colliders.Count;
                colliders.Add(ConvertCollider(component, rootTransform));
            }

            // PhysBone を変換（上限で打ち切り）
            var bones = new List<PhysBoneData>();
            foreach (Component component in ReflectionUtility.FindComponents(root, PhysBoneTypeName))
            {
                if (bones.Count >= PhysBoneSet.MaxBones)
                {
                    break;
                }

                bones.Add(ConvertBone(component, rootTransform, colliderIndices));
            }

            return new PhysBoneSet { bones = bones.ToArray(), colliders = colliders.ToArray() };
        }

        private static PhysBoneData ConvertBone(Component bone, Transform root, Dictionary<Component, int> colliderIndices)
        {
            // rootTransform 未設定ならコンポーネント自身の Transform
            Transform chainRoot = ReflectionUtility.GetField(bone, "rootTransform") as Transform;
            if (chainRoot == null)
            {
                chainRoot = bone.transform;
            }

            var data = new PhysBoneData
            {
                rootPath = ReflectionUtility.PathOf(chainRoot, root),
                ignorePaths = ConvertTransforms(ReflectionUtility.GetField(bone, "ignoreTransforms"), root),
                endpointPosition = ReflectionUtility.GetField(bone, "endpointPosition") is Vector3 endpoint ? endpoint : Vector3.zero,
                multiChildType = ConvertMultiChild(ReflectionUtility.GetField(bone, "multiChildType")?.ToString()),

                // 各パラメーターは安全な範囲に丸める（カーブは無視）
                pull = Mathf.Clamp01(ReflectionUtility.GetFloat(bone, "pull", 0.2f)),
                spring = Mathf.Clamp01(ReflectionUtility.GetFloat(bone, "spring", 0.2f)),
                stiffness = Mathf.Clamp01(ReflectionUtility.GetFloat(bone, "stiffness", 0.2f)),
                gravity = Mathf.Clamp(ReflectionUtility.GetFloat(bone, "gravity", 0f), -1f, 1f),
                gravityFalloff = Mathf.Clamp01(ReflectionUtility.GetFloat(bone, "gravityFalloff", 0f)),
                immobile = Mathf.Clamp01(ReflectionUtility.GetFloat(bone, "immobile", 0f)),
                radius = Mathf.Clamp(ReflectionUtility.GetFloat(bone, "radius", 0f), 0f, PhysBoneSet.MaxRadius),
                maxAngle = Mathf.Clamp(ReflectionUtility.GetFloat(bone, "maxAngleX", 0f), 0f, 180f),

                // チェーン沿いの倍率カーブ
                pullCurve = SampleCurve(bone, "pullCurve"),
                springCurve = SampleCurve(bone, "springCurve"),
                stiffnessCurve = SampleCurve(bone, "stiffnessCurve"),
                gravityCurve = SampleCurve(bone, "gravityCurve"),
                immobileCurve = SampleCurve(bone, "immobileCurve"),
                radiusCurve = SampleCurve(bone, "radiusCurve"),
                maxAngleCurve = SampleCurve(bone, "maxAngleXCurve"),
            };

            // 角度制限は種類を問わず円錐で近似
            string limitType = ReflectionUtility.GetField(bone, "limitType")?.ToString();
            data.limitType = string.IsNullOrEmpty(limitType) || limitType == "None" ? PhysBoneData.LimitNone : PhysBoneData.LimitAngle;

            // 変換済みコライダーへの参照だけを残す
            var indices = new List<int>();
            if (ReflectionUtility.GetField(bone, "colliders") is IEnumerable colliders)
            {
                foreach (object collider in colliders)
                {
                    if (collider is Component component && component != null
                        && colliderIndices.TryGetValue(component, out int index)
                        && indices.Count < PhysBoneSet.MaxPathsPerBone)
                    {
                        indices.Add(index);
                    }
                }
            }

            data.colliders = indices.ToArray();
            return data;
        }

        private static PhysBoneColliderData ConvertCollider(Component collider, Transform root)
        {
            // rootTransform 未設定ならコンポーネント自身の Transform
            Transform target = ReflectionUtility.GetField(collider, "rootTransform") as Transform;
            if (target == null)
            {
                target = collider.transform;
            }

            // 形状は列挙名を小文字化（未知は球）
            string shape = ReflectionUtility.GetField(collider, "shapeType")?.ToString();
            string shapeName = shape == "Capsule" ? PhysBoneColliderData.ShapeCapsule
                : shape == "Plane" ? PhysBoneColliderData.ShapePlane
                : PhysBoneColliderData.ShapeSphere;

            return new PhysBoneColliderData
            {
                path = ReflectionUtility.PathOf(target, root),
                shape = shapeName,
                radius = Mathf.Clamp(ReflectionUtility.GetFloat(collider, "radius", 0f), 0f, PhysBoneSet.MaxRadius),
                height = Mathf.Clamp(ReflectionUtility.GetFloat(collider, "height", 0f), 0f, PhysBoneSet.MaxRadius * 2f),
                position = ReflectionUtility.GetField(collider, "position") is Vector3 position ? position : Vector3.zero,
                rotation = ReflectionUtility.GetField(collider, "rotation") is Quaternion rotation ? rotation : Quaternion.identity,
                insideBounds = ReflectionUtility.GetField(collider, "insideBounds") is bool inside && inside,
            };
        }

        private static string[] ConvertTransforms(object list, Transform root)
        {
            var paths = new List<string>();

            // List<Transform> を相対パスへ（null と上限超過は除外）
            if (list is IEnumerable transforms)
            {
                foreach (object item in transforms)
                {
                    if (item is Transform transform && transform != null && paths.Count < PhysBoneSet.MaxPathsPerBone)
                    {
                        paths.Add(ReflectionUtility.PathOf(transform, root));
                    }
                }
            }

            return paths.ToArray();
        }

        private static float[] SampleCurve(Component bone, string fieldName)
        {
            // 未設定・キー無しのカーブは「倍率 1」として空配列
            if (!(ReflectionUtility.GetField(bone, fieldName) is AnimationCurve curve) || curve.length == 0)
            {
                return System.Array.Empty<float>();
            }

            // 0〜1 を等間隔にサンプリングし、範囲内に丸める
            var samples = new float[CurveSamples];
            for (int i = 0; i < CurveSamples; i++)
            {
                float value = curve.Evaluate(i / (float)(CurveSamples - 1));

                // NaN / 無限大は倍率 1 とみなす（Clamp では除去できない）
                samples[i] = float.IsNaN(value) || float.IsInfinity(value)
                    ? 1f
                    : Mathf.Clamp(value, -PhysBoneData.MaxCurveValue, PhysBoneData.MaxCurveValue);
            }

            return samples;
        }

        private static string ConvertMultiChild(string value)
        {
            // 列挙名 Ignore / First / Average を小文字化（未知は ignore）
            switch (value)
            {
                case "First":
                    return PhysBoneData.MultiChildFirst;
                case "Average":
                    return PhysBoneData.MultiChildAverage;
                default:
                    return PhysBoneData.MultiChildIgnore;
            }
        }
    }
}
