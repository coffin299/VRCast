using System.Collections.Generic;
using UnityEngine;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 作成モードで 1 つのメッシュをブラシで編集するための情報（作成モードに入ってから必要になったときに作る）。
    /// 編集できる頂点（Head 以下のボーンを使う頂点）、UV の継ぎ目で分かれた同じ位置の頂点の代表、代表どうしの隣接、
    /// 左右対称の相手、現在の形のワールド座標（BakeMesh）、頂点ごとのスキニング行列を扱う。
    /// </summary>
    public sealed class SculptMeshData
    {
        // 同じ位置とみなす距離・対称の相手とみなす距離（メッシュの境界の対角線に対する割合）
        private const float WeldToleranceRatio = 1e-6f;
        private const float MirrorToleranceRatio = 2e-4f;

        private readonly Transform[] _bones;
        private readonly Matrix4x4[] _bindposes;
        private readonly BoneWeight[] _weights;
        private readonly Matrix4x4[] _boneMatrices;
        private readonly Mesh _bake = new Mesh();
        private readonly List<Vector3> _baked = new List<Vector3>();

        private SculptMeshData(EditableFaceMesh mesh, Transform head)
        {
            Mesh = mesh;
            Mesh original = mesh.Original;
            _bones = mesh.Renderer.bones;
            _bindposes = original.bindposes;
            _weights = original.boneWeights;
            _boneMatrices = new Matrix4x4[_bones.Length];

            // 編集できる頂点と、その中の同じ位置の頂点の代表
            bool[] mask = BuildMask(head);
            float diagonal = Mathf.Max(original.bounds.size.magnitude, 1e-4f);
            Rep = VertexMatching.Weld(mesh.BaseVertices, mask, diagonal * WeldToleranceRatio);
            Members = BuildMembers(Rep);
            Triangles = BuildTriangles(mesh.Triangles, Rep);
            Neighbors = BuildNeighbors(Triangles, Rep);
            Mirror = VertexMatching.FindMirror(mesh.BaseVertices, Rep, diagonal * MirrorToleranceRatio);
            World = new Vector3[mesh.VertexCount];
        }

        public EditableFaceMesh Mesh { get; }

        /// <summary>
        /// 頂点番号 → 代表頂点の番号（編集できない頂点は -1）。
        /// </summary>
        public int[] Rep { get; }

        /// <summary>
        /// 代表頂点 → 同じ位置の頂点（自分を含む。代表でない頂点は null）。
        /// </summary>
        public int[][] Members { get; }

        /// <summary>
        /// 代表頂点 → 隣接する代表頂点（代表でない頂点は null）。
        /// </summary>
        public int[][] Neighbors { get; }

        /// <summary>
        /// 3 頂点とも編集できる三角形（頂点番号の 3 つ組。ブラシの当たり判定に使う）。
        /// </summary>
        public int[] Triangles { get; }

        /// <summary>
        /// 左右対称の相手（代表頂点どうし）。
        /// </summary>
        public MirrorResult Mirror { get; }

        /// <summary>
        /// 直近の Bake での、編集できる頂点のワールド座標。
        /// </summary>
        public Vector3[] World { get; }

        /// <summary>
        /// メッシュから作る。
        /// </summary>
        public static SculptMeshData Build(EditableFaceMesh mesh, Transform head)
        {
            return new SculptMeshData(mesh, head);
        }

        /// <summary>
        /// 表示中なら true（非表示のメッシュにはブラシを当てない）。
        /// </summary>
        public bool IsVisible => Mesh.Renderer != null && Mesh.Renderer.enabled && Mesh.Renderer.gameObject.activeInHierarchy;

        /// <summary>
        /// 現在の形（ボーン・BlendShape・編集中の表示を含む）のワールド座標と、ボーンの行列を取り直す。
        /// </summary>
        public void Bake()
        {
            // 拡大率込みでレンダラー基準の位置を求め、位置・回転だけでワールドへ
            SkinnedMeshRenderer renderer = Mesh.Renderer;
            renderer.BakeMesh(_bake, true);
            _bake.GetVertices(_baked);
            Transform transform = renderer.transform;
            Matrix4x4 toWorld = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            int count = Mathf.Min(_baked.Count, World.Length);
            for (int i = 0; i < count; i++)
            {
                // 編集できる頂点だけを変換する
                if (Rep[i] >= 0)
                {
                    World[i] = toWorld.MultiplyPoint3x4(_baked[i]);
                }
            }

            // ボーンの行列（メッシュ空間 → ワールド）
            for (int k = 0; k < _bones.Length; k++)
            {
                _boneMatrices[k] = _bones[k] != null && k < _bindposes.Length
                    ? _bones[k].localToWorldMatrix * _bindposes[k]
                    : renderer.localToWorldMatrix;
            }
        }

        /// <summary>
        /// 頂点のメッシュ空間 → ワールドの行列（ボーン行列の重み付き和。ボーンが無ければレンダラーの行列）。直近の Bake の値を使う。
        /// </summary>
        public Matrix4x4 SkinMatrix(int vertex)
        {
            // ボーンの無いメッシュはレンダラーの変換だけ
            if (_bones.Length == 0 || vertex >= _weights.Length)
            {
                return Mesh.Renderer.localToWorldMatrix;
            }

            // 4 本までのボーンの行列を重みで足す
            BoneWeight weight = _weights[vertex];
            Matrix4x4 matrix = Scale(BoneMatrix(weight.boneIndex0), weight.weight0);
            matrix = Add(matrix, Scale(BoneMatrix(weight.boneIndex1), weight.weight1));
            matrix = Add(matrix, Scale(BoneMatrix(weight.boneIndex2), weight.weight2));
            return Add(matrix, Scale(BoneMatrix(weight.boneIndex3), weight.weight3));
        }

        /// <summary>
        /// 光線と編集できる三角形の最も近い交点。当たらなければ false。
        /// </summary>
        public bool Raycast(Ray ray, out float distance, out Vector3 point)
        {
            distance = float.MaxValue;
            point = Vector3.zero;
            bool hit = false;
            for (int t = 0; t < Triangles.Length; t += 3)
            {
                // 三角形ごとに交点を求め、最も近いものを残す
                if (IntersectTriangle(ray, World[Triangles[t]], World[Triangles[t + 1]], World[Triangles[t + 2]],
                        out float candidate) && candidate < distance)
                {
                    distance = candidate;
                    hit = true;
                }
            }

            if (hit)
            {
                point = ray.GetPoint(distance);
            }

            return hit;
        }

        /// <summary>
        /// center から radius 以内の代表頂点と影響度（(1 - (d/r)²)²）を集める。
        /// </summary>
        public void CollectInRadius(Vector3 center, float radius, List<KeyValuePair<int, float>> result)
        {
            result.Clear();
            float radiusSqr = radius * radius;
            for (int i = 0; i < Rep.Length; i++)
            {
                // 代表頂点だけを距離で選ぶ
                if (Rep[i] != i)
                {
                    continue;
                }

                float distanceSqr = (World[i] - center).sqrMagnitude;
                if (distanceSqr < radiusSqr)
                {
                    float falloff = 1f - distanceSqr / radiusSqr;
                    result.Add(new KeyValuePair<int, float>(i, falloff * falloff));
                }
            }
        }

        /// <summary>
        /// 作成モードを抜けるときに一時メッシュを破棄する。
        /// </summary>
        public void Dispose()
        {
            Object.Destroy(_bake);
        }

        private bool[] BuildMask(Transform head)
        {
            // Head が無い・ボーンの無いメッシュは全頂点を編集できる
            var mask = new bool[Mesh.VertexCount];
            if (head == null || _bones.Length == 0 || _weights.Length != Mesh.VertexCount)
            {
                for (int i = 0; i < mask.Length; i++)
                {
                    mask[i] = true;
                }

                return mask;
            }

            // Head 自身か子孫のボーン
            var headBones = new bool[_bones.Length];
            for (int k = 0; k < _bones.Length; k++)
            {
                headBones[k] = _bones[k] != null && (_bones[k] == head || _bones[k].IsChildOf(head));
            }

            // そのボーンに重みがある頂点だけ（体・服の頂点は動かさない）
            for (int i = 0; i < mask.Length; i++)
            {
                BoneWeight weight = _weights[i];
                mask[i] = UsesBone(headBones, weight.boneIndex0, weight.weight0)
                    || UsesBone(headBones, weight.boneIndex1, weight.weight1)
                    || UsesBone(headBones, weight.boneIndex2, weight.weight2)
                    || UsesBone(headBones, weight.boneIndex3, weight.weight3);
            }

            return mask;
        }

        private static bool UsesBone(bool[] headBones, int bone, float weight)
        {
            return weight > 0f && bone >= 0 && bone < headBones.Length && headBones[bone];
        }

        private static int[][] BuildMembers(int[] rep)
        {
            // 代表ごとに同じ位置の頂点を集める
            var lists = new List<int>[rep.Length];
            for (int i = 0; i < rep.Length; i++)
            {
                if (rep[i] < 0)
                {
                    continue;
                }

                (lists[rep[i]] ?? (lists[rep[i]] = new List<int>())).Add(i);
            }

            var members = new int[rep.Length][];
            for (int i = 0; i < rep.Length; i++)
            {
                members[i] = lists[i]?.ToArray();
            }

            return members;
        }

        private static int[] BuildTriangles(int[] triangles, int[] rep)
        {
            // 3 頂点とも編集できる三角形だけを残す
            var result = new List<int>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t];
                int b = triangles[t + 1];
                int c = triangles[t + 2];
                if (rep[a] >= 0 && rep[b] >= 0 && rep[c] >= 0)
                {
                    result.Add(a);
                    result.Add(b);
                    result.Add(c);
                }
            }

            return result.ToArray();
        }

        private static int[][] BuildNeighbors(int[] triangles, int[] rep)
        {
            // 三角形の辺でつながる代表頂点どうしを隣接とする（継ぎ目の両側の頂点は代表で 1 つになる）
            var lists = new List<int>[rep.Length];
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = rep[triangles[t]];
                int b = rep[triangles[t + 1]];
                int c = rep[triangles[t + 2]];
                Link(lists, a, b);
                Link(lists, b, c);
                Link(lists, c, a);
            }

            var neighbors = new int[rep.Length][];
            for (int i = 0; i < rep.Length; i++)
            {
                neighbors[i] = lists[i]?.ToArray();
            }

            return neighbors;
        }

        private static void Link(List<int>[] lists, int a, int b)
        {
            // 同じ頂点・登録済みの組は足さない
            if (a == b)
            {
                return;
            }

            List<int> listA = lists[a] ?? (lists[a] = new List<int>());
            List<int> listB = lists[b] ?? (lists[b] = new List<int>());
            if (!listA.Contains(b))
            {
                listA.Add(b);
                listB.Add(a);
            }
        }

        private Matrix4x4 BoneMatrix(int bone)
        {
            // 範囲外の番号はレンダラーの行列
            return bone >= 0 && bone < _boneMatrices.Length ? _boneMatrices[bone] : Mesh.Renderer.localToWorldMatrix;
        }

        private static Matrix4x4 Scale(Matrix4x4 matrix, float weight)
        {
            // 全要素に重みを掛ける
            for (int i = 0; i < 16; i++)
            {
                matrix[i] *= weight;
            }

            return matrix;
        }

        private static Matrix4x4 Add(Matrix4x4 a, Matrix4x4 b)
        {
            // 要素ごとの和
            for (int i = 0; i < 16; i++)
            {
                a[i] += b[i];
            }

            return a;
        }

        private static bool IntersectTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float distance)
        {
            // Möller–Trumbore 法（両面とも当てる）
            distance = 0f;
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 p = Vector3.Cross(ray.direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < 1e-12f)
            {
                return false;
            }

            float inverse = 1f / determinant;
            Vector3 s = ray.origin - v0;
            float u = Vector3.Dot(s, p) * inverse;
            if (u < 0f || u > 1f)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(ray.direction, q) * inverse;
            if (v < 0f || u + v > 1f)
            {
                return false;
            }

            distance = Vector3.Dot(edge2, q) * inverse;
            return distance > 0f;
        }
    }
}
