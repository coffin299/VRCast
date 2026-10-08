using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// Modular Avatar の Blendshape Sync を読み、同期の一覧（BlendShapeSyncSet）にする。
    /// NDMF がコンポーネントを消す前に Capture で控え、改変の適用後に Build で今のパスへ直す
    /// （統合でオブジェクトが動いても追えるよう Transform で持つ。消えたメッシュ・BlendShape は除く）。
    /// </summary>
    public static class BlendShapeSyncExtractor
    {
        // MA のコンポーネント型名と、AvatarObjectReference のアバタールートを指すパス
        private const string SyncTypeName = "ModularAvatarBlendshapeSync";
        private const string AvatarRootPath = "$$$AVATAR_ROOT$$$";

        internal sealed class Entry
        {
            public Transform Source;
            public string SourceBlendShape;
            public Transform Target;
            public string TargetBlendShape;
        }

        /// <summary>
        /// NDMF 実行前に控えた同期の設定。
        /// </summary>
        public sealed class Plan
        {
            internal Transform Root;
            internal readonly List<Entry> Entries = new List<Entry>();
        }

        /// <summary>
        /// MA のコンポーネントが消される前に、同期元・同期先と BlendShape 名を控える。
        /// </summary>
        public static Plan Capture(GameObject root)
        {
            var plan = new Plan { Root = root.transform };
            foreach (Component component in ReflectionUtility.FindComponents(root, SyncTypeName))
            {
                // 同期の一覧（MA の BlendshapeBinding の List）
                if (!(ReflectionUtility.GetField(component, "Bindings") is IList bindings))
                {
                    continue;
                }

                foreach (object binding in bindings)
                {
                    // 同期元のメッシュと BlendShape 名（解決できない・名前が無いものは MA でも同期されない）
                    Transform source = ResolveReference(ReflectionUtility.GetField(binding, "ReferenceMesh"), component,
                        root.transform);
                    string sourceShape = ReflectionUtility.GetField(binding, "Blendshape") as string;
                    if (source == null || string.IsNullOrEmpty(sourceShape))
                    {
                        continue;
                    }

                    // 同期先はコンポーネントのあるメッシュ。名前が空なら同期元と同じ名前
                    string localShape = ReflectionUtility.GetField(binding, "LocalBlendshape") as string;
                    plan.Entries.Add(new Entry
                    {
                        Source = source,
                        SourceBlendShape = sourceShape,
                        Target = component.transform,
                        TargetBlendShape = string.IsNullOrEmpty(localShape) ? sourceShape : localShape,
                    });
                }
            }

            return plan;
        }

        /// <summary>
        /// 控えた同期を今のパスで一覧にする（どちらかのメッシュ・BlendShape が無くなったもの、自分自身への同期は除く）。
        /// </summary>
        public static BlendShapeSyncSet Build(Plan plan)
        {
            var bindings = new List<BlendShapeSyncBinding>();
            var seen = new HashSet<(string, string)>();
            foreach (Entry entry in plan.Entries)
            {
                // 同期先ごとに 1 つ（同じ同期先が重なれば先のものを使う）
                if (!HasBlendShape(entry.Source, entry.SourceBlendShape)
                    || !HasBlendShape(entry.Target, entry.TargetBlendShape)
                    || (entry.Source == entry.Target && entry.SourceBlendShape == entry.TargetBlendShape))
                {
                    continue;
                }

                string targetPath = ReflectionUtility.PathOf(entry.Target, plan.Root);
                if (!seen.Add((targetPath, entry.TargetBlendShape)))
                {
                    continue;
                }

                bindings.Add(new BlendShapeSyncBinding
                {
                    sourcePath = ReflectionUtility.PathOf(entry.Source, plan.Root),
                    sourceBlendShape = entry.SourceBlendShape,
                    targetPath = targetPath,
                    targetBlendShape = entry.TargetBlendShape,
                });
            }

            return new BlendShapeSyncSet { bindings = bindings.ToArray() };
        }

        private static Transform ResolveReference(object reference, Component container, Transform root)
        {
            // 参照が無ければ解決できない
            if (reference == null)
            {
                return null;
            }

            // MA の AvatarObjectReference.Get（版によって参照の持ち方が違うため、あれば MA 自身に解決させる）
            MethodInfo get = reference.GetType().GetMethod("Get", new[] { typeof(Component) });
            if (get != null)
            {
                try
                {
                    if (get.Invoke(reference, new object[] { container }) is GameObject found && found != null)
                    {
                        return found.transform;
                    }
                }
                catch (TargetInvocationException)
                {
                    // MA 内部の失敗はパスでの解決に任せる
                }
            }

            // アバタールートからのパス
            if (!(ReflectionUtility.GetField(reference, "referencePath") is string path))
            {
                return null;
            }

            return path == AvatarRootPath || path.Length == 0 ? root : root.Find(path);
        }

        private static bool HasBlendShape(Transform transform, string blendShape)
        {
            // 破棄済み・メッシュの無いもの・その名前の BlendShape が無いものは同期できない
            var renderer = transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
            Mesh mesh = renderer != null ? renderer.sharedMesh : null;
            return mesh != null && mesh.GetBlendShapeIndex(blendShape) >= 0;
        }
    }
}
