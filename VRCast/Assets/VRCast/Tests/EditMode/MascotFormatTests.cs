using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using VRCast.Animations;
using VRCast.AvatarFormat;
using VRCast.Mascot;

namespace VRCast.Tests
{
    /// <summary>
    /// マスコットの言語モデル・読み上げ・立ちポーズの、通信しない部分を検証する。
    /// </summary>
    public class MascotFormatTests
    {
        [Test]
        public void Reply_ReadsTrimmedContent()
        {
            const string json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"  hello  \"}}]}";
            Assert.That(LocalLlm.TryParseReply(json, out string text), Is.True);
            Assert.That(text, Is.EqualTo("hello"));
        }

        [Test]
        public void Models_ReadsDistinctIds()
        {
            const string json = "{\"data\":[{\"id\":\"a\"},{\"id\":\"\"},{\"id\":\"a\"},{\"id\":\"b\"}]}";
            var ids = new List<string>();
            Assert.That(LocalLlm.TryParseModels(json, ids), Is.True);
            Assert.That(ids, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void ChatBody_PutsSystemFirst()
        {
            string body = LocalLlm.BuildChatBody("model", "be nice", new[] { new LlmMessage("user", "hi") });
            Assert.That(body, Does.Contain("\"role\":\"system\""));
            Assert.That(body, Does.Contain("be nice"));
            Assert.That(body.IndexOf("system"), Is.LessThan(body.IndexOf("\"role\":\"user\"")));
        }

        [Test]
        public void Speakers_ReadsStyleId()
        {
            const string json = "[{\"name\":\"A\",\"styles\":[{\"name\":\"れいせい\",\"id\":3}]}]";
            var speakers = new List<TtsSpeaker>();
            Assert.That(LocalTts.TryParseSpeakers(json, speakers), Is.True);
            Assert.That(speakers[0].Id, Is.EqualTo(3));
            Assert.That(speakers[0].Name, Does.Contain("A"));
        }

        [Test]
        public void Sentences_SplitOnPunctuation()
        {
            List<string> sentences = LocalTts.SplitSentences("こんにちは。元気ですか？");
            Assert.That(sentences, Is.EqualTo(new[] { "こんにちは。", "元気ですか？" }));
        }

        [Test]
        public void Wav_Decodes16BitSilence()
        {
            byte[] wav = Pcm16(24000, 1, new short[] { 0, 16384 });
            Assert.That(LocalTts.TryDecodeWav(wav, out int rate, out int channels, out float[] samples), Is.True);
            Assert.That(rate, Is.EqualTo(24000));
            Assert.That(channels, Is.EqualTo(1));
            Assert.That(samples[0], Is.EqualTo(0f).Within(0.0001f));
            Assert.That(samples[1], Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void KnownEngineUrl_IsReplacedWhenSwitching()
        {
            Assert.That(LocalTts.IsKnownDefault(LocalTts.CoeiroinkUrl), Is.True);
            Assert.That(LocalTts.UrlFor(1), Is.EqualTo(LocalTts.VoicevoxUrl));
            Assert.That(LocalTts.IsKnownDefault("http://127.0.0.1:9"), Is.False);
        }

        [Test]
        public void StandNames_MatchIdleAndIgnoreWalk()
        {
            Assert.That(StandClipNames.LooksLikeStand("Standing Idle"), Is.True);
            Assert.That(StandClipNames.LooksLikeStand("待機"), Is.True);
            Assert.That(StandClipNames.LooksLikeStand("Locomotion"), Is.False);
        }

        [Test]
        public void StandPose_WeightShiftPeaksThenReturns()
        {
            StandPoseSample start = StandPose.Evaluate(0f, 1f);
            StandPoseSample peak = StandPose.Evaluate(StandPose.Period * 0.25f, 1f);
            StandPoseSample end = StandPose.Evaluate(StandPose.Period, 1f);
            Assert.That(start.HipRoll, Is.EqualTo(0f).Within(0.001f));
            Assert.That(peak.HipRoll, Is.EqualTo(StandPose.MaxHipRoll).Within(0.001f));
            Assert.That(peak.LeftKnee, Is.GreaterThan(0f));
            Assert.That(peak.RightKnee, Is.EqualTo(0f).Within(0.001f));
            Assert.That(end.HipRoll, Is.EqualTo(0f).Within(0.001f));
            Assert.That(Mathf.Abs(peak.HipRoll), Is.LessThanOrEqualTo(StandPose.MaxHipRoll));
            Assert.That(Mathf.Abs(peak.LeftKnee), Is.LessThanOrEqualTo(StandPose.MaxKnee));
        }

        private static byte[] Pcm16(int rate, int channels, short[] samples)
        {
            int data = samples.Length * 2;
            var wav = new byte[44 + data];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
            System.BitConverter.GetBytes(36 + data).CopyTo(wav, 4);
            Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
            Encoding.ASCII.GetBytes("fmt ").CopyTo(wav, 12);
            System.BitConverter.GetBytes(16).CopyTo(wav, 16);
            System.BitConverter.GetBytes((short)1).CopyTo(wav, 20);
            System.BitConverter.GetBytes((short)channels).CopyTo(wav, 22);
            System.BitConverter.GetBytes(rate).CopyTo(wav, 24);
            System.BitConverter.GetBytes(rate * channels * 2).CopyTo(wav, 28);
            System.BitConverter.GetBytes((short)(channels * 2)).CopyTo(wav, 32);
            System.BitConverter.GetBytes((short)16).CopyTo(wav, 34);
            Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
            System.BitConverter.GetBytes(data).CopyTo(wav, 40);
            for (int i = 0; i < samples.Length; i++)
            {
                System.BitConverter.GetBytes(samples[i]).CopyTo(wav, 44 + i * 2);
            }

            return wav;
        }

        [Test]
        public void Speech_TrimsPhraseAndDropsBlank()
        {
            Assert.That(MascotSpeechInput.Normalize("  こんにちは  "), Is.EqualTo("こんにちは"));
            Assert.That(MascotSpeechInput.Normalize("   "), Is.Null);
            Assert.That(MascotSpeechInput.Normalize(null), Is.Null);
        }
    }
}
