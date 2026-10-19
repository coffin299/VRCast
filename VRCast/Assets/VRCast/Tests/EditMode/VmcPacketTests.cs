using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;
using static VRCast.Tests.OscTestData;

namespace VRCast.Tests
{
    /// <summary>
    /// VMC プロトコルの受信（値の保持・Apply での確定・左右の入れ替え・頭の向き・壊れたパケット）を検証する。
    /// </summary>
    public class VmcPacketTests
    {
        [Test]
        public void Read_BlendValuesThenApply_CompletesFrame()
        {
            var vmc = new VmcPacket();

            // 値だけのパケットではフレームは確定しないこと
            Assert.That(Read(vmc, Bundle(Blend("jawOpen", 0.7f), Blend("mouthSmileLeft", 0.5f)), out bool hasFrame, out _),
                Is.True);
            Assert.That(hasFrame, Is.False);

            // Apply で確定し、前のパケットの値も保持されていること
            Assert.That(Read(vmc, Message("/VMC/Ext/Blend/Apply", ","), out hasFrame, out FaceTrackingFrame frame), Is.True);
            Assert.That(hasFrame, Is.True);
            Assert.That(frame.MouthOpen, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(frame.BlendShapes, Is.Not.Null);
            Assert.That(frame.HasExpression, Is.True);
            Assert.That(frame.BlendShapeRange.BlinkClosed, Is.EqualTo(ArKitRange.IPhone.BlinkClosed));
            Assert.That(vmc.ArKitShapeCount, Is.EqualTo(2));
        }

        [Test]
        public void Read_KeepsSenderAvatarSides()
        {
            // 送信側アバター（鏡像済み）の左目を閉じ、左の口角を上げる（名前の揺れも受け付ける）
            var vmc = new VmcPacket();
            Read(vmc, Bundle(Blend("EyeBlinkLeft", 1f), Blend("mouthSmile_L", 0.6f), Apply()), out _,
                out FaceTrackingFrame frame);

            // 左右を入れ替えずに並び、鏡像 ON でアバターの同じ側へ書かれる位置（SourceIndex(i, true) = i）に入ること
            Assert.That(frame.BlendShapes[ArKitFace.EyeBlinkLeft], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(frame.BlendShapes[ArKitFace.IndexOf("mouthSmileLeft")], Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(frame.BlendShapes[ArKitFace.IndexOf("mouthSmileRight")], Is.EqualTo(0f).Within(1e-5f));
            Assert.That(frame.EyeOpenRight, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(frame.EyeOpenLeft, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Read_TongueOut_IsAccepted()
        {
            // tongueOut / tongue_out をパーフェクトシンク用の配列へ入れること
            var vmc = new VmcPacket();
            Read(vmc, Bundle(Blend("tongue_out", 0.9f), Apply()), out _, out FaceTrackingFrame frame);
            Assert.That(frame.BlendShapes[ArKitFace.IndexOf("tongueOut")], Is.EqualTo(0.9f).Within(1e-5f));
        }

        [Test]
        public void Read_HeadBone_SetsHeadRotation()
        {
            // Head ボーンの回転（鏡像済み）を左右反転して頭の向きにし、ほかのボーンは無視すること
            Quaternion turn = Quaternion.Euler(10f, 30f, 5f);
            var vmc = new VmcPacket();
            Read(vmc, Bundle(Bone("Neck", Quaternion.Euler(45f, 0f, 0f)), Bone("Head", turn), Apply()), out _,
                out FaceTrackingFrame frame);

            Assert.That(vmc.HasHead, Is.True);
            var mirrored = new Quaternion(turn.x, -turn.y, -turn.z, turn.w);
            Assert.That(Quaternion.Angle(frame.HeadRotation, mirrored), Is.LessThan(0.01f));
        }

        [Test]
        public void Read_Bones_KeepsFullBodyPoseAsSent()
        {
            // 全ボーンのローカル回転・位置を送信側の値のまま保持し、腰と両上腕がそろえば全身として扱うこと
            Quaternion arm = Quaternion.Euler(0f, 0f, 60f);
            var vmc = new VmcPacket();
            byte[] packet = Bundle(Bone("Hips", Quaternion.identity, new Vector3(0f, 0.9f, 0f)), Bone("LeftUpperArm", arm));
            Assert.That(vmc.Read(packet, packet.Length, out bool hasFrame, out _), Is.True);

            // ボーンだけのパケットでも姿勢は更新され、顔のフレームは確定しないこと
            Assert.That(hasFrame, Is.False);
            Assert.That(vmc.PoseChanged, Is.True);
            Assert.That(vmc.Pose.ReceivedCount, Is.EqualTo(2));
            Assert.That(vmc.Pose.HasBody, Is.False);
            Assert.That(vmc.Pose.Positions[(int)HumanBodyBones.Hips].y, Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(Quaternion.Angle(vmc.Pose.Rotations[(int)HumanBodyBones.LeftUpperArm], arm), Is.LessThan(0.01f));

            // 右上腕が届けば全身になること（同じボーンは数え直さない）
            Read(vmc, Bundle(Bone("RightUpperArm", Quaternion.identity), Bone("LeftUpperArm", arm)), out _, out _);
            Assert.That(vmc.Pose.HasBody, Is.True);
            Assert.That(vmc.Pose.ReceivedCount, Is.EqualTo(3));

            // 表情だけのパケットでは姿勢は更新されないこと
            Read(vmc, Bundle(Blend("jawOpen", 0.5f), Apply()), out _, out _);
            Assert.That(vmc.PoseChanged, Is.False);
        }

        [Test]
        public void Read_Root_SetsRootAndHipsPosition()
        {
            // ルートの位置・向き・拡大率を腰の位置へ反映すること
            var vmc = new VmcPacket();
            Read(vmc, Bundle(
                Message("/VMC/Ext/Root/Pos", ",sffffffffff", Str("root"), Float(1f), Float(0f), Float(2f),
                    Float(0f), Float(0f), Float(0f), Float(1f), Float(2f), Float(2f), Float(2f)),
                Bone("Hips", Quaternion.identity, new Vector3(0f, 0.5f, 0f))), out _, out _);

            Assert.That(vmc.Pose.HasRoot, Is.True);
            Assert.That(Vector3.Distance(vmc.Pose.HipsPosition, new Vector3(1f, 1f, 2f)), Is.LessThan(1e-5f));
        }

        [Test]
        public void Read_UnknownOrBrokenBone_IsIgnored()
        {
            // Humanoid でない名前・壊れた回転のボーンは使わないこと
            var vmc = new VmcPacket();
            Read(vmc, Bundle(Bone("Tail", Quaternion.identity), Bone("Spine", new Quaternion(0f, 0f, 0f, 0f))), out _, out _);

            Assert.That(vmc.Pose.ReceivedCount, Is.EqualTo(0));
            Assert.That(vmc.PoseChanged, Is.False);
        }

        [Test]
        public void Read_VrmNamesOnly_DrivesEyesAndMouth()
        {
            // ARKit 名が来ない送信元は VRM の表情名で目と口だけ動き、パーフェクトシンク・表情は使わないこと
            var vmc = new VmcPacket();
            Read(vmc, Bundle(Blend("Blink_L", 1f), Blend("A", 0.5f), Apply()), out _, out FaceTrackingFrame frame);

            // 送信側アバターの左目は ARKit の eyeBlinkLeft と同じ側（フレームでは右目）
            Assert.That(frame.EyeOpenRight, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(frame.EyeOpenLeft, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(frame.MouthOpen, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(frame.BlendShapes, Is.Null);
            Assert.That(frame.HasExpression, Is.False);
        }

        [Test]
        public void Read_Vrm1NamesAndOtherVowels_DriveEyesAndMouth()
        {
            // VRM 1.0 の名前も受け付け、「あ」以外の母音でも口が開くこと（いちばん大きい母音の値）
            var vmc = new VmcPacket();
            Read(vmc, Bundle(Blend("blinkRight", 1f), Blend("aa", 0.2f), Blend("oh", 0.6f), Apply()), out _,
                out FaceTrackingFrame frame);

            Assert.That(frame.EyeOpenRight, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(frame.EyeOpenLeft, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(frame.MouthOpen, Is.EqualTo(0.6f).Within(1e-5f));
        }

        [Test]
        public void Read_ArKitWithoutJawOrBlink_FallsBackToVrmNames()
        {
            // 一部の ARKit 名だけ届く送信元では、届かない顎・まばたきを VRM の表情名で補うこと（パーフェクトシンクの値にも入る）
            var vmc = new VmcPacket();
            Read(vmc, Bundle(Blend("mouthSmileLeft", 0.5f), Blend("Blink_L", 1f), Blend("A", 1f), Apply()), out _,
                out FaceTrackingFrame frame);

            Assert.That(frame.MouthOpen, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(frame.BlendShapes[ArKitFace.JawOpen], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(frame.BlendShapes[ArKitFace.EyeBlinkLeft], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(frame.EyeOpenRight, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(frame.EyeOpenLeft, Is.EqualTo(1f).Within(1e-5f));

            // ARKit の jawOpen が届けば、そちらを優先すること
            Read(vmc, Bundle(Blend("jawOpen", 0f), Apply()), out _, out frame);
            Assert.That(frame.MouthOpen, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void Read_NonVmcOrBrokenPacket_ReturnsFalse()
        {
            var vmc = new VmcPacket();

            // VMC 以外の OSC・OSC でないデータは不正として扱うこと
            Assert.That(Read(vmc, Message("/vrcast/neutral", ","), out _, out _), Is.False);
            Assert.That(Read(vmc, System.Text.Encoding.ASCII.GetBytes("{\"v\":1}"), out _, out _), Is.False);

            // 値の無い Blend/Val は無視し、Apply までは読めること
            Assert.That(Read(vmc, Bundle(Message("/VMC/Ext/Blend/Val", ",s", Str("jawOpen")), Apply()), out bool hasFrame,
                out _), Is.True);
            Assert.That(hasFrame, Is.True);
            Assert.That(vmc.ArKitShapeCount, Is.EqualTo(0));
        }

        [Test]
        public void Reset_ForgetsValues()
        {
            // 待ち受けのやり直しで、前に受け取った値を持ち越さないこと
            var vmc = new VmcPacket();
            Read(vmc, Bundle(Blend("jawOpen", 1f), Bone("Head", Quaternion.Euler(0f, 30f, 0f)), Apply()), out _, out _);
            vmc.Reset();
            Read(vmc, Apply(), out _, out FaceTrackingFrame frame);

            Assert.That(vmc.ArKitShapeCount, Is.EqualTo(0));
            Assert.That(vmc.HasHead, Is.False);
            Assert.That(vmc.Pose.ReceivedCount, Is.EqualTo(0));
            Assert.That(frame.MouthOpen, Is.EqualTo(0f));
            Assert.That(Quaternion.Angle(frame.HeadRotation, Quaternion.identity), Is.LessThan(0.01f));
        }

        [Test]
        public void Read_CollectsNewAddressesOnlyWhenAsked()
        {
            // 求めたときだけ、初めてのアドレスを名前付きで 1 回ずつ記録すること
            var vmc = new VmcPacket();
            byte[] packet = Bundle(Blend("jawOpen", 0.1f), Apply());
            vmc.Read(packet, packet.Length, out _, out _);
            Assert.That(vmc.NewAddresses, Is.Empty);

            vmc.Read(packet, packet.Length, out _, out _, true);
            Assert.That(vmc.NewAddresses, Is.EqualTo(new List<string> { "/VMC/Ext/Blend/Val jawOpen", "/VMC/Ext/Blend/Apply" }));
            vmc.Read(packet, packet.Length, out _, out _, true);
            Assert.That(vmc.NewAddresses, Is.Empty);
        }

        private static bool Read(VmcPacket vmc, byte[] packet, out bool hasFrame, out FaceTrackingFrame frame)
        {
            // パケット全体を読む
            return vmc.Read(packet, packet.Length, out hasFrame, out frame);
        }

        private static byte[] Blend(string name, float value)
        {
            // 表情の値（名前 + 値）
            return Message("/VMC/Ext/Blend/Val", ",sf", Str(name), Float(value));
        }

        private static byte[] Bone(string name, Quaternion rotation, Vector3 position = default)
        {
            // ボーンの位置（省略時は 0）と回転
            return Message("/VMC/Ext/Bone/Pos", ",sfffffff", Str(name), Float(position.x), Float(position.y),
                Float(position.z), Float(rotation.x), Float(rotation.y), Float(rotation.z), Float(rotation.w));
        }

        private static byte[] Apply()
        {
            // フレームの確定
            return Message("/VMC/Ext/Blend/Apply", ",");
        }
    }
}
