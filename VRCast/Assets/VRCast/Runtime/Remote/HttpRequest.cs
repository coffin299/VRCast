using System;

namespace VRCast.Remote
{
    /// <summary>
    /// 外部操作の HTTP リクエストのヘッダー部分（リクエスト行と、Web ページからの送信かの判定）。
    /// </summary>
    public readonly struct HttpRequest
    {
        public readonly string Method;
        public readonly string Path;
        public readonly string Query;

        /// <summary>
        /// ブラウザで開いた Web ページから送られたなら true（Origin、または別サイトを示す Sec-Fetch-Site がある）。
        /// 悪意のあるページに勝手に操作されないよう拒否する。アドレス欄に直接入力したもの・Stream Deck・curl は該当しない。
        /// </summary>
        public readonly bool FromWebPage;

        private HttpRequest(string method, string path, string query, bool fromWebPage)
        {
            Method = method;
            Path = path;
            Query = query;
            FromWebPage = fromWebPage;
        }

        /// <summary>
        /// ヘッダー部分（空行の手前まで）を読む。リクエスト行が壊れていれば false。
        /// </summary>
        public static bool TryParse(string head, out HttpRequest request)
        {
            request = default;
            if (string.IsNullOrEmpty(head))
            {
                return false;
            }

            // 1 行目は「メソッド 対象 HTTP/x.x」
            string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] parts = lines[0].Split(' ');
            if (parts.Length != 3 || !parts[2].StartsWith("HTTP/", StringComparison.Ordinal) || !parts[1].StartsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            // 対象をパスとクエリに分ける
            string target = parts[1];
            int question = target.IndexOf('?');
            string path = question >= 0 ? target.Substring(0, question) : target;
            string query = question >= 0 ? target.Substring(question + 1) : string.Empty;

            // Web ページからの送信を示すヘッダーを探す
            bool fromWebPage = false;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                string name = lines[i].Substring(0, colon).Trim();
                string value = lines[i].Substring(colon + 1).Trim();

                // Origin があればページからの送信、Sec-Fetch-Site は none（アドレス欄に直接入力）以外ならページからの送信
                if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase)
                    || (name.Equals("Sec-Fetch-Site", StringComparison.OrdinalIgnoreCase)
                        && !value.Equals("none", StringComparison.OrdinalIgnoreCase)))
                {
                    fromWebPage = true;
                }
            }

            request = new HttpRequest(parts[0].ToUpperInvariant(), path, query, fromWebPage);
            return true;
        }

        /// <summary>
        /// クエリから値を取り出す（URL エンコードを戻す。無ければ null）。
        /// </summary>
        public string QueryValue(string key)
        {
            // "a=1&b=2" を順に見る
            if (string.IsNullOrEmpty(Query))
            {
                return null;
            }

            foreach (string pair in Query.Split('&'))
            {
                int equals = pair.IndexOf('=');
                string name = Decode(equals >= 0 ? pair.Substring(0, equals) : pair);
                if (name == key)
                {
                    return equals >= 0 ? Decode(pair.Substring(equals + 1)) : string.Empty;
                }
            }

            return null;
        }

        private static string Decode(string text)
        {
            // "+" は空白、"%xx" は UTF-8 のバイトとして戻す（壊れた表記はそのまま）
            try
            {
                return Uri.UnescapeDataString(text.Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                return text;
            }
        }
    }
}
