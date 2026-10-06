using NUnit.Framework;
using VRCast.Animations;
using VRCast.Core;
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
        public void Guess_FindsWinkSquintAndPout()
        {
            // ウインク・ジト目・ふくれっ面のプリセット名を推定できること
            string[] names = { "ウインク", "ジト目", "ぷくー" };
            Assert.That(ExpressionMapping.Guess(names, FaceExpression.Wink), Is.EqualTo(0));
            Assert.That(ExpressionMapping.Guess(names, FaceExpression.Squint), Is.EqualTo(1));
            Assert.That(ExpressionMapping.Guess(names, FaceExpression.Pout), Is.EqualTo(2));
        }

        [Test]
        public void Candidates_OnlyExpressionsWithPreset()
        {
            // 自動で見つかる表情だけが選べること（Names にジト目・ふくれっ面のプリセットは無い）
            var settings = new AppSettings();
            int mask = ExpressionMapping.Candidates(Names, settings);
            Assert.That(FaceExpressions.Contains(mask, FaceExpression.Smile), Is.True);
            Assert.That(FaceExpressions.Contains(mask, FaceExpression.Wink), Is.True);
            Assert.That(FaceExpressions.Contains(mask, FaceExpression.Squint), Is.False);
            Assert.That(FaceExpressions.Contains(mask, FaceExpression.Pout), Is.False);

            // 「割り当てなし」にした表情は選べなくなること
            settings.expressionSmile = ExpressionMapping.None;
            mask = ExpressionMapping.Candidates(Names, settings);
            Assert.That(FaceExpressions.Contains(mask, FaceExpression.Smile), Is.False);
        }

        [Test]
        public void GetThreshold_UnsetUsesCommonValue()
        {
            // 未設定の表情は共通のしきい値（旧版の設定）を使い、設定した表情はその値を使うこと
            var settings = new AppSettings { trackingExpressionThreshold = 0.45f };
            ExpressionMapping.SetThreshold(settings, FaceExpression.Wink, 0.2f);
            Assert.That(ExpressionMapping.GetThreshold(settings, FaceExpression.Smile), Is.EqualTo(0.45f));
            Assert.That(ExpressionMapping.GetThreshold(settings, FaceExpression.Wink), Is.EqualTo(0.2f));
        }

        [Test]
        public void SanitizeThreshold_KeepsUnsetAndClamps()
        {
            // 負・NaN は未設定、設定済みは範囲内に制限すること
            Assert.That(AppSettings.SanitizeThreshold(-5f), Is.EqualTo(AppSettings.UseCommonThreshold));
            Assert.That(AppSettings.SanitizeThreshold(float.NaN), Is.EqualTo(AppSettings.UseCommonThreshold));
            Assert.That(AppSettings.SanitizeThreshold(2f), Is.EqualTo(AppSettings.MaxExpressionThreshold));
            Assert.That(AppSettings.SanitizeThreshold(0.5f), Is.EqualTo(0.5f));
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
