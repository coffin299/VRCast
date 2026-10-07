using System.Collections.Generic;
using UnityEngine;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 左右対称の対応の結果（対称面の軸・位置と、代表頂点ごとの相手の代表頂点）。
    /// </summary>
    public sealed class MirrorResult
    {
        public MirrorResult(int axis, float plane, int[] partners, int matched)
        {
            Axis = axis;
            Plane = plane;
            Partners = partners;
            Matched = matched;
        }

        /// <summary>
        /// 対称面に垂直な軸（0 = X、1 = Y、2 = Z、メッシュ空間）。
        /// </summary>
        public int Axis { get; }

        /// <summary>
        /// 対称面の位置（Axis 方向の座標）。
        /// </summary>
        public float Plane { get; }

        /// <summary>
        /// 頂点番号 → 対称の位置にある代表頂点の番号（見つからない・代表でない頂点は -1）。
        /// </summary>
        public int[] Partners { get; }

        /// <summary>
        /// 相手が見つかった代表頂点の数。
        /// </summary>
        public int Matched { get; }

        /// <summary>
        /// メッシュ空間の向き（差分）を対称面で反転する。
        /// </summary>
        public Vector3 MirrorVector(Vector3 vector)
        {
            vector[Axis] = -vector[Axis];
            return vector;
        }
    }

    /// <summary>
    /// 頂点の位置どうしの照合（UV の継ぎ目で分かれた同じ位置の頂点の統合、左右対称の相手探し）。
    /// </summary>
    public static class VertexMatching
    {
        // 対称面の候補を選ぶときに調べる頂点の数（全頂点を 6 候補ぶん調べると重いため間引く）
        private const int MirrorSampleCount = 4000;

        /// <summary>
        /// 同じ位置（tolerance 以内）の頂点を 1 つの代表頂点にまとめる。
        /// 戻り値は頂点番号 → 代表頂点の番号（mask が false の頂点は -1）。代表は同じ位置の中で最も小さい番号。
        /// </summary>
        public static int[] Weld(IReadOnlyList<Vector3> positions, bool[] mask, float tolerance)
        {
            var reps = new int[positions.Count];
            var hash = new SpatialHash(tolerance);
            for (int i = 0; i < positions.Count; i++)
            {
                // 対象外の頂点は -1
                if (mask != null && !mask[i])
                {
                    reps[i] = -1;
                    continue;
                }

                // 既に同じ位置の代表があればそれに、無ければ自分が代表になる
                int found = hash.FindNearest(positions, positions[i], tolerance);
                if (found >= 0)
                {
                    reps[i] = found;
                    continue;
                }

                reps[i] = i;
                hash.Add(positions[i], i);
            }

            return reps;
        }

        /// <summary>
        /// 代表頂点の左右対称の相手を探す。メッシュ空間の X / Y / Z 軸について、原点と境界の中央を対称面の候補にし、
        /// 相手が最も多く見つかる面を使う。reps は Weld の結果。
        /// </summary>
        public static MirrorResult FindMirror(IReadOnlyList<Vector3> positions, int[] reps, float tolerance)
        {
            // 代表頂点の一覧と境界
            var representatives = new List<int>();
            var bounds = new Bounds();
            bool first = true;
            for (int i = 0; i < reps.Length; i++)
            {
                if (reps[i] != i)
                {
                    continue;
                }

                representatives.Add(i);
                if (first)
                {
                    bounds = new Bounds(positions[i], Vector3.zero);
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(positions[i]);
                }
            }

            // 代表を空間ハッシュへ
            var hash = new SpatialHash(tolerance);
            foreach (int index in representatives)
            {
                hash.Add(positions[index], index);
            }

            // 候補（軸 × 原点 / 中央）ごとに、間引いた頂点で相手が見つかる数を数えて最も多いものを選ぶ
            int step = Mathf.Max(1, representatives.Count / MirrorSampleCount);
            int bestAxis = 0;
            float bestPlane = 0f;
            int bestCount = -1;
            for (int axis = 0; axis < 3; axis++)
            {
                foreach (float plane in new[] { 0f, bounds.center[axis] })
                {
                    int count = 0;
                    for (int k = 0; k < representatives.Count; k += step)
                    {
                        Vector3 mirrored = Mirror(positions[representatives[k]], axis, plane);
                        if (hash.FindNearest(positions, mirrored, tolerance) >= 0)
                        {
                            count++;
                        }
                    }

                    if (count > bestCount)
                    {
                        bestCount = count;
                        bestAxis = axis;
                        bestPlane = plane;
                    }
                }
            }

            // 選んだ面で全代表頂点の相手を求める
            var partners = new int[reps.Length];
            for (int i = 0; i < partners.Length; i++)
            {
                partners[i] = -1;
            }

            int matched = 0;
            foreach (int index in representatives)
            {
                int partner = hash.FindNearest(positions, Mirror(positions[index], bestAxis, bestPlane), tolerance);
                partners[index] = partner;
                matched += partner >= 0 ? 1 : 0;
            }

            return new MirrorResult(bestAxis, bestPlane, partners, matched);
        }

        private static Vector3 Mirror(Vector3 position, int axis, float plane)
        {
            // 対称面で位置を反転
            position[axis] = plane * 2f - position[axis];
            return position;
        }

        /// <summary>
        /// 一定の大きさの格子で頂点を探す空間ハッシュ（格子の大きさ = 探す距離、近傍 27 マスを調べる）。
        /// </summary>
        private sealed class SpatialHash
        {
            private readonly float _cell;
            private readonly Dictionary<(int, int, int), List<int>> _cells = new Dictionary<(int, int, int), List<int>>();

            public SpatialHash(float cell)
            {
                // 0 の格子は作れないため下限を設ける
                _cell = Mathf.Max(cell, 1e-7f);
            }

            public void Add(Vector3 position, int index)
            {
                // 位置のマスへ番号を追加
                (int, int, int) key = KeyOf(position);
                if (!_cells.TryGetValue(key, out List<int> list))
                {
                    list = new List<int>();
                    _cells[key] = list;
                }

                list.Add(index);
            }

            public int FindNearest(IReadOnlyList<Vector3> positions, Vector3 position, float tolerance)
            {
                // 近傍 27 マスの中で、距離が tolerance 以内の最も近い頂点
                (int x, int y, int z) = KeyOf(position);
                float bestDistance = tolerance * tolerance;
                int best = -1;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!_cells.TryGetValue((x + dx, y + dy, z + dz), out List<int> list))
                            {
                                continue;
                            }

                            foreach (int index in list)
                            {
                                float distance = (positions[index] - position).sqrMagnitude;
                                if (distance <= bestDistance)
                                {
                                    bestDistance = distance;
                                    best = index;
                                }
                            }
                        }
                    }
                }

                return best;
            }

            private (int, int, int) KeyOf(Vector3 position)
            {
                return (Mathf.FloorToInt(position.x / _cell), Mathf.FloorToInt(position.y / _cell),
                    Mathf.FloorToInt(position.z / _cell));
            }
        }
    }
}
