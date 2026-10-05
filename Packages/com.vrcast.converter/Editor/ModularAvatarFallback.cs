using System.Collections.Generic;
using UnityEngine;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// Modular Avatar の Merge Armature / Bone Proxy が NDMF で適用されなかった場合に、
    /// 衣装・小物のボーンをアバターのボーンの子へ付け替えて追従させる補助処理。
    /// NDMF の実行前に Capture で設定を控え、実行後に Apply で未適用の分だけを処理する。
    /// NDMF が無いプロジェクトでも、MA の設定だけでボーンの追従を再現する。
    /// </summary>
    public static class ModularAvatarFallback
    {
        // MA のコンポーネント型名（アセンブリ参照なしで型名から探す）
        private const string MergeArmatureTypeName = "ModularAvatarMergeArmature";
        private const string BoneProxyTypeName = "ModularAvatarBoneProxy";

        // Bone Proxy の配置モード名（MA の BoneProxyAttachmentMode。それ以外は原点に吸着）
        private const string KeepWorldPoseMode = "AsChildKeepWorldPose";
        private const string KeepPositionMode = "AsChildKeepPosition";
        private const string KeepRotationMode = "AsChildKeepRotation";

        internal sealed class MergeEntry
        {
            public Transform Source;
            public Transform Target;
            public string Prefix;
            public string Suffix;
        }

        internal sealed class ProxyEntry
        {
            public Transform Proxy;
            public Transform Target;
            public bool KeepPosition;
            public bool KeepRotation;
            public bool MatchScale;
            public Vector3 WorldPosition;
            public Quaternion WorldRotation;
        }

        /// <summary>
        /// NDMF 実行前に控えた MA の設定。
        /// </summary>
        public sealed class Plan
        {
            internal Transform Root;
            internal readonly List<MergeEntry> Merges = new List<MergeEntry>();
            internal readonly List<ProxyEntry> Proxies = new List<ProxyEntry>();
        }

        /// <summary>
        /// Apply の結果。MovedObjects は付け替え前のパス → 移動後の Transform（FX の焼き込みでパスを読み替えるため）。
        /// </summary>
        public struct Result
        {
            public int FixedCount;
            public Dictionary<string, Transform> MovedObjects;
        }

        /// <summary>
        /// MA のコンポーネントが消される前に、統合元・統合先と配置設定を控える。
        /// </summary>
        public static Plan Capture(GameObject root)
        {
            var plan = new Plan { Root = root.transform };

            // Merge Armature: 衣装側アーマチュア → アバター側アーマチュア
            foreach (Component component in ReflectionUtility.FindComponents(root, MergeArmatureTypeName))
            {
                // 統合先が解決できない設定は MA でも処理されないので控えない
                Transform target = ResolveMergeTarget(component, root.transform);
                if (target == null)
                {
                    continue;
                }

                plan.Merges.Add(new MergeEntry
                {
                    Source = component.transform,
                    Target = target,
                    Prefix = ReflectionUtility.GetField(component, "prefix") as string ?? string.Empty,
                    Suffix = ReflectionUtility.GetField(component, "suffix") as string ?? string.Empty,
                });
            }

            // Bone Proxy: 小物 → 指定ボーン
            foreach (Component component in ReflectionUtility.FindComponents(root, BoneProxyTypeName))
            {
                // アバター外や未解決のボーンは MA でも付け替えられないので控えない
                var target = ReflectionUtility.GetProperty(component, "target") as Transform;
                if (target == null || !target.IsChildOf(root.transform))
                {
                    continue;
                }

                // 列挙値は名前で比べる（MA の型を参照しないため）
                string mode = ReflectionUtility.GetField(component, "attachmentMode")?.ToString();
                plan.Proxies.Add(new ProxyEntry
                {
                    Proxy = component.transform,
                    Target = target,
                    KeepPosition = mode == KeepWorldPoseMode || mode == KeepPositionMode,
                    KeepRotation = mode == KeepWorldPoseMode || mode == KeepRotationMode,
                    MatchScale = ReflectionUtility.GetBool(component, "matchScale", false),
                    WorldPosition = component.transform.position,
                    WorldRotation = component.transform.rotation,
                });
            }

            return plan;
        }

        /// <summary>
        /// 控えた設定のうち、まだアバターのボーンの子になっていないものを付け替える。
        /// </summary>
        public static Result Apply(Plan plan)
        {
            var result = new Result { MovedObjects = new Dictionary<string, Transform>() };

            // 付け替え前のパスを記録（移動後に FX のカーブを読み替えるため、動かす前に全体を控える）
            Dictionary<Transform, string> originalPaths = CollectPaths(plan.Root);

            // 別の Merge Armature の統合元は、親側の再帰では動かさず個別に処理する
            var sources = new HashSet<Transform>();
            foreach (MergeEntry entry in plan.Merges)
            {
                sources.Add(entry.Source);
            }

            // 統合先が他の衣装の中にあるもの（入れ子）から先に処理する（MA と同じ順序）
            var pending = new List<MergeEntry>(plan.Merges);
            while (pending.Count > 0)
            {
                int index = FindReadyMerge(pending);
                // 循環参照は MA でもエラーになるため、残りは処理しない
                if (index < 0)
                {
                    break;
                }

                MergeEntry entry = pending[index];
                pending.RemoveAt(index);

                // NDMF で統合済み（統合先の子になった or 不要として削除された）ならスキップ
                if (entry.Source == null || entry.Target == null || entry.Source.IsChildOf(entry.Target))
                {
                    continue;
                }

                Debug.LogWarning(
                    $"[VRCast][Exporter] Merge Armature was not applied by NDMF; merging '{entry.Source.name}' " +
                    $"into '{entry.Target.name}' as a fallback.");
                MergeRecursive(entry, entry.Source, entry.Target, sources, originalPaths, result.MovedObjects);
                result.FixedCount++;
            }

            // Bone Proxy は付け替えを先に全て行い、親から順に位置を合わせる（MA と同じ順序）
            var attached = new List<ProxyEntry>();
            foreach (ProxyEntry entry in plan.Proxies)
            {
                // NDMF で付け替え済み・削除済みならスキップ
                if (entry.Proxy == null || entry.Target == null || entry.Proxy.parent == entry.Target)
                {
                    continue;
                }

                Debug.LogWarning(
                    $"[VRCast][Exporter] Bone Proxy was not applied by NDMF; attaching '{entry.Proxy.name}' " +
                    $"to '{entry.Target.name}' as a fallback.");
                Reparent(entry.Proxy, entry.Target, originalPaths, result.MovedObjects);
                attached.Add(entry);
            }

            attached.Sort((a, b) => Depth(a.Proxy).CompareTo(Depth(b.Proxy)));
            foreach (ProxyEntry entry in attached)
            {
                AdjustProxyTransform(entry);
                result.FixedCount++;
            }

            return result;
        }

        private static Transform ResolveMergeTarget(Component mergeArmature, Transform root)
        {
            // MA 自身の解決結果を優先
            if (ReflectionUtility.GetProperty(mergeArmature, "mergeTargetObject") is GameObject resolved
                && resolved != null && resolved.transform.IsChildOf(root))
            {
                return resolved.transform;
            }

            // 解決できなければ参照パス（アバタールート基準）から探す
            object reference = ReflectionUtility.GetField(mergeArmature, "mergeTarget");
            string path = ReflectionUtility.GetField(reference, "referencePath") as string;
            return string.IsNullOrEmpty(path) ? null : root.Find(path);
        }

        private static int FindReadyMerge(List<MergeEntry> pending)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                // 自分の衣装の中を統合先にしている未処理の設定があれば、そちらを先に処理する
                if (!HasPendingNestedMerge(pending, pending[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool HasPendingNestedMerge(List<MergeEntry> pending, MergeEntry candidate)
        {
            // 統合元が消えていれば待つ必要は無い
            if (candidate.Source == null)
            {
                return false;
            }

            foreach (MergeEntry other in pending)
            {
                if (other != candidate && other.Target != null && other.Target.IsChildOf(candidate.Source))
                {
                    return true;
                }
            }

            return false;
        }

        private static void MergeRecursive(
            MergeEntry entry,
            Transform source,
            Transform newParent,
            HashSet<Transform> sources,
            Dictionary<Transform, string> originalPaths,
            Dictionary<string, Transform> moved)
        {
            // 付け替えで子の並びが変わるため先に控える
            var children = new List<Transform>();
            foreach (Transform child in source)
            {
                children.Add(child);
            }

            // 見た目を変えないようワールド姿勢を保って対応ボーンの子にする（スキニングはボーンのワールド姿勢で決まる）
            Reparent(source, newParent, originalPaths, moved);

            foreach (Transform child in children)
            {
                // 入れ子の Merge Armature は個別に処理済み or 処理予定
                if (sources.Contains(child))
                {
                    continue;
                }

                // 名前（prefix + ボーン名 + suffix）が対応するボーンだけ再帰的に統合し、それ以外は親ごと追従させる
                Transform match = FindCorrespondingBone(child, newParent, entry.Prefix, entry.Suffix);
                if (match != null)
                {
                    MergeRecursive(entry, child, match, sources, originalPaths, moved);
                }
            }
        }

        private static Transform FindCorrespondingBone(Transform bone, Transform baseParent, string prefix, string suffix)
        {
            // MA と同じく前後の文字列を除いた名前で、アバター側の直下の子を探す
            string name = bone.name;
            if (!name.StartsWith(prefix, System.StringComparison.Ordinal)
                || !name.EndsWith(suffix, System.StringComparison.Ordinal)
                || name.Length <= prefix.Length + suffix.Length)
            {
                return null;
            }

            return baseParent.Find(name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length));
        }

        private static void Reparent(
            Transform target, Transform newParent, Dictionary<Transform, string> originalPaths, Dictionary<string, Transform> moved)
        {
            // ワールド姿勢を保って付け替え、元のパスを記録
            target.SetParent(newParent, true);
            if (originalPaths.TryGetValue(target, out string path))
            {
                moved[path] = target;
            }
        }

        private static void AdjustProxyTransform(ProxyEntry entry)
        {
            Transform transform = entry.Proxy;

            // 位置: 保持モードなら元のワールド位置、それ以外はボーンの原点
            if (entry.KeepPosition)
            {
                transform.position = entry.WorldPosition;
            }
            else
            {
                transform.localPosition = Vector3.zero;
            }

            // 回転: 保持モードなら元のワールド回転、それ以外はボーンと同じ向き
            if (entry.KeepRotation)
            {
                transform.rotation = entry.WorldRotation;
            }
            else
            {
                transform.localRotation = Quaternion.identity;
            }

            // スケール合わせが有効ならボーンのスケールに従う
            if (entry.MatchScale)
            {
                transform.localScale = Vector3.one;
            }
        }

        private static Dictionary<Transform, string> CollectPaths(Transform root)
        {
            var paths = new Dictionary<Transform, string>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                paths[t] = ReflectionUtility.PathOf(t, root);
            }

            return paths;
        }

        private static int Depth(Transform transform)
        {
            // 親の数を数える（浅い＝親側から処理するため）
            int depth = 0;
            for (Transform t = transform.parent; t != null; t = t.parent)
            {
                depth++;
            }

            return depth;
        }
    }
}
