using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;
using VRCast.Dynamics;

namespace VRCast.Avatars
{
    /// <summary>
    /// VRM の表情・まばたき・口の形・揺れものを、.vrcaster の metadata と同じ形式へ変換する（UniVRM に依存しない部分）。
    /// 揺れものは VRM の SpringBone を PhysBone の近似パラメーターへ置き換える。
    /// </summary>
    public static class VrmMetadataBuilder
    {
        // 表情の重み（VRM は 0〜1）→ BlendShape の重み（0〜100）
        private const float WeightScale = 100f;

        // VRM の stiffness（0〜4 程度、既定 1）→ PhysBone の pull（0〜1）の倍率
        private const float PullPerStiffness = 0.5f;

        // VRM の gravityPower（1 フレームの移動量 m/s）→ PhysBone の gravity（重力加速度に対する比）の倍率
        private const float GravityScale = 1f / (PhysBoneChain.GravityAcceleration * PhysBoneSimulator.TimeStep);

        // パラメーターが関節ごとに違うとみなす差
        private const float Epsilon = 1e-5f;

        /// <summary>
        /// 感情・独自の表情を表情プリセットにする。BlendShape を動かさない表情（マテリアルだけ等）は除く。
        /// </summary>
        public static ExpressionSet BuildExpressions(IEnumerable<VrmExpression> expressions)
        {
            var presets = new List<ExpressionPreset>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (VrmExpression expression in expressions)
            {
                // 件数上限
                if (presets.Count >= ExpressionSet.MaxPresets)
                {
                    break;
                }

                // BlendShape の値に変換（上限件数まで）
                ExpressionPreset preset = ToPreset(expression);
                if (preset.values.Length == 0)
                {
                    continue;
                }

                // 同じ名前は番号を付けて区別する（ショートカットキー等を名前で記録するため）
                preset.name = UniqueName(preset.name, names);
                presets.Add(preset);
            }

            return new ExpressionSet { presets = presets.ToArray() };
        }

        private static ExpressionPreset ToPreset(VrmExpression expression)
        {
            // 名前が無ければ「Expression」
            string name = string.IsNullOrWhiteSpace(expression.name) ? "Expression" : expression.name.Trim();
            name = Truncate(name);

            // 重みを 0〜100 へ（範囲外は丸める）
            var values = new List<BlendShapeValue>();
            foreach (VrmMorphBinding binding in expression.bindings)
            {
                // 件数上限・名前の無い BlendShape
                if (values.Count >= ExpressionSet.MaxValuesPerPreset || !IsUsable(binding))
                {
                    continue;
                }

                values.Add(new BlendShapeValue
                {
                    path = binding.path ?? string.Empty,
                    blendShape = binding.blendShape,
                    weight = Mathf.Clamp(binding.weight * WeightScale, 0f, WeightScale),
                });
            }

            return new ExpressionPreset { name = name, values = values.ToArray() };
        }

        private static string UniqueName(string name, HashSet<string> used)
        {
            // 未使用ならそのまま
            string candidate = name;

            // 使用済みなら " 2"、" 3" … を付ける
            for (int i = 2; !used.Add(candidate); i++)
            {
                candidate = Truncate($"{name} {i}");
            }

            return candidate;
        }

        /// <summary>
        /// まばたき（blink）と片目（blinkLeft / blinkRight、VRM もアバターから見た左右）をまぶた設定にする。
        /// まぶた設定は 1 つのメッシュに限られるため、最も多く使われているメッシュの BlendShape だけを使う。
        /// </summary>
        public static EyelidData BuildEyelids(
            IReadOnlyList<VrmMorphBinding> blink, IReadOnlyList<VrmMorphBinding> blinkLeft,
            IReadOnlyList<VrmMorphBinding> blinkRight)
        {
            var data = new EyelidData();

            // 両目用を優先してメッシュを決める（無ければ片目用から）
            string mesh = PickMesh(blink) ?? PickMesh(blinkLeft.Concat(blinkRight).ToList());
            if (mesh == null)
            {
                return data;
            }

            data.meshPath = mesh;

            // 片目用は左右とも同じメッシュにある場合だけ使う
            string left = FirstShape(blinkLeft, mesh);
            string right = FirstShape(blinkRight, mesh);
            if (left != null && right != null)
            {
                data.winkLeftBlendShape = left;
                data.winkRightBlendShape = right;
            }

            // 両目用（上限件数まで）。無ければ左右の片目用を同時に閉じる
            List<string> shapes = ShapesOn(blink, mesh);
            if (shapes.Count == 0 && left != null && right != null)
            {
                shapes.Add(left);
                shapes.Add(right);
            }

            data.blinkBlendShapes = shapes.Take(EyelidData.MaxBlinkBlendShapes).ToArray();
            return data;
        }

