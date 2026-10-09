using System;
using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// トラッカーから不定期に届く点の列を、描画の毎フレームなめらかに追従させる。
    /// 点ごとに速度を持つバネ（SmoothDamp）で最新の値へ寄せるため、届く間隔が揺れても動きが止まったり跳ねたりしない。
    /// </summary>
    public sealed class PointSmoother
    {
        private readonly Vector3[] _target;
        private readonly Vector3[] _current;
        private readonly Vector3[] _velocity;
        private readonly float _smoothTime;
        private float _updatedAt;

        /// <param name="count">点の数</param>
        /// <param name="smoothTime">最新の値へ寄る目安の秒数（大きいほどなめらかで遅れる）</param>
        public PointSmoother(int count, float smoothTime)
        {
            _target = new Vector3[count];
            _current = new Vector3[count];
            _velocity = new Vector3[count];
            _smoothTime = smoothTime;
        }

        /// <summary>
        /// 現在の表示位置（Update で更新される）。
        /// </summary>
        public Vector3[] Current => _current;

        /// <summary>
        /// 値を受け取っていれば true（Reset で false）。
        /// </summary>
        public bool HasValue { get; private set; }

        /// <summary>
        /// 新しく届いた値を目標にする（最初の値はそのまま表示位置にする）。now は秒。
        /// </summary>
        public void Push(Vector3[] points, float now)
        {
            int count = Mathf.Min(points.Length, _target.Length);
            Array.Copy(points, _target, count);

            // 最初の値（見失った後を含む）は追従させずに合わせ、止まった状態から始める
            if (!HasValue)
            {
                Array.Copy(points, _current, count);
                Array.Clear(_velocity, 0, _velocity.Length);
                _updatedAt = now;
                HasValue = true;
            }
        }

        /// <summary>
        /// 表示位置を now（秒）の時点まで目標へ寄せる。
        /// </summary>
        public void Update(float now)
        {
            if (!HasValue)
            {
                return;
            }

            // 前回からの経過時間（同じ時刻・逆行なら動かさない）
            float deltaTime = now - _updatedAt;
            _updatedAt = now;
            if (deltaTime <= 0f)
            {
                return;
            }

            for (int i = 0; i < _current.Length; i++)
            {
                _current[i] = Vector3.SmoothDamp(
                    _current[i], _target[i], ref _velocity[i], _smoothTime, Mathf.Infinity, deltaTime);
            }
        }

        /// <summary>
        /// 見失ったときに呼ぶ（次の値は追従させずに合わせる）。
        /// </summary>
        public void Reset()
        {
            HasValue = false;
        }
    }
}
