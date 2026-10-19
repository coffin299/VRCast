using System.Collections.Generic;
using NUnit.Framework;
using VRCast.AvatarFormat;
using VRCast.PerfectSync;

namespace VRCast.Tests
{
    /// <summary>
    /// 既存の BlendShape の名前から ARKit 名への割り当てを推定する処理を確認する。
    /// </summary>
    public class PerfectSyncAutoMapperTests
    {
        [Test]
        public void Normalize_StripsPrefixesAndSymbols()
        {
            // FBX の接頭辞・記号・大文字を除き、vrc. の接頭辞は残すこと
            Assert.That(PerfectSyncAutoMapper.Normalize("blendShape1.Blink_L"), Is.EqualTo("blinkl"));
            Assert.That(PerfectSyncAutoMapper.Normalize("vrc.v_aa"), Is.EqualTo("vrcvaa"));
            Assert.That(PerfectSyncAutoMapper.Normalize("まばたき"), Is.EqualTo("まばたき"));
            Assert.That(PerfectSyncAutoMapper.Normalize(null), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Map_AssignsVisemesBlinkAndSmile()
        {
            // VRChat の Viseme・左右別のまばたき・両側の笑顔を持つ顔
            var candidates = new List<PerfectSyncAutoMapper.Candidate>
            {
                new PerfectSyncAutoMapper.Candidate("Body", "vrc.v_aa"),
                new PerfectSyncAutoMapper.Candidate("Body", "vrc.v_ih"),
                new PerfectSyncAutoMapper.Candidate("Body", "Blink_L"),
                new PerfectSyncAutoMapper.Candidate("Body", "Blink_R"),
                new PerfectSyncAutoMapper.Candidate("Body", "Smile"),
            };
            List<PerfectSyncBinding> bindings = PerfectSyncAutoMapper.Map(candidates);

            // 口の「あ」は jawOpen、「い」は口の横への伸び（弱め）に割り当てること
            Assert.That(Find(bindings, "jawOpen")?.blendShape, Is.EqualTo("vrc.v_aa"));
            Assert.That(Find(bindings, "mouthStretchLeft")?.blendShape, Is.EqualTo("vrc.v_ih"));
            Assert.That(Find(bindings, "mouthStretchLeft").weight, Is.LessThan(1f));

            // 左右別のまばたきは左右それぞれへ、両側の笑顔は左右の両方へ割り当てること
            Assert.That(Find(bindings, "eyeBlinkLeft")?.blendShape, Is.EqualTo("Blink_L"));
            Assert.That(Find(bindings, "eyeBlinkRight")?.blendShape, Is.EqualTo("Blink_R"));
            Assert.That(Find(bindings, "mouthSmileLeft")?.blendShape, Is.EqualTo("Smile"));
            Assert.That(Find(bindings, "mouthSmileRight")?.blendShape, Is.EqualTo("Smile"));
            Assert.That(Find(bindings, "jawOpen").path, Is.EqualTo("Body"));
        }

        [Test]
        public void Map_AssignsJapaneseLookShapes()
        {
            // アッシュ等の両目まとめの視線用 BlendShape を eyeLook* へ割り当てること
            var candidates = new List<PerfectSyncAutoMapper.Candidate>
            {
                new PerfectSyncAutoMapper.Candidate("Body", "上を見る"),
                new PerfectSyncAutoMapper.Candidate("Body", "下を見る"),
                new PerfectSyncAutoMapper.Candidate("Body", "右を見る"),
                new PerfectSyncAutoMapper.Candidate("Body", "左を見る"),
            };
            List<PerfectSyncBinding> bindings = PerfectSyncAutoMapper.Map(candidates);

            Assert.That(Find(bindings, "eyeLookUpLeft")?.blendShape, Is.EqualTo("上を見る"));
            Assert.That(Find(bindings, "eyeLookUpRight")?.blendShape, Is.EqualTo("上を見る"));
            Assert.That(Find(bindings, "eyeLookDownLeft")?.blendShape, Is.EqualTo("下を見る"));
            Assert.That(Find(bindings, "eyeLookDownRight")?.blendShape, Is.EqualTo("下を見る"));
            Assert.That(Find(bindings, "eyeLookInLeft")?.blendShape, Is.EqualTo("右を見る"));
            Assert.That(Find(bindings, "eyeLookOutRight")?.blendShape, Is.EqualTo("右を見る"));
            Assert.That(Find(bindings, "eyeLookOutLeft")?.blendShape, Is.EqualTo("左を見る"));
            Assert.That(Find(bindings, "eyeLookInRight")?.blendShape, Is.EqualTo("左を見る"));
        }

        [Test]
        public void Map_AssignsTongueOut()
        {
            // 日本語名・ tongue 名を tongueOut へ割り当てること
            var candidates = new List<PerfectSyncAutoMapper.Candidate>
            {
                new PerfectSyncAutoMapper.Candidate("Face", "舌出し"),
            };
            List<PerfectSyncBinding> bindings = PerfectSyncAutoMapper.Map(candidates);
            Assert.That(Find(bindings, "tongueOut")?.blendShape, Is.EqualTo("舌出し"));
        }

        [Test]
        public void Map_PrefersEarlierCandidatesAndSkipsArKitNames()
        {
            // 同じ名前は先に並べた（顔メッシュの）候補を使い、ARKit 名そのものは割り当てに使わないこと
            var candidates = new List<PerfectSyncAutoMapper.Candidate>
            {
                new PerfectSyncAutoMapper.Candidate("Body", "blink"),
                new PerfectSyncAutoMapper.Candidate("Hair", "blink"),
                new PerfectSyncAutoMapper.Candidate("Body", "jawOpen"),
            };
            List<PerfectSyncBinding> bindings = PerfectSyncAutoMapper.Map(candidates);

            Assert.That(Find(bindings, "eyeBlinkLeft")?.path, Is.EqualTo("Body"));
            Assert.That(Find(bindings, "eyeBlinkRight")?.path, Is.EqualTo("Body"));
            Assert.That(Find(bindings, "jawOpen"), Is.Null);
        }

        private static PerfectSyncBinding Find(List<PerfectSyncBinding> bindings, string name)
        {
            return bindings.Find(b => b.name == name);
        }
    }
}
