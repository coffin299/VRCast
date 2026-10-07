using System;
using System.Collections.Generic;
using System.Text;

namespace VRCast.Remote
{
    /// <summary>
    /// OSC の最初の引数の種類。
    /// </summary>
    public enum OscValueKind
    {
        None,
        Int,
        Float,
        String,
        True,
        False,
    }

    /// <summary>
    /// 受信した 1 つの OSC メッセージ（アドレス、最初の引数、全引数のうちの float）。
    /// </summary>
    public struct OscMessage
    {
        public string Address;
        public OscValueKind Kind;
        public int Int;
        public float Float;
        public string String;

        // 位置に関係なく、すべての float 引数を順に並べたもの（無ければ null）。
        // VMC プロトコルの "/VMC/Ext/Bone/Pos (s, f×7)" のように、名前の後に値が続くメッセージで使う
        public float[] Floats;

        /// <summary>
        /// ボタンを離したときの値（0 / false）なら true。OSC アプリのボタンは押下で 1、離すと 0 を送るため、0 は無視する。
        /// </summary>
        public bool IsRelease => (Kind == OscValueKind.Int && Int == 0)
            || (Kind == OscValueKind.Float && Float == 0f)
            || Kind == OscValueKind.False;
    }

    /// <summary>
    /// OSC 1.0 のパケット（メッセージ・バンドル）を読む。読めない部分は捨てる。
    /// 外部操作（RemoteControl）と VMC プロトコルの受信（TrackingReceiver）で共用する。
    /// </summary>
    public static class OscPacket
    {
        // バンドルの入れ子の上限（壊れたパケットで深く潜らないように）
        private const int MaxDepth = 4;

        // バンドルの先頭（"#bundle\0"）とタイムタグの長さ
        private const string BundleTag = "#bundle";
        private const int BundleHeaderLength = 16;

        /// <summary>
        /// パケットを読み、含まれるメッセージを output に追加する。
        /// </summary>
        public static void Parse(byte[] data, int offset, int length, List<OscMessage> output)
        {
            // 範囲外を指していれば何もしない
            if (data == null || offset < 0 || length <= 0 || offset + length > data.Length)
            {
                return;
            }

            ParseElement(data, offset, offset + length, output, 0);
        }

        private static void ParseElement(byte[] data, int start, int end, List<OscMessage> output, int depth)
        {
            // バンドルなら中の要素（長さ + 本体）を順に読む
            if (IsBundle(data, start, end))
            {
                // 入れ子が深すぎれば読まない
                if (depth >= MaxDepth)
                {
                    return;
                }

                int position = start + BundleHeaderLength;
                while (position + 4 <= end)
                {
                    // 要素の長さが壊れていれば以降を捨てる
                    int size = ReadInt32(data, position);
                    if (size <= 0 || position + 4 + size > end)
                    {
                        return;
                    }

                    ParseElement(data, position + 4, position + 4 + size, output, depth + 1);
                    position += 4 + size;
                }

                return;
            }

            // メッセージとして読めたものだけ追加する
            if (TryParseMessage(data, start, end, out OscMessage message))
            {
                output.Add(message);
            }
        }

        private static bool IsBundle(byte[] data, int start, int end)
        {
            // "#bundle\0" とタイムタグが収まり、先頭が一致すること
            if (end - start < BundleHeaderLength)
            {
                return false;
            }

            for (int i = 0; i < BundleTag.Length; i++)
            {
                if (data[start + i] != BundleTag[i])
                {
                    return false;
                }
            }

            return data[start + BundleTag.Length] == 0;
        }

