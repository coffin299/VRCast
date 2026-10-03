using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using VRCast.AvatarFormat;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// VRC Constraint（リフレクション、SDK 非依存）と Unity 標準の Constraint を ConstraintSet へ変換する。
    /// アバター外の Transform を指すソースは除外し、Freeze To World は対象外（通常の Constraint として扱う）。
    /// </summary>
    public static class ConstraintExtractor
    {
        // VRC Constraint の型名と変換後の種類
        private static readonly Dictionary<string, string> VrcTypes = new Dictionary<string, string>
        {
            { "VRCPositionConstraint", ConstraintData.TypePosition },
            { "VRCRotationConstraint", ConstraintData.TypeRotation },
            { "VRCScaleConstraint", ConstraintData.TypeScale },
            { "VRCParentConstraint", ConstraintData.TypeParent },
            { "VRCAimConstraint", ConstraintData.TypeAim },
            { "VRCLookAtConstraint", ConstraintData.TypeLookAt },
        };

        // VRC Constraint のソース一覧がリストとして列挙できない場合に読む固定スロット数
        private const int VrcSourceSlots = 16;

        public static ConstraintSet Extract(GameObject root)
        {
            Transform rootTransform = root.transform;
            var constraints = new List<ConstraintData>();

            // Unity 標準の Constraint（非アクティブ含む）
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                // 上限で打ち切り、Missing Script は除外
                if (constraints.Count >= ConstraintSet.MaxConstraints)
                {
                    break;
                }

                ConstraintData data = component is IConstraint unity
                    ? ConvertUnity(component, unity, rootTransform)
                    : component != null && VrcTypes.TryGetValue(component.GetType().Name, out string type)
                        ? ConvertVrc(component, type, rootTransform)
                        : null;
                if (data != null)
                {
                    constraints.Add(data);
                }
            }

            return new ConstraintSet { constraints = constraints.ToArray() };
        }

        private static ConstraintData ConvertUnity(Component component, IConstraint constraint, Transform root)
        {
            var data = new ConstraintData
            {
                targetPath = ReflectionUtility.PathOf(component.transform, root),
                active = constraint.constraintActive && IsEnabled(component),
                weight = Mathf.Clamp01(constraint.weight),
            };

            // 種類ごとの設定（オイラー角は四元数へ）
            var offsets = new List<(Vector3 position, Quaternion rotation)>();
            switch (component)
            {
                case PositionConstraint position:
                    data.type = ConstraintData.TypePosition;
                    data.positionAtRest = position.translationAtRest;
                    data.positionOffset = position.translationOffset;
                    data.positionAxes = (int)position.translationAxis;
                    break;
                case RotationConstraint rotation:
                    data.type = ConstraintData.TypeRotation;
                    data.rotationAtRest = Quaternion.Euler(rotation.rotationAtRest);
                    data.rotationOffset = Quaternion.Euler(rotation.rotationOffset);
                    data.rotationAxes = (int)rotation.rotationAxis;
                    break;
                case ScaleConstraint scale:
                    data.type = ConstraintData.TypeScale;
                    data.scaleAtRest = scale.scaleAtRest;
                    data.scaleOffset = scale.scaleOffset;
                    data.scaleAxes = (int)scale.scalingAxis;
                    break;
                case ParentConstraint parent:
                    data.type = ConstraintData.TypeParent;
                    data.positionAtRest = parent.translationAtRest;
                    data.rotationAtRest = Quaternion.Euler(parent.rotationAtRest);
                    data.positionAxes = (int)parent.translationAxis;
                    data.rotationAxes = (int)parent.rotationAxis;
                    for (int i = 0; i < parent.sourceCount; i++)
                    {
                        offsets.Add((parent.GetTranslationOffset(i), Quaternion.Euler(parent.GetRotationOffset(i))));
                    }

                    break;
                case AimConstraint aim:
                    data.type = ConstraintData.TypeAim;
                    data.rotationAtRest = Quaternion.Euler(aim.rotationAtRest);
                    data.rotationOffset = Quaternion.Euler(aim.rotationOffset);
                    data.rotationAxes = (int)aim.rotationAxis;
                    data.aimAxis = aim.aimVector;
                    data.upAxis = aim.upVector;
                    data.worldUpType = ConvertWorldUp(aim.worldUpType.ToString());
                    data.worldUpVector = aim.worldUpVector;
                    data.worldUpPath = OptionalPath(aim.worldUpObject, root);
                    break;
                case LookAtConstraint lookAt:
                    data.type = ConstraintData.TypeLookAt;
                    data.rotationAtRest = Quaternion.Euler(lookAt.rotationAtRest);
                    data.rotationOffset = Quaternion.Euler(lookAt.rotationOffset);
                    data.roll = lookAt.roll;
                    data.useUpObject = lookAt.useUpObject;
                    data.worldUpPath = OptionalPath(lookAt.worldUpObject, root);
                    break;
                default:
                    // 未知の Constraint は対象外
                    return null;
            }

            // ソース（アバター外・未設定は除外）
            var sources = new List<ConstraintSourceData>();
            for (int i = 0; i < constraint.sourceCount && sources.Count < ConstraintSet.MaxSources; i++)
            {
                ConstraintSource source = constraint.GetSource(i);
                (Vector3 position, Quaternion rotation) offset = i < offsets.Count ? offsets[i] : (Vector3.zero, Quaternion.identity);
                AddSource(sources, source.sourceTransform, source.weight, offset.position, offset.rotation, root);
            }

            data.sources = sources.ToArray();
            return Sanitize(data);
        }

        private static ConstraintData ConvertVrc(Component component, string type, Transform root)
        {
            // TargetTransform 未設定ならコンポーネント自身の Transform
            Transform target = ReflectionUtility.GetField(component, "TargetTransform") as Transform;
            if (target == null)
            {
                target = component.transform;
            }

            // 共通設定（オイラー角は四元数へ）
            var data = new ConstraintData
            {
                type = type,
                targetPath = ReflectionUtility.PathOf(target, root),
                active = ReflectionUtility.GetBool(component, "IsActive", true) && IsEnabled(component),
                weight = Mathf.Clamp01(ReflectionUtility.GetFloat(component, "GlobalWeight", 1f)),
                localSpace = ReflectionUtility.GetBool(component, "SolveInLocalSpace", false),
                positionAxes = Axes(component, "AffectsPosition"),
                rotationAxes = Axes(component, "AffectsRotation"),
                scaleAxes = Axes(component, "AffectsScale"),
                positionAtRest = ReflectionUtility.GetVector3(component, "PositionAtRest", Vector3.zero),
                positionOffset = ReflectionUtility.GetVector3(component, "PositionOffset", Vector3.zero),
                rotationAtRest = Quaternion.Euler(ReflectionUtility.GetVector3(component, "RotationAtRest", Vector3.zero)),
                rotationOffset = Quaternion.Euler(ReflectionUtility.GetVector3(component, "RotationOffset", Vector3.zero)),
                scaleAtRest = ReflectionUtility.GetVector3(component, "ScaleAtRest", Vector3.one),
                scaleOffset = ReflectionUtility.GetVector3(component, "ScaleOffset", Vector3.one),
                aimAxis = ReflectionUtility.GetVector3(component, "AimAxis", Vector3.forward),
                upAxis = ReflectionUtility.GetVector3(component, "UpAxis", Vector3.up),
                worldUpType = ConvertWorldUp(ReflectionUtility.GetField(component, "WorldUp")?.ToString()),
                worldUpVector = ReflectionUtility.GetVector3(component, "WorldUpVector", Vector3.up),
                worldUpPath = OptionalPath(ReflectionUtility.GetField(component, "WorldUpTransform") as Transform, root),
                roll = ReflectionUtility.GetFloat(component, "Roll", 0f),
                useUpObject = ReflectionUtility.GetBool(component, "UseUpTransform", false),
            };

            // ソース（リストとして列挙できなければ固定スロットを読む）
            var sources = new List<ConstraintSourceData>();
            foreach (object source in EnumerateVrcSources(ReflectionUtility.GetField(component, "Sources")))
            {
                // 上限で打ち切り
                if (sources.Count >= ConstraintSet.MaxSources)
                {
                    break;
                }

                AddSource(sources,
                    ReflectionUtility.GetField(source, "SourceTransform") as Transform,
                    ReflectionUtility.GetFloat(source, "Weight", 1f),
                    ReflectionUtility.GetVector3(source, "ParentPositionOffset", Vector3.zero),
                    Quaternion.Euler(ReflectionUtility.GetVector3(source, "ParentRotationOffset", Vector3.zero)),
                    root);
            }

            data.sources = sources.ToArray();
            return Sanitize(data);
        }

        private static IEnumerable<object> EnumerateVrcSources(object list)
        {
            // 未設定
            if (list == null)
            {
                yield break;
            }

            // IEnumerable を実装していればそのまま列挙
            if (list is IEnumerable items)
            {
                foreach (object item in items)
                {
                    yield return item;
                }

                yield break;
            }

            // 固定スロット（source0〜）を順に読む（未使用スロットはソース Transform が null なので後で除外される）
            for (int i = 0; i < VrcSourceSlots; i++)
            {
                object item = ReflectionUtility.GetField(list, "source" + i);
                if (item != null)
                {
                    yield return item;
                }
            }
        }

        private static void AddSource(
            List<ConstraintSourceData> sources, Transform transform, float weight, Vector3 positionOffset,
            Quaternion rotationOffset, Transform root)
        {
            // 未設定・アバター外のソースは Runtime で解決できないため除外
            if (transform == null || !transform.IsChildOf(root))
            {
                return;
            }

            sources.Add(new ConstraintSourceData
            {
                path = ReflectionUtility.PathOf(transform, root),
                weight = Mathf.Clamp01(weight),
                positionOffset = positionOffset,
                rotationOffset = rotationOffset,
            });
        }

        private static ConstraintData Sanitize(ConstraintData data)
        {
            // 範囲外の値（巨大なオフセット・長さ 0 の軸等）を含む Constraint は全体の検証を通らないため除外する
            return data.Validate() == null ? data : null;
        }

        private static bool IsEnabled(Component component)
        {
            // Behaviour なら有効状態、それ以外（無いはず）は有効扱い
            return !(component is Behaviour behaviour) || behaviour.enabled;
        }

        private static int Axes(Component component, string prefix)
        {
            // AffectsXxxX / Y / Z の bool をビットマスクへ（フィールドが無ければ全軸）
            int axes = 0;
            axes |= ReflectionUtility.GetBool(component, prefix + "X", true) ? 1 : 0;
            axes |= ReflectionUtility.GetBool(component, prefix + "Y", true) ? 2 : 0;
            axes |= ReflectionUtility.GetBool(component, prefix + "Z", true) ? 4 : 0;
            return axes;
        }

        private static string ConvertWorldUp(string value)
        {
            // 列挙名（SceneUp / ObjectUp / ObjectRotationUp / Vector / None）を小文字始まりへ（未知はシーンの上）
            switch (value)
            {
                case "ObjectUp":
                    return ConstraintData.UpObject;
                case "ObjectRotationUp":
                    return ConstraintData.UpObjectRotation;
                case "Vector":
                    return ConstraintData.UpVector;
                case "None":
                    return ConstraintData.UpNone;
                default:
                    return ConstraintData.UpScene;
            }
        }

        private static string OptionalPath(Transform transform, Transform root)
        {
            // 未設定・アバター外は空（Runtime では「無し」扱い）
            return transform != null && transform.IsChildOf(root) ? ReflectionUtility.PathOf(transform, root) : string.Empty;
        }
    }
}
