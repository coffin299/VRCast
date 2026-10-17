using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// カメラの傾き補正のカウントダウン記録。押してから数秒後に、そのときの頭の向きの逆を設定へ保存する。
    /// </summary>
    public sealed class TiltCalibrationRecorder
    {
        /// <summary>
        /// ボタンを押してから記録するまでの秒数。
        /// </summary>
        public const float DelaySeconds = 5f;

        private float _remaining = -1f;

        /// <summary>
        /// カウントダウン中なら true。
        /// </summary>
        public bool IsRunning => _remaining >= 0f;

        /// <summary>
        /// 残り秒数（カウントダウン中のみ。切り上げ表示用）。
        /// </summary>
        public float Remaining => Mathf.Max(0f, _remaining);

        /// <summary>
        /// カウントダウンを始める（実行中ならやり直す）。
        /// </summary>
        public void Start()
        {
            _remaining = DelaySeconds;
        }

        /// <summary>
        /// カウントダウンをやめる（補正は消さない）。
        /// </summary>
        public void Cancel()
        {
            _remaining = -1f;
        }

        /// <summary>
        /// 毎フレーム進める。時間切れで頭の向きから補正を保存したら true。
        /// </summary>
        public bool Tick(IFaceTrackingProvider tracker, AppSettings settings, float deltaTime)
        {
            if (!IsRunning)
            {
                return false;
            }

            _remaining -= Mathf.Max(0f, deltaTime);
            if (_remaining > 0f)
            {
                return false;
            }

            _remaining = -1f;

            // 顔が取れなければ補正しない（もう一度押してもらう）
            if (tracker == null || settings == null || !tracker.TryGetFrame(out FaceTrackingFrame face))
            {
                return false;
            }

            // まっすぐ立ったときの頭の向きを正面のカメラ空間へ直す回転
            settings.TrackingTiltCorrection = Quaternion.Inverse(face.HeadRotation);
            return true;
        }
    }
}
