using System;
using UnityEngine;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// metadata/constraints.json の内容。VRC Constraint / Unity 標準の Constraint を Runtime の評価用に変換したもの。
    /// 回転はすべて Quaternion（オイラー角は Converter が変換済み）。パスはアバタールートからの相対パス。
    /// </summary>
    [Serializable]
    public class ConstraintSet : IMetadata
    {
        // 信頼できない入力に対する上限
        public const int MaxConstraints = 512;
        public const int MaxSources = 32;

        public ConstraintData[] constraints = Array.Empty<ConstraintData>();

        public string Validate()
        {
            // 配列欠落と件数上限
            if (constraints == null || constraints.Length > MaxConstraints)
            {
                return $"constraints must be 0-{MaxConstraints} items.";
            }

            foreach (ConstraintData constraint in constraints)
            {
                string error = constraint == null ? "null constraint" : constraint.Validate();
                if (error != null)
                {
                    return error;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// 1 つの Constraint。type ごとに使うフィールドが異なる（使わないフィールドは既定値のまま）。
    /// </summary>
    [Serializable]
    public class ConstraintData
    {
        // 種類
        public const string TypePosition = "position";
        public const string TypeRotation = "rotation";
        public const string TypeScale = "scale";
        public const string TypeParent = "parent";
        public const string TypeAim = "aim";
        public const string TypeLookAt = "lookAt";

        // Aim の上方向の決め方（Unity の AimConstraint.WorldUpType と同じ意味）
        public const string UpScene = "sceneUp";
        public const string UpObject = "objectUp";
        public const string UpObjectRotation = "objectRotationUp";
        public const string UpVector = "vector";
        public const string UpNone = "none";

        // 軸のビットマスク（x = 1, y = 2, z = 4。Unity の Axis 列挙と同じ）
        public const int AllAxes = 7;

        // 位置・オフセットの上限
        public const float MaxOffset = 1000f;

        public string type = TypeParent;

        // 動かす Transform
        public string targetPath = string.Empty;

        // 有効か（無効なら評価しない）と、ソースの影響度（0 = 静止値、1 = ソースどおり）
        public bool active = true;
        public float weight = 1f;

        // ソースのローカル値をそのままターゲットのローカル値へ使う（VRC の Solve In Local Space）
        public bool localSpace;

        // 影響を受ける軸（位置・回転・スケール）
        public int positionAxes = AllAxes;
        public int rotationAxes = AllAxes;
        public int scaleAxes = AllAxes;

        // 静止値（weight 0・ソース無しのときのローカル値）とオフセット
        public Vector3 positionAtRest;
        public Vector3 positionOffset;
        public Quaternion rotationAtRest = Quaternion.identity;
        public Quaternion rotationOffset = Quaternion.identity;
        public Vector3 scaleAtRest = Vector3.one;
        public Vector3 scaleOffset = Vector3.one;

        // Aim: ターゲットのローカル軸のうちソースへ向ける軸・上へ向ける軸と、上方向の決め方
        public Vector3 aimAxis = Vector3.forward;
        public Vector3 upAxis = Vector3.up;
        public string worldUpType = UpScene;
        public Vector3 worldUpVector = Vector3.up;
        public string worldUpPath = string.Empty;

        // LookAt: 視線軸まわりの回転（度）と、上方向に worldUpPath の上を使うか
        public float roll;
        public bool useUpObject;

        public ConstraintSourceData[] sources = Array.Empty<ConstraintSourceData>();

        public string Validate()
        {
            // 種類
            bool knownType = type == TypePosition || type == TypeRotation || type == TypeScale
                || type == TypeParent || type == TypeAim || type == TypeLookAt;
            if (!knownType)
            {
                return $"Unknown constraint type '{type}'.";
            }

            // パス
            if (!ExpressionSet.IsValidString(targetPath, true) || !ExpressionSet.IsValidString(worldUpPath, true))
            {
                return "Constraint has an invalid path.";
            }

            // 上方向の決め方
            bool knownUp = worldUpType == UpScene || worldUpType == UpObject || worldUpType == UpObjectRotation
                || worldUpType == UpVector || worldUpType == UpNone;
            if (!knownUp)
            {
                return $"Unknown worldUpType '{worldUpType}'.";
            }

            // 数値範囲
            bool valid = PhysBoneSet.InRange(weight, 0f, 1f)
                && IsAxes(positionAxes) && IsAxes(rotationAxes) && IsAxes(scaleAxes)
                && PhysBoneSet.InRange(positionAtRest, MaxOffset) && PhysBoneSet.InRange(positionOffset, MaxOffset)
                && PhysBoneSet.InRange(scaleAtRest, MaxOffset) && PhysBoneSet.InRange(scaleOffset, MaxOffset)
                && IsRotation(rotationAtRest) && IsRotation(rotationOffset)
                && IsDirection(aimAxis) && IsDirection(upAxis) && PhysBoneSet.InRange(worldUpVector, MaxOffset)
                && PhysBoneSet.InRange(roll, -360f, 360f);
            if (!valid)
            {
                return "Constraint has an out-of-range parameter.";
            }

            // ソース
            if (sources == null || sources.Length > ConstraintSet.MaxSources)
            {
                return $"Constraint must have 0-{ConstraintSet.MaxSources} sources.";
            }

            foreach (ConstraintSourceData source in sources)
            {
                string error = source == null ? "null source" : source.Validate();
                if (error != null)
                {
                    return error;
                }
            }

            return null;
        }

        private static bool IsAxes(int axes)
        {
            // 3 ビットの範囲
            return axes >= 0 && axes <= AllAxes;
        }

        private static bool IsDirection(Vector3 axis)
        {
            // 有限かつ長さ 0 でない（Aim の軸に使う）
            return PhysBoneSet.InRange(axis, MaxOffset) && axis.sqrMagnitude > 1e-8f;
        }

        /// <summary>
        /// 四元数として使える値か（各成分 ±1 程度で長さ 0 でない。正規化は Runtime 側で行う）。
        /// </summary>
        public static bool IsRotation(Quaternion rotation)
        {
            return PhysBoneSet.InRange(rotation.x, -1.01f, 1.01f) && PhysBoneSet.InRange(rotation.y, -1.01f, 1.01f)
                && PhysBoneSet.InRange(rotation.z, -1.01f, 1.01f) && PhysBoneSet.InRange(rotation.w, -1.01f, 1.01f)
                && Quaternion.Dot(rotation, rotation) > 0.5f;
        }
    }

    /// <summary>
    /// Constraint のソース 1 つ。オフセットは Parent Constraint 用（ソースのローカル空間）。
    /// </summary>
    [Serializable]
    public class ConstraintSourceData
    {
        public string path = string.Empty;
        public float weight = 1f;
        public Vector3 positionOffset;
        public Quaternion rotationOffset = Quaternion.identity;

        public string Validate()
        {
            // パス・重み・オフセット
            bool valid = ExpressionSet.IsValidString(path, true)
                && PhysBoneSet.InRange(weight, 0f, 1f)
                && PhysBoneSet.InRange(positionOffset, ConstraintData.MaxOffset)
                && ConstraintData.IsRotation(rotationOffset);
            return valid ? null : "Constraint source has an invalid parameter.";
        }
    }
}
