using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Dynamics
{
    /// <summary>
    /// PhysBone 用コライダー（球・カプセル・平面）。粒子を表面の外側（insideBounds なら内側）へ押し出す。
    /// </summary>
    internal sealed class PhysBoneCollider
    {
        // ゼロ除算を避けるための最小距離
        private const float Epsilon = 1e-6f;

        // てこ比の上限を決める接触位置の下限（根元付近の接触で粒子が飛ばないように）
        private const float MinLeverageT = 0.25f;

        private readonly Transform _transform;
        private readonly PhysBoneColliderData _data;
        private readonly Quaternion _localRotation;

        // フレーム毎に更新するワールド空間の形状
        private Vector3 _start;
        private Vector3 _end;
        private Vector3 _normal;
        private float _radius;

        private PhysBoneCollider(Transform transform, PhysBoneColliderData data)
        {
            _transform = transform;
            _data = data;

            // 書き出し値は正規化されていない可能性があるため正規化
            _localRotation = Quaternion.Normalize(data.rotation);
        }

        /// <summary>
        /// パスを解決して生成する。見つからなければ null。
        /// </summary>
        public static PhysBoneCollider Create(Transform avatarRoot, PhysBoneColliderData data)
        {
            Transform target = string.IsNullOrEmpty(data.path) ? avatarRoot : avatarRoot.Find(data.path);
            return target != null ? new PhysBoneCollider(target, data) : null;
        }

        /// <summary>
        /// 現在の Transform からワールド空間の形状を計算する（フレーム毎に 1 回）。
        /// </summary>
        public void UpdateWorld()
        {
            // 中心・軸・半径をワールドへ
            Vector3 center = _transform.TransformPoint(_data.position);
            Vector3 axis = _transform.rotation * _localRotation * Vector3.up;
            float scale = Mathf.Abs(_transform.lossyScale.x);
            _radius = _data.radius * scale;

            // カプセルは高さ（両端の半球込み）から芯の線分を求める。球・平面は点
            float half = _data.shape == PhysBoneColliderData.ShapeCapsule
                ? Mathf.Max(0f, _data.height * scale * 0.5f - _radius)
                : 0f;
            _start = center - axis * half;
            _end = center + axis * half;
            _normal = axis;
        }

        /// <summary>
        /// ワールド空間での形状の要約（診断ログ用）。UpdateWorld 後に呼ぶ。
        /// </summary>
        public string Describe()
        {
            return $"{_transform.name}: {_data.shape} radius {_radius:F3}m, core {Vector3.Distance(_start, _end):F3}m";
        }

        /// <summary>
        /// 親粒子 parent → 粒子 position のボーン（半径 particleRadius の太さを持つ線分）を押し出す。
        /// 動かすのは子側の粒子のみ。根元付近の接触ほど大きく動かし、長さ拘束でボーンを回転させる。
        /// </summary>
        public void Collide(Vector3 parent, ref Vector3 position, float particleRadius)
        {
            // 平面: ボーン両端のうち深い方が法線側に出るまで押し出す
            if (_data.shape == PhysBoneColliderData.ShapePlane)
            {
                float distance = Vector3.Dot(position - _start, _normal);
                if (distance < particleRadius)
                {
                    position += _normal * (particleRadius - distance);
                }

                return;
            }

            // 内側制限は粒子の点で判定（ボーン全体を閉じ込める必要はない）
            if (_data.insideBounds)
            {
                Vector3 core = ClosestPointOnSegment(_start, _end, position);
                Vector3 offset = position - core;
                float limit = Mathf.Max(0f, _radius - particleRadius);
                if (offset.magnitude > limit)
                {
                    position = core + offset.normalized * limit;
                }

                return;
            }

            // 親粒子が既にめり込んでいる場合は線分で押し出せない（根元側で詰まって跳ね上がる）ため、子の点だけで判定
            float required = _radius + particleRadius;
            if ((parent - ClosestPointOnSegment(_start, _end, parent)).sqrMagnitude < required * required)
            {
                PushOutPoint(ref position, required);
                return;
            }

            // ボーン線分とコライダー芯（球は点）の最近接点
            ClosestPointsBetweenSegments(parent, position, _start, _end, out Vector3 onBone, out Vector3 onCore, out float t);
            Vector3 separation = onBone - onCore;
            float length = separation.magnitude;
            if (length >= required)
            {
                return;
            }

            // 押し出し方向（芯上にある場合は粒子から芯への逆方向）
            Vector3 normal = length > Epsilon ? separation / length : (position - onCore).normalized;
            if (normal.sqrMagnitude < Epsilon)
            {
                return;
            }

            // 接触点のめり込みを解消するよう、てこ比（1/t）で子粒子を動かす
            float leverage = 1f / Mathf.Max(t, MinLeverageT);
            position += normal * ((required - length) * leverage);
        }

        private void PushOutPoint(ref Vector3 position, float required)
        {
            // 芯から粒子へのベクトル
            Vector3 offset = position - ClosestPointOnSegment(_start, _end, position);
            float length = offset.magnitude;

            // 十分離れている、または芯上で方向が定まらなければ何もしない
            if (length >= required || length < Epsilon)
            {
                return;
            }

            // 表面まで押し出す
            position += offset / length * (required - length);
        }

        private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
        {
            // 線分が点ならその点
            Vector3 segment = b - a;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < Epsilon)
            {
                return a;
            }

            // 射影位置を線分内に制限
            float t = Mathf.Clamp01(Vector3.Dot(point - a, segment) / lengthSquared);
            return a + segment * t;
        }

        /// <summary>
        /// 線分 p1-q1 と p2-q2 の最近接点（Real-Time Collision Detection 5.1.9）。s は p1-q1 上の位置（0〜1）。
        /// </summary>
        private static void ClosestPointsBetweenSegments(
            Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2, out float s)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);
            float t;

            if (a < Epsilon && e < Epsilon)
            {
                // 両方とも点
                s = 0f;
                t = 0f;
            }
            else if (a < Epsilon)
            {
                // ボーンが点
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e < Epsilon)
                {
                    // 芯が点（球）
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    // 一般の場合（平行なら s = 0 から求める）
                    float b = Vector3.Dot(d1, d2);
                    float denominator = a * e - b * b;
                    s = denominator > Epsilon ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
                    t = (b * s + f) / e;

                    // t が範囲外なら端に固定して s を求め直す
                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((b - c) / a);
                    }
                }
            }

            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }
    }
}
