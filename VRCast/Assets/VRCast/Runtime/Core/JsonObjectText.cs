using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VRCast.Core
{
    /// <summary>
    /// JSON オブジェクトの一番外側のキーと値（値は JSON のテキストのまま）を切り分け・結合する。
    /// JsonUtility は一部のキーだけを書き出せず、知らない形の JSON も扱えないため、値の変換は JsonUtility に任せてキー単位の振り分けだけをここで行う。
    /// </summary>
    public static class JsonObjectText
    {
        // 結合時の字下げ 1 段分
        private const string Indent = "    ";

        /// <summary>
        /// オブジェクトのテキストを、出てきた順のキーと値（JSON のテキスト）へ切り分ける。オブジェクトでない・壊れているなら false。
        /// </summary>
        public static bool TryParseObject(string json, out List<KeyValuePair<string, string>> members)
        {
            members = new List<KeyValuePair<string, string>>();
            // null は壊れたものとして扱う
            if (json == null)
            {
                return false;
            }

            int index = 0;
            SkipWhitespace(json, ref index);
            // 先頭は必ず {
            if (!Consume(json, ref index, '{'))
            {
                return false;
            }

            SkipWhitespace(json, ref index);
            // 空のオブジェクト
            if (Consume(json, ref index, '}'))
            {
                return IsOnlyWhitespaceFrom(json, index);
            }

            while (true)
            {
                // キー（文字列）と : を読む
                SkipWhitespace(json, ref index);
                if (!TryReadStringToken(json, ref index, out string key))
                {
                    return false;
                }

                SkipWhitespace(json, ref index);
                if (!Consume(json, ref index, ':'))
                {
                    return false;
                }

                // 値の範囲をそのまま切り出す
                SkipWhitespace(json, ref index);
                int start = index;
                if (!SkipValue(json, ref index))
                {
                    return false;
                }

                members.Add(new KeyValuePair<string, string>(key, json.Substring(start, index - start)));

                // 続きがあれば , 、終わりなら } （その後は空白だけ）
                SkipWhitespace(json, ref index);
                if (Consume(json, ref index, ','))
                {
                    continue;
                }

                return Consume(json, ref index, '}') && IsOnlyWhitespaceFrom(json, index);
            }
        }

        /// <summary>
        /// キーと値（JSON のテキスト）から、字下げ付きのオブジェクトのテキストを作る。depth は外側の字下げの段数。
        /// </summary>
        public static string Build(IEnumerable<KeyValuePair<string, string>> members, int depth = 0)
        {
            var builder = new StringBuilder();
            string outer = Repeat(Indent, depth);
            string inner = outer + Indent;
            bool first = true;
            builder.Append('{');
            foreach (KeyValuePair<string, string> member in members)
            {
                // 2 件目以降は , で区切る
                builder.Append(first ? "\n" : ",\n");
                first = false;
                builder.Append(inner).Append(Quote(member.Key)).Append(": ").Append(member.Value);
            }

            // 中身が無ければ {} の 1 行にする
            if (!first)
            {
                builder.Append('\n').Append(outer);
            }

            builder.Append('}');
            return builder.ToString();
        }

        /// <summary>
        /// 文字列を JSON の文字列リテラル（"..."）にする。
        /// </summary>
        public static string Quote(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        // そのほかの制御文字は \uXXXX にする
                        if (c < ' ')
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        /// <summary>
        /// 値のテキストが文字列リテラルなら中身を取り出す。文字列でなければ false。
        /// </summary>
        public static bool TryReadString(string valueText, out string value)
        {
            int index = 0;
            value = null;
            // 前後の空白を許し、文字列の後に余計なものがあれば不可
            SkipWhitespace(valueText, ref index);
            return TryReadStringToken(valueText, ref index, out value) && IsOnlyWhitespaceFrom(valueText, index);
        }

        private static bool TryReadStringToken(string json, ref int index, out string value)
        {
            value = null;
            if (!Consume(json, ref index, '"'))
            {
                return false;
            }

            var builder = new StringBuilder();
            while (index < json.Length)
            {
                char c = json[index++];
                // 閉じの " で終わり
                if (c == '"')
                {
                    value = builder.ToString();
                    return true;
                }

                // エスケープ以外はそのまま
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                // エスケープの次の文字が無ければ壊れている
                if (index >= json.Length)
                {
                    return false;
                }

                char escaped = json[index++];
                switch (escaped)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escaped);
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        // \uXXXX（16 進 4 桁）
                        if (index + 4 > json.Length || !int.TryParse(json.Substring(index, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out int code))
                        {
                            return false;
                        }

                        builder.Append((char)code);
                        index += 4;
                        break;
                    default:
                        return false;
                }
            }

            // 閉じの " が無い
            return false;
        }

        private static bool SkipValue(string json, ref int index)
        {
            // 値が無い
            if (index >= json.Length)
            {
                return false;
            }

            char c = json[index];
            // 文字列
            if (c == '"')
            {
                return TryReadStringToken(json, ref index, out _);
            }

            // オブジェクト・配列は対応する閉じ括弧まで（中の文字列の括弧は数えない）
            if (c == '{' || c == '[')
            {
                return SkipContainer(json, ref index);
            }

            // 数値・true・false・null は区切り（, } ] 空白）の手前まで
            int start = index;
            while (index < json.Length && json[index] != ',' && json[index] != '}' && json[index] != ']'
                && !char.IsWhiteSpace(json[index]))
            {
                index++;
            }

            return index > start;
        }

        private static bool SkipContainer(string json, ref int index)
        {
            // 開き括弧の数を数え、0 に戻ったところで終わる
            var closers = new Stack<char>();
            while (index < json.Length)
            {
                char c = json[index];
                if (c == '"')
                {
                    // 文字列の中の括弧は数えない
                    if (!TryReadStringToken(json, ref index, out _))
                    {
                        return false;
                    }

                    continue;
                }

                index++;
                if (c == '{' || c == '[')
                {
                    closers.Push(c == '{' ? '}' : ']');
                }
                else if (c == '}' || c == ']')
                {
                    // 種類の合わない閉じ括弧は壊れている
                    if (closers.Count == 0 || closers.Pop() != c)
                    {
                        return false;
                    }

                    if (closers.Count == 0)
                    {
                        return true;
                    }
                }
            }

            // 閉じ括弧が足りない
            return false;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }
        }

        private static bool Consume(string json, ref int index, char expected)
        {
            // 期待した文字なら 1 文字進める
            if (index < json.Length && json[index] == expected)
            {
                index++;
                return true;
            }

            return false;
        }

        private static bool IsOnlyWhitespaceFrom(string json, int index)
        {
            SkipWhitespace(json, ref index);
            return index == json.Length;
        }

        private static string Repeat(string text, int count)
        {
            var builder = new StringBuilder(text.Length * count);
            for (int i = 0; i < count; i++)
            {
                builder.Append(text);
            }

            return builder.ToString();
        }
    }
}
