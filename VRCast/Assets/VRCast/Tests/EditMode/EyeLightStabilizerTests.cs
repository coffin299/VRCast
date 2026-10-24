using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// 視線で目を回しても、トゥーンのライト方向との角度が変わらないことを確認する。
    /// </summary>
    public class EyeLightStabilizerTests
    {
        [Test]
        public void Compensate_Identity_KeepsDirection()
        {
            // 正面では、シェーダーが足し合わせるベクトルが元のまま
            Vector3 custom = new Vector3(0.2f, -0.1f, 0.05f);
            Vector3 main = new Vector3(0.1f, 0.8f, 0.4f);
            Vector3 shifted = EyeLightStabilizer.Compensate(custom, Quaternion.identity, main);
            Assert.That(shifted.x, Is.EqualTo(custom.x).Within(1e-5f));
            Assert.That(shifted.y, Is.EqualTo(custom.y).Within(1e-5f));
            Assert.That(shifted.z, Is.EqualTo(custom.z).Within(1e-5f));
        }

        [Test]
        public void Compensate_Pitch_KeepsAngleBetweenNormalAndLight()
        {
            // 上を向く回転を法線とライトの両方に掛けると、内積（陰影）は回す前と同じ
            Vector3 custom = new Vector3(0.001f, 0.002f, 0.001f);
            Vector3 main = new Vector3(0.2f, 0.5f, 1f);
            Vector3 normal = Vector3.forward;
            Quaternion lookUp = Quaternion.Euler(-15f, 8f, 0f);
            Vector3 shifted = EyeLightStabilizer.Compensate(custom, lookUp, main);

            float before = Vector3.Dot(normal, (main + custom).normalized);
            float after = Vector3.Dot((lookUp * normal).normalized, (main + shifted).normalized);
            Assert.That(after, Is.EqualTo(before).Within(1e-5f));
        }
    }
}
