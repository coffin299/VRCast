using System;

namespace VRCast.Remote
{
    /// <summary>
    /// 外部操作のコマンドの書き方（OSC / HTTP タブの一覧に出す文字列）。RemoteCommand が読める形に合わせる。
    /// </summary>
    public static class RemoteCommandText
    {
        // OSC のアドレスに使えない文字（空白・制御文字・ASCII 以外も不可）
        private const string OscReservedCharacters = " #*,/?[]{}";

        /// <summary>
        /// 一覧の 1 行分の OSC の書き方（アドレスと、必要なら引数。引数が無ければ null）。
        /// </summary>
        public readonly struct OscText
        {
            public readonly string Address;
            public readonly string Argument;

            public OscText(string address, string argument)
            {
                Address = address;
                Argument = argument;
            }
        }

        /// <summary>
        /// 名前を OSC のアドレスにそのまま入れられるなら true（ASCII の記号以外の印字可能文字だけ）。
        /// </summary>
        public static bool IsOscAddressSafe(string name)
        {
            // 空・ASCII 以外・制御文字・予約文字を含めば不可
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            foreach (char c in name)
            {
                if (c <= ' ' || c > '~' || OscReservedCharacters.IndexOf(c) >= 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 表情を名前で選ぶ HTTP の URL（toggle なら固定中の同じ表情で自動検出へ戻す）。
        /// </summary>
        public static string HttpExpression(int port, string name, bool toggle)
        {
            // 日本語・記号を含む名前も送れるよう URL エンコードする
            return $"{HttpBase(port)}/expression?name={Uri.EscapeDataString(name)}{ToggleQuery(toggle, true)}";
        }

        /// <summary>
        /// ニュートラルを選ぶ HTTP の URL。
        /// </summary>
        public static string HttpNeutral(int port, bool toggle)
        {
            return $"{HttpBase(port)}/neutral{ToggleQuery(toggle, false)}";
        }

        /// <summary>
        /// 引数の要らない HTTP の URL（/auto・/status など）。
        /// </summary>
        public static string HttpPath(int port, string path)
        {
            return HttpBase(port) + path;
        }

        /// <summary>
        /// 表情を名前で選ぶ OSC の書き方（アドレスに入れられる名前ならアドレス末尾、それ以外は文字列の引数）。
        /// </summary>
        public static OscText OscExpression(string name, bool toggle)
        {
            // アドレス末尾に名前を付ける形が、ボタン 1 つで送れて扱いやすい
            string verb = toggle ? "/vrcast/toggle" : "/vrcast/expression";
            return IsOscAddressSafe(name) ? new OscText($"{verb}/{name}", null) : new OscText(verb, $"\"{name}\"");
        }

        /// <summary>
        /// ニュートラルを選ぶ OSC の書き方（toggle は番号 0 で送る）。
        /// </summary>
        public static OscText OscNeutral(bool toggle)
        {
            return toggle ? new OscText("/vrcast/toggle", "0") : new OscText("/vrcast/neutral", null);
        }

        /// <summary>
        /// 表示用の 1 行（引数があれば後ろに付ける）。
        /// </summary>
        public static string Describe(OscText osc)
        {
            return osc.Argument != null ? $"{osc.Address} {osc.Argument}" : osc.Address;
        }

        private static string HttpBase(int port)
        {
            // この PC からだけ接続できる待ち受けの先頭
            return $"http://127.0.0.1:{port}";
        }

        private static string ToggleQuery(bool toggle, bool hasQuery)
        {
            // 既にクエリがあれば & で、無ければ ? で付ける
            return toggle ? (hasQuery ? "&toggle=1" : "?toggle=1") : string.Empty;
        }
    }
}
