using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// VRChat の Base / Action レイヤーから、立ち・待機の Humanoid クリップを控える。
    /// 既定の VRChat コントローラー（isDefault）はアバターに含まれないので対象外。
    /// </summary>
    public static class StandClipExporter
    {
        // 取り込みすぎない
        private const int MaxClips = 12;

        // 短すぎるクリップはポーズとして扱わない
        private const float MinLength = 0.2f;

        public static void Collect(Component descriptor, List<AnimationClip> clips, List<string> labels)
        {
            clips.Clear();
            labels.Clear();
            if (descriptor == null)
            {
                return;
            }

            if (!(ReflectionUtility.GetField(descriptor, "baseAnimationLayers") is System.Collections.IEnumerable layers))
            {
                return;
            }

            foreach (object layer in layers)
            {
                if (layer == null)
                {
                    continue;
                }

                string type = ReflectionUtility.GetField(layer, "type")?.ToString();
                if (type != "Base" && type != "Action")
                {
                    continue;
                }

                if (ReflectionUtility.GetField(layer, "isDefault") is bool isDefault && isDefault)
                {
                    continue;
                }

                var controller = ReflectionUtility.GetField(layer, "animatorController") as RuntimeAnimatorController;
                if (controller == null)
                {
                    continue;
                }

                // Base の初期状態は名前が立ちでなくても、立っているときの動きとして入れる
                bool unnamedDefault = type == "Base";
                if (controller is AnimatorController animator)
                {
                    Walk(animator, unnamedDefault, clips, labels);
                }
                else
                {
                    WalkOverride(controller, clips, labels);
                }
            }
        }

        public static void Attach(GameObject root, List<AnimationClip> clips, List<string> labels)
        {
            if (root == null || clips == null || clips.Count == 0)
            {
                return;
            }

            StandClipHolder holder = root.GetComponent<StandClipHolder>();
            if (holder == null)
            {
                holder = root.AddComponent<StandClipHolder>();
            }

            holder.clips = clips.ToArray();
            holder.labels = labels.ToArray();
        }

        private static void Walk(AnimatorController controller, bool unnamedDefault, List<AnimationClip> clips,
            List<string> labels)
        {
            if (controller.layers == null || controller.layers.Length == 0)
            {
                return;
            }

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            if (machine == null)
            {
                return;
            }

            if (unnamedDefault && machine.defaultState != null)
            {
                AddMotion(machine.defaultState.motion, machine.defaultState.name, false, clips, labels);
            }

            WalkMachine(machine, clips, labels);
        }

        private static void WalkMachine(AnimatorStateMachine machine, List<AnimationClip> clips, List<string> labels)
        {
            foreach (ChildAnimatorState child in machine.states)
            {
                if (child.state == null)
                {
                    continue;
                }

                AddMotion(child.state.motion, child.state.name, true, clips, labels);
            }

            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
            {
                if (child.stateMachine != null)
                {
                    WalkMachine(child.stateMachine, clips, labels);
                }
            }
        }

        private static void WalkOverride(RuntimeAnimatorController controller, List<AnimationClip> clips,
            List<string> labels)
        {
            if (!(controller is AnimatorOverrideController over))
            {
                return;
            }

            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            over.GetOverrides(pairs);
            foreach (KeyValuePair<AnimationClip, AnimationClip> pair in pairs)
            {
                AnimationClip clip = pair.Value != null ? pair.Value : pair.Key;
                string name = pair.Key != null ? pair.Key.name : clip != null ? clip.name : string.Empty;
                AddClip(clip, name, true, clips, labels);
            }
        }

        private static void AddMotion(Motion motion, string name, bool nameRequired, List<AnimationClip> clips,
            List<string> labels)
        {
            if (motion is AnimationClip clip)
            {
                AddClip(clip, string.IsNullOrEmpty(name) ? clip.name : name, nameRequired, clips, labels);
                return;
            }

            if (!(motion is BlendTree tree) || tree.children == null)
            {
                return;
            }

            // 状態名が立ちらしいブレンドは、中の Humanoid クリップも立ちとして入れる
            bool parentMatches = StandClipNames.LooksLikeStand(name);
            foreach (ChildMotion child in tree.children)
            {
                AddMotion(child.motion, name, nameRequired && !parentMatches, clips, labels);
            }
        }

        private static void AddClip(AnimationClip clip, string name, bool nameRequired, List<AnimationClip> clips,
            List<string> labels)
        {
            if (clips.Count >= MaxClips || clip == null || !clip.isHumanMotion || clip.length < MinLength)
            {
                return;
            }

            if (nameRequired && !StandClipNames.LooksLikeStand(name) && !StandClipNames.LooksLikeStand(clip.name))
            {
                return;
            }

            if (clips.Contains(clip))
            {
                return;
            }

            clips.Add(clip);
            labels.Add(string.IsNullOrEmpty(name) ? clip.name : name);
        }
    }
}
