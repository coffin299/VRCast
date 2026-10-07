using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Avatars
{
    /// <summary>
    /// .vrcaster から読み込んで検証・展開済みの、VRCast で作ったパーフェクトシンクの形状（無ければ空）。
    /// </summary>
    public sealed class PerfectSyncData
    {
        /// <summary>
        /// 空のデータ（パッケージに形状が無い・不正なとき）。
        /// </summary>
        public static readonly PerfectSyncData Empty = new PerfectSyncData(
            Array.Empty<PerfectSyncMeshInfo>(), Array.Empty<PerfectSyncShapeData>());

        public PerfectSyncData(IReadOnlyList<PerfectSyncMeshInfo> meshes, IReadOnlyList<PerfectSyncShapeData> shapes)
        {
            Meshes = meshes;
            Shapes = shapes;
        }

        /// <summary>
        /// 差分を当てるメッシュの照合情報（保存時のパス・頂点数・頂点ハッシュ）。
        /// </summary>
        public IReadOnlyList<PerfectSyncMeshInfo> Meshes { get; }

        /// <summary>
        /// 形状（名前と、メッシュごとの差分）。
        /// </summary>
        public IReadOnlyList<PerfectSyncShapeData> Shapes { get; }

        /// <summary>
        /// 形状が 1 つも無ければ true。
        /// </summary>
        public bool IsEmpty => Shapes.Count == 0;
    }

    /// <summary>
    /// 1 つの形状（ARKit 名）の、メッシュごとの差分。
    /// </summary>
    public sealed class PerfectSyncShapeData
    {
        public PerfectSyncShapeData(string name, IReadOnlyList<PerfectSyncMeshDelta> meshes)
        {
            Name = name;
            Meshes = meshes;
        }

        public string Name { get; }

        public IReadOnlyList<PerfectSyncMeshDelta> Meshes { get; }
    }

    /// <summary>
    /// 1 つのメッシュに対する差分（Meshes の位置、動かす頂点の番号と位置の差分）。
    /// </summary>
    public sealed class PerfectSyncMeshDelta
    {
        public PerfectSyncMeshDelta(int mesh, int[] indices, Vector3[] deltas)
        {
            Mesh = mesh;
            Indices = indices;
            Deltas = deltas;
        }

        public int Mesh { get; }

        public int[] Indices { get; }

        public Vector3[] Deltas { get; }
    }
}
