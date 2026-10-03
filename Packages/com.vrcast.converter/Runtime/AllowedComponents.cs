using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// アバター bundle に含めてよい Unity 標準コンポーネントの許可リスト。
    /// Exporter は許可外を除去し、Runtime は読込後に許可外を再除去する。
    /// </summary>
    public static class AllowedComponents
    {
        // 許可するコンポーネント型（完全一致で判定する）
        private static readonly HashSet<Type> Types = new HashSet<Type>
        {
            typeof(Transform),
            typeof(Animator),
            typeof(SkinnedMeshRenderer),
            typeof(MeshRenderer),
            typeof(MeshFilter),
        };

        public static bool IsAllowed(Component component)
        {
            // Missing Script は null として返るため許可しない
            if (component == null)
            {
                return false;
            }

            // 派生型の混入を防ぐため型の完全一致で判定
            return Types.Contains(component.GetType());
        }
    }
}
