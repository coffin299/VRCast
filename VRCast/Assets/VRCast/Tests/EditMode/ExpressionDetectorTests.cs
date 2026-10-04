using NUnit.Framework;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// 表情の判定（しきい値・保持時間・ヒステリシス・発話中の笑顔の抑制）を検証する。
    /// </summary>
    public class ExpressionDetectorTests
    {
        // 1 フレームの秒数（60 fps）
        private const float Frame = 1f / 60f;

        [Test]
        public void Update_StrongSmile_SwitchesAfterHoldTime()
        {
            var detector = new ExpressionDetector();
            var smile = new ExpressionScores { Smile = 0.8f };

            // 一瞬だけではニュートラルのまま
            Assert.That(detector.Update(smile, 0f, 1f, Frame), Is.EqualTo(FaceExpression.Neutral));

            // 保持時間（0.3 秒）続けば笑顔になること
            FaceExpression result = Run(detector, smile, 0f, 0.4f);
            Assert.That(result, Is.EqualTo(FaceExpression.Smile));
        }

        [Test]
        public void Update_WeakScores_StayNeutral()
        {
            // しきい値に届かない表情は反映しないこと
            var detector = new ExpressionDetector();
            var weak = new ExpressionScores { Smile = 0.2f, Surprise = 0.2f, Angry = 0.2f, Sad = 0.2f };
            Assert.That(Run(detector, weak, 0f, 1f), Is.EqualTo(FaceExpression.Neutral));
        }

        [Test]
        public void Update_Hysteresis_KeepsExpressionUntilBelowExitThreshold()
        {
            // 笑顔に入った後
            var detector = new ExpressionDetector();
            Run(detector, new ExpressionScores { Smile = 0.8f }, 0f, 0.5f);

            // 入るしきい値より下でも、抜けるしきい値以上なら笑顔のまま
            Assert.That(Run(detector, new ExpressionScores { Smile = 0.35f }, 0f, 1f), Is.EqualTo(FaceExpression.Smile));

            // 抜けるしきい値を下回ればニュートラルへ戻ること
            Assert.That(Run(detector, new ExpressionScores { Smile = 0.1f }, 0f, 1f), Is.EqualTo(FaceExpression.Neutral));
        }

        [Test]
        public void Update_PicksStrongestExpression()
        {
            // 複数の表情が届いたら最も強いものを選ぶこと
            var detector = new ExpressionDetector();
            var scores = new ExpressionScores { Smile = 0.5f, Surprise = 0.9f };
            Assert.That(Run(detector, scores, 0f, 0.5f), Is.EqualTo(FaceExpression.Surprise));
        }

        [Test]
        public void Update_Talking_RaisesSmileThreshold()
        {
            // 口を開けている間は、同じ強さの笑顔でも反映しないこと
            var detector = new ExpressionDetector();
            var smile = new ExpressionScores { Smile = 0.5f };
            Assert.That(Run(detector, smile, 0.8f, 1f), Is.EqualTo(FaceExpression.Neutral));

            // 口を閉じれば反映すること
            Assert.That(Run(detector, smile, 0f, 1f), Is.EqualTo(FaceExpression.Smile));
        }

        [Test]
        public void Update_HigherSensitivity_ReactsToWeakerExpression()
        {
            // 感度 1 では届かない強さでも、感度 2 なら反映すること
            var scores = new ExpressionScores { Sad = 0.3f };
            Assert.That(Run(new ExpressionDetector(), scores, 0f, 1f, 1f), Is.EqualTo(FaceExpression.Neutral));
            Assert.That(Run(new ExpressionDetector(), scores, 0f, 1f, 2f), Is.EqualTo(FaceExpression.Sad));
        }

        [Test]
        public void Reset_ReturnsToNeutral()
        {
            // 判定中の表情を捨ててニュートラルに戻ること
            var detector = new ExpressionDetector();
            Run(detector, new ExpressionScores { Angry = 0.9f }, 0f, 0.5f);
            detector.Reset();
            Assert.That(detector.Current, Is.EqualTo(FaceExpression.Neutral));
        }

        private static FaceExpression Run(
            ExpressionDetector detector, ExpressionScores scores, float mouthOpen, float seconds, float sensitivity = 1f)
        {
            // 指定秒数ぶん同じ値でフレームを進める
            FaceExpression result = detector.Current;
            for (float elapsed = 0f; elapsed < seconds; elapsed += Frame)
            {
                result = detector.Update(scores, mouthOpen, sensitivity, Frame);
            }

            return result;
        }
    }
}
