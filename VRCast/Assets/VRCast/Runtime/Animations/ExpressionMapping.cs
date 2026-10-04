using System.Collections.Generic;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.Animations
{
    /// <summary>
    /// 検出した表情（笑顔・驚き・怒り・悲しみ）を、アバターの表情プリセットへ対応付ける。
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
                default:
                    return null;
            }
        }
    }
}
