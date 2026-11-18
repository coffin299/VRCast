using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Tracking;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 手本の顔の移動量を、打った目印へ合わせた薄板スプラインでアバターの頂点へ写す。
    /// </summary>
    public static class PerfectSyncTransfer
    {
        // 顔の大きさに対する比: 写すときに混ぜる近さ / これより小さい移動は形状に入れない / 1 点の移動の上限
        private const float RadiusRatio = 0.36f;
        private const float MinDeltaRatio = 1e-4f;
        private const float MaxDeltaRatio = 0.8f;

        private static readonly HashSet<string> JawShapes = new HashSet<string>
        {
            "jawOpen", "jawForward", "jawLeft", "jawRight",
        };

        /// <summary>
        /// strengths（ARKit の並び。0 以下は作らない）の強さで、転写した形状を作る。
        /// </summary>
        public static List<PerfectSyncShape> Generate(FaceRig rig, IReadOnlyList<PerfectSyncGenerator.MeshInput> meshes,
            float[] strengths)
        {
            if (strengths == null || strengths.Length != ArKitFace.BlendShapeNames.Length)
            {
                throw new System.ArgumentException("strengths must match the ARKit blend shape count.", nameof(strengths));
            }

            ExpressionTemplate template = ExpressionTemplate.Shared;
            ThinPlateSpline spline = Fit(rig, template);
            if (spline == null)
            {
                return new List<PerfectSyncShape>();
            }

            // 手本の点をアバターの顔へ写し、移動量はそこでの伸び縮みで向きを変える
            Vector3[] samples = template.Samples;
            var warped = new Vector3[samples.Length];
            var jacobianX = new Vector3[samples.Length];
            var jacobianY = new Vector3[samples.Length];
            var jacobianZ = new Vector3[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                warped[i] = spline.Evaluate(samples[i]);
                spline.Jacobian(samples[i], out jacobianX[i], out jacobianY[i], out jacobianZ[i]);
            }

            // 作る形状だけ、写したあとの移動量を点ごとに持っておく
            var active = new List<int>();
            for (int shape = 0; shape < strengths.Length; shape++)
            {
                if (strengths[shape] > 0f)
                {
                    active.Add(shape);
                }
            }

            var fields = new Vector3[active.Count][];
            for (int a = 0; a < active.Count; a++)
            {
                int shape = active[a];
                float strength = strengths[shape];
                var field = new Vector3[samples.Length];
                for (int i = 0; i < samples.Length; i++)
                {
                    Vector3 delta = template.Delta(shape, i);
                    if (delta.sqrMagnitude <= 0f)
                    {
                        continue;
                    }

                    // ヤコビアンでアバターの顔の向き・大きさにし、強さを掛ける
                    field[i] = (jacobianX[i] * delta.x + jacobianY[i] * delta.y + jacobianZ[i] * delta.z) * strength;
                }

                fields[a] = field;
            }

            float radius = rig.Scale * RadiusRatio;
            float minDelta = rig.Scale * MinDeltaRatio;
            float maxDelta = rig.Scale * MaxDeltaRatio;
            var shapes = new List<PerfectSyncShape>();
            foreach (PerfectSyncGenerator.MeshInput mesh in meshes)
            {
                // 頂点を顔の局所座標にしてから、近い手本の点の重みを一度だけ求める
                Vector3[] local = new Vector3[mesh.Vertices.Length];
                for (int v = 0; v < local.Length; v++)
                {
                    local[v] = rig.ToLocal(mesh.Vertices[v]);
                }

                float[] pins = mesh.IsMouthPart ? null : PerfectSyncGenerator.SeamPins(rig, local, mesh.Triangles);
                List<int>[] neighbors = Neighbors(local, warped, radius);

                for (int a = 0; a < active.Count; a++)
                {
                    string name = ArKitFace.BlendShapeNames[active[a]];
                    if (mesh.IsMouthPart && !JawShapes.Contains(name))
                    {
                        // 歯・舌のメッシュは顎の形状だけ
                        continue;
                    }

                    PerfectSyncShape shape = Build(rig, mesh, name, local, pins, neighbors, fields[a], warped, radius,
                        minDelta, maxDelta);
                    if (shape != null)
                    {
                        shapes.Add(shape);
                    }
                }
            }

            return shapes;
        }

        private static ThinPlateSpline Fit(FaceRig rig, ExpressionTemplate template)
        {
            // 手本の目印を、アバターに打った（足りない分は推定した）目印へ写す
            LandmarkId[] landmarks = template.Landmarks;
            var source = template.LandmarkPositions;
            var target = new Vector3[landmarks.Length];
            for (int i = 0; i < landmarks.Length; i++)
            {
                target[i] = rig[landmarks[i]];
            }

            return ThinPlateSpline.Fit(source, target);
        }

        private static List<int>[] Neighbors(Vector3[] local, Vector3[] warped, float radius)
        {
            // 半径の中にある手本の点だけを、頂点ごとに覚えておく
            float radiusSquared = radius * radius;
            var neighbors = new List<int>[local.Length];
            for (int v = 0; v < local.Length; v++)
            {
                var hits = new List<int>();
                Vector3 point = local[v];
                for (int i = 0; i < warped.Length; i++)
                {
                    if ((point - warped[i]).sqrMagnitude <= radiusSquared)
                    {
                        hits.Add(i);
                    }
                }

                neighbors[v] = hits;
            }

            return neighbors;
        }

        private static PerfectSyncShape Build(FaceRig rig, PerfectSyncGenerator.MeshInput mesh, string name,
            Vector3[] local, float[] pins, List<int>[] neighbors, Vector3[] field, Vector3[] warped, float radius,
            float minDelta, float maxDelta)
        {
            float maxDeltaSquared = maxDelta * maxDelta;
            var vertices = new List<int>();
            var deltas = new List<float>();
            for (int v = 0; v < local.Length; v++)
            {
                float pin = pins != null ? pins[v] : 1f;
                if (pin <= 0f || neighbors[v].Count == 0)
                {
                    continue;
                }

                // 近い点ほど重い重みで、写した移動量を混ぜる
                Vector3 point = local[v];
                Vector3 sum = Vector3.zero;
                float weightSum = 0f;
                foreach (int index in neighbors[v])
                {
                    float distance = Vector3.Distance(point, warped[index]);
                    float weight = 1f - distance / radius;
                    weight *= weight;
                    sum += field[index] * weight;
                    weightSum += weight;
                }

                if (weightSum <= 0f)
                {
                    continue;
                }

                Vector3 delta = sum / weightSum * pin;
                if (delta.sqrMagnitude > maxDeltaSquared)
                {
                    // 写しの伸びで飛び抜けた移動は、顔の大きさの範囲へ戻す
                    delta = delta.normalized * maxDelta;
                }

                if (delta.magnitude < minDelta)
                {
                    continue;
                }

                // 局所座標の移動量を、このメッシュの座標系へ戻す
                Vector3 meshDelta = mesh.FaceToMesh.MultiplyVector(rig.DirectionToMesh(delta));
                vertices.Add(v);
                deltas.Add(meshDelta.x);
                deltas.Add(meshDelta.y);
                deltas.Add(meshDelta.z);
            }

            return vertices.Count > 0
                ? new PerfectSyncShape(mesh.Path, name, vertices.ToArray(), deltas.ToArray())
                : null;
        }
    }
}