        /// <summary>
        /// 口の形（aa / ih / ou / ee / oh）を Viseme 方式のリップシンクにする（あ・い・う・え・お以外の Viseme は空）。
        /// リップシンクは 1 つのメッシュに限られるため、最も多く使われているメッシュの BlendShape だけを使う。
        /// </summary>
        public static LipSyncData BuildLipSync(
            IReadOnlyList<VrmMorphBinding> aa, IReadOnlyList<VrmMorphBinding> ih, IReadOnlyList<VrmMorphBinding> ou,
            IReadOnlyList<VrmMorphBinding> ee, IReadOnlyList<VrmMorphBinding> oh)
        {
            var data = new LipSyncData();

            // 全母音を通して最も多いメッシュ
            string mesh = PickMesh(aa.Concat(ih).Concat(ou).Concat(ee).Concat(oh).ToList());
            if (mesh == null)
            {
                return data;
            }

            // 母音の位置にだけ名前を入れる
            var visemes = new string[AvatarDescriptorData.VisemeCount];
            for (int i = 0; i < visemes.Length; i++)
            {
                visemes[i] = string.Empty;
            }

            visemes[AvatarDescriptorData.VisemeAa] = FirstShape(aa, mesh) ?? string.Empty;
            visemes[AvatarDescriptorData.VisemeI] = FirstShape(ih, mesh) ?? string.Empty;
            visemes[AvatarDescriptorData.VisemeU] = FirstShape(ou, mesh) ?? string.Empty;
            visemes[AvatarDescriptorData.VisemeE] = FirstShape(ee, mesh) ?? string.Empty;
            visemes[AvatarDescriptorData.VisemeO] = FirstShape(oh, mesh) ?? string.Empty;

            data.mode = LipSyncData.ModeVisemeBlendShape;
            data.meshPath = mesh;
            data.visemes = visemes;
            return data;
        }

        private static string PickMesh(IReadOnlyList<VrmMorphBinding> bindings)
        {
            // 使える値が無ければ対象なし
            List<VrmMorphBinding> usable = bindings.Where(IsUsable).ToList();
            if (usable.Count == 0)
            {
                return null;
            }

            // 最も多く使われているパス（同数なら先に現れた方）
            return usable
                .GroupBy(binding => binding.path ?? string.Empty)
                .OrderByDescending(group => group.Count())
                .First()
                .Key;
        }

        private static string FirstShape(IReadOnlyList<VrmMorphBinding> bindings, string mesh)
        {
            // 指定メッシュの最初の BlendShape（無ければ null）
            return ShapesOn(bindings, mesh).FirstOrDefault();
        }

        private static List<string> ShapesOn(IReadOnlyList<VrmMorphBinding> bindings, string mesh)
        {
            // 指定メッシュの BlendShape 名（重み 0 は除き、重複は 1 つに）
            return bindings
                .Where(binding => IsUsable(binding) && binding.weight > 0f && (binding.path ?? string.Empty) == mesh)
                .Select(binding => binding.blendShape)
                .Distinct()
                .ToList();
        }

        private static bool IsUsable(VrmMorphBinding binding)
        {
            // BlendShape 名があり、パス・名前が metadata の長さ上限内
            return ExpressionSet.IsValidString(binding.blendShape, false)
                && ExpressionSet.IsValidString(binding.path ?? string.Empty, true);
        }

        private static string Truncate(string value)
        {
            // metadata の文字列の長さ上限まで
            return value.Length <= ExpressionSet.MaxStringLength ? value : value.Substring(0, ExpressionSet.MaxStringLength);
        }

