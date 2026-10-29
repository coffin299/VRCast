using NUnit.Framework;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// 視線用 BlendShape の名前照合を確認する。
    /// </summary>
    public class GazeBlendShapesTests
    {
        [Test]
        public void Match_RecognizesJapaneseLookShapes()
        {
            Assert.That(GazeBlendShapes.Match("上を見る"), Is.EqualTo(0));
            Assert.That(GazeBlendShapes.Match("下を見る"), Is.EqualTo(1));
            Assert.That(GazeBlendShapes.Match("右を見る"), Is.EqualTo(2));
            Assert.That(GazeBlendShapes.Match("左を見る"), Is.EqualTo(3));
        }

        [Test]
        public void Match_RecognizesEnglishAliasesAndSkipsArKitPerEye()
        {
            Assert.That(GazeBlendShapes.Match("Look_Up"), Is.EqualTo(0));
            Assert.That(GazeBlendShapes.Match("blendShape1.LookRight"), Is.EqualTo(2));
            Assert.That(GazeBlendShapes.Match("eyeLookUpLeft"), Is.EqualTo(-1));
            Assert.That(GazeBlendShapes.Match("eyeLookInRight"), Is.EqualTo(-1));
            // Unified の両目まとめもパーフェクトシンク側（しきい値の対象）
            Assert.That(GazeBlendShapes.Match("EyeLookUp"), Is.EqualTo(-1));
            Assert.That(GazeBlendShapes.Match("EyeLookOut"), Is.EqualTo(-1));
            Assert.That(GazeBlendShapes.Match("blink"), Is.EqualTo(-1));
            Assert.That(GazeBlendShapes.Match(null), Is.EqualTo(-1));
        }
    }
}
