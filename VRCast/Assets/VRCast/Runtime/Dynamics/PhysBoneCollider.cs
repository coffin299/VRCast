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
        /// 半径 particleRadius の粒子を押し出す。
        /// </summary>
        public void Collide(ref Vector3 position, float particleRadius)
        {
            // 平面は法線側に留める
            if (_data.shape == PhysBoneColliderData.ShapePlane)
            {
                float distance = Vector3.Dot(position - _start, _normal);
                if (distance < particleRadius)
                {
                    position += _normal * (particleRadius - distance);
                }

                return;
            }

            // 球・カプセルは芯の最近点からの距離で判定
            Vector3 closest = ClosestPointOnSegment(position);
            Vector3 offset = position - closest;
            float length = offset.magnitude;
            if (length < Epsilon)
            {
                return;
            }

            if (_data.insideBounds)
            {
                // 内側に閉じ込める
                float limit = Mathf.Max(0f, _radius - particleRadius);
                if (length > limit)
                {
                    position = closest + offset / length * limit;
                }
            }
            else
            {
                // 外側へ押し出す
                float limit = _radius + particleRadius;
                if (length < limit)
                {
                    position = closest + offset / length * limit;
                }
            }
        }

        private Vector3 ClosestPointOnSegment(Vector3 point)
        {
            // 線分が点（球）ならその点
            Vector3 segment = _end - _start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < Epsilon)
            {
                return _start;
            }

            // 射影位置を線分内に制限
            float t = Mathf.Clamp01(Vector3.Dot(point - _start, segment) / lengthSquared);
            return _start + segment * t;
        }
    }
}
