using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// ランダム間隔の自動まばたき。Descriptor の eyelids（BlendShape 方式）の blink を上乗せする。
    /// AppSettings.autoBlink で ON/OFF。
    /// </summary>
    public class BlinkController : MonoBehaviour
    {
        // まばたき間隔（秒）の範囲
        private const float MinInterval = 2f;
        private const float MaxInterval = 6f;

        // 閉じる・閉じたまま・開くの時間（秒）
        private const float CloseDuration = 0.06f;
        private const float HoldDuration = 0.04f;
        private const float OpenDuration = 0.12f;
        private const float TotalDuration = CloseDuration + HoldDuration + OpenDuration;

        private BlendShapeOverlay _eyelid;
        private AppSettings _settings;
        private float _nextBlinkTime;

        // まばたき開始時刻（負なら非まばたき中）
        private float _blinkStart = -1f;

        public bool IsAvailable => _eyelid != null;

        public void Initialize(Transform root, EyelidData data, AppSettings settings)
        {
            _settings = settings;

            // まぶた BlendShape が無ければまばたき無し
            _eyelid = BlendShapeOverlay.Create(root, data.meshPath, data.blinkBlendShape);
            ScheduleNext();
        }

        private void LateUpdate()
        {
            // 対象が無ければ何もしない
            if (_eyelid == null)
            {
                return;
            }

            // OFF の間は開いたまま（元の値）
            if (!_settings.autoBlink)
            {
                _blinkStart = -1f;
                _eyelid.Write(0f);
                return;
            }

            // 予定時刻になったらまばたき開始
            float now = Time.time;
            if (_blinkStart < 0f && now >= _nextBlinkTime)
            {
                _blinkStart = now;
            }

            // まばたき中なら経過時間から閉じ具合を求める
            float closed = 0f;
            if (_blinkStart >= 0f)
            {
                float elapsed = now - _blinkStart;
                closed = Evaluate(elapsed);

                // 終わったら次を予約
                if (elapsed >= TotalDuration)
                {
                    _blinkStart = -1f;
                    ScheduleNext();
                }
            }

            _eyelid.Write(closed * 100f);
        }

        private static float Evaluate(float elapsed)
        {
            // 閉じる途中
            if (elapsed < CloseDuration)
            {
                return elapsed / CloseDuration;
            }

            // 閉じたまま
            if (elapsed < CloseDuration + HoldDuration)
            {
                return 1f;
            }

            // 開く途中
            return 1f - Mathf.Clamp01((elapsed - CloseDuration - HoldDuration) / OpenDuration);
        }

        private void ScheduleNext()
        {
            // 次のまばたきをランダムな間隔で予約
            _nextBlinkTime = Time.time + Random.Range(MinInterval, MaxInterval);
        }
    }
}
