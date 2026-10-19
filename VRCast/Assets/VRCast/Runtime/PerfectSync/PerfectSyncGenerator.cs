using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Tracking;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 打った目印（FaceRig）から、ARKit 互換の形状（頂点の移動量）を作る。
    /// 顔の各部位（目・眉・口・顎・頬・鼻）の領域をなめらかな重みで決め、部位ごとの向きに動かす。
    /// 歯・舌などの別メッシュは顎の形状（jawOpen 等）だけ一緒に動かす。
    /// </summary>
    public static class PerfectSyncGenerator
    {
        // 小さすぎる移動（顔の大きさに対する比）は形状に入れない
        private const float MinDeltaRatio = 1e-4f;

        // 顔の大きさに対する比: 同じ位置とみなす距離 / 肌の外周から動かし始めるまでの距離
        private const float WeldRatio = 1e-3f;
        private const float PinRadiusRatio = 0.3f;

        // 顎と一緒に動かす形状（歯・舌のメッシュにも作る）
        private static readonly HashSet<string> JawShapes = new HashSet<string>
        {
            "jawOpen", "jawForward", "jawLeft", "jawRight",
        };

        /// <summary>
        /// 形状を作るメッシュ 1 つ。頂点は顔メッシュの座標系に直したもの。
        /// FaceToMesh は顔メッシュの座標系の向きを、このメッシュの座標系へ戻す行列（顔メッシュ自身は単位行列）。
        /// </summary>
        public sealed class MeshInput
        {
            public string Path = string.Empty;
            public Vector3[] Vertices = Array.Empty<Vector3>();
            public Vector3[] Normals;
            public Matrix4x4 FaceToMesh = Matrix4x4.identity;
            public bool IsMouthPart;

            // 三角形（肌の外周 = 別のメッシュとの継ぎ目を動かさないために使う。null なら外周も動かす）
            public int[] Triangles;
        }

        /// <summary>
        /// strengths（ARKit の並びと同じ数。0 以下は作らない）の強さで形状を作る。
        /// </summary>
        public static List<PerfectSyncShape> Generate(FaceRig rig, IReadOnlyList<MeshInput> meshes, float[] strengths)
        {
            if (strengths == null || strengths.Length != ArKitFace.BlendShapeNames.Length)
            {
                throw new ArgumentException("strengths must match the ARKit blend shape count.", nameof(strengths));
            }

            // 部位の領域は目印から一度だけ作る
            var regions = new Regions(rig);
            var shapes = new List<PerfectSyncShape>();
            foreach (MeshInput mesh in meshes)
            {
                // 顔の局所座標にした頂点と法線
                Vector3[] local = new Vector3[mesh.Vertices.Length];
                Vector3[] normals = mesh.Normals != null && mesh.Normals.Length == local.Length
                    ? new Vector3[local.Length]
                    : null;
                for (int v = 0; v < local.Length; v++)
                {
                    local[v] = rig.ToLocal(mesh.Vertices[v]);
                    if (normals != null)
                    {
                        normals[v] = rig.DirectionToLocal(mesh.Normals[v]);
                    }
                }

                // 肌の外周の近くは動かさない（歯・舌のメッシュは顎と一緒にそのまま動かす）
                float[] pins = mesh.IsMouthPart ? null : regions.Pins(local, mesh.Triangles);
                for (int i = 0; i < strengths.Length; i++)
                {
                    // 作らない形状と、歯・舌のメッシュの顎以外の形状は飛ばす
                    string name = ArKitFace.BlendShapeNames[i];
                    if (strengths[i] <= 0f || (mesh.IsMouthPart && !JawShapes.Contains(name)))
                    {
                        continue;
                    }

                    PerfectSyncShape shape = Build(rig, regions, mesh, name, local, normals, pins, strengths[i]);
                    if (shape != null)
                    {
                        shapes.Add(shape);
                    }
                }
            }

            return shapes;
        }

        private static PerfectSyncShape Build(FaceRig rig, Regions regions, MeshInput mesh, string name,
            Vector3[] local, Vector3[] normals, float[] pins, float strength)
        {
            // 動く頂点だけを集める（pins は外周の近くほど 0 に近い倍率。null なら 1）
            float minDelta = rig.Scale * MinDeltaRatio;
            var vertices = new List<int>();
            var deltas = new List<float>();
            for (int v = 0; v < local.Length; v++)
            {
                Vector3 normal = normals != null ? normals[v] : Vector3.zero;
                float pin = pins != null ? pins[v] : 1f;
                Vector3 delta = pin > 0f ? regions.Delta(name, local[v], normal) * (strength * pin) : Vector3.zero;
                if (delta.magnitude < minDelta)
                {
                    continue;
                }

                // 顔の局所座標 → 顔メッシュの座標系 → このメッシュの座標系
                Vector3 meshDelta = mesh.FaceToMesh.MultiplyVector(rig.DirectionToMesh(delta));
                vertices.Add(v);
                deltas.Add(meshDelta.x);
                deltas.Add(meshDelta.y);
                deltas.Add(meshDelta.z);
            }

            // 1 頂点も動かない形状は作らない
            return vertices.Count > 0 ? new PerfectSyncShape(mesh.Path, name, vertices.ToArray(), deltas.ToArray()) : null;
        }

        /// <summary>
        /// 部位の領域と、形状ごとの移動量の計算。
        /// </summary>
        private sealed class Regions
        {
            private readonly float _s;
            private readonly EyeRegion _eyeLeft;
            private readonly EyeRegion _eyeRight;
            private readonly BrowRegion _browLeft;
            private readonly BrowRegion _browRight;
            private readonly MouthRegion _mouth;
            private readonly Vector3 _cheekLeft;
            private readonly Vector3 _cheekRight;
            private readonly Vector3 _noseWingLeft;
            private readonly Vector3 _noseWingRight;

            public Regions(FaceRig rig)
            {
                _s = rig.Scale;
                _eyeLeft = new EyeRegion(rig, LandmarkId.EyeInnerLeft, LandmarkId.EyeUpperInnerLeft,
                    LandmarkId.EyeUpperLeft, LandmarkId.EyeUpperOuterLeft, LandmarkId.EyeOuterLeft,
                    LandmarkId.EyeLowerInnerLeft, LandmarkId.EyeLowerLeft, LandmarkId.EyeLowerOuterLeft);
                _eyeRight = new EyeRegion(rig, LandmarkId.EyeInnerRight, LandmarkId.EyeUpperInnerRight,
                    LandmarkId.EyeUpperRight, LandmarkId.EyeUpperOuterRight, LandmarkId.EyeOuterRight,
                    LandmarkId.EyeLowerInnerRight, LandmarkId.EyeLowerRight, LandmarkId.EyeLowerOuterRight);
                _browLeft = new BrowRegion(rig, LandmarkId.BrowInnerLeft, LandmarkId.BrowMidLeft,
                    LandmarkId.BrowOuterLeft, LandmarkId.EyeUpperLeft);
                _browRight = new BrowRegion(rig, LandmarkId.BrowInnerRight, LandmarkId.BrowMidRight,
                    LandmarkId.BrowOuterRight, LandmarkId.EyeUpperRight);
                _mouth = new MouthRegion(rig);
                _cheekLeft = rig[LandmarkId.CheekLeft];
                _cheekRight = rig[LandmarkId.CheekRight];
                _noseWingLeft = rig[LandmarkId.NoseWingLeft];
                _noseWingRight = rig[LandmarkId.NoseWingRight];
            }

            /// <summary>
            /// 頂点ごとの動かす倍率（肌の外周 = 別のメッシュとの継ぎ目で 0、離れるほど 1）。三角形が無い・外周が無ければ null。
            /// 目・口の穴の縁と口の中は外周から除く（まばたき・口の開閉で動かす必要がある）。
            /// </summary>
            public float[] Pins(Vector3[] local, int[] triangles)
            {
                List<Vector3> seams = FaceMeshSeams.OuterBoundary(local, triangles, _s * WeldRatio);
                seams.RemoveAll(IsOpening);
                if (seams.Count == 0)
                {
                    return null;
                }

                // 最も近い外周の頂点までの距離で、外周から PinRadiusRatio の範囲をなめらかに 0 へ寄せる
                float radius = _s * PinRadiusRatio;
                float limit = radius * radius;
                var pins = new float[local.Length];
                for (int v = 0; v < local.Length; v++)
                {
                    float nearest = limit;
                    foreach (Vector3 seam in seams)
                    {
                        nearest = Mathf.Min(nearest, (local[v] - seam).sqrMagnitude);
                    }

                    pins[v] = 1f - FaceShapeMath.Falloff(Mathf.Sqrt(nearest), radius);
                }

                return pins;
            }

            private bool IsOpening(Vector3 p)
            {
                // 目のまわり（まぶたの縁 = 目の穴。まばたきで動かす上下の範囲だけ）
                foreach (EyeRegion eye in new[] { _eyeLeft, _eyeRight })
                {
                    eye.Coordinates(p, out float t, out float v);
                    if (eye.Mask(p, t, _s) > 0f && v < eye.Upper(t) + eye.Height * 1.2f
                        && v > eye.Lower(t) - eye.Height * 0.8f)
                    {
                        return true;
                    }
                }

                // 口の穴と口の中（口の幅の 1.5 倍以内で、あごより上・鼻より下）
                return Mathf.Abs(p.x - _mouth.Center.x) <= _mouth.HalfWidth * 1.5f
                    && p.y > _mouth.ChinY && p.y < _mouth.NoseY;
            }

            public Vector3 Delta(string name, Vector3 p, Vector3 normal)
            {
                // 名前の末尾の Left / Right で側を決める（1 = アバターの左）
                float side = name.EndsWith("Right", StringComparison.Ordinal) ? -1f : 1f;
                EyeRegion eye = side > 0f ? _eyeLeft : _eyeRight;
                BrowRegion brow = side > 0f ? _browLeft : _browRight;
                switch (name)
                {
                    case "eyeBlinkLeft":
                    case "eyeBlinkRight":
                        return Blink(eye, p);
                    case "eyeWideLeft":
                    case "eyeWideRight":
                        return Wide(eye, p);
                    case "eyeSquintLeft":
                    case "eyeSquintRight":
                        return Squint(eye, p);
                    case "eyeLookUpLeft":
                    case "eyeLookUpRight":
                        return Look(eye, p, 0f, 0.15f);
                    case "eyeLookDownLeft":
                    case "eyeLookDownRight":
                        return Look(eye, p, 0f, -0.15f);
                    case "eyeLookInLeft":
                    case "eyeLookInRight":
                        return Look(eye, p, -0.15f, 0f);
                    case "eyeLookOutLeft":
                    case "eyeLookOutRight":
                        return Look(eye, p, 0.15f, 0f);
                    case "browDownLeft":
                    case "browDownRight":
                        return BrowDown(brow, p, side);
                    case "browOuterUpLeft":
                    case "browOuterUpRight":
                        return BrowOuterUp(brow, p);
                    case "browInnerUp":
                        return BrowInnerUp(_browLeft, p, 1f) + BrowInnerUp(_browRight, p, -1f);
                    case "jawOpen":
                        return _mouth.JawOpen(p) * _mouth.Jaw(p);
                    case "jawForward":
                        return new Vector3(0f, 0f, _s * 0.08f) * _mouth.Jaw(p);
                    case "jawLeft":
                        return new Vector3(_s * 0.08f, 0f, 0f) * _mouth.Jaw(p);
                    case "jawRight":
                        return new Vector3(-_s * 0.08f, 0f, 0f) * _mouth.Jaw(p);
                    case "mouthClose":
                        return MouthClose(p);
                    case "mouthFunnel":
                        return Funnel(p);
                    case "mouthPucker":
                        return Pucker(p);
                    case "mouthLeft":
                        return new Vector3(_mouth.HalfWidth * 0.35f, 0f, 0f) * _mouth.Lips(p, 1.6f, 0.6f);
                    case "mouthRight":
                        return new Vector3(-_mouth.HalfWidth * 0.35f, 0f, 0f) * _mouth.Lips(p, 1.6f, 0.6f);
                    case "mouthSmileLeft":
                    case "mouthSmileRight":
                        return Smile(p, side);
                    case "mouthFrownLeft":
                    case "mouthFrownRight":
                        return Corner(p, side, 0.03f, -0.18f, 0f);
                    case "mouthDimpleLeft":
                    case "mouthDimpleRight":
                        return Corner(p, side, 0.06f, 0f, -0.08f);
                    case "mouthStretchLeft":
                    case "mouthStretchRight":
                        return Corner(p, side, 0.22f, -0.06f, 0f);
                    case "mouthRollLower":
                        return LowerLip(p, 0.03f, -0.06f, 0.5f);
                    case "mouthRollUpper":
                        return UpperLip(p, -0.03f, -0.06f, 0.5f);
                    case "mouthShrugLower":
                        return LowerLip(p, 0.05f, 0.02f, 0.9f);
                    case "mouthShrugUpper":
                        return UpperLip(p, 0.04f, 0f, 0.7f);
                    case "mouthPressLeft":
                    case "mouthPressRight":
                        return Press(p, side);
                    case "mouthLowerDownLeft":
                    case "mouthLowerDownRight":
                        return LowerLip(p, -0.08f, 0f, 0.7f) * _mouth.Side(p, side);
                    case "mouthUpperUpLeft":
                    case "mouthUpperUpRight":
                        return UpperLip(p, 0.06f, 0f, 0.7f) * _mouth.Side(p, side);
                    case "cheekPuff":
                        return CheekPuff(p, normal, _cheekLeft, 1f) + CheekPuff(p, normal, _cheekRight, -1f);
                    case "cheekSquintLeft":
                    case "cheekSquintRight":
                        return new Vector3(0f, _s * 0.05f, 0f) * CheekWeight(p, side > 0f ? _cheekLeft : _cheekRight);
                    case "noseSneerLeft":
                    case "noseSneerRight":
                        return new Vector3(0f, _s * 0.04f, -_s * 0.01f)
                            * FaceShapeMath.Ellipse(p, side > 0f ? _noseWingLeft : _noseWingRight, _s * 0.18f, _s * 0.18f)
                            * FaceShapeMath.FrontMask(p.z, (side > 0f ? _noseWingLeft : _noseWingRight).z, _s * 0.3f);
                    default:
                        return Vector3.zero;
                }
            }

            private Vector3 Blink(EyeRegion eye, Vector3 p)
            {
                // 上まぶたの上は閉じた線との差だけ下げ、目の開いた部分は閉じた線へ集め、下まぶたの下は少し上げる
                eye.Coordinates(p, out float t, out float v);
                float mask = eye.Mask(p, t, _s);
                if (mask <= 0f)
                {
                    return Vector3.zero;
                }

                float upper = eye.Upper(t);
                float lower = eye.Lower(t);
                float close = eye.Close(t);
                float dv;
                if (v > upper)
                {
                    dv = (close - upper) * FaceShapeMath.Falloff(v - upper, eye.Height * 1.2f);
                }
                else if (v >= lower)
                {
                    dv = close - v;
                }
                else
                {
                    dv = (close - lower) * FaceShapeMath.Falloff(lower - v, eye.Height * 0.8f);
                }

                // 閉じたまぶたが目の玉に埋もれないよう、動く分の少しだけ前へ出す
                return eye.ToLocal(0f, dv, Mathf.Abs(dv) * 0.05f) * mask;
            }

            private Vector3 Wide(EyeRegion eye, Vector3 p)
            {
                // 上まぶたを上げ、下まぶたを少し下げる
                eye.Coordinates(p, out float t, out float v);
                float mask = eye.Mask(p, t, _s);
                if (mask <= 0f)
                {
                    return Vector3.zero;
                }

                float raise = eye.Height * 0.25f;
                float drop = eye.Height * 0.08f;
                return eye.ToLocal(0f, Lids(eye, t, v, raise, -drop), 0f) * mask;
            }

            private Vector3 Squint(EyeRegion eye, Vector3 p)
            {
                // 下まぶたを上げ、上まぶたを少し下げる
                eye.Coordinates(p, out float t, out float v);
                float mask = eye.Mask(p, t, _s);
                if (mask <= 0f)
                {
                    return Vector3.zero;
                }

                float close = eye.Close(t);
                float raise = (close - eye.Lower(t)) * 0.45f;
                float drop = (eye.Upper(t) - close) * 0.12f;
                return eye.ToLocal(0f, Lids(eye, t, v, -drop, raise), 0f) * mask;
            }

            private static float Lids(EyeRegion eye, float t, float v, float upperMove, float lowerMove)
            {
                // 上まぶた側は upperMove、下まぶた側は lowerMove だけ動かし、目の開いた部分は閉じた線を境に引き伸ばす
                float upper = eye.Upper(t);
                float lower = eye.Lower(t);
                float close = eye.Close(t);
                if (v > upper)
                {
                    return upperMove * FaceShapeMath.Falloff(v - upper, eye.Height * 1.2f);
                }

                if (v >= close)
                {
                    return upperMove * (v - close) / Mathf.Max(upper - close, 1e-9f);
                }

                if (v >= lower)
                {
                    return lowerMove * (close - v) / Mathf.Max(close - lower, 1e-9f);
                }

                return lowerMove * FaceShapeMath.Falloff(lower - v, eye.Height * 0.9f);
            }

            private Vector3 Look(EyeRegion eye, Vector3 p, float du, float dv)
            {
                // 目の開いた部分（顔に描かれた瞳）だけを動かす。目が別メッシュなら何も動かない
                eye.Coordinates(p, out float t, out float v);
                float upper = eye.Upper(t);
                float lower = eye.Lower(t);
                if (Mathf.Abs(t) >= 1f || v <= lower || v >= upper)
                {
                    return Vector3.zero;
                }

                float inside = FaceShapeMath.Smooth01(1f - Mathf.Abs(t))
                    * FaceShapeMath.Smooth01(Mathf.Min(v - lower, upper - v) / (eye.Height * 0.25f));
                return eye.ToLocal(du * eye.HalfWidth, dv * eye.Height, 0f) * inside
                    * FaceShapeMath.FrontMask(p.z, eye.Z, _s * 0.3f);
            }

            private Vector3 BrowDown(BrowRegion brow, Vector3 p, float side)
            {
                // 眉を下げ、眉頭ほど強く中央へ寄せる
                float weight = brow.Weight(p, out float along);
                return new Vector3(-side * _s * 0.03f * (1f - along), -_s * 0.1f * Mathf.Lerp(1f, 0.5f, along), 0f)
                    * weight;
            }

            private Vector3 BrowInnerUp(BrowRegion brow, Vector3 p, float side)
            {
                // 眉頭ほど強く上げ、少し中央へ寄せる
                float weight = brow.Weight(p, out float along);
                float inner = 1f - along;
                return new Vector3(-side * _s * 0.015f * inner, _s * 0.1f * inner, 0f) * weight;
            }

            private Vector3 BrowOuterUp(BrowRegion brow, Vector3 p)
            {
                // 眉尻ほど強く上げる
                float weight = brow.Weight(p, out float along);
                return new Vector3(0f, _s * 0.1f * along, 0f) * weight;
            }

            private Vector3 MouthClose(Vector3 p)
            {
                // 顎を開いたまま唇を閉じる: 上唇を下げ、下唇を上げる（それぞれ開いた幅の半分）
                float gap = _s * MouthRegion.JawDropRatio * 0.5f;
                float lips = _mouth.Lips(p, 1.1f, 0.4f);
                float upper = _mouth.UpperSide(p);
                return new Vector3(0f, (-upper + (1f - upper)) * gap, 0f) * lips;
            }

            private Vector3 Funnel(Vector3 p)
            {
                // 口角を中央へ寄せ、唇を前へ出して少し開く
                float lips = _mouth.Lips(p, 1.3f, 0.5f);
                float upper = _mouth.UpperSide(p);
                float dx = -(p.x - _mouth.Center.x) * 0.3f;
                float dy = upper * _s * 0.04f - (1f - upper) * _s * 0.06f;
                return new Vector3(dx, dy, _s * 0.1f) * lips;
            }

            private Vector3 Pucker(Vector3 p)
            {
                // 口角を強く中央へ寄せ、唇を前へ出して閉じる
                float lips = _mouth.Lips(p, 1.3f, 0.5f);
                float dx = -(p.x - _mouth.Center.x) * 0.45f;
                float dy = (_mouth.Center.y - p.y) * 0.3f;
                return new Vector3(dx, dy, _s * 0.12f) * lips;
            }

            private Vector3 Smile(Vector3 p, float side)
            {
                // 口角を上・外・奥へ動かし、頬を少し持ち上げる
                Vector3 corner = Corner(p, side, 0.1f, 0.22f, -0.06f);
                Vector3 cheek = new Vector3(0f, _s * 0.04f, 0f) * CheekWeight(p, side > 0f ? _cheekLeft : _cheekRight);
                return corner + cheek;
            }

            private Vector3 Corner(Vector3 p, float side, float outward, float up, float forward)
            {
                // 口角のまわりを、口の幅に対する比で動かす（outward は外向き）
                float weight = _mouth.Corner(p, side, 0.9f);
                float w = _mouth.HalfWidth;
                return new Vector3(side * outward * w, up * w, forward * w) * weight;
            }

            private Vector3 UpperLip(Vector3 p, float up, float forward, float reach)
            {
                // 上唇のまわりを動かす
                float weight = _mouth.Lips(p, 1.1f, reach) * _mouth.UpperSide(p);
                return new Vector3(0f, up * _s, forward * _s) * weight;
            }

            private Vector3 LowerLip(Vector3 p, float up, float forward, float reach)
            {
                // 下唇のまわりを動かす
                float weight = _mouth.Lips(p, 1.1f, reach) * (1f - _mouth.UpperSide(p));
                return new Vector3(0f, up * _s, forward * _s) * weight;
            }

            private Vector3 Press(Vector3 p, float side)
            {
                // 片側の唇を押し合わせる（上唇を下げ、下唇を上げ、少し奥へ）
                float weight = _mouth.Lips(p, 1.1f, 0.4f) * _mouth.Side(p, side);
                float upper = _mouth.UpperSide(p);
                return new Vector3(0f, (-upper + (1f - upper)) * _s * 0.02f, -_s * 0.01f) * weight;
            }

            private Vector3 CheekPuff(Vector3 p, Vector3 normal, Vector3 cheek, float side)
            {
                // 頬を表面の外向き（法線が無ければ横と前の間）へ膨らませる
                Vector3 outward = normal.sqrMagnitude > 1e-12f ? normal.normalized : new Vector3(side * 0.7f, 0f, 0.7f);
                return outward * (_s * 0.07f) * CheekWeight(p, cheek);
            }

            private float CheekWeight(Vector3 p, Vector3 cheek)
            {
                // 頬のまわりの重み
                return FaceShapeMath.Ellipse(p, cheek, _s * 0.45f, _s * 0.4f)
                    * FaceShapeMath.FrontMask(p.z, cheek.z, _s * 0.5f);
            }
        }
    }
}
