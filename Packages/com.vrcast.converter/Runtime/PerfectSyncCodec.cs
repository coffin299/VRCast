using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// パーフェクトシンクの形状の差分と base64 文字列の変換、メッシュ照合用の頂点ハッシュ。
    /// 頂点番号は int32、差分は half float の x, y, z（どちらもリトルエンディアン）。
    /// </summary>
    public static class PerfectSyncCodec
    {
        // 差分の各成分の上限（m）
        public const float MaxDelta = 10f;

        // 1 頂点あたりのバイト数（番号・差分）
        private const int IndexBytes = 4;
        private const int DeltaBytes = 6;

        // 頂点ハッシュで位置を丸める単位の逆数（0.1 mm）
        private const float HashScale = 10000f;

        // FNV-1a 64 bit の初期値と乗数
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>
        /// 昇順の頂点番号と、同じ数の差分を base64 にする。
        /// </summary>
        public static void Encode(IReadOnlyList<int> indices, IReadOnlyList<Vector3> deltas,
            out string indicesText, out string deltasText)
        {
            // 数が合わないのは呼び出し側の誤り
            if (indices.Count != deltas.Count)
            {
                throw new ArgumentException("indices and deltas must have the same length.");
            }

            var indexBytes = new byte[indices.Count * IndexBytes];
            var deltaBytes = new byte[deltas.Count * DeltaBytes];
            for (int i = 0; i < indices.Count; i++)
            {
                // 番号は 4 バイト、差分は成分ごとに half float の 2 バイト
                WriteInt32(indexBytes, i * IndexBytes, indices[i]);
                Vector3 delta = deltas[i];
                WriteUInt16(deltaBytes, i * DeltaBytes, Mathf.FloatToHalf(Clamp(delta.x)));
                WriteUInt16(deltaBytes, i * DeltaBytes + 2, Mathf.FloatToHalf(Clamp(delta.y)));
                WriteUInt16(deltaBytes, i * DeltaBytes + 4, Mathf.FloatToHalf(Clamp(delta.z)));
            }

            indicesText = Convert.ToBase64String(indexBytes);
            deltasText = Convert.ToBase64String(deltaBytes);
        }

        /// <summary>
        /// base64 を頂点番号と差分に戻す。番号は vertexCount 未満の昇順、差分は有限で MaxDelta 以内であること。
        /// 不正なら false とエラーメッセージを返す。
        /// </summary>
        public static bool TryDecode(string indicesText, string deltasText, int vertexCount,
            out int[] indices, out Vector3[] deltas, out string error)
        {
            indices = null;
            deltas = null;

            // base64 として読めなければ不正
            byte[] indexBytes;
            byte[] deltaBytes;
            try
            {
                indexBytes = Convert.FromBase64String(indicesText ?? string.Empty);
                deltaBytes = Convert.FromBase64String(deltasText ?? string.Empty);
            }
            catch (FormatException)
            {
                error = "Data is not valid base64.";
                return false;
            }

            // バイト数が 1 頂点あたりの大きさの倍数で、番号と差分の数が一致すること
            int count = indexBytes.Length / IndexBytes;
            if (indexBytes.Length % IndexBytes != 0 || deltaBytes.Length != count * DeltaBytes)
            {
                error = "Index and delta counts do not match.";
                return false;
            }

            // 頂点数を超える数の頂点は動かせない
            if (count > vertexCount)
            {
                error = "Too many vertices.";
                return false;
            }

            var decodedIndices = new int[count];
            var decodedDeltas = new Vector3[count];
            int previous = -1;
            for (int i = 0; i < count; i++)
            {
                // 番号は範囲内の昇順（重複なし）
                int index = ReadInt32(indexBytes, i * IndexBytes);
                if (index <= previous || index >= vertexCount)
                {
                    error = "Vertex indices must be ascending and within the mesh.";
                    return false;
                }

                previous = index;

                // 差分は有限で上限以内
                var delta = new Vector3(
                    Mathf.HalfToFloat(ReadUInt16(deltaBytes, i * DeltaBytes)),
                    Mathf.HalfToFloat(ReadUInt16(deltaBytes, i * DeltaBytes + 2)),
                    Mathf.HalfToFloat(ReadUInt16(deltaBytes, i * DeltaBytes + 4)));
                if (!IsValidComponent(delta.x) || !IsValidComponent(delta.y) || !IsValidComponent(delta.z))
                {
                    error = "Delta is out of range.";
                    return false;
                }

                decodedIndices[i] = index;
                decodedDeltas[i] = delta;
            }

            indices = decodedIndices;
            deltas = decodedDeltas;
            error = null;
            return true;
        }

        /// <summary>
        /// 頂点位置を 0.1 mm 単位に丸めた値の FNV-1a 64 bit（小文字 16 進 16 桁）。
        /// 保存時と読み込み時でメッシュが同じかを確かめる（書き出し直しで頂点の並びが変わったら別物とみなす）。
        /// </summary>
        public static string HashVertices(IReadOnlyList<Vector3> vertices)
        {
            ulong hash = FnvOffset;
            foreach (Vector3 vertex in vertices)
            {
                // 各成分を整数に丸めて 4 バイトずつ混ぜる
                hash = Mix(hash, Quantize(vertex.x));
                hash = Mix(hash, Quantize(vertex.y));
                hash = Mix(hash, Quantize(vertex.z));
            }

            return hash.ToString("x16");
        }

        private static int Quantize(float value)
        {
            // 非有限・極端な値はハッシュが安定するよう 0 に
            double scaled = Math.Round((double)value * HashScale);
            return double.IsNaN(scaled) || Math.Abs(scaled) > int.MaxValue ? 0 : (int)scaled;
        }

        private static ulong Mix(ulong hash, int value)
        {
            // 下位バイトから順に 1 バイトずつ混ぜる
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash ^= (byte)(value >> shift);
                hash *= FnvPrime;
            }

            return hash;
        }

        private static float Clamp(float value)
        {
            // 非有限は 0、範囲外は上限で切る（half float の範囲にも収まる）
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, -MaxDelta, MaxDelta);
        }

        private static bool IsValidComponent(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) <= MaxDelta;
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static int ReadInt32(byte[] buffer, int offset)
        {
            return buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24);
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }
    }
}
