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

        // Direct BlendTree の子を有効とみなす重みの下限
        private const float DirectWeightThreshold = 0.5f;

        // ON/OFF 系カーブを ON とみなす値の下限
        private const float OnThreshold = 0.5f;

        // 適用対象のプロパティ名
        private const string ActivePropertyName = "m_IsActive";
        private const string EnabledPropertyName = "m_Enabled";
        private const string BlendShapePrefix = "blendShape.";
        private const string MaterialPropertyPrefix = "m_Materials.Array.data[";

        /// <summary>
        /// 焼き込んだアニメーションクリップ数を返す。
        /// movedObjects は書き出し中に付け替えたオブジェクト（元のパス → 移動後）で、古いパスのカーブを読み替える。
        /// </summary>
        public static int Bake(
            GameObject target,
            AnimatorController controller,
            IReadOnlyDictionary<string, float> overrides,
            IReadOnlyDictionary<string, Transform> movedObjects = null)
        {
            // コントローラーのパラメーター既定値に Expression Parameters の既定値を上書き
            Dictionary<string, float> values = BuildParameterValues(controller, overrides);

            var clips = new List<AnimationClip>();
            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                AnimatorControllerLayer layer = layers[i];

                // 重み 0 のレイヤー（先頭は常に有効）と Synced レイヤーはスキップ
                if ((i > 0 && layer.defaultWeight <= 0f) || layer.syncedLayerIndex >= 0)
                {
                    continue;
                }

                // 初期状態で到達するステートのモーションからクリップを集める
                AnimatorState state = ResolveState(layer.stateMachine, values);
                if (state != null)
                {
                    CollectClips(state.motion, values, clips);
                }
            }

            // レイヤー順に適用（後のレイヤーが優先される Animator の挙動に合わせる）
            foreach (AnimationClip clip in clips)
            {
                ApplyClipAtStart(target, clip, movedObjects);
            }

            return clips.Count;
        }

        /// <summary>
        /// クリップの 0 秒時点の値のうち、見た目の ON/OFF に関わるものだけを適用する。
        /// SampleAnimation は Humanoid のマッスルを既定ポーズへ戻し、Constraint 前提の Transform 値も
        /// 書き込んでしまうため使わない。Transform・マッスル・マテリアルプロパティは対象外。
        /// </summary>
        private static void ApplyClipAtStart(
            GameObject root, AnimationClip clip, IReadOnlyDictionary<string, Transform> movedObjects)
        {
            // 数値カーブ: GameObject 有効状態 / Renderer 有効状態 / BlendShape
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null)
                {
                    continue;
                }

                ApplyFloat(ResolveAnimatedObject(root, binding, movedObjects), binding.propertyName, curve.Evaluate(0f));
            }

            // 参照カーブ: マテリアル差し替え
            foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (keys == null || keys.Length == 0)
                {
                    continue;
                }

                ApplyMaterial(ResolveAnimatedObject(root, binding, movedObjects), binding.propertyName, keys[0].value as Material);
            }
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
            if (animated is SkinnedMeshRenderer skinned && skinned.sharedMesh != null
                && propertyName.StartsWith(BlendShapePrefix, System.StringComparison.Ordinal))
            {
                int index = skinned.sharedMesh.GetBlendShapeIndex(propertyName.Substring(BlendShapePrefix.Length));
                if (index >= 0)
                {
                    skinned.SetBlendShapeWeight(index, value);
                }
            }
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

        private static void CollectClips(Motion motion, Dictionary<string, float> values, List<AnimationClip> clips)
        {
            // クリップはそのまま追加
            if (motion is AnimationClip clip)
            {
                clips.Add(clip);
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
                    // Direct は重みパラメーターが有効な子をすべて適用（WD Off トグル手法に対応）
                    foreach (ChildMotion child in children)
                    {
                        values.TryGetValue(child.directBlendParameter, out float weight);
                        if (weight >= DirectWeightThreshold)
                        {
                            CollectClips(child.motion, values, clips);
                        }
                    }

                    break;
                case BlendTreeType.Simple1D:
                    // 1D はパラメーター値に最も近い閾値の子を採用
                    values.TryGetValue(tree.blendParameter, out float blendValue);
                    CollectClips(FindNearestChild(children, blendValue).motion, values, clips);
                    break;
                default:
                    // 2D 系は近似として先頭の子を採用
                    CollectClips(children[0].motion, values, clips);
                    break;
            }
        }

        private static ChildMotion FindNearestChild(ChildMotion[] children, float value)
        {
            // 閾値との差が最小の子を線形探索
            ChildMotion nearest = children[0];
            float bestDistance = Mathf.Abs(children[0].threshold - value);
            for (int i = 1; i < children.Length; i++)
            {
                float distance = Mathf.Abs(children[i].threshold - value);
                if (distance < bestDistance)
                {
                    nearest = children[i];
                    bestDistance = distance;
                }
            }

            return nearest;
        }
    }
}
