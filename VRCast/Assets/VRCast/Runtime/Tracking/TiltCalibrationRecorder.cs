using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// カメラの傾き補正のカウントダウン記録。押してから数秒後に、両肩と背骨（なければ頭の向き）から補正を保存する。
    /// MediaPipe の顔の行列は正面でも、体の world 座標だけカメラの俯角で傾くことがあるため、体を優先する。
    /// </summary>
    public sealed class TiltCalibrationRecorder
    {
        /// <summary>
        /// ボタンを押してから記録するまでの秒数。
        /// </summary>
        public const float DelaySeconds = 5f;

        // 背骨がこれより短い（m）ときは体から補正できない
        private const float MinSpineLength = 0.05f;

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
        /// 毎フレーム進める。時間切れで補正を保存したら true。
        /// </summary>
        public bool Tick(
            IFaceTrackingProvider face, IBodyTrackingProvider body, AppSettings settings, float deltaTime)
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
            if (settings == null)
            {
                return false;
            }

            // 体（両肩）が取れていれば背骨を直立にする補正を優先（顔だけの補正だと 0° のまま残ることがある）
            if (TryCorrectionFromBody(body, out Quaternion correction))
            {
                settings.TrackingTiltCorrection = correction;
                return true;
            }

            // 体が無いときだけ頭の向きの逆（OpenSeeFace 等）
            if (face != null && face.TryGetFrame(out FaceTrackingFrame frame))
            {
                settings.TrackingTiltCorrection = Quaternion.Inverse(frame.HeadRotation);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 腰原点→両肩の中点を背骨とし、それを上向きにする回転を返す。
        /// </summary>
        public static bool TryCorrectionFromBody(IBodyTrackingProvider body, out Quaternion correction)
        {
            correction = Quaternion.identity;
            return body != null && body.TryGetBody(out BodyTrackingFrame frame)
                && TryCorrectionFromBody(frame, out correction);
        }

        /// <summary>
        /// 腰原点→両肩の中点を背骨とし、それを上向きにする回転を返す。
        /// </summary>
        public static bool TryCorrectionFromBody(BodyTrackingFrame frame, out Quaternion correction)
        {
            correction = Quaternion.identity;

            // 両肩が映っていなければ背骨が取れない
            if (!frame.Left.HasShoulder || !frame.Right.HasShoulder)
            {
                return false;
            }

            // MediaPipe の world（ToUnity 後）は腰付近が原点。両肩の中点が首付近
            Vector3 neck = (frame.Left.Shoulder + frame.Right.Shoulder) * 0.5f;
            if (neck.sqrMagnitude < MinSpineLength * MinSpineLength)
            {
                return false;
            }

            // 今の背骨をワールド上向きへ（カメラの俯角・ロールで倒れている分を戻す）
            correction = Quaternion.FromToRotation(neck.normalized, Vector3.up);
            return true;
        }
    }
}
