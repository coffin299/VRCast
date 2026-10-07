using System.Collections.Generic;
using NUnit.Framework;
using VRCast.Remote;
using static VRCast.Tests.OscTestData;

namespace VRCast.Tests
{
    /// <summary>
    /// OSC の複数引数の読み取り（VMC プロトコルの名前 + 値の形）を検証する。
    /// </summary>
    public class OscPacketTests
    {
        [Test]
        public void Parse_NameThenFloats_ReadsAllFloats()
        {
            // 文字列の後に float が 7 つ続くメッセージ（VMC の Bone/Pos と同じ形）
            byte[] packet = Message("/VMC/Ext/Bone/Pos", ",sfffffff", Str("Head"),
                Float(1f), Float(2f), Float(3f), Float(0.1f), Float(0.2f), Float(0.3f), Float(0.9f));

            // 最初の引数は文字列、float は 7 つ順に読めること
            OscMessage message = ParseSingle(packet);
            Assert.That(message.Kind, Is.EqualTo(OscValueKind.String));
            Assert.That(message.String, Is.EqualTo("Head"));
            Assert.That(message.Floats, Is.EqualTo(new[] { 1f, 2f, 3f, 0.1f, 0.2f, 0.3f, 0.9f }));
        }

        [Test]
        public void Parse_FirstFloat_IsAlsoInFloats()
        {
            // 最初の引数が float なら Float と Floats の先頭の両方に入ること（外部操作の互換）
            OscMessage message = ParseSingle(Message("/vrcast/neutral", ",f", Float(1f)));
            Assert.That(message.Kind, Is.EqualTo(OscValueKind.Float));
            Assert.That(message.Float, Is.EqualTo(1f));
            Assert.That(message.Floats, Is.EqualTo(new[] { 1f }));
        }

        [Test]
        public void Parse_SkipsOtherTypesBetweenFloats()
        {
            // 整数・文字列を挟んでも float だけを順に集めること
            byte[] packet = Message("/test", ",ifsf", Int(5), Float(0.5f), Str("x"), Float(0.25f));
            OscMessage message = ParseSingle(packet);
            Assert.That(message.Int, Is.EqualTo(5));
            Assert.That(message.Floats, Is.EqualTo(new[] { 0.5f, 0.25f }));
        }

        [Test]
        public void Parse_TruncatedLaterArgument_KeepsReadValues()
        {
            // 2 つ目の float が欠けたメッセージは、読めた分だけを持つこと（メッセージ自体は捨てない）
            byte[] packet = Message("/VMC/Ext/Blend/Val", ",sff", Str("eyeBlinkLeft"), Float(0.7f));
            OscMessage message = ParseSingle(packet);
            Assert.That(message.String, Is.EqualTo("eyeBlinkLeft"));
            Assert.That(message.Floats, Is.EqualTo(new[] { 0.7f }));
        }

        [Test]
        public void Parse_NoFloats_LeavesFloatsNull()
        {
            // float の無いメッセージは配列を作らないこと
            OscMessage message = ParseSingle(Message("/VMC/Ext/Blend/Apply", ","));
            Assert.That(message.Floats, Is.Null);
        }

        private static OscMessage ParseSingle(byte[] packet)
        {
            // 1 つだけメッセージが読めること
            var messages = new List<OscMessage>();
            OscPacket.Parse(packet, 0, packet.Length, messages);
            Assert.That(messages.Count, Is.EqualTo(1));
            return messages[0];
        }
    }
}
