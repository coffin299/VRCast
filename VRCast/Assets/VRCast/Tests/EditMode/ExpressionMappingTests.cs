using NUnit.Framework;
using VRCast.Animations;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// 検出した表情と表情プリセットの対応付け（保存した名前・キーワード推定・割り当てなし）を検証する。
    /// </summary>
    public class ExpressionMappingTests
    {
        // よくある表情プリセット名
        private static readonly string[] Names = { "Face_Joy", "Face_Angry", "びっくり", "かなしい", "Wink" };

        [Test]
        public void Guess_FindsPresetByKeyword()
        {
            // 英語・日本語のキーワードで推定できること（大文字小文字は区別しない）
            Assert.That(ExpressionMapping.Guess(Names, FaceExpression.Smile), Is.EqualTo(0));
            Assert.That(ExpressionMapping.Guess(Names, FaceExpression.Angry), Is.EqualTo(1));
            Assert.That(ExpressionMapping.Guess(Names, FaceExpression.Surprise), Is.EqualTo(2));
            Assert.That(ExpressionMapping.Guess(Names, FaceExpression.Sad), Is.EqualTo(3));
        }

        [Test]
        public void Guess_FindsChinesePresetNames()
        {
            // 中国語（簡体字・繁体字）のプリセット名でも推定できること
            string[] names = { "开心", "生氣", "吃惊", "傷心" };
            Assert.That(ExpressionMapping.Guess(names, FaceExpression.Smile), Is.EqualTo(0));
            Assert.That(ExpressionMapping.Guess(names, FaceExpression.Angry), Is.EqualTo(1));
            Assert.That(ExpressionMapping.Guess(names, FaceExpression.Surprise), Is.EqualTo(2));
            Assert.That(ExpressionMapping.Guess(names, FaceExpression.Sad), Is.EqualTo(3));
        }

        [Test]
        public void Guess_NoMatchOrNeutral_ReturnsMinusOne()
        {
            // 該当するプリセットが無い・ニュートラルは -1 になること
            string[] names = { "Wink", "Tongue" };
            Assert.That(ExpressionMapping.Guess(names, FaceExpression.Smile), Is.EqualTo(-1));
            Assert.That(ExpressionMapping.Guess(Names, FaceExpression.Neutral), Is.EqualTo(-1));
        }

        [Test]
        public void Resolve_SavedName_TakesPriorityOverGuess()
        {
            // 保存した名前があればキーワード推定より優先すること
            Assert.That(ExpressionMapping.Resolve(Names, FaceExpression.Smile, "Wink"), Is.EqualTo(4));
        }

        [Test]
        public void Resolve_EmptyOrMissingName_FallsBackToGuess()
        {
            // 空欄（自動）と、今のアバターに無い名前はキーワード推定になること
            Assert.That(ExpressionMapping.Resolve(Names, FaceExpression.Smile, string.Empty), Is.EqualTo(0));
            Assert.That(ExpressionMapping.Resolve(Names, FaceExpression.Smile, "OtherAvatarSmile"), Is.EqualTo(0));
        }

        [Test]
        public void Resolve_NoneOrNeutral_ReturnsMinusOne()
        {
            // 「割り当てなし」とニュートラルはプリセットを使わないこと
            Assert.That(ExpressionMapping.Resolve(Names, FaceExpression.Smile, ExpressionMapping.None), Is.EqualTo(-1));
            Assert.That(ExpressionMapping.Resolve(Names, FaceExpression.Neutral, string.Empty), Is.EqualTo(-1));
        }
    }
}