        /// <summary>
        /// SpringBone を PhysBone の近似へ変換する。チェーンは VRM の関節の並びだけをたどり（それ以外の子は除外）、
        /// 関節ごとに違うパラメーターはチェーン沿いのカーブにする。
        /// </summary>
        public static PhysBoneSet BuildPhysBones(
            Transform root, IReadOnlyList<VrmSpring> springs, IReadOnlyList<VrmCollider> colliders)
        {
            // コライダーを変換し、VRM の番号 → 変換後の番号を記録（変換できないものは -1）
            var colliderData = new List<PhysBoneColliderData>();
            var colliderIndex = new int[colliders.Count];
            for (int i = 0; i < colliders.Count; i++)
            {
                PhysBoneColliderData data = colliderData.Count < PhysBoneSet.MaxColliders
                    ? ToCollider(root, colliders[i])
                    : null;
                colliderIndex[i] = data != null ? colliderData.Count : -1;
                if (data != null)
                {
                    colliderData.Add(data);
                }
            }

            // チェーンを変換（件数上限まで）
            var bones = new List<PhysBoneData>();
            foreach (VrmSpring spring in springs)
            {
                if (bones.Count >= PhysBoneSet.MaxBones)
                {
                    break;
                }

                PhysBoneData bone = ToPhysBone(root, spring, colliderIndex);
                if (bone != null)
                {
                    bones.Add(bone);
                }
            }

            return new PhysBoneSet { bones = bones.ToArray(), colliders = colliderData.ToArray() };
        }

        private static PhysBoneColliderData ToCollider(Transform root, VrmCollider collider)
        {
            // アバター外・不正な Transform は使えない
            if (collider == null || collider.transform == null || !collider.transform.IsChildOf(root))
            {
                return null;
            }

            var data = new PhysBoneColliderData
            {
                path = TransformPath.Of(collider.transform, root),
                radius = collider.radius,
                insideBounds = collider.inside,
            };

            switch (collider.shape)
            {
                case VrmColliderShape.Capsule:
                    // 両端の中点を中心に、端から端の向きを軸、長さ + 両端の半球を高さにする
                    Vector3 axis = collider.tail - collider.offset;
                    data.position = (collider.offset + collider.tail) * 0.5f;
                    if (axis.sqrMagnitude > Epsilon * Epsilon)
                    {
                        data.shape = PhysBoneColliderData.ShapeCapsule;
                        data.rotation = Quaternion.FromToRotation(Vector3.up, axis);
                        data.height = axis.magnitude + collider.radius * 2f;
                    }
                    else
                    {
                        // 長さ 0 のカプセルは球
                        data.shape = PhysBoneColliderData.ShapeSphere;
                    }

                    break;
                case VrmColliderShape.Plane:
                    // 法線をローカル Y 軸にする
                    data.shape = PhysBoneColliderData.ShapePlane;
                    data.position = collider.offset;
                    data.rotation = collider.normal.sqrMagnitude > Epsilon * Epsilon
                        ? Quaternion.FromToRotation(Vector3.up, collider.normal)
                        : Quaternion.identity;
                    data.radius = 0f;
                    break;
                default:
                    data.shape = PhysBoneColliderData.ShapeSphere;
                    data.position = collider.offset;
                    break;
            }

            // 範囲外の値を含むものは使わない
            return data.Validate() == null ? data : null;
        }

        private static PhysBoneData ToPhysBone(Transform root, VrmSpring spring, int[] colliderIndex)
        {
            // 親 → 子でつながっている関節だけ（途中で切れていたらそこまで）
            List<VrmSpringJoint> joints = ConnectedJoints(root, spring.joints);
            if (joints.Count < 2)
            {
                return null;
            }

            var data = new PhysBoneData
            {
                rootPath = TransformPath.Of(joints[0].transform, root),
                ignorePaths = IgnoredChildren(root, joints),
                multiChildType = PhysBoneData.MultiChildFirst,
                stiffness = 0f,
                gravityFalloff = 0f,
                immobile = 0f,
                limitType = PhysBoneData.LimitNone,
            };

            // 関節ごとのパラメーターを PhysBone の値へ換算し、基本値 × カーブにする
            (data.pull, data.pullCurve) = ToCurve(joints, joint => Mathf.Clamp01(joint.stiffness * PullPerStiffness));
            (data.spring, data.springCurve) = ToCurve(joints, joint => ToSpring(joint.dragForce));
            (data.gravity, data.gravityCurve) = ToCurve(joints, ToGravity);
            (data.radius, data.radiusCurve) = ToCurve(joints, joint => Mathf.Clamp(joint.radius, 0f, PhysBoneSet.MaxRadius));

            // 当たるコライダー（変換できたものだけ、重複なし・上限件数まで）
            data.colliders = spring.colliders
                .Where(index => index >= 0 && index < colliderIndex.Length && colliderIndex[index] >= 0)
                .Select(index => colliderIndex[index])
                .Distinct()
                .Take(PhysBoneSet.MaxPathsPerBone)
                .ToArray();

            // 範囲外の値を含むものは使わない
            return data.Validate(int.MaxValue) == null ? data : null;
        }

