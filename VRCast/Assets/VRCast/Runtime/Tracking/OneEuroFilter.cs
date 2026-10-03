using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// Vector3 用の One Euro フィルター。止まっている間は強く平滑化して細かい揺れを消し、
    /// 速く動いている間は平滑化を弱めて遅れを抑える。
    /// </summary>
    public sealed class OneEuroFilter
    {
        private readonly float _minCutoff;
        private readonly float _beta;
        private readonly float _derivativeCutoff;
        private Vector3 _value;
        private Vector3 _derivative;
        private bool _initialized;

        /// <param name="minCutoff">静止時のカットオフ周波数（Hz、小さいほど強く平滑化）</param>
        /// <param name="beta">速度に応じてカットオフを上げる係数（大きいほど速い動きに追従）</param>
        /// <param name="derivativeCutoff">速度の推定に使うカットオフ周波数（Hz）</param>
        public OneEuroFilter(float minCutoff, float beta, float derivativeCutoff = 1f)
        {
            _minCutoff = minCutoff;
            _beta = beta;
            _derivativeCutoff = derivativeCutoff;
        }

        public Vector3 Value => _value;

        /// <summary>
        /// 平滑化せずに値を合わせる（追従の開始時など）。
        /// </summary>
        public void Reset(Vector3 value)
        {
            _value = value;
            _derivative = Vector3.zero;
            _initialized = true;
        }

        public Vector3 Filter(Vector3 sample, float deltaTime)
        {
            // 初回はそのまま採用
            if (!_initialized)
            {
                Reset(sample);
                return _value;
            }

            // 時間が進んでいなければ前回の値のまま
            if (deltaTime <= 0f)
            {
                return _value;
            }

            // 速度を平滑化して推定し、速いほどカットオフを上げる
            Vector3 derivative = (sample - _value) / deltaTime;
            _derivative = Vector3.Lerp(_derivative, derivative, Alpha(_derivativeCutoff, deltaTime));
            float cutoff = _minCutoff + _beta * _derivative.magnitude;

            // カットオフに応じた割合で新しい値へ寄せる
            _value = Vector3.Lerp(_value, sample, Alpha(cutoff, deltaTime));
            return _value;
        }

        private static float Alpha(float cutoff, float deltaTime)
        {
            // 1 次ローパスフィルターの係数
            float tau = 1f / (2f * Mathf.PI * cutoff);
            return 1f / (1f + tau / deltaTime);
        }
    }
}
