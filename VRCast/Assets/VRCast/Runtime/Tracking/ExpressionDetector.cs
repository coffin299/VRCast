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
        // 表情に入る・表情を抜けるしきい値（感度 1 のとき）
        private const float EnterThreshold = 0.45f;
        private const float ExitThreshold = 0.3f;

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
        /// <param name="sensitivity">感度（AppSettings の範囲内。大きいほど弱い表情でも反応し、しきい値はこの値で割る）</param>
        /// <param name="deltaTime">前回からの経過秒数</param>
        public FaceExpression Update(ExpressionScores scores, float mouthOpen, float sensitivity, float deltaTime)
        {
            FaceExpression target = FindTarget(scores, mouthOpen, sensitivity);

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

        private FaceExpression FindTarget(ExpressionScores scores, float mouthOpen, float sensitivity)
        {
            // 感度は範囲内に収めてしきい値の倍率にする
            float scale = 1f / Clamp(
                sensitivity, AppSettings.MinExpressionSensitivity, AppSettings.MaxExpressionSensitivity);
            bool talking = mouthOpen >= TalkingMouthOpen;

            // しきい値を超えた中で最も強い表情（無ければニュートラル）
            FaceExpression best = FaceExpression.Neutral;
            float bestScore = 0f;
            for (var expression = FaceExpression.Smile; expression <= FaceExpression.Sad; expression++)
            {
                // 今の表情は抜けるしきい値、それ以外は入るしきい値で比べる
                float threshold = (expression == Current ? ExitThreshold : EnterThreshold) * scale;

                // 発話中は笑顔を出にくくする
                if (talking && expression == FaceExpression.Smile)
                {
                    threshold *= TalkingSmileFactor;
                }

                // 届いていて、これまでより強ければ採用
                float score = scores.Get(expression);
                if (score >= threshold && score > bestScore)
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
