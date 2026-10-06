using System.Collections.Generic;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// 表情の強さから、今の表情（笑顔・驚き・怒り・悲しみ・ウインク・ジト目・ふくれっ面・ニュートラル）を 1 つに決める。
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

        // すべての表情に同じしきい値を使うときの作業用（FaceExpression の値で引く）
        private readonly float[] _uniformThresholds = new float[(int)FaceExpressions.Last + 1];

        /// <summary>
        /// 確定した今の表情。
        /// </summary>
        public FaceExpression Current { get; private set; } = FaceExpression.Neutral;

        /// <summary>
        /// すべての表情に同じしきい値を使って 1 フレーム分の判定を進める。
        /// </summary>
        public FaceExpression Update(ExpressionScores scores, float mouthOpen, float threshold, float deltaTime,
            int candidates = FaceExpressions.All)
        {
            // 同じ値を表情ごとの並びへ詰めて共通の判定へ渡す
            for (int i = 0; i < _uniformThresholds.Length; i++)
            {
                _uniformThresholds[i] = threshold;
            }

            return Update(scores, mouthOpen, _uniformThresholds, deltaTime, candidates);
        }

        /// <summary>
        /// 1 フレーム分の値で判定を進め、確定した表情を返す。
        /// </summary>
        /// <param name="scores">表情の強さ（0〜1）</param>
        /// <param name="mouthOpen">口の開き（0〜1）</param>
        /// <param name="thresholds">表情ごとの入るしきい値（FaceExpression の値で引く。AppSettings の範囲に収める）</param>
        /// <param name="deltaTime">前回からの経過秒数</param>
        /// <param name="candidates">選んでよい表情のビット列（FaceExpressions.Bit の組。割り当て先の無い表情を除く）</param>
        public FaceExpression Update(ExpressionScores scores, float mouthOpen, IReadOnlyList<float> thresholds,
            float deltaTime, int candidates = FaceExpressions.All)
        {
            FaceExpression target = FindTarget(scores, mouthOpen, thresholds, candidates);

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

        private FaceExpression FindTarget(
            ExpressionScores scores, float mouthOpen, IReadOnlyList<float> thresholds, int candidates)
        {
            bool talking = mouthOpen >= TalkingMouthOpen;

            // しきい値を超えた中で最も強い表情（無ければニュートラル）
            FaceExpression best = FaceExpression.Neutral;
            float bestScore = 0f;
            for (var expression = FaceExpressions.First; expression <= FaceExpressions.Last; expression++)
            {
                // 選べない表情（割り当て先が無い）は、ほかの表情を押しのけないよう比べない
                if (!FaceExpressions.Contains(candidates, expression))
                {
                    continue;
                }

                // その表情のしきい値を範囲内に収める（並びが足りなければ最も反応しにくい値）
                int index = (int)expression;
                float threshold = thresholds != null && index < thresholds.Count
                    ? thresholds[index]
                    : AppSettings.MaxExpressionThreshold;
                float enter = Clamp(threshold, AppSettings.MinExpressionThreshold, AppSettings.MaxExpressionThreshold);

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
