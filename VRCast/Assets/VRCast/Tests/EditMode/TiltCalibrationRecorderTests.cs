using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// カメラの傾き補正（背骨→上向き）を検証する。
    /// </summary>
    public class TiltCalibrationRecorderTests
    {
        [Test]
        public void TryCorrectionFromBody_TiltsSpineToUp()
        {
            // 背骨が前に 45° 倒れている（カメラ俯角）とき、補正後は上を向く
            var body = new BodyTrackingFrame
            {
                Left = new ArmTrackingData { HasShoulder = true, Shoulder = new Vector3(-0.2f, 0.5f, 0.5f) },
                Right = new ArmTrackingData { HasShoulder = true, Shoulder = new Vector3(0.2f, 0.5f, 0.5f) },
            };

            Assert.That(TiltCalibrationRecorder.TryCorrectionFromBody(body, out Quaternion correction), Is.True);
            Vector3 neck = (body.Left.Shoulder + body.Right.Shoulder) * 0.5f;
            Vector3 upright = correction * neck.normalized;
            Assert.That(Vector3.Angle(upright, Vector3.up), Is.LessThan(0.5f));
            Assert.That(Quaternion.Angle(correction, Quaternion.identity), Is.GreaterThan(1f));
        }

        [Test]
        public void TryCorrectionFromBody_NeedsBothShoulders()
        {
            var body = new BodyTrackingFrame
            {
                Left = new ArmTrackingData { HasShoulder = true, Shoulder = new Vector3(-0.2f, 0.7f, 0f) },
                Right = new ArmTrackingData { HasShoulder = false, Shoulder = new Vector3(0.2f, 0.7f, 0f) },
            };

            Assert.That(TiltCalibrationRecorder.TryCorrectionFromBody(body, out _), Is.False);
        }
    }
}
