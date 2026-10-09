using System;
using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// トラッカーから不定期に届く点の列を、描画の毎フレームなめらかに動かす。
    /// 届いた時点の表示位置から最新の値へ、届く間隔（推定値）をかけて直線で動かす（遅れは約 1 回分の間隔）。
    /// </summary>
    public sealed class PointInterpolator
    {
        // 届く間隔の推定の初期値と範囲（秒）。途切れや同じ描画フレームでの連続受信は範囲内に収める
        private const float DefaultInterval = 1f / 30f;
        private const float MinInterval = 1f / 120f;
        private const float MaxInterval = 0.2f;

        // 受信のたびに間隔の推定を測った値へ寄せる割合（受信の揺らぎで動きの速さが変わらないように）
        private const float IntervalSmoothing = 0.2f;

        private readonly Vector3[] _from;
        private readonly Vector3[] _to;
        private readonly Vector3[] _current;
        private float _receivedAt;
        private float _interval = DefaultInterval;

        public PointInterpolator(int count)
        {
            _from = new Vector3[count];
            _to = new Vector3[count];
            _current = new Vector3[count];
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
        /// 推定した届く間隔（秒）。
        /// </summary>
        public float Interval => _interval;

        /// <summary>
        /// 新しく届いた値を目標にする（最初の値はそのまま表示位置にする）。now は秒。
        /// </summary>
        public void Push(Vector3[] points, float now)
        {
            int count = Mathf.Min(points.Length, _current.Length);

            // 最初の値（見失った後を含む）は補間せずに合わせる
            if (!HasValue)
            {
                Array.Copy(points, _from, count);
                Array.Copy(points, _to, count);
                Array.Copy(points, _current, count);
                _receivedAt = now;
                HasValue = true;
                return;
            }

            // 前回からの間隔で推定を更新する
            float measured = Mathf.Clamp(now - _receivedAt, MinInterval, MaxInterval);
            _interval = Mathf.Lerp(_interval, measured, IntervalSmoothing);

            // 今の表示位置から動かし始めるので、間隔が揺らいでも位置は飛ばない
            Array.Copy(_current, _from, _current.Length);
            Array.Copy(points, _to, count);
            _receivedAt = now;
        }

        /// <summary>
        /// 表示位置を now（秒）の時点へ進める。最新の値に着いたら次が届くまで止まる。
        /// </summary>
        public void Update(float now)
        {
            if (!HasValue)
            {
                return;
            }

            // 届いてからの経過を間隔で割った進み具合
            float t = Mathf.Clamp01((now - _receivedAt) / _interval);
            for (int i = 0; i < _current.Length; i++)
            {
                _current[i] = Vector3.LerpUnclamped(_from[i], _to[i], t);
            }
        }

        /// <summary>
        /// 見失ったときに呼ぶ（次の値は補間せずに合わせる。間隔の推定は残す）。
        /// </summary>
        public void Reset()
        {
            HasValue = false;
        }
    }
}
