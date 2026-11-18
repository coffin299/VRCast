using System.Collections.Generic;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 転写の手本。両目の間隔を 1 とした顔の上の点と、ARKit の形状ごとの移動量。
    /// まぶたは閉じた線へ寄せ、唇の巻き込みは唇の前を軸に回し、顎は回転の中心まわりに回す。
    /// </summary>
    public sealed class ExpressionTemplate
    {
        // 顔の表面に置く点の間隔（両目の間隔に対する比）
        private const float SampleStepRatio = 0.1f;

        private static ExpressionTemplate _shared;

        private readonly Vector3[][] _deltas;

        private ExpressionTemplate(LandmarkId[] landmarks, Vector3[] landmarkPositions, Vector3[] samples,
            Vector3[][] deltas)
        {
            Landmarks = landmarks;
            LandmarkPositions = landmarkPositions;
            Samples = samples;
            _deltas = deltas;
        }

        /// <summary>
        /// 最初に使ったときに一度だけ作る。
        /// </summary>
        public static ExpressionTemplate Shared => _shared ?? (_shared = Build());

        /// <summary>手本の目印（詳細の全部）。</summary>
        public LandmarkId[] Landmarks { get; }

        /// <summary>手本の目印の位置（局所座標。両目の間隔が 1）。</summary>
        public Vector3[] LandmarkPositions { get; }

        /// <summary>移動量を持っている点（局所座標）。</summary>
        public Vector3[] Samples { get; }

        /// <summary>
        /// 形状 name の、点 index の移動量。無い形状は 0。
        /// </summary>
        public Vector3 Delta(int shape, int index)
        {
            Vector3[] deltas = _deltas[shape];
            return deltas == null ? Vector3.zero : deltas[index];
        }

        private static ExpressionTemplate Build()
        {
            // 両目の間隔が 1 の顔（アバターの左が +X、前が +Z）
            Dictionary<LandmarkId, Vector3> points = CanonPoints();
            var normals = new Dictionary<LandmarkId, Vector3>();
            foreach (LandmarkId id in points.Keys)
            {
                normals[id] = Vector3.forward;
            }

            FaceRig rig = FaceRig.Build(points, normals, out List<LandmarkId> missing);
            if (rig == null)
            {
                throw new System.InvalidOperationException(
                    "The expression template is missing points: " + string.Join(", ", missing));
            }

            // 転写の制御点は、推定で補ったものも含む全部の目印
            List<LandmarkId> landmarks = FaceLandmarks.For(LandmarkDetail.Detailed);
            var positions = new Vector3[landmarks.Count];
            for (int i = 0; i < landmarks.Count; i++)
            {
                positions[i] = rig[landmarks[i]];
            }

            // 顔の前面に点を敷き、形状ごとの移動量を求める
            Vector3[] samples = PlaceSamples(rig);
            var sampleNormals = new Vector3[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                sampleNormals[i] = SampleNormal(rig.Scale, samples[i]);
            }

            var muscles = new TemplateMuscles(rig);
            var deltas = new Vector3[ArKitFace.BlendShapeNames.Length][];
            for (int shape = 0; shape < deltas.Length; shape++)
            {
                var field = new Vector3[samples.Length];
                bool any = false;
                for (int i = 0; i < samples.Length; i++)
                {
                    Vector3 delta = muscles.Delta(ArKitFace.BlendShapeNames[shape], samples[i], sampleNormals[i]);
                    field[i] = delta;
                    any |= delta.sqrMagnitude > 0f;
                }

                // 1 点も動かない形状（舌など）は配列を持たない
                deltas[shape] = any ? field : null;
            }

            return new ExpressionTemplate(landmarks.ToArray(), positions, samples, deltas);
        }

        private static Dictionary<LandmarkId, Vector3> CanonPoints()
        {
            // 最小の目印だけを置き、残りは FaceRig の推定に任せる（アバター側と同じ補い方）
            return new Dictionary<LandmarkId, Vector3>
            {
                { LandmarkId.EyeUpperLeft, new Vector3(0.5f, 0.16f, 0.08f) },
                { LandmarkId.EyeUpperRight, new Vector3(-0.5f, 0.16f, 0.08f) },
                { LandmarkId.EyeLowerLeft, new Vector3(0.5f, -0.02f, 0.06f) },
                { LandmarkId.EyeLowerRight, new Vector3(-0.5f, -0.02f, 0.06f) },
                { LandmarkId.BrowMidLeft, new Vector3(0.52f, 0.42f, 0.12f) },
                { LandmarkId.BrowMidRight, new Vector3(-0.52f, 0.42f, 0.12f) },
                { LandmarkId.MouthCornerLeft, new Vector3(0.32f, -0.58f, 0.1f) },
                { LandmarkId.MouthCornerRight, new Vector3(-0.32f, -0.58f, 0.1f) },
                { LandmarkId.LipUpper, new Vector3(0f, -0.5f, 0.16f) },
                { LandmarkId.LipLower, new Vector3(0f, -0.66f, 0.12f) },
                { LandmarkId.Chin, new Vector3(0f, -1.15f, 0.06f) },
            };
        }

        private static Vector3[] PlaceSamples(FaceRig rig)
        {
            // 楕円の中だけを顔の表面とする（後頭・耳は置かない）
            float scale = rig.Scale;
            float step = scale * SampleStepRatio;
            float radiusX = scale * 1.15f;
            float radiusY = scale * 1.2f;
            float centerY = -scale * 0.15f;
            var samples = new List<Vector3>();
            for (float y = -scale * 1.4f; y <= scale * 0.95f; y += step)
            {
                for (float x = -radiusX; x <= radiusX; x += step)
                {
                    float nx = x / radiusX;
                    float ny = (y - centerY) / radiusY;
                    float inside = 1f - nx * nx - ny * ny;
                    if (inside <= 0f)
                    {
                        continue;
                    }

                    // 中央ほど前へ出す（頬の法線が外側を向く）
                    float z = scale * 0.28f * inside;
                    samples.Add(new Vector3(x, y, z));
                }
            }

            return samples.ToArray();
        }

        private static Vector3 SampleNormal(float scale, Vector3 point)
        {
            // 膨らみ z = scale * 0.28 * (1 - (x/rx)^2 - ((y-cy)/ry)^2) の、前を向く法線
            float radiusX = scale * 1.15f;
            float radiusY = scale * 1.2f;
            float centerY = -scale * 0.15f;
            float dzdx = scale * 0.28f * (-2f * point.x / (radiusX * radiusX));
            float dzdy = scale * 0.28f * (-2f * (point.y - centerY) / (radiusY * radiusY));
            return new Vector3(-dzdx, -dzdy, 1f).normalized;
        }

        /// <summary>
        /// 手本の顔の上の、形状ごとの移動量。
        /// </summary>
        private sealed class TemplateMuscles
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

            public TemplateMuscles(FaceRig rig)
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

            public Vector3 Delta(string name, Vector3 point, Vector3 normal)
            {
                // 名前の末尾で側を決める（1 = アバターの左）
                float side = name.EndsWith("Right", System.StringComparison.Ordinal) ? -1f : 1f;
                EyeRegion eye = side > 0f ? _eyeLeft : _eyeRight;
                BrowRegion brow = side > 0f ? _browLeft : _browRight;
                switch (name)
                {
                    case "eyeBlinkLeft":
                    case "eyeBlinkRight":
                        return Blink(eye, point);
                    case "eyeWideLeft":
                    case "eyeWideRight":
                        return Lids(eye, point, eye.Height * 0.25f, -eye.Height * 0.08f);
                    case "eyeSquintLeft":
                    case "eyeSquintRight":
                        return Squint(eye, point);
                    case "eyeLookUpLeft":
                    case "eyeLookUpRight":
                        return Look(eye, point, 0f, 0.15f);
                    case "eyeLookDownLeft":
                    case "eyeLookDownRight":
                        return Look(eye, point, 0f, -0.15f);
                    case "eyeLookInLeft":
                    case "eyeLookInRight":
                        return Look(eye, point, -0.15f, 0f);
                    case "eyeLookOutLeft":
                    case "eyeLookOutRight":
                        return Look(eye, point, 0.15f, 0f);
                    case "browDownLeft":
                    case "browDownRight":
                        return Brow(brow, point, -side * _s * 0.03f, -1f);
                    case "browOuterUpLeft":
                    case "browOuterUpRight":
                        return Brow(brow, point, 0f, 1f, outer: true);
                    case "browInnerUp":
                        return Brow(_browLeft, point, -_s * 0.015f, 1f, inner: true)
                            + Brow(_browRight, point, _s * 0.015f, 1f, inner: true);
                    case "jawOpen":
                        return _mouth.JawOpen(point) * _mouth.Jaw(point);
                    case "jawForward":
                        return new Vector3(0f, 0f, _s * 0.08f) * _mouth.Jaw(point);
                    case "jawLeft":
                        return new Vector3(_s * 0.08f, 0f, 0f) * _mouth.Jaw(point);
                    case "jawRight":
                        return new Vector3(-_s * 0.08f, 0f, 0f) * _mouth.Jaw(point);
                    case "mouthClose":
                        return MouthClose(point);
                    case "mouthFunnel":
                        return Gather(point, 0.45f, 0.35f, _s * 0.1f);
                    case "mouthPucker":
                        return Gather(point, 0.7f, 0.6f, _s * 0.14f);
                    case "mouthLeft":
                        return new Vector3(_mouth.HalfWidth * 0.35f, 0f, 0f) * _mouth.Lips(point, 1.6f, 0.6f);
                    case "mouthRight":
                        return new Vector3(-_mouth.HalfWidth * 0.35f, 0f, 0f) * _mouth.Lips(point, 1.6f, 0.6f);
                    case "mouthSmileLeft":
                    case "mouthSmileRight":
                        return Smile(point, side);
                    case "mouthFrownLeft":
                    case "mouthFrownRight":
                        return Corner(point, side, 0.03f, -0.18f, 0f);
                    case "mouthDimpleLeft":
                    case "mouthDimpleRight":
                        return Corner(point, side, 0.06f, 0f, -0.08f);
                    case "mouthStretchLeft":
                    case "mouthStretchRight":
                        return Corner(point, side, 0.22f, -0.06f, 0f);
                    case "mouthRollLower":
                        return Roll(point, _mouth.Lower, 0.9f, lower: true);
                    case "mouthRollUpper":
                        return Roll(point, _mouth.Upper, -0.9f, lower: false);
                    case "mouthShrugLower":
                        return Lip(point, 0.05f, 0.02f, lower: true);
                    case "mouthShrugUpper":
                        return Lip(point, 0.04f, 0f, lower: false);
                    case "mouthPressLeft":
                    case "mouthPressRight":
                        return Press(point, side);
                    case "mouthLowerDownLeft":
                    case "mouthLowerDownRight":
                        return Lip(point, -0.08f, 0f, lower: true) * _mouth.Side(point, side);
                    case "mouthUpperUpLeft":
                    case "mouthUpperUpRight":
                        return Lip(point, 0.06f, 0f, lower: false) * _mouth.Side(point, side);
                    case "cheekPuff":
                        return CheekPuff(point, normal, _cheekLeft) + CheekPuff(point, normal, _cheekRight);
                    case "cheekSquintLeft":
                    case "cheekSquintRight":
                        return new Vector3(0f, _s * 0.05f, 0f) * CheekWeight(point, side > 0f ? _cheekLeft : _cheekRight);
                    case "noseSneerLeft":
                    case "noseSneerRight":
                        return Nose(point, side > 0f ? _noseWingLeft : _noseWingRight);
                    default:
                        return Vector3.zero;
                }
            }

            private Vector3 Blink(EyeRegion eye, Vector3 point)
            {
                // 上まぶたは閉じた線まで下げ、目の中はその線へ集め、下まぶたは少し上げる
                eye.Coordinates(point, out float t, out float v);
                float mask = eye.Mask(point, t, _s);
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

                // 閉じたまぶたが奥へ埋まらないよう、下がる分だけ前へ出す
                return eye.ToLocal(0f, dv, Mathf.Abs(dv) * 0.2f) * mask;
            }

            private Vector3 Lids(EyeRegion eye, Vector3 point, float upperMove, float lowerMove)
            {
                // 上まぶたと下まぶたを、閉じた線を境に逆へ動かす
                eye.Coordinates(point, out float t, out float v);
                float mask = eye.Mask(point, t, _s);
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
                    dv = upperMove * FaceShapeMath.Falloff(v - upper, eye.Height * 1.2f);
                }
                else if (v >= close)
                {
                    dv = upperMove * (v - close) / Mathf.Max(upper - close, 1e-6f);
                }
                else if (v >= lower)
                {
                    dv = lowerMove * (close - v) / Mathf.Max(close - lower, 1e-6f);
                }
                else
                {
                    dv = lowerMove * FaceShapeMath.Falloff(lower - v, eye.Height * 0.9f);
                }

                return eye.ToLocal(0f, dv, 0f) * mask;
            }

            private Vector3 Squint(EyeRegion eye, Vector3 point)
            {
                // 下まぶたを閉じた線の方へ上げ、上まぶたは少し下げる
                eye.Coordinates(point, out float t, out float _);
                float raise = (eye.Close(t) - eye.Lower(t)) * 0.45f;
                float drop = (eye.Upper(t) - eye.Close(t)) * 0.12f;
                return Lids(eye, point, -drop, raise);
            }

            private Vector3 Look(EyeRegion eye, Vector3 point, float du, float dv)
            {
                // 目の開いた中（描かれた瞳）だけを動かす
                eye.Coordinates(point, out float t, out float v);
                float upper = eye.Upper(t);
                float lower = eye.Lower(t);
                if (Mathf.Abs(t) >= 1f || v <= lower || v >= upper)
                {
                    return Vector3.zero;
                }

                float inside = FaceShapeMath.Smooth01(1f - Mathf.Abs(t))
                    * FaceShapeMath.Smooth01(Mathf.Min(v - lower, upper - v) / (eye.Height * 0.25f));
                return eye.ToLocal(du * eye.HalfWidth, dv * eye.Height, 0f) * inside
                    * FaceShapeMath.FrontMask(point.z, eye.Z, _s * 0.3f);
            }

            private Vector3 Brow(BrowRegion brow, Vector3 point, float x, float ySign, bool outer = false, bool inner = false)
            {
                // 眉頭 0 〜 眉尻 1。下げるときは眉頭ほど寄せ、上げるときは指定の端ほど強くする
                float weight = brow.Weight(point, out float along);
                float alongWeight = outer ? along : inner ? 1f - along : Mathf.Lerp(1f, 0.5f, along);
                float y = ySign < 0f ? -_s * 0.1f * alongWeight : _s * 0.1f * alongWeight;
                float sideX = (inner || ySign < 0f) ? x * (inner ? 1f - along : 1f) : 0f;
                return new Vector3(sideX, y, 0f) * weight;
            }

            private Vector3 MouthClose(Vector3 point)
            {
                // 開いた顎のまま、上唇を下げて下唇を上げる
                float gap = _s * MouthRegion.JawDropRatio * 0.5f;
                float upper = _mouth.UpperSide(point);
                return new Vector3(0f, (-upper + (1f - upper)) * gap, 0f) * _mouth.Lips(point, 1.1f, 0.4f);
            }

            private Vector3 Gather(Vector3 point, float inward, float close, float forward)
            {
                // 唇を、口の前の点へ寄せてから前へ出す（ふくれっ面は寄せを強く、上下も閉じる）
                float lips = _mouth.Lips(point, 1.3f, 0.5f);
                Vector3 focus = _mouth.Center + new Vector3(0f, 0f, forward);
                Vector3 toward = focus - point;
                return new Vector3(toward.x * inward, toward.y * close, forward) * lips;
            }

            private Vector3 Smile(Vector3 point, float side)
            {
                // 口角を上・外・奥へ回し、頬を法線の外へ少し持ち上げる
                Vector3 corner = Corner(point, side, 0.12f, 0.26f, -0.06f);
                Vector3 cheekPoint = side > 0f ? _cheekLeft : _cheekRight;
                Vector3 cheek = (Vector3.up * (_s * 0.04f) + SampleNormal(_s, cheekPoint) * (_s * 0.02f))
                    * CheekWeight(point, cheekPoint);
                return corner + cheek;
            }

            private Vector3 Corner(Vector3 point, float side, float outward, float up, float forward)
            {
                // 口角のまわりを、口の幅に対する比で動かす
                float weight = _mouth.Corner(point, side, 0.9f);
                float width = _mouth.HalfWidth;
                return new Vector3(side * outward * width, up * width, forward * width) * weight;
            }

            private Vector3 Roll(Vector3 point, Vector3 lip, float angle, bool lower)
            {
                // 唇の表面より前を軸に回す（上唇は下げて奥へ、下唇は上げて奥へ巻き込む）
                float weight = _mouth.Lips(point, 1.05f, 0.45f) * (lower ? 1f - _mouth.UpperSide(point) : _mouth.UpperSide(point));
                Vector3 center = lip + new Vector3(0f, 0f, _s * 0.06f);
                return Rotate(point, center, Vector3.right, angle) * weight;
            }

            private Vector3 Lip(Vector3 point, float up, float forward, bool lower)
            {
                // 上唇か下唇だけを、顔の大きさに対する比で動かす
                float weight = _mouth.Lips(point, 1.1f, 0.7f) * (lower ? 1f - _mouth.UpperSide(point) : _mouth.UpperSide(point));
                return new Vector3(0f, up * _s, forward * _s) * weight;
            }

            private Vector3 Press(Vector3 point, float side)
            {
                // 片側の唇を押し合わせ、少し奥へ入れる
                float weight = _mouth.Lips(point, 1.1f, 0.4f) * _mouth.Side(point, side);
                float upper = _mouth.UpperSide(point);
                return new Vector3(0f, (-upper + (1f - upper)) * _s * 0.02f, -_s * 0.02f) * weight;
            }

            private Vector3 CheekPuff(Vector3 point, Vector3 normal, Vector3 cheek)
            {
                // 頬を表面の外向きへ膨らませる
                Vector3 outward = normal.sqrMagnitude > 1e-12f ? normal : Vector3.forward;
                return outward * (_s * 0.09f) * CheekWeight(point, cheek);
            }

            private float CheekWeight(Vector3 point, Vector3 cheek)
            {
                return FaceShapeMath.Ellipse(point, cheek, _s * 0.42f, _s * 0.38f)
                    * FaceShapeMath.FrontMask(point.z, cheek.z, _s * 0.5f);
            }

            private Vector3 Nose(Vector3 point, Vector3 wing)
            {
                // 小鼻を上げて少し奥へ
                return new Vector3(0f, _s * 0.045f, -_s * 0.015f)
                    * FaceShapeMath.Ellipse(point, wing, _s * 0.16f, _s * 0.16f)
                    * FaceShapeMath.FrontMask(point.z, wing.z, _s * 0.3f);
            }

            private static Vector3 Rotate(Vector3 point, Vector3 center, Vector3 axis, float radians)
            {
                // 軸まわりの回転から、元の位置を引いた移動量
                if (Mathf.Abs(radians) < 1e-6f)
                {
                    return Vector3.zero;
                }

                Vector3 unit = axis.sqrMagnitude > 1e-12f ? axis.normalized : Vector3.right;
                Vector3 offset = point - center;
                float cos = Mathf.Cos(radians);
                float sin = Mathf.Sin(radians);
                Vector3 rotated = offset * cos + Vector3.Cross(unit, offset) * sin
                    + unit * Vector3.Dot(unit, offset) * (1f - cos);
                return rotated - offset;
            }
        }
    }
}
