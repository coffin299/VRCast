using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// FX コントローラーの初期状態（パラメーター既定値で到達するステート）のアニメーションのうち、
    /// 表示 ON/OFF・BlendShape・マテリアル差し替えを 0 秒時点の値で複製アバターへ焼き込む近似処理。
    /// </summary>
    public static class FxDefaultStateBaker
    {
        // 遷移を辿る最大回数（循環対策）
        private const int MaxHops = 16;

        // ON/OFF 系カーブを ON とみなす値の下限
        private const float OnThreshold = 0.5f;

        // 適用対象のプロパティ名
        private const string ActivePropertyName = "m_IsActive";
        private const string EnabledPropertyName = "m_Enabled";
        private const string BlendShapePrefix = "blendShape.";
        private const string MaterialPropertyPrefix = "m_Materials.Array.data[";

        // BlendTree の重みを掛け合わせたクリップ
        private readonly struct WeightedClip
        {
            public readonly AnimationClip Clip;
            public readonly float Weight;

            public WeightedClip(AnimationClip clip, float weight)
            {
                Clip = clip;
                Weight = weight;
            }
        }

        // 1 レイヤー内で同じプロパティを動かすカーブの重み付き合計
        private struct FloatSample
        {
            public float WeightedSum;
            public float TotalWeight;
        }

        /// <summary>
        /// 焼き込んだアニメーションクリップ数を返す。
        /// movedObjects は書き出し中に付け替えたオブジェクト（元のパス → 移動後）で、古いパスのカーブを読み替える。
        /// keepSceneBlendShapes が true なら BlendShape は焼き込まず、シーン上の値を残す。
        /// </summary>
        public static int Bake(
            GameObject target,
            AnimatorController controller,
            IReadOnlyDictionary<string, float> overrides,
            IReadOnlyDictionary<string, Transform> movedObjects = null,
            bool keepSceneBlendShapes = false)
        {
            // コントローラーのパラメーター既定値に Expression Parameters の既定値を上書き
            Dictionary<string, float> values = BuildParameterValues(controller, overrides);

            int bakedCount = 0;
            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                AnimatorControllerLayer layer = layers[i];

                // 重み 0 のレイヤー（先頭は常に有効）と Synced レイヤーはスキップ
                if ((i > 0 && layer.defaultWeight <= 0f) || layer.syncedLayerIndex >= 0)
                {
                    continue;
                }

                // 初期状態で到達するステートのモーションから、重み付きでクリップを集める
                AnimatorState state = ResolveState(layer.stateMachine, values);
                if (state == null)
                {
                    continue;
                }

                var clips = new List<WeightedClip>();
                CollectClips(state.motion, 1f, values, clips);

                // レイヤー順に適用（後のレイヤーが優先される Animator の挙動に合わせる）
                ApplyLayerAtStart(target, clips, movedObjects, keepSceneBlendShapes);
                bakedCount += clips.Count;
            }

            return bakedCount;
        }

        /// <summary>
        /// 1 レイヤー分のクリップの 0 秒時点の値を重みで混ぜ、見た目の ON/OFF に関わるものだけを適用する。
        /// SampleAnimation は Humanoid のマッスルを既定ポーズへ戻し、Constraint 前提の Transform 値も
        /// 書き込んでしまうため使わない。Transform・マッスル・マテリアルプロパティは対象外。
        /// </summary>
        private static void ApplyLayerAtStart(
            GameObject root,
            List<WeightedClip> clips,
            IReadOnlyDictionary<string, Transform> movedObjects,
            bool keepSceneBlendShapes)
        {
            var floats = new Dictionary<(Object, string), FloatSample>();
            var materials = new Dictionary<(Object, string), (Material material, float weight)>();

            foreach (WeightedClip weighted in clips)
            {
                // 数値カーブ: GameObject 有効状態 / Renderer 有効状態 / BlendShape
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(weighted.Clip))
                {
                    // シーンの値を優先する指定なら BlendShape は触らない
                    if (keepSceneBlendShapes && IsBlendShapeProperty(binding.propertyName))
                    {
                        continue;
                    }

                    AnimationCurve curve = AnimationUtility.GetEditorCurve(weighted.Clip, binding);
                    Object animated = ResolveAnimatedObject(root, binding, movedObjects);
                    if (curve == null || animated == null)
                    {
                        continue;
                    }

                    // 同じプロパティへの値を重み付きで足し込む
                    var key = (animated, binding.propertyName);
                    floats.TryGetValue(key, out FloatSample sample);
                    sample.WeightedSum += weighted.Weight * curve.Evaluate(0f);
                    sample.TotalWeight += weighted.Weight;
                    floats[key] = sample;
                }

                // 参照カーブ: マテリアル差し替え（混ぜられないため最も重いクリップの値を採用）
                foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(weighted.Clip))
                {
                    ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(weighted.Clip, binding);
                    Object animated = ResolveAnimatedObject(root, binding, movedObjects);
                    if (keys == null || keys.Length == 0 || !(keys[0].value is Material material) || animated == null)
                    {
                        continue;
                    }

                    var key = (animated, binding.propertyName);
                    if (!materials.TryGetValue(key, out var current) || weighted.Weight > current.weight)
                    {
                        materials[key] = (material, weighted.Weight);
                    }
                }
            }

            foreach (KeyValuePair<(Object, string), FloatSample> pair in floats)
            {
                (Object animated, string propertyName) = pair.Key;
                FloatSample sample = pair.Value;

                // 重みの合計が 1 未満なら、足りない分は現在の値（既定値の近似）で埋める
                float value = sample.TotalWeight >= 1f
                    ? sample.WeightedSum / sample.TotalWeight
                    : sample.WeightedSum + (1f - sample.TotalWeight) * ReadFloat(animated, propertyName);
                ApplyFloat(animated, propertyName, value);
            }

            foreach (KeyValuePair<(Object, string), (Material material, float weight)> pair in materials)
            {
                ApplyMaterial(pair.Key.Item1, pair.Key.Item2, pair.Value.material);
            }
        }

        private static bool IsBlendShapeProperty(string propertyName)
        {
            return propertyName.StartsWith(BlendShapePrefix, System.StringComparison.Ordinal);
        }

        private static Object ResolveAnimatedObject(
            GameObject root, EditorCurveBinding binding, IReadOnlyDictionary<string, Transform> movedObjects)
        {
            // 通常はパスどおりに解決
            Object animated = AnimationUtility.GetAnimatedObject(root, binding);
            if (animated != null || movedObjects == null)
            {
                return animated;
            }

            // 付け替え前のパスで書かれたカーブは、最も深く一致する移動済みオブジェクトから残りのパスを辿る
            Transform best = null;
            string rest = null;
            int bestLength = -1;
            foreach (KeyValuePair<string, Transform> pair in movedObjects)
            {
                if (pair.Value == null || pair.Key.Length <= bestLength)
                {
                    continue;
                }

                if (binding.path == pair.Key)
                {
                    best = pair.Value;
                    rest = string.Empty;
                    bestLength = pair.Key.Length;
                }
                else if (binding.path.StartsWith(pair.Key + "/", System.StringComparison.Ordinal))
                {
                    best = pair.Value;
                    rest = binding.path.Substring(pair.Key.Length + 1);
                    bestLength = pair.Key.Length;
                }
            }

            // 一致が無い・残りのパスが見つからなければ解決できない
            Transform resolved = best == null ? null : (rest.Length == 0 ? best : best.Find(rest));
            if (resolved == null)
            {
                return null;
            }

            // GameObject 自体のカーブ（有効状態）とコンポーネントのカーブを区別
            return binding.type == typeof(GameObject) ? resolved.gameObject : (Object)resolved.GetComponent(binding.type);
        }

        private static void ApplyFloat(Object animated, string propertyName, float value)
        {
            // GameObject の有効状態
            if (animated is GameObject go && propertyName == ActivePropertyName)
            {
                go.SetActive(value >= OnThreshold);
                return;
            }

            // Renderer の有効状態
            if (animated is Renderer renderer && propertyName == EnabledPropertyName)
            {
                renderer.enabled = value >= OnThreshold;
                return;
            }

            // BlendShape の重み（"blendShape.<名前>"）
            if (TryGetBlendShape(animated, propertyName, out SkinnedMeshRenderer skinned, out int index))
            {
                skinned.SetBlendShapeWeight(index, value);
            }
        }

        private static float ReadFloat(Object animated, string propertyName)
        {
            // ApplyFloat と同じ対象について、焼き込み前の値を数値で返す
            if (animated is GameObject go && propertyName == ActivePropertyName)
            {
                return go.activeSelf ? 1f : 0f;
            }

            if (animated is Renderer renderer && propertyName == EnabledPropertyName)
            {
                return renderer.enabled ? 1f : 0f;
            }

            return TryGetBlendShape(animated, propertyName, out SkinnedMeshRenderer skinned, out int index)
                ? skinned.GetBlendShapeWeight(index)
                : 0f;
        }

        private static bool TryGetBlendShape(
            Object animated, string propertyName, out SkinnedMeshRenderer skinned, out int index)
        {
            // "blendShape.<名前>" を SkinnedMeshRenderer のメッシュ上の添字に解決
            skinned = animated as SkinnedMeshRenderer;
            index = skinned != null && skinned.sharedMesh != null && IsBlendShapeProperty(propertyName)
                ? skinned.sharedMesh.GetBlendShapeIndex(propertyName.Substring(BlendShapePrefix.Length))
                : -1;
            return index >= 0;
        }

        private static void ApplyMaterial(Object animated, string propertyName, Material material)
        {
            // Renderer の "m_Materials.Array.data[i]" のみ対象
            if (!(animated is Renderer renderer) || material == null
                || !propertyName.StartsWith(MaterialPropertyPrefix, System.StringComparison.Ordinal))
            {
                return;
            }

            // 末尾の "]" を除いた添字を取り出す
            string indexText = propertyName.Substring(MaterialPropertyPrefix.Length).TrimEnd(']');
            if (!int.TryParse(indexText, out int index))
            {
                return;
            }

            // 範囲内ならその枠だけ差し替え
            Material[] materials = renderer.sharedMaterials;
            if (index >= 0 && index < materials.Length)
            {
                materials[index] = material;
                renderer.sharedMaterials = materials;
            }
        }

        private static Dictionary<string, float> BuildParameterValues(
            AnimatorController controller, IReadOnlyDictionary<string, float> overrides)
        {
            var values = new Dictionary<string, float>();

            // 型ごとの既定値を数値として格納（Trigger は 0）
            foreach (AnimatorControllerParameter parameter in controller.parameters)
            {
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        values[parameter.name] = parameter.defaultBool ? 1f : 0f;
                        break;
                    case AnimatorControllerParameterType.Int:
                        values[parameter.name] = parameter.defaultInt;
                        break;
                    case AnimatorControllerParameterType.Float:
                        values[parameter.name] = parameter.defaultFloat;
                        break;
                    default:
                        values[parameter.name] = 0f;
                        break;
                }
            }

            // 同期パラメーターの既定値で上書き
            foreach (KeyValuePair<string, float> pair in overrides)
            {
                values[pair.Key] = pair.Value;
            }

            return values;
        }

        private static AnimatorState ResolveState(AnimatorStateMachine stateMachine, Dictionary<string, float> values)
        {
            AnimatorState current = stateMachine.defaultState;
            var visited = new HashSet<AnimatorState>();

            // 条件を満たす遷移を辿る（訪問済みに戻ったら停止）
            for (int hop = 0; hop < MaxHops && current != null && visited.Add(current); hop++)
            {
                // Any State 遷移を優先（条件無しは常時発火して判定不能なため除外）
                AnimatorState next = FindDestination(stateMachine.anyStateTransitions, values, current, true)
                    ?? FindDestination(current.transitions, values, current, false);
                if (next == null)
                {
                    break;
                }

                current = next;
            }

            return current;
        }

        private static AnimatorState FindDestination(
            AnimatorStateTransition[] transitions, Dictionary<string, float> values, AnimatorState current, bool requireConditions)
        {
            foreach (AnimatorStateTransition transition in transitions)
            {
                // ステート以外（Exit / サブステートマシン）や自己遷移は対象外
                if (transition.destinationState == null || transition.destinationState == current || transition.mute)
                {
                    continue;
                }

                // Any State の条件無し遷移はスキップ
                if (requireConditions && transition.conditions.Length == 0)
                {
                    continue;
                }

                // 全条件が既定値で成立すれば遷移先とする
                if (AllConditionsMet(transition.conditions, values))
                {
                    return transition.destinationState;
                }
            }

            return null;
        }

        private static bool AllConditionsMet(AnimatorCondition[] conditions, Dictionary<string, float> values)
        {
            foreach (AnimatorCondition condition in conditions)
            {
                // 未定義パラメーター（VRChat 組み込み等）は 0 とみなす
                values.TryGetValue(condition.parameter, out float value);
                if (!IsConditionMet(condition.mode, value, condition.threshold))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsConditionMet(AnimatorConditionMode mode, float value, float threshold)
        {
            // Animator の条件モードを数値比較に置き換える
            switch (mode)
            {
                case AnimatorConditionMode.If:
                    return value != 0f;
                case AnimatorConditionMode.IfNot:
                    return value == 0f;
                case AnimatorConditionMode.Greater:
                    return value > threshold;
                case AnimatorConditionMode.Less:
                    return value < threshold;
                case AnimatorConditionMode.Equals:
                    return Mathf.Approximately(value, threshold);
                case AnimatorConditionMode.NotEqual:
                    return !Mathf.Approximately(value, threshold);
                default:
                    return false;
            }
        }

        private static void CollectClips(
            Motion motion, float weight, Dictionary<string, float> values, List<WeightedClip> clips)
        {
            // 重みが無いモーションは結果に影響しない
            if (weight <= 0f)
            {
                return;
            }

            // クリップは親から掛け合わせた重みで追加
            if (motion is AnimationClip clip)
            {
                clips.Add(new WeightedClip(clip, weight));
                return;
            }

            // BlendTree 以外（null 含む）は無視
            if (!(motion is BlendTree tree) || tree.children.Length == 0)
            {
                return;
            }

            ChildMotion[] children = tree.children;
            switch (tree.blendType)
            {
                case BlendTreeType.Direct:
                    // Direct は各子を重みパラメーターの値で適用（WD Off トグル手法に対応）
                    foreach (ChildMotion child in children)
                    {
                        values.TryGetValue(child.directBlendParameter, out float childWeight);
                        CollectClips(child.motion, weight * Mathf.Clamp01(childWeight), values, clips);
                    }

                    break;
                case BlendTreeType.Simple1D:
                    // 1D はパラメーター値を挟む 2 つの子を線形補間（ラジアルメニューのスライダー等）
                    values.TryGetValue(tree.blendParameter, out float blendValue);
                    CollectSimple1D(children, blendValue, weight, values, clips);
                    break;
                default:
                    // 2D 系は近似として先頭の子を採用
                    CollectClips(children[0].motion, weight, values, clips);
                    break;
            }
        }

        private static void CollectSimple1D(
            ChildMotion[] children, float value, float weight, Dictionary<string, float> values, List<WeightedClip> clips)
        {
            // 閾値の昇順に並べ替える（元の配列は変更しない）
            var sorted = (ChildMotion[])children.Clone();
            System.Array.Sort(sorted, (a, b) => a.threshold.CompareTo(b.threshold));

            // 範囲外は端の子だけを採用（Animator と同じくクランプ）
            if (value <= sorted[0].threshold)
            {
                CollectClips(sorted[0].motion, weight, values, clips);
                return;
            }

            int last = sorted.Length - 1;
            if (value >= sorted[last].threshold)
            {
                CollectClips(sorted[last].motion, weight, values, clips);
                return;
            }

            // 値を挟む区間を探し、区間内の位置で 2 つの子へ重みを配分
            for (int i = 0; i < last; i++)
            {
                float lower = sorted[i].threshold;
                float upper = sorted[i + 1].threshold;
                if (value > upper)
                {
                    continue;
                }

                // 同じ閾値が並ぶ区間は上側だけを採用（0 除算の回避）
                float t = upper > lower ? (value - lower) / (upper - lower) : 1f;
                CollectClips(sorted[i].motion, weight * (1f - t), values, clips);
                CollectClips(sorted[i + 1].motion, weight * t, values, clips);
                return;
            }
        }
    }
}
