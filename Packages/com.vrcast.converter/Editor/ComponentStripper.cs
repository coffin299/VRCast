using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// エクスポート用の複製アバターから、許可リスト外のコンポーネントと EditorOnly オブジェクトを除去する。
    /// </summary>
    public static class ComponentStripper
    {
        // EditorOnly タグ（ビルドに含めない慣例）
        private const string EditorOnlyTag = "EditorOnly";

        // RequireComponent の依存順で消せない場合に備えた最大試行回数
        private const int MaxPasses = 8;

        public struct Result
        {
            public int RemovedComponents;
            public int RemovedMissingScripts;
            public int RemovedEditorOnlyObjects;
        }

        public static Result Strip(GameObject root)
        {
            var result = new Result();

            // EditorOnly オブジェクトは子ごと削除
            result.RemovedEditorOnlyObjects = RemoveEditorOnlyObjects(root);

            // Missing Script を全階層から除去
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                result.RemovedMissingScripts += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }

            // 依存関係で一度に消せないものがあるため、変化が無くなるまで繰り返す
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                int removed = RemoveDisallowedOnce(root);
                result.RemovedComponents += removed;
                if (removed == 0)
                {
                    break;
                }
            }

            // VRChat の FX 等は VRC 固有の StateMachineBehaviour を含むため Controller は外す
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            {
                animator.runtimeAnimatorController = null;
            }

            return result;
        }

        /// <summary>
        /// 非アクティブ（エディタでチェックを外した）オブジェクトを子ごと削除し、削除した数を返す（ルートは残す）。
        /// FX の初期状態で表示されるものも含め、書き出しには一切含めない。
        /// </summary>
        public static int RemoveInactiveObjects(GameObject root)
        {
            return RemoveObjects(root, go => !go.activeSelf);
        }

        private static int RemoveEditorOnlyObjects(GameObject root)
        {
            return RemoveObjects(root, go => go.CompareTag(EditorOnlyTag));
        }

        private static int RemoveObjects(GameObject root, System.Predicate<GameObject> match)
        {
            // 列挙中に破棄しないよう先に対象を集める
            var targets = new List<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                // ルート自体は消さない
                if (t.gameObject != root && match(t.gameObject))
                {
                    targets.Add(t.gameObject);
                }
            }

            // 親が先に消えた子は null になるのでスキップ
            int count = 0;
            foreach (GameObject go in targets)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                    count++;
                }
            }

            return count;
        }

        private static int RemoveDisallowedOnce(GameObject root)
        {
            int removed = 0;
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                // null（Missing Script）と許可済みはスキップ
                if (component == null || AllowedComponents.IsAllowed(component))
                {
                    continue;
                }

                // 他コンポーネントから RequireComponent されている場合は後のパスで消す
                if (!CanRemove(component))
                {
                    continue;
                }

                Object.DestroyImmediate(component);
                removed++;
            }

            return removed;
        }

        private static bool CanRemove(Component target)
        {
            // 同じ GameObject 上の他コンポーネントが target の型を要求していないか確認
            foreach (Component other in target.GetComponents<Component>())
            {
                if (other == null || other == target)
                {
                    continue;
                }

                // RequireComponent 属性を走査
                foreach (object attr in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                {
                    var require = (RequireComponent)attr;
                    if (IsRequired(require, target))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsRequired(RequireComponent require, Component target)
        {
            // 3 つの型指定のいずれかに target が該当すれば必須
            System.Type type = target.GetType();
            return (require.m_Type0 != null && require.m_Type0.IsAssignableFrom(type))
                || (require.m_Type1 != null && require.m_Type1.IsAssignableFrom(type))
                || (require.m_Type2 != null && require.m_Type2.IsAssignableFrom(type));
        }
    }
}