        private static bool TryParseMessage(byte[] data, int start, int end, out OscMessage message)
        {
            message = default;
            int position = start;

            // アドレスは "/" で始まる文字列
            if (!TryReadString(data, ref position, end, out string address) || !address.StartsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            message.Address = address;

            // 型タグが無い（古い送信元）・読めなければ引数なしとして扱う
            if (!TryReadString(data, ref position, end, out string tags) || tags.Length < 2 || tags[0] != ',')
            {
                return true;
            }

            // 最初の引数を読む（壊れていればメッセージごと捨てる）
            if (!TryReadFirstArgument(data, ref position, end, tags[1], ref message))
            {
                return false;
            }

            // float 引数を全部集める（2 つ目以降が壊れていれば、読めたところまでで止める）
            message.Floats = ReadFloats(data, position, end, tags, message);
            return true;
        }

        private static bool TryReadFirstArgument(byte[] data, ref int position, int end, char tag, ref OscMessage message)
        {
            switch (tag)
            {
                case 'i':
                    // 32bit 整数（ビッグエンディアン）
                    if (position + 4 > end)
                    {
                        return false;
                    }

                    message.Kind = OscValueKind.Int;
                    message.Int = ReadInt32(data, position);
                    position += 4;
                    return true;
                case 'f':
                    // 32bit 浮動小数（ビッグエンディアン）
                    if (position + 4 > end)
                    {
                        return false;
                    }

                    message.Kind = OscValueKind.Float;
                    message.Float = BitConverter.Int32BitsToSingle(ReadInt32(data, position));
                    position += 4;
                    return true;
                case 's':
                    // 文字列（UTF-8）
                    if (!TryReadString(data, ref position, end, out string text))
                    {
                        return false;
                    }

                    message.Kind = OscValueKind.String;
                    message.String = text;
                    return true;
                case 'T':
                    message.Kind = OscValueKind.True;
                    return true;
                case 'F':
                    message.Kind = OscValueKind.False;
                    return true;
                default:
                    // 未対応の型は引数なしとして扱う（以降の位置も分からないため、後続の引数は読まない）
                    position = end;
                    return true;
            }
        }

        private static float[] ReadFloats(byte[] data, int position, int end, string tags, OscMessage first)
        {
            // float 引数の数を数える（無ければ配列を作らない）
            int count = 0;
            foreach (char tag in tags)
            {
                if (tag == 'f')
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return null;
            }

            // 最初の引数が float ならそれを先頭に入れ、2 つ目以降を順に読む
            var floats = new float[count];
            int found = 0;
            if (first.Kind == OscValueKind.Float)
            {
                floats[found++] = first.Float;
            }

            for (int i = 2; i < tags.Length && found < count; i++)
            {
                // 型ごとの長さだけ進め、float だけ取り出す（未対応・壊れた引数で止める）
                if (!TrySkipOrReadFloat(data, ref position, end, tags[i], out bool isFloat, out float value))
                {
                    break;
                }

                if (isFloat)
                {
                    floats[found++] = value;
                }
            }

            // 読めた分だけに詰める
            if (found < count)
            {
                Array.Resize(ref floats, found);
            }

            return found > 0 ? floats : null;
        }

        private static bool TrySkipOrReadFloat(
            byte[] data, ref int position, int end, char tag, out bool isFloat, out float value)
        {
            isFloat = false;
            value = 0f;
            switch (tag)
            {
                case 'f':
                    // 32bit 浮動小数
                    if (position + 4 > end)
                    {
                        return false;
                    }

                    isFloat = true;
                    value = BitConverter.Int32BitsToSingle(ReadInt32(data, position));
                    position += 4;
                    return true;
                case 'i':
                case 'c':
                case 'r':
                case 'm':
                    // 4 バイトの値（整数・文字・色・MIDI）
                    position += 4;
                    return position <= end;
                case 'h':
                case 't':
                case 'd':
                    // 8 バイトの値（64bit 整数・タイムタグ・倍精度）
                    position += 8;
                    return position <= end;
                case 's':
                case 'S':
                    // 文字列は終端まで読み飛ばす
                    return TryReadString(data, ref position, end, out _);
                case 'b':
                    // バイナリ（長さ + 本体、4 バイト境界）
                    if (position + 4 > end)
                    {
                        return false;
                    }

                    int size = ReadInt32(data, position);
                    position += 4 + ((size + 3) & ~3);
                    return size >= 0 && position <= end;
                case 'T':
                case 'F':
                case 'N':
                case 'I':
                    // 値を持たない型
                    return true;
                default:
                    // 配列（[ ]）などの未対応の型
                    return false;
            }
        }

        private static bool TryReadString(byte[] data, ref int position, int end, out string text)
        {
            text = null;

            // 終端の 0 を探す（見つからなければ壊れている）
            int terminator = Array.IndexOf(data, (byte)0, position, end - position);
            if (terminator < 0)
            {
                return false;
            }

            // 文字列を読み、終端を含めて 4 バイト境界まで進める
            text = Encoding.UTF8.GetString(data, position, terminator - position);
            int padded = (terminator - position + 4) & ~3;
            position = Math.Min(end, position + padded);
            return true;
        }

        private static int ReadInt32(byte[] data, int position)
        {
            // ビッグエンディアンの 32bit 整数
            return (data[position] << 24) | (data[position + 1] << 16) | (data[position + 2] << 8) | data[position + 3];
        }
    }
}
