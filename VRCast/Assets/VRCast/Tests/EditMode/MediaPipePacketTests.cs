using System;
using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// MediaPipe トラッカーの JSON 解析（座標変換・BlendShape の割り当て・部分的な欠けの扱い）を検証する。
    /// </summary>
    public class MediaPipePacketTests
    {
        [Test]
        public void TryParse_Face_ReadsPositionEyesAndMouth()
        {
            // 単位回転・平行移動 (1, 2, 3) cm、映像上の右目（本人の左目）を閉じ、口を最大まで開いた顔
            MediaPipePacket.Message message = CreateMessage();
            message.matrix = RowMajor(Matrix4x4.Translate(new Vector3(1f, 2f, 3f)));
            SetScore(message, "eyeBlinkRight", 1f);
            SetScore(message, "jawOpen", 0.6f);

            // 位置は z 反転・dm 単位、目は本人の左だけ閉じ、口は 1 になること
            Assert.That(Parse(message, out bool hasFace, out FaceTrackingFrame face, out _), Is.True);
            Assert.That(hasFace, Is.True);
            Assert.That(Quaternion.Angle(face.HeadRotation, Quaternion.identity), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(face.HeadPosition, new Vector3(0.1f, 0.2f, -0.3f)), Is.LessThan(1e-5f));
            Assert.That(face.EyeOpenLeft, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(face.EyeOpenRight, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(face.MouthOpen, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void TryParse_Face_ConvertsRotationAxes()
        {
            // MediaPipe の回転 (x, y, z, w) は Unity の (x, -y, -z, w) になること
            Quaternion source = Quaternion.Normalize(new Quaternion(0.1f, 0.2f, 0.3f, 0.927f));
            MediaPipePacket.Message message = CreateMessage();
            message.matrix = RowMajor(Matrix4x4.Rotate(source));

            Assert.That(Parse(message, out _, out FaceTrackingFrame face, out _), Is.True);
            Quaternion expected = new Quaternion(source.x, -source.y, -source.z, source.w);
            Assert.That(Quaternion.Angle(face.HeadRotation, expected), Is.LessThan(0.01f));
        }

        [Test]
        public void TryParse_Face_GazeFromLookShapes()
        {
            // 本人の右を見る（左目の内寄せ・右目の外寄せ）・半分上を見る
            MediaPipePacket.Message message = CreateMessage();
            SetScore(message, "eyeLookInLeft", 1f);
            SetScore(message, "eyeLookOutRight", 1f);
            SetScore(message, "eyeLookUpLeft", 0.5f);
            SetScore(message, "eyeLookUpRight", 0.5f);

            // 右 30°・上 15° になること
            Assert.That(Parse(message, out _, out FaceTrackingFrame face, out _), Is.True);
            Assert.That(face.HasGaze, Is.True);
            Assert.That(face.Gaze.x, Is.EqualTo(30f).Within(1e-4f));
            Assert.That(face.Gaze.y, Is.EqualTo(15f).Within(1e-4f));
        }

        [Test]
        public void TryParse_BrokenFace_KeepsPacketWithoutFace()
        {
            // 行列の要素数が足りない顔は「顔なし」で、パケット自体は使えること
            MediaPipePacket.Message message = CreateMessage();
            message.matrix = new float[3];

            Assert.That(Parse(message, out bool hasFace, out _, out _), Is.True);
            Assert.That(hasFace, Is.False);
        }

        [Test]
        public void TryParse_Arms_FlipsXYAndChecksVisibility()
        {
            // 左腕は全点が見え、右腕は手首が見えていない
            MediaPipePacket.Message message = CreateMessage();
            message.pose = true;
            message.arms = new float[MediaPipePacket.ArmPointCount * 3];
            message.visibility = new[] { 1f, 1f, 1f, 1f, 1f, 0.1f };
            WritePoint(message.arms, MediaPipePacket.LeftElbow, new Vector3(0.1f, 0.2f, 0.3f));

            // 左腕だけ使え、x は映像の右向き・y は上向きに反転していること
            Assert.That(Parse(message, out _, out _, out BodyTrackingFrame body), Is.True);
            Assert.That(body.Left.HasArm, Is.True);
            Assert.That(body.Right.HasArm, Is.False);
            Assert.That(Vector3.Distance(body.Left.Elbow, new Vector3(-0.1f, -0.2f, 0.3f)), Is.LessThan(1e-5f));
        }

        [Test]
        public void TryParse_Hands_ReadsOnlyVisibleHand()
        {
            // 左手だけ映っている（右手は空配列）
            MediaPipePacket.Message message = CreateMessage();
            message.leftHand = new float[MediaPipePacket.HandPointCount * 3];
            message.rightHand = new float[0];
            WritePoint(message.leftHand, 20, new Vector3(0.01f, 0.02f, 0.03f));

            // 左手は 21 点（x・y 反転）、右手は無しになること
            Assert.That(Parse(message, out _, out _, out BodyTrackingFrame body), Is.True);
            Assert.That(body.Left.HasHand, Is.True);
            Assert.That(body.Left.Hand.Length, Is.EqualTo(MediaPipePacket.HandPointCount));
            Assert.That(Vector3.Distance(body.Left.Hand[20], new Vector3(-0.01f, -0.02f, 0.03f)), Is.LessThan(1e-5f));
            Assert.That(body.Right.HasHand, Is.False);
        }

        [Test]
        public void TryParse_WrongVersionOrInvalidJson_ReturnsFalse()
        {
            // 別バージョンの送信側は拒否すること
            MediaPipePacket.Message message = CreateMessage();
            message.v = MediaPipePacket.ProtocolVersion + 1;
            Assert.That(Parse(message, out _, out _, out _), Is.False);

            // JSON として読めないものは拒否すること
            Assert.That(MediaPipePacket.TryParse("{ not json", out _, out _, out _), Is.False);
            Assert.That(MediaPipePacket.TryParse(string.Empty, out _, out _, out _), Is.False);
        }

        private static MediaPipePacket.Message CreateMessage()
        {
            // 単位行列・全スコア 0 の顔だけを含むメッセージ
            return new MediaPipePacket.Message
            {
                v = MediaPipePacket.ProtocolVersion,
                face = true,
                matrix = RowMajor(Matrix4x4.identity),
                blendshapes = new float[MediaPipePacket.BlendShapeNames.Length],
            };
        }

        private static bool Parse(
            MediaPipePacket.Message message, out bool hasFace, out FaceTrackingFrame face, out BodyTrackingFrame body)
        {
            // 送信側と同じく JSON（UTF-8 バイト列）にしてから解析する
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));
            return MediaPipePacket.TryParse(bytes, bytes.Length, out hasFace, out face, out body);
        }

        private static void SetScore(MediaPipePacket.Message message, string name, float value)
        {
            // 名前の位置にスコアを書き込む
            message.blendshapes[Array.IndexOf(MediaPipePacket.BlendShapeNames, name)] = value;
        }

        private static float[] RowMajor(Matrix4x4 matrix)
        {
            // 送信側（numpy の flatten）と同じ行優先の 16 要素
            var values = new float[16];
            for (int i = 0; i < 16; i++)
            {
                values[i] = matrix[i / 4, i % 4];
            }

            return values;
        }

        private static void WritePoint(float[] values, int point, Vector3 value)
        {
            // 1 点は x, y, z の 3 要素
            values[point * 3] = value.x;
            values[point * 3 + 1] = value.y;
            values[point * 3 + 2] = value.z;
        }
    }
}
