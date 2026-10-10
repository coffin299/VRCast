using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// 改変適用後の Expression Menu の FaceEmo のサブメニューをたどり、各ボタンが切り替えるパラメータと値から
    /// FX で再生されるクリップを探して、メニューの名前付きの表情として返す（VRChat と同じ道筋で読む）。
    /// VRChat SDK のアセンブリは参照せず、フィールド名と SerializedObject で読む。
    /// </summary>
    public static class ExpressionMenuReader
    {
        // このサブメニュー名を含む階層の下だけを読む（FaceEmo の既定のメニュー名。大文字小文字は区別しない）
        private const string FaceEmoMenuKeyword = "FaceEmo";

        // VRCExpressionsMenu.Control.ControlType の値（ボタン・トグル・サブメニュー）
        private const int ControlButton = 101;
        private const int ControlToggle = 102;
        private const int ControlSubMenu = 103;

        // サブメニューの入れ子の上限（循環・壊れたデータでの無限再帰を防ぐ）
        private const int MaxDepth = 16;

        // 小数のパラメータ値を比べるときの許容差
        private const float ValueTolerance = 0.001f;

        // ハンドジェスチャーのパラメータ名（この条件が 0 のものを「手を動かしていない表情」として優先する）
        private static readonly string[] GestureParameters = { "GestureLeft", "GestureRight" };

        /// <param name="descriptor">改変適用後の複製の VRCAvatarDescriptor</param>
        /// <param name="fx">改変適用後の FX コントローラー</param>
        /// <param name="trace">読んだメニューと見つからなかったボタンを書き足す一覧（不要なら null）</param>
        public static List<ExpressionExtractor.NamedClip> Read(Component descriptor, AnimatorController fx,
            List<string> trace = null)
        {
            var clips = new List<ExpressionExtractor.NamedClip>();
            var menu = descriptor != null ? ReflectionUtility.GetField(descriptor, "expressionsMenu") as Object : null;
            if (menu == null || fx == null)
            {
                return clips;
            }

            List<Transition> transitions = CollectTransitions(fx);
            ReadMenu(menu, string.Empty, false, transitions, clips, trace, new HashSet<Object>(), 0);
            return clips;
        }

        private static void ReadMenu(Object menu, string path, bool inFaceEmo, List<Transition> transitions,
            List<ExpressionExtractor.NamedClip> clips, List<string> trace, HashSet<Object> visited, int depth)
        {
            // 未設定・循環・深すぎるメニューは読まない
            if (menu == null || depth > MaxDepth || !visited.Add(menu))
            {
                return;
            }

            using (var serialized = new SerializedObject(menu))
            {
                SerializedProperty controls = serialized.FindProperty("controls");
                for (int i = 0; controls != null && controls.isArray && i < controls.arraySize; i++)
                {
                    SerializedProperty control = controls.GetArrayElementAtIndex(i);
                    string name = control.FindPropertyRelative("name")?.stringValue ?? string.Empty;
                    int type = control.FindPropertyRelative("type")?.intValue ?? 0;
                    string controlPath = string.IsNullOrEmpty(path) ? name : $"{path} / {name}";

                    // サブメニューは中へ（名前に FaceEmo を含む階層から下を対象にする）
                    if (type == ControlSubMenu)
                    {
                        bool faceEmo = inFaceEmo
                            || name.IndexOf(FaceEmoMenuKeyword, StringComparison.OrdinalIgnoreCase) >= 0;
                        Object subMenu = control.FindPropertyRelative("subMenu")?.objectReferenceValue;
                        ReadMenu(subMenu, controlPath, faceEmo, transitions, clips, trace, visited, depth + 1);
                        continue;
                    }

                    // FaceEmo の下のボタン・トグルだけを表情として読む
                    if (!inFaceEmo || (type != ControlButton && type != ControlToggle))
                    {
                        continue;
                    }

                    string parameter = control.FindPropertyRelative("parameter")?.FindPropertyRelative("name")
                        ?.stringValue;
                    float value = control.FindPropertyRelative("value")?.floatValue ?? 1f;
                    AnimationClip clip = FindClip(transitions, parameter, value);
                    if (clip == null)
                    {
                        trace?.Add($"menu {controlPath}: no clip for {parameter} = {value}");
                        continue;
                    }

                    // 値がすべて 0・BlendShape を動かさないもの（設定用のトグル等）は表情にしない
                    ClipCheck check = ExpressionExtractor.Check(clip);
                    if (check == ClipCheck.NoCurves || check == ClipCheck.AllZero)
                    {
                        trace?.Add($"menu {controlPath}: {check} ({clip.name})");
                        continue;
                    }

                    trace?.Add($"menu {controlPath}: {clip.name}");
                    clips.Add(new ExpressionExtractor.NamedClip { Clip = clip, Name = name, Source = "menu " + controlPath });
                }
            }
        }

        private struct Transition
        {
            public AnimatorCondition[] Conditions;
            public AnimatorState Destination;
        }

        private static List<Transition> CollectTransitions(AnimatorController fx)
        {
            // 全レイヤーの遷移（Any State・ステート・サブステートマシン・Entry）を、遷移先のステートと条件の組で集める
            var result = new List<Transition>();
            foreach (AnimatorControllerLayer layer in fx.layers)
            {
                CollectTransitions(layer.stateMachine, result, 0);
            }

            return result;
        }

        private static void CollectTransitions(AnimatorStateMachine machine, List<Transition> result, int depth)
        {
            if (machine == null || depth > MaxDepth)
            {
                return;
            }

            Add(machine.anyStateTransitions, result);
            Add(machine.entryTransitions, result);
            foreach (ChildAnimatorState child in machine.states)
            {
                Add(child.state.transitions, result);
            }

            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
            {
                Add(machine.GetStateMachineTransitions(child.stateMachine), result);
                CollectTransitions(child.stateMachine, result, depth + 1);
            }
        }

        private static void Add(AnimatorTransitionBase[] transitions, List<Transition> result)
        {
            // 遷移先がステートのものだけ（サブステートマシン・Exit 行きは再生するクリップが決まらない）
            foreach (AnimatorTransitionBase transition in transitions)
            {
                if (transition != null && transition.destinationState != null)
                {
                    result.Add(new Transition
                    {
                        Conditions = transition.conditions,
                        Destination = transition.destinationState,
                    });
                }
            }
        }

        private static AnimationClip FindClip(List<Transition> transitions, string parameter, float value)
        {
            // パラメータ未設定のボタンは対象外
            if (string.IsNullOrEmpty(parameter))
            {
                return null;
            }

            // パラメータが値と一致する遷移のうち、ほかの条件が少ない（手のジェスチャーが 0 のものを含む）ものを選ぶ
            AnimationClip best = null;
            int bestScore = int.MaxValue;
            foreach (Transition transition in transitions)
            {
                if (!Matches(transition.Conditions, parameter, value, out int otherConditions))
                {
                    continue;
                }

                AnimationClip clip = ClipOf(transition.Destination.motion);
                if (clip != null && otherConditions < bestScore)
                {
                    best = clip;
                    bestScore = otherConditions;
                }
            }

            return best;
        }

        private static bool Matches(AnimatorCondition[] conditions, string parameter, float value, out int score)
        {
            // 対象のパラメータの条件が値と一致するか。ほかの条件の数（ジェスチャーが 0 の条件は数えない）を score に返す
            bool matched = false;
            score = 0;
            foreach (AnimatorCondition condition in conditions)
            {
                if (condition.parameter == parameter)
                {
                    if (!Satisfies(condition, value))
                    {
                        return false;
                    }

                    matched = true;
                }
                else if (!IsNeutralGesture(condition))
                {
                    score++;
                }
            }

            return matched;
        }

        private static bool Satisfies(AnimatorCondition condition, float value)
        {
            // ボタンを押したときの値で、その条件が成り立つか
            switch (condition.mode)
            {
                case AnimatorConditionMode.Equals:
                    return Mathf.Abs(condition.threshold - value) < ValueTolerance;
                case AnimatorConditionMode.NotEqual:
                    return Mathf.Abs(condition.threshold - value) >= ValueTolerance;
                case AnimatorConditionMode.Greater:
                    return value > condition.threshold;
                case AnimatorConditionMode.Less:
                    return value < condition.threshold;
                case AnimatorConditionMode.If:
                    return value > 0f;
                case AnimatorConditionMode.IfNot:
                    return value <= 0f;
                default:
                    return false;
            }
        }

        private static bool IsNeutralGesture(AnimatorCondition condition)
        {
            // 「手のジェスチャーが 0（何もしていない）」の条件
            return Array.IndexOf(GestureParameters, condition.parameter) >= 0
                && condition.mode == AnimatorConditionMode.Equals && Mathf.Abs(condition.threshold) < ValueTolerance;
        }

        private static AnimationClip ClipOf(Motion motion)
        {
            // クリップならそのまま、ブレンドツリーなら最後の子（ジェスチャーを握り切ったとき等の表情）
            if (motion is AnimationClip clip)
            {
                return clip;
            }

            if (motion is BlendTree tree && tree.children.Length > 0)
            {
                for (int i = tree.children.Length - 1; i >= 0; i--)
                {
                    AnimationClip child = ClipOf(tree.children[i].motion);
                    if (child != null)
                    {
                        return child;
                    }
                }
            }

            return null;
        }
    }
}
