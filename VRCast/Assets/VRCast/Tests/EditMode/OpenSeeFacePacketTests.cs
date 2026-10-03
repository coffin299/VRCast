using System;
using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// OpenSeeFace パケット解析の値の位置・座標変換・不正入力の拒否を検証する。
    /// </summary>
    public class OpenSeeFacePacketTests
    {
        // 解析対象の値の先頭位置（OpenSeeFacePacket と同じ配置）
        private const int RightEyeOffset = 20;
        private const int LeftEyeOffset = 24;
        private const int QuaternionOffset = 33;
        private const int TranslationOffset = 61;
        private const int Got3DOffset = 28;
        private const int Points3DOffset = 889;
        private const int MouthOpenOffset = 1729 + 12 * 4;

        [Test]
        public void TryParse_ValidPacket_ReadsValues()
        {
            // 単位四元数・目の開き・口の特徴量を書き込んだパケット
            byte[] packet = CreatePacket(Quaternion.identity, 0.9f, 0.3f, 0.8f);

            // 解析できること
            Assert.That(OpenSeeFacePacket.TryParse(packet, 0, packet.Length, out FaceTrackingFrame frame), Is.True);

            // 目は左右そのまま、口は特徴量の上限で 1 になること
            Assert.That(frame.EyeOpenRight, Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(frame.EyeOpenLeft, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(frame.MouthOpen, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(Quaternion.Angle(frame.HeadRotation, Quaternion.identity), Is.LessThan(0.01f));
        }

        [Test]
        public void TryParse_ConvertsQuaternionAxes()
        {
            // OpenSeeFace の (x, y, z, w) は Unity の (-y, -x, z, w) になること
            var source = new Quaternion(0.1f, 0.2f, 0.3f, 0.927f);
            byte[] packet = CreatePacket(source, 1f, 1f, 0f);

            Assert.That(OpenSeeFacePacket.TryParse(packet, 0, packet.Length, out FaceTrackingFrame frame), Is.True);
            Quaternion expected = Quaternion.Normalize(new Quaternion(-0.2f, -0.1f, 0.3f, 0.927f));
            Assert.That(Quaternion.Angle(frame.HeadRotation, expected), Is.LessThan(0.01f));
        }

        [Test]
        public void TryParse_ConvertsTranslationAxes()
        {
            // OpenSeeFace の位置 (x, y, z) は Unity の (-y, x, -z) になること
            byte[] packet = CreatePacket(Quaternion.identity, 1f, 1f, 0f);
            WriteFloat(packet, TranslationOffset, 1f);
            WriteFloat(packet, TranslationOffset + 4, 2f);
            WriteFloat(packet, TranslationOffset + 8, 3f);

            Assert.That(OpenSeeFacePacket.TryParse(packet, 0, packet.Length, out FaceTrackingFrame frame), Is.True);
            Assert.That(frame.HeadPosition, Is.EqualTo(new Vector3(-2f, 1f, -3f)));
        }

        [Test]
        public void TryParse_Gaze_FromPupilAndEyeCenter()
        {
            // 両目とも瞳が眼球中心から (+x, 0, -z) 方向にある（3D 推定成功）
            byte[] packet = CreatePacket(Quaternion.identity, 1f, 1f, 0f);
            packet[Got3DOffset] = 1;
            WritePoint(packet, 66, new Vector3(1f, 0f, -1f));
            WritePoint(packet, 67, new Vector3(1f, 0f, -1f));

            // 視線方向 (x, y, -z) = (1, 0, 1) で左右 45°・上下 0° になること
            Assert.That(OpenSeeFacePacket.TryParse(packet, 0, packet.Length, out FaceTrackingFrame frame), Is.True);
            Assert.That(frame.HasGaze, Is.True);
            Assert.That(frame.Gaze.x, Is.EqualTo(45f).Within(0.01f));
            Assert.That(frame.Gaze.y, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void TryParse_No3DPoints_HasNoGaze()
        {
            // 3D 推定失敗のフレームは視線なし（他の値は使える）
            byte[] packet = CreatePacket(Quaternion.identity, 1f, 1f, 0f);
            WritePoint(packet, 66, new Vector3(1f, 0f, -1f));
            WritePoint(packet, 67, new Vector3(1f, 0f, -1f));

            Assert.That(OpenSeeFacePacket.TryParse(packet, 0, packet.Length, out FaceTrackingFrame frame), Is.True);
            Assert.That(frame.HasGaze, Is.False);
        }

        [Test]
        public void TryParse_ShortPacket_ReturnsFalse()
        {
            // 1 顔分に満たない長さは拒否すること
            byte[] packet = CreatePacket(Quaternion.identity, 1f, 1f, 0f);
            Assert.That(OpenSeeFacePacket.TryParse(packet, 0, OpenSeeFacePacket.FrameSize - 1, out _), Is.False);
        }

        [Test]
        public void TryParse_NonFiniteValue_ReturnsFalse()
        {
            // NaN を含むフレームは拒否すること
            byte[] packet = CreatePacket(Quaternion.identity, float.NaN, 1f, 0f);
            Assert.That(OpenSeeFacePacket.TryParse(packet, 0, packet.Length, out _), Is.False);
        }

        [Test]
        public void TryParse_ZeroQuaternion_ReturnsFalse()
        {
            // 長さ 0 の四元数は回転として使えないため拒否すること
            byte[] packet = CreatePacket(new Quaternion(0f, 0f, 0f, 0f), 1f, 1f, 0f);
            Assert.That(OpenSeeFacePacket.TryParse(packet, 0, packet.Length, out _), Is.False);
        }

        private static byte[] CreatePacket(Quaternion rotation, float rightEye, float leftEye, float mouth)
        {
            // 1 顔分のゼロ埋めパケットに対象の値だけ書き込む
            var packet = new byte[OpenSeeFacePacket.FrameSize];
            WriteFloat(packet, RightEyeOffset, rightEye);
            WriteFloat(packet, LeftEyeOffset, leftEye);
            WriteFloat(packet, QuaternionOffset, rotation.x);
            WriteFloat(packet, QuaternionOffset + 4, rotation.y);
            WriteFloat(packet, QuaternionOffset + 8, rotation.z);
            WriteFloat(packet, QuaternionOffset + 12, rotation.w);
            WriteFloat(packet, MouthOpenOffset, mouth);
            return packet;
        }

        private static void WritePoint(byte[] packet, int point, Vector3 value)
        {
            // 3D 点 1 つ（眼球中心 68・69 は 0 のまま）
            int offset = Points3DOffset + point * 12;
            WriteFloat(packet, offset, value.x);
            WriteFloat(packet, offset + 4, value.y);
            WriteFloat(packet, offset + 8, value.z);
        }

        private static void WriteFloat(byte[] packet, int offset, float value)
        {
            // リトルエンディアンで書き込む（テスト環境は Windows x64）
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, packet, offset, sizeof(float));
        }
    }
}
