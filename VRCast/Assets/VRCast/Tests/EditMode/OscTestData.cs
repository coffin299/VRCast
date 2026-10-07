using System;
using System.Collections.Generic;
using System.Text;

namespace VRCast.Tests
{
    /// <summary>
    /// テスト用の OSC パケットの組み立て（外部操作・VMC プロトコルのテストで共用）。
    /// </summary>
    internal static class OscTestData
    {
        /// <summary>
        /// アドレス・型タグ・引数を並べた OSC メッセージ。
        /// </summary>
        public static byte[] Message(string address, string tags, params byte[][] arguments)
        {
            var bytes = new List<byte>();
            bytes.AddRange(Str(address));
            bytes.AddRange(Str(tags));
            foreach (byte[] argument in arguments)
            {
                bytes.AddRange(argument);
            }

            return bytes.ToArray();
        }

        /// <summary>
        /// 要素（メッセージ）を並べた OSC バンドル（タイムタグは 0）。
        /// </summary>
        public static byte[] Bundle(params byte[][] elements)
        {
            var bytes = new List<byte>();
            bytes.AddRange(Str("#bundle"));
            bytes.AddRange(new byte[8]);
            foreach (byte[] element in elements)
            {
                // 要素ごとに長さを前置する
                bytes.AddRange(Int(element.Length));
                bytes.AddRange(element);
            }

            return bytes.ToArray();
        }

        /// <summary>
        /// 終端の 0 を含めて 4 バイト境界まで埋めた OSC 文字列。
        /// </summary>
        public static byte[] Str(string text)
        {
            byte[] raw = Encoding.UTF8.GetBytes(text);
            var padded = new byte[(raw.Length + 4) & ~3];
            Array.Copy(raw, padded, raw.Length);
            return padded;
        }

        /// <summary>
        /// ビッグエンディアンの 32bit 整数。
        /// </summary>
        public static byte[] Int(int value)
        {
            return new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
        }

        /// <summary>
        /// ビッグエンディアンの 32bit 浮動小数。
        /// </summary>
        public static byte[] Float(float value)
        {
            return Int(BitConverter.SingleToInt32Bits(value));
        }
    }
}
