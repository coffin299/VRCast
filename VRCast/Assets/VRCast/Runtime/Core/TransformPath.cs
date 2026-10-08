using System.Collections.Generic;
using UnityEngine;

namespace VRCast.Core
{
    /// <summary>
    /// アバタールートからの相対パス（metadata や設定でオブジェクトを指す形式）。
    /// </summary>
    public static class TransformPath
    {
        /// <summary>
        /// root からの相対パス（"Body" や "Armature/Hips/Hair"。ルート自身は空）。
        /// </summary>
        public static string Of(Transform target, Transform root)
        {
            // 親をたどって名前を集める
            var names = new List<string>();
            for (Transform node = target; node != null && node != root; node = node.parent)
            {
                names.Add(node.name);
            }

            // ルート側から順に並べて連結
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
