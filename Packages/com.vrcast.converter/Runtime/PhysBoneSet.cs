using System;
using UnityEngine;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// metadata/physbones.json の内容。VRCPhysBone / VRCPhysBoneCollider を近似用パラメーターへ変換したもの。
    /// </summary>
    [Serializable]
    public class PhysBoneSet : IMetadata
    {
        // 信頼できない入力に対する上限
        public const int MaxBones = 256;
        public const int MaxColliders = 256;
        public const int MaxPathsPerBone = 64;
        public const float MaxRadius = 10f;
        public const float MaxOffset = 100f;

        public PhysBoneData[] bones = Array.Empty<PhysBoneData>();
        public PhysBoneColliderData[] colliders = Array.Empty<PhysBoneColliderData>();

        public string Validate()
        {
            // 配列欠落と件数上限
            if (bones == null || bones.Length > MaxBones || colliders == null || colliders.Length > MaxColliders)
            {
                return $"bones must be 0-{MaxBones} and colliders 0-{MaxColliders} items.";
            }

            foreach (PhysBoneData bone in bones)
            {
                string error = bone == null ? "null bone" : bone.Validate(colliders.Length);
                if (error != null)
                {
                    return error;
                }
            }

            foreach (PhysBoneColliderData collider in colliders)
            {
                string error = collider == null ? "null collider" : collider.Validate();
                if (error != null)
                {
                    return error;
                }
            }

            return null;
        }

        /// <summary>
        /// 値が有限かつ範囲内か。
        /// </summary>
        public static bool InRange(float value, float min, float max)
        {
            // NaN / 無限大は範囲比較で弾く
            return value >= min && value <= max;
        }

        /// <summary>
        /// ベクトルの各成分が有限かつ ±limit 以内か。
        /// </summary>
        public static bool InRange(Vector3 value, float limit)
        {
            return InRange(value.x, -limit, limit) && InRange(value.y, -limit, limit) && InRange(value.z, -limit, limit);
        }
    }

    /// <summary>
    /// 1 つの PhysBone（root 以下の Transform 階層をチェーンとして揺らす）。
    /// </summary>
    [Serializable]
    public class PhysBoneData
    {
        // 複数の子を持つ Transform の扱い
        public const string MultiChildIgnore = "ignore";
        public const string MultiChildFirst = "first";
        public const string MultiChildAverage = "average";

        // 角度制限（Hinge / Polar は円錐で近似して angle として書き出す）
        public const string LimitNone = "none";
        public const string LimitAngle = "angle";

        public string rootPath = string.Empty;
        public string[] ignorePaths = Array.Empty<string>();
        public Vector3 endpointPosition;
        public string multiChildType = MultiChildIgnore;
        public float pull = 0.2f;
        public float spring = 0.2f;
        public float stiffness = 0.2f;
        public float gravity;
        public float gravityFalloff;
        public float immobile;
        public float radius;
        public int[] colliders = Array.Empty<int>();
        public string limitType = LimitNone;
        public float maxAngle;

        // チェーン沿い（root = 0 → 末端 = 1）の倍率カーブを等間隔サンプリングしたもの。空なら倍率 1
        public float[] pullCurve = Array.Empty<float>();
        public float[] springCurve = Array.Empty<float>();
        public float[] stiffnessCurve = Array.Empty<float>();
        public float[] gravityCurve = Array.Empty<float>();
        public float[] immobileCurve = Array.Empty<float>();
        public float[] radiusCurve = Array.Empty<float>();
        public float[] maxAngleCurve = Array.Empty<float>();

        // カーブのサンプル数上限と倍率の範囲
        public const int MaxCurveSamples = 16;
        public const float MaxCurveValue = 10f;

        /// <summary>
        /// サンプリング済みカーブを位置 t（0〜1）で線形補間する。空なら 1。
        /// </summary>
        public static float EvaluateCurve(float[] samples, float t)
        {
            // カーブ無し
            if (samples == null || samples.Length == 0)
            {
                return 1f;
            }

            // 1 点なら定数
            if (samples.Length == 1)
            {
                return samples[0];
            }

            // 隣接サンプル間で補間
            float position = Mathf.Clamp01(t) * (samples.Length - 1);
            int index = Mathf.Min((int)position, samples.Length - 2);
            return Mathf.Lerp(samples[index], samples[index + 1], position - index);
        }

        private static bool IsValidCurve(float[] samples)
        {
            // 件数上限と各値の範囲
            if (samples == null || samples.Length > MaxCurveSamples)
            {
                return false;
            }

            foreach (float value in samples)
            {
                if (!PhysBoneSet.InRange(value, -MaxCurveValue, MaxCurveValue))
                {
                    return false;
                }
            }

            return true;
        }

        public string Validate(int colliderCount)
        {
            // カーブ
            if (!IsValidCurve(pullCurve) || !IsValidCurve(springCurve) || !IsValidCurve(stiffnessCurve)
                || !IsValidCurve(gravityCurve) || !IsValidCurve(immobileCurve) || !IsValidCurve(radiusCurve)
                || !IsValidCurve(maxAngleCurve))
            {
                return "PhysBone has an invalid curve.";
            }

            // パス類
            if (!ExpressionSet.IsValidString(rootPath, true) || ignorePaths == null
                || ignorePaths.Length > PhysBoneSet.MaxPathsPerBone)
            {
                return "PhysBone has an invalid path.";
            }

            foreach (string path in ignorePaths)
            {
                if (!ExpressionSet.IsValidString(path, true))
                {
                    return "PhysBone has an invalid ignore path.";
                }
            }

            // 列挙値
            if (multiChildType != MultiChildIgnore && multiChildType != MultiChildFirst && multiChildType != MultiChildAverage)
            {
                return $"Unknown multiChildType '{multiChildType}'.";
            }

            if (limitType != LimitNone && limitType != LimitAngle)
            {
                return $"Unknown limitType '{limitType}'.";
            }

            // 数値範囲
            bool valid = PhysBoneSet.InRange(endpointPosition, PhysBoneSet.MaxOffset)
                && PhysBoneSet.InRange(pull, 0f, 1f) && PhysBoneSet.InRange(spring, 0f, 1f)
                && PhysBoneSet.InRange(stiffness, 0f, 1f) && PhysBoneSet.InRange(gravity, -1f, 1f)
                && PhysBoneSet.InRange(gravityFalloff, 0f, 1f) && PhysBoneSet.InRange(immobile, 0f, 1f)
                && PhysBoneSet.InRange(radius, 0f, PhysBoneSet.MaxRadius) && PhysBoneSet.InRange(maxAngle, 0f, 180f);
            if (!valid)
            {
                return "PhysBone has an out-of-range parameter.";
            }

            // コライダー参照
            if (colliders == null || colliders.Length > PhysBoneSet.MaxPathsPerBone)
            {
                return "PhysBone has too many colliders.";
            }

            foreach (int index in colliders)
            {
                if (index < 0 || index >= colliderCount)
                {
                    return "PhysBone references an unknown collider.";
                }
            }

            return null;
        }
    }

    /// <summary>
    /// PhysBone 用コライダー。位置・回転は path の Transform のローカル空間。
    /// </summary>
    [Serializable]
    public class PhysBoneColliderData
    {
        public const string ShapeSphere = "sphere";
        public const string ShapeCapsule = "capsule";
        public const string ShapePlane = "plane";

        public string path = string.Empty;
        public string shape = ShapeSphere;
        public float radius;
        public float height;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public bool insideBounds;

        public string Validate()
        {
            // パスと形状
            if (!ExpressionSet.IsValidString(path, true))
            {
                return "Collider has an invalid path.";
            }

            if (shape != ShapeSphere && shape != ShapeCapsule && shape != ShapePlane)
            {
                return $"Unknown collider shape '{shape}'.";
            }

            // 数値範囲（回転は各成分 ±1 程度、正規化は Runtime 側で行う）
            bool valid = PhysBoneSet.InRange(radius, 0f, PhysBoneSet.MaxRadius)
                && PhysBoneSet.InRange(height, 0f, PhysBoneSet.MaxRadius * 2f)
                && PhysBoneSet.InRange(position, PhysBoneSet.MaxOffset)
                && PhysBoneSet.InRange(rotation.x, -1.01f, 1.01f) && PhysBoneSet.InRange(rotation.y, -1.01f, 1.01f)
                && PhysBoneSet.InRange(rotation.z, -1.01f, 1.01f) && PhysBoneSet.InRange(rotation.w, -1.01f, 1.01f);
            return valid ? null : "Collider has an out-of-range parameter.";
        }
    }
}
