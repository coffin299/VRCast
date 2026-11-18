using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.PerfectSync;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// 薄板スプラインと、手本の顔から写す形状の生成を確認する。
    /// </summary>
    public class PerfectSyncTransferTests
    {
        [Test]
        public void Spline_ReproducesShiftAndScale()
        {
            // 平面に乗らない格子を、2 倍して横へずらす
            var source = new List<Vector3>();
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    source.Add(new Vector3(x, y, 0f));
                    source.Add(new Vector3(x, y, 0.5f));
                }
            }

            var target = new Vector3[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                target[i] = source[i] * 2f + new Vector3(1f, 0f, 0f);
            }

            ThinPlateSpline spline = ThinPlateSpline.Fit(source.ToArray(), target);
            Assert.That(spline, Is.Not.Null);

            // 制御点の間の点も同じ拡大と移動になり、ヤコビアンは 2 倍の向きを保つこと
            var probe = new Vector3(0.25f, -0.4f, 0.2f);
            Vector3 mapped = spline.Evaluate(probe);
            Assert.That(Vector3.Distance(mapped, probe * 2f + new Vector3(1f, 0f, 0f)), Is.LessThan(0.05f));
            spline.Jacobian(probe, out Vector3 axisX, out Vector3 axisY, out Vector3 axisZ);
            Assert.That(axisX.x, Is.EqualTo(2f).Within(0.08f));
            Assert.That(axisY.y, Is.EqualTo(2f).Within(0.08f));
            Assert.That(axisZ.z, Is.EqualTo(2f).Within(0.08f));
            Assert.That(axisX.y, Is.EqualTo(0f).Within(0.08f));
            Assert.That(axisY.x, Is.EqualTo(0f).Within(0.08f));
        }

        [Test]
        public void Spline_Fit_RejectsTooFewPoints()
        {
            // 多項式の分だけ点が無いときは作れないこと
            var points = new[] { Vector3.zero, Vector3.right, Vector3.up };
            Assert.That(ThinPlateSpline.Fit(points, points), Is.Null);
            Assert.That(ThinPlateSpline.Fit(points, new[] { Vector3.zero }), Is.Null);
        }

        [Test]
        public void Transfer_MovesTheJawAndTheNearEyeOnly()
        {
            FaceMeshGeometry geometry = PerfectSyncTestFace.CreateGeometry();
            List<PerfectSyncShape> shapes = Generate(geometry, 1f, withTriangles: false);

            // 口を開くとあご先は下がり、額は動かないこと
            PerfectSyncShape jawOpen = Find(shapes, "jawOpen");
            Assert.That(jawOpen, Is.Not.Null);
            int chin = PerfectSyncTestFace.VertexAt(new Vector3(0f, -0.85f, 0f));
            int forehead = PerfectSyncTestFace.VertexAt(new Vector3(0f, 0.85f, 0f));
            Assert.That(Delta(jawOpen, chin).y, Is.LessThan(0f));
            Assert.That(Delta(jawOpen, forehead), Is.EqualTo(Vector3.zero));

            // まばたき（左）は左の上まぶたを下げ、右の上まぶたより大きく動くこと
            PerfectSyncShape blinkLeft = Find(shapes, "eyeBlinkLeft");
            Assert.That(blinkLeft, Is.Not.Null);
            int leftLid = PerfectSyncTestFace.VertexAt(new Vector3(-0.35f, 0.35f, 0f));
            int rightLid = PerfectSyncTestFace.VertexAt(new Vector3(0.35f, 0.35f, 0f));
            Assert.That(Delta(blinkLeft, leftLid).y, Is.LessThan(0f));
            Assert.That(Delta(blinkLeft, leftLid).magnitude, Is.GreaterThan(Delta(blinkLeft, rightLid).magnitude));

            // 唇の巻き込みは、これまでの式とは別の移動量になること
            PerfectSyncShape roll = Find(shapes, "mouthRollUpper");
            PerfectSyncShape formulaRoll = Find(GenerateFormula(geometry), "mouthRollUpper");
            int lip = PerfectSyncTestFace.VertexAt(new Vector3(0f, -0.4f, 0f));
            Assert.That(roll, Is.Not.Null);
            Assert.That(Vector3.Distance(Delta(roll, lip), Delta(formulaRoll, lip)), Is.GreaterThan(1e-4f));
        }

        [Test]
        public void Transfer_HonorsStrengthMouthPartsAndSeams()
        {
            FaceMeshGeometry geometry = PerfectSyncTestFace.CreateGeometry();

            // 強さ 0 なら何も作らず、強さ 2 ならあごが 2 倍動くこと
            Assert.That(Generate(geometry, 0f, withTriangles: false), Is.Empty);
            int chin = PerfectSyncTestFace.VertexAt(new Vector3(0f, -0.85f, 0f));
            float once = Delta(Find(Generate(geometry, 1f, withTriangles: false), "jawOpen"), chin).y;
            float twice = Delta(Find(Generate(geometry, 2f, withTriangles: false), "jawOpen"), chin).y;
            Assert.That(twice, Is.EqualTo(once * 2f).Within(1e-4f));

            // 歯・舌のメッシュには顎の形状だけを作ること
            Dictionary<LandmarkId, Vector3> points = PerfectSyncTestFace.MinimalPoints();
            FaceRig rig = FaceRig.Build(points, PerfectSyncTestFace.Normals(points.Keys), out _);
            var teeth = new PerfectSyncGenerator.MeshInput
            {
                Path = "Teeth",
                Vertices = new[] { new Vector3(0f, -0.5f, -0.05f), new Vector3(0f, -0.6f, -0.05f) },
                IsMouthPart = true,
            };
            List<PerfectSyncShape> mouth = PerfectSyncGenerator.Generate(rig, new[] { teeth }, Strengths(1f),
                PerfectSyncMethod.Transfer);
            Assert.That(mouth, Is.Not.Empty);
            foreach (PerfectSyncShape shape in mouth)
            {
                Assert.That(shape.Name, Does.StartWith("jaw"));
                Assert.That(shape.Path, Is.EqualTo("Teeth"));
            }

            // 肌の外周は顎を開いても動かないこと
            PerfectSyncShape jaw = Find(Generate(geometry, 1f, withTriangles: true), "jawOpen");
            int edge = PerfectSyncTestFace.VertexAt(new Vector3(0f, PerfectSyncTestFace.MinY, 0f));
            Assert.That(Delta(jaw, edge), Is.EqualTo(Vector3.zero));
            Assert.That(Delta(jaw, chin).y, Is.LessThan(0f));
        }

        private static List<PerfectSyncShape> Generate(FaceMeshGeometry geometry, float strength, bool withTriangles)
        {
            Dictionary<LandmarkId, Vector3> points = PerfectSyncTestFace.MinimalPoints();
            FaceRig rig = FaceRig.Build(points, PerfectSyncTestFace.Normals(points.Keys), out _);
            var face = new PerfectSyncGenerator.MeshInput
            {
                Path = "Body",
                Vertices = geometry.Vertices,
                Normals = geometry.Normals,
                Triangles = withTriangles ? geometry.Triangles : null,
            };
            return PerfectSyncGenerator.Generate(rig, new[] { face }, Strengths(strength), PerfectSyncMethod.Transfer);
        }

        private static List<PerfectSyncShape> GenerateFormula(FaceMeshGeometry geometry)
        {
            Dictionary<LandmarkId, Vector3> points = PerfectSyncTestFace.MinimalPoints();
            FaceRig rig = FaceRig.Build(points, PerfectSyncTestFace.Normals(points.Keys), out _);
            var face = new PerfectSyncGenerator.MeshInput
            {
                Path = "Body",
                Vertices = geometry.Vertices,
                Normals = geometry.Normals,
            };
            return PerfectSyncGenerator.Generate(rig, new[] { face }, Strengths(1f));
        }

        private static float[] Strengths(float strength)
        {
            var strengths = new float[ArKitFace.BlendShapeNames.Length];
            for (int i = 0; i < strengths.Length; i++)
            {
                strengths[i] = strength;
            }

            return strengths;
        }

        private static PerfectSyncShape Find(List<PerfectSyncShape> shapes, string name)
        {
            return shapes.Find(shape => shape.Name == name);
        }

        private static Vector3 Delta(PerfectSyncShape shape, int vertex)
        {
            int index = Array.IndexOf(shape.Vertices, vertex);
            return index < 0
                ? Vector3.zero
                : new Vector3(shape.Deltas[index * 3], shape.Deltas[index * 3 + 1], shape.Deltas[index * 3 + 2]);
        }
    }
}
