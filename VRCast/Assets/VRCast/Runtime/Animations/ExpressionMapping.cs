using System.Collections.Generic;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.Animations
{
    /// <summary>
    /// 検出した表情（笑顔・驚き・怒り・悲しみ・ウインク・ジト目・ふくれっ面）を、アバターの表情プリセットへ対応付ける。
    /// 設定にはプリセット名を保存し（空欄 = 自動、None = 割り当てなし）、
    /// 名前が今のアバターに無いときはプリセット名のキーワードから推定する。
    /// </summary>
    public static class ExpressionMapping
    {
        /// <summary>
        /// 「割り当てなし」を表す保存値（プリセット名には使われない文字列）。
        /// </summary>
        public const string None = "<none>";

        // 表情ごとの推定キーワード（小文字で比較。日本語・韓国語・中国語（簡体字・繁体字）の表記も含める）
        private static readonly string[] SmileKeywords =
        {
            "smile", "joy", "happy", "laugh", "笑", "にこ", "ニコ", "喜", "웃", "开心", "開心",
        };

        private static readonly string[] SurpriseKeywords =
        {
            "surprise", "shock", "驚", "びっくり", "ビックリ", "おどろ", "놀", "惊", "吃惊",
        };

        private static readonly string[] AngryKeywords =
        {
            "angry", "anger", "怒", "おこ", "むっ", "ムッ", "화", "生气", "生氣",
        };

        private static readonly string[] SadKeywords =
        {
            "sad", "cry", "悲", "泣", "かなし", "슬", "울", "哭", "伤心", "傷心",
        };

        private static readonly string[] WinkKeywords =
        {
            "wink", "ウィンク", "ウインク", "うぃんく", "片目", "윙크", "眨单眼", "眨單眼", "单眼", "單眼",
        };

        private static readonly string[] SquintKeywords =
        {
            "jito", "squint", "half", "ジト", "じと", "半目", "細目", "ほそめ", "실눈", "眯", "半眼",
        };

        private static readonly string[] PoutKeywords =
        {
            "pout", "puff", "ぷく", "プク", "ふくれ", "フクレ", "むす", "ムス", "膨", "とが", "뾰로통", "嘟",
        };

        /// <summary>
        /// 表情に割り当てるプリセットの位置を返す（割り当てなし・見つからないときは -1）。
        /// </summary>
        /// <param name="names">表情プリセット名の一覧</param>
        /// <param name="expression">検出した表情</param>
        /// <param name="saved">設定に保存した名前（空欄 = 自動、None = 割り当てなし）</param>
        public static int Resolve(IReadOnlyList<string> names, FaceExpression expression, string saved)
        {
            // ニュートラルと「割り当てなし」はプリセットを使わない
            if (expression == FaceExpression.Neutral || saved == None)
            {
                return -1;
            }

            // 保存した名前が今のアバターにあればそれを使い、自動・見つからないときはキーワードで推定
            int found = IndexOf(names, saved);
            return found >= 0 ? found : Guess(names, expression);
        }

        /// <summary>
        /// 割り当て先のプリセットがある表情のビット列（FaceExpressions.Bit の組）を返す。
        /// </summary>
        public static int Candidates(IReadOnlyList<string> names, AppSettings settings)
        {
            // 表情ごとに割り当てを解決し、プリセットが見つかったものだけ立てる
            int mask = 0;
            for (var expression = FaceExpressions.First; expression <= FaceExpressions.Last; expression++)
            {
                if (Resolve(names, expression, GetSaved(settings, expression)) >= 0)
                {
                    mask |= FaceExpressions.Bit(expression);
                }
            }

            return mask;
        }

        /// <summary>
        /// 名前が一致するプリセットの位置を返す（空欄・見つからなければ -1）。
        /// </summary>
        public static int IndexOf(IReadOnlyList<string> names, string name)
        {
            // 空欄は「自動」なので一致させない
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }

            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] == name)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// プリセット名のキーワードから表情に合うプリセットを推定する（見つからなければ -1）。
        /// </summary>
        public static int Guess(IReadOnlyList<string> names, FaceExpression expression)
        {
            string[] keywords = KeywordsOf(expression);
            if (keywords == null)
            {
                return -1;
            }

            // 先頭から順に、キーワードを含む最初のプリセット
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i]?.ToLowerInvariant();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                foreach (string keyword in keywords)
                {
                    if (name.Contains(keyword))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// 表情に対応する設定値（プリセット名）を返す。
        /// </summary>
        public static string GetSaved(AppSettings settings, FaceExpression expression)
        {
            // 表情ごとの設定項目を返す（ニュートラルは割り当てなし）
            switch (expression)
            {
                case FaceExpression.Smile:
                    return settings.expressionSmile;
                case FaceExpression.Surprise:
                    return settings.expressionSurprise;
                case FaceExpression.Angry:
                    return settings.expressionAngry;
                case FaceExpression.Sad:
                    return settings.expressionSad;
                case FaceExpression.Wink:
                    return settings.expressionWink;
                case FaceExpression.Squint:
                    return settings.expressionSquint;
                case FaceExpression.Pout:
                    return settings.expressionPout;
                default:
                    return None;
            }
        }

        /// <summary>
        /// 表情に対応する設定値（プリセット名）を書き換える。
        /// </summary>
        public static void SetSaved(AppSettings settings, FaceExpression expression, string value)
        {
            // 表情ごとの設定項目へ書き込む（ニュートラルは保存しない）
            switch (expression)
            {
                case FaceExpression.Smile:
                    settings.expressionSmile = value;
                    break;
                case FaceExpression.Surprise:
                    settings.expressionSurprise = value;
                    break;
                case FaceExpression.Angry:
                    settings.expressionAngry = value;
                    break;
                case FaceExpression.Sad:
                    settings.expressionSad = value;
                    break;
                case FaceExpression.Wink:
                    settings.expressionWink = value;
                    break;
                case FaceExpression.Squint:
                    settings.expressionSquint = value;
                    break;
                case FaceExpression.Pout:
                    settings.expressionPout = value;
                    break;
            }
        }

        /// <summary>
        /// 表情のしきい値を返す（未設定なら共通のしきい値）。
        /// </summary>
        public static float GetThreshold(AppSettings settings, FaceExpression expression)
        {
            // 未設定（負）なら旧版から引き継いだ共通の値を使う
            float value = ThresholdField(settings, expression);
            return value >= 0f ? value : settings.trackingExpressionThreshold;
        }

        /// <summary>
        /// 表情のしきい値を書き換える（ニュートラルは保存しない）。
        /// </summary>
        public static void SetThreshold(AppSettings settings, FaceExpression expression, float value)
        {
            // 表情ごとの設定項目へ書き込む
            switch (expression)
            {
                case FaceExpression.Smile:
                    settings.thresholdSmile = value;
                    break;
                case FaceExpression.Surprise:
                    settings.thresholdSurprise = value;
                    break;
                case FaceExpression.Angry:
                    settings.thresholdAngry = value;
                    break;
                case FaceExpression.Sad:
                    settings.thresholdSad = value;
                    break;
                case FaceExpression.Wink:
                    settings.thresholdWink = value;
                    break;
                case FaceExpression.Squint:
                    settings.thresholdSquint = value;
                    break;
                case FaceExpression.Pout:
                    settings.thresholdPout = value;
                    break;
            }
        }

        private static float ThresholdField(AppSettings settings, FaceExpression expression)
        {
            // 表情ごとの設定項目を返す（ニュートラルは未設定扱い）
            switch (expression)
            {
                case FaceExpression.Smile:
                    return settings.thresholdSmile;
                case FaceExpression.Surprise:
                    return settings.thresholdSurprise;
                case FaceExpression.Angry:
                    return settings.thresholdAngry;
                case FaceExpression.Sad:
                    return settings.thresholdSad;
                case FaceExpression.Wink:
                    return settings.thresholdWink;
                case FaceExpression.Squint:
                    return settings.thresholdSquint;
                case FaceExpression.Pout:
                    return settings.thresholdPout;
                default:
                    return AppSettings.UseCommonThreshold;
            }
        }

        private static string[] KeywordsOf(FaceExpression expression)
        {
            // 表情ごとのキーワード一覧（ニュートラルは無し）
            switch (expression)
            {
                case FaceExpression.Smile:
                    return SmileKeywords;
                case FaceExpression.Surprise:
                    return SurpriseKeywords;
                case FaceExpression.Angry:
                    return AngryKeywords;
                case FaceExpression.Sad:
                    return SadKeywords;
                case FaceExpression.Wink:
                    return WinkKeywords;
                case FaceExpression.Squint:
                    return SquintKeywords;
                case FaceExpression.Pout:
                    return PoutKeywords;
                default:
                    return null;
            }
        }
    }
}
