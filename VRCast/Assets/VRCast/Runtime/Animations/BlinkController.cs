using System.Collections.Generic;
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

        // 左右別の BlendShape は同時に閉じる
        private readonly List<BlendShapeOverlay> _eyelids = new List<BlendShapeOverlay>();
        private AppSettings _settings;
        private float _nextBlinkTime;

        // まばたき開始時刻（負なら非まばたき中）
        private float _blinkStart = -1f;

        public bool IsAvailable => _eyelids.Count > 0;

        public void Initialize(Transform root, EyelidData data, AppSettings settings)
        {
            _settings = settings;

            // 見つかったまぶた BlendShape だけを対象にする（0 件ならまばたき無し）
            foreach (string shape in data.blinkBlendShapes)
            {
                BlendShapeOverlay eyelid = BlendShapeOverlay.Create(root, data.meshPath, shape);
                if (eyelid != null)
                {
                    _eyelids.Add(eyelid);
                }
            }

            ScheduleNext();
        }

        private void LateUpdate()
        {
            // 対象が無ければ何もしない
            if (!IsAvailable)
            {
                return;
            }

            // OFF の間は開いたまま（元の値）
            if (!_settings.autoBlink)
            {
                _blinkStart = -1f;
                Write(0f);
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

            Write(closed * 100f);
        }

        private void Write(float weight)
        {
            // 全まぶた BlendShape に同じ値を上乗せ
            foreach (BlendShapeOverlay eyelid in _eyelids)
            {
                eyelid.Write(weight);
            }
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