        private static List<VrmSpringJoint> ConnectedJoints(Transform root, List<VrmSpringJoint> joints)
        {
            var result = new List<VrmSpringJoint>();
            foreach (VrmSpringJoint joint in joints)
            {
                // 欠けた関節・アバター外で終わり
                if (joint == null || joint.transform == null || !joint.transform.IsChildOf(root) || joint.transform == root)
                {
                    break;
                }

                // 前の関節の直接の子でなければ終わり
                if (result.Count > 0 && joint.transform.parent != result[result.Count - 1].transform)
                {
                    break;
                }

                result.Add(joint);
            }

            return result;
        }

        private static string[] IgnoredChildren(Transform root, List<VrmSpringJoint> joints)
        {
            // チェーンに含まれない子（最後の関節は全ての子）を除外してチェーンを VRM の並びに限る
            var ignored = new List<string>();
            for (int i = 0; i < joints.Count; i++)
            {
                Transform next = i + 1 < joints.Count ? joints[i + 1].transform : null;
                foreach (Transform child in joints[i].transform)
                {
                    if (child != next)
                    {
                        ignored.Add(TransformPath.Of(child, root));
                    }
                }
            }

            // 上限を超えた分は除外できない（揺れる範囲が広がるだけなので続行）
            return ignored.Take(PhysBoneSet.MaxPathsPerBone).ToArray();
        }

        private static float ToSpring(float dragForce)
        {
            // VRM は 1 フレームで速度の (1 - dragForce) を残す。PhysBone の spring は残す割合を MinMomentum〜MaxMomentum に割り当てる
            float momentum = 1f - Mathf.Clamp01(dragForce);
            return Mathf.Clamp01(
                (momentum - PhysBoneChain.MinMomentum) / (PhysBoneChain.MaxMomentum - PhysBoneChain.MinMomentum));
        }

        private static float ToGravity(VrmSpringJoint joint)
        {
            // 下向き成分だけを重力として使う（PhysBone の重力は真下のみ）
            Vector3 direction = joint.gravityDir.sqrMagnitude > Epsilon * Epsilon ? joint.gravityDir.normalized : Vector3.down;
            float downward = Vector3.Dot(direction, Vector3.down);
            return Mathf.Clamp(joint.gravityPower * downward * GravityScale, -1f, 1f);
        }

        /// <summary>
        /// 関節ごとの値を「基本値 × チェーン沿いの倍率カーブ」にする。全て同じならカーブは空。
        /// 粒子 d（root = 0）の動きは関節 d-1 のパラメーターで決まる（VRM は関節から次の関節への区間に値を持つ）。
        /// </summary>
        private static (float value, float[] curve) ToCurve(List<VrmSpringJoint> joints, Func<VrmSpringJoint, float> map)
        {
            // 粒子の段数ごとの値（root は先頭の関節と同じ）
            int depth = joints.Count - 1;
            var values = new float[depth + 1];
            for (int d = 0; d <= depth; d++)
            {
                values[d] = map(joints[Mathf.Max(d - 1, 0)]);
            }

            // 基本値は絶対値の最大（0 ならカーブ不要）
            float baseValue = values.OrderByDescending(Mathf.Abs).First();
            bool uniform = values.All(value => Mathf.Abs(value - values[0]) < Epsilon);
            if (uniform || Mathf.Abs(baseValue) < Epsilon)
            {
                return (values[0], Array.Empty<float>());
            }

            // 等間隔にサンプリング（段数が上限以内なら段ごとに 1 つ）
            int samples = Mathf.Min(values.Length, PhysBoneData.MaxCurveSamples);
            var curve = new float[samples];
            for (int s = 0; s < samples; s++)
            {
                float position = samples > 1 ? s * depth / (float)(samples - 1) : 0f;
                int index = Mathf.Min((int)position, depth - 1);
                float value = Mathf.Lerp(values[index], values[index + 1], position - index);
                curve[s] = value / baseValue;
            }

            return (baseValue, curve);
        }
    }
}
