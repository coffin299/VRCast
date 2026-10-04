using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// 表情の強さから、今の表情（笑顔・驚き・怒り・悲しみ・ニュートラル）を 1 つに決める。
    /// ちらつかないよう、入るときより抜けるときのしきい値を低くし（ヒステリシス）、
    /// 別の表情へは一定時間続いたときだけ切り替える。口を開けている間（発話中）は笑顔を出にくくする。
    /// </summary>
    public class ExpressionDetector
    {
        // 表情を抜けるしきい値（入るしきい値に対する倍率）
        private const float ExitRatio = 0.65f;

        // 別の表情へ切り替えるまでに続く必要がある秒数
        private const float HoldSeconds = 0.3f;

        // 口の開きがこれ以上なら発話中とみなし、笑顔のしきい値をこの倍率で上げる
        private const float TalkingMouthOpen = 0.35f;
        private const float TalkingSmileFactor = 1.5f;

        // 切替候補と、候補が続いている秒数
        private FaceExpression _candidate = FaceExpression.Neutral;
        private float _candidateSeconds;

        /// <summary>
        /// 確定した今の表情。
        /// </summary>
        public FaceExpression Current { get; private set; } = FaceExpression.Neutral;

        /// <summary>
        /// 1 フレーム分の値で判定を進め、確定した表情を返す。
        /// </summary>
        /// <param name="scores">表情の強さ（0〜1）</param>
        /// <param name="mouthOpen">口の開き（0〜1）</param>
        /// <param name="threshold">表情に入るしきい値（AppSettings の範囲内。表情の強さがこれ以上で切り替える）</param>
        /// <param name="deltaTime">前回からの経過秒数</param>
        public FaceExpression Update(ExpressionScores scores, float mouthOpen, float threshold, float deltaTime)
        {
            FaceExpression target = FindTarget(scores, mouthOpen, threshold);

            // 今の表情が続いているなら候補を捨てる
            if (target == Current)
            {
                _candidate = Current;
                _candidateSeconds = 0f;
                return Current;
            }

            // 新しい候補なら数え直す
            if (target != _candidate)
            {
                _candidate = target;
                _candidateSeconds = 0f;
            }

            // 候補が一定時間続いたら確定
            _candidateSeconds += deltaTime;
            if (_candidateSeconds >= HoldSeconds)
            {
                Current = target;
                _candidateSeconds = 0f;
            }

            return Current;
        }

        /// <summary>
        /// ニュートラルへ戻し、判定途中の候補も捨てる。
        /// </summary>
        public void Reset()
        {
            Current = FaceExpression.Neutral;
            _candidate = FaceExpression.Neutral;
            _candidateSeconds = 0f;
        }

        private FaceExpression FindTarget(ExpressionScores scores, float mouthOpen, float threshold)
        {
            // しきい値は範囲内に収める
            float enter = Clamp(threshold, AppSettings.MinExpressionThreshold, AppSettings.MaxExpressionThreshold);
            bool talking = mouthOpen >= TalkingMouthOpen;

            // しきい値を超えた中で最も強い表情（無ければニュートラル）
            FaceExpression best = FaceExpression.Neutral;
            float bestScore = 0f;
            for (var expression = FaceExpression.Smile; expression <= FaceExpression.Sad; expression++)
            {
                // 今の表情は抜けるしきい値、それ以外は入るしきい値で比べる
                float required = expression == Current ? enter * ExitRatio : enter;

                // 発話中は笑顔を出にくくする
                if (talking && expression == FaceExpression.Smile)
                {
                    required *= TalkingSmileFactor;
                }

                // 届いていて、これまでより強ければ採用
                float score = scores.Get(expression);
                if (score >= required && score > bestScore)
                {
                    best = expression;
                    bestScore = score;
                }
            }

            return best;
        }

        private static float Clamp(float value, float min, float max)
        {
            // 範囲外の値を端へ寄せる
            return value < min ? min : value > max ? max : value;
        }
    }
}
