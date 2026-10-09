using System;
using System.Collections.Generic;
using System.IO;

namespace VRCast.UI
{
    /// <summary>
    /// GIF の 1 コマ（RGBA32。Unity のテクスチャと同じく下の行から並ぶ）と表示する時間。
    /// </summary>
    public sealed class GifFrame
    {
        public byte[] Rgba;
        public int DelayMs;
    }

    /// <summary>
    /// 合成済みのコマを縮小して並べた GIF。
    /// </summary>
    public sealed class GifImage
    {
        public int Width;
        public int Height;
        public readonly List<GifFrame> Frames = new List<GifFrame>();
    }

    /// <summary>
    /// GIF（87a / 89a）の読み込み。Unity の Texture2D.LoadImage は GIF を読めないため自前で展開する。
    /// コマごとの位置・透過色・消し方（disposal）・インターレースを反映して全体の絵に合成し、縮小して返す。
    /// Unity の API を使わないため別スレッドで呼べる。
    /// </summary>
    public static class GifDecoder
    {
        // 一覧の画像として受け付ける GIF の大きさの上限（合成用の画面を 2 枚持つため、大きすぎる画像は断る）
        public const int MaxCanvasPixels = 2048 * 2048;

        // 表示時間が無い・短すぎるコマの表示時間（ブラウザーと同じく 0.1 秒にそろえる）
        private const int DefaultDelayMs = 100;
        private const int MinDelayMs = 20;

        // LZW の符号の最大ビット数と、辞書の大きさ
        private const int MaxCodeBits = 12;
        private const int MaxCodes = 1 << MaxCodeBits;

        // ブロックの種類（拡張・画像・終端）と、拡張の種類（表示時間・透過色）
        private const byte ExtensionIntroducer = 0x21;
        private const byte ImageSeparator = 0x2C;
        private const byte Trailer = 0x3B;
        private const byte GraphicControlLabel = 0xF9;

        // コマの消し方（2 = 背景（透明）に戻す、3 = 描く前の状態に戻す）
        private const int DisposeToBackground = 2;
        private const int DisposeToPrevious = 3;

        // インターレースの行の並び（開始行と間隔の組）
        private static readonly int[] InterlaceStart = { 0, 4, 2, 1 };
        private static readonly int[] InterlaceStep = { 8, 8, 4, 2 };

        /// <summary>
        /// 先頭が GIF の印（GIF87a / GIF89a）なら true。
        /// </summary>
        public static bool IsGif(byte[] data)
        {
            return data != null && data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F'
                && data[3] == '8' && (data[4] == '7' || data[4] == '9') && data[5] == 'a';
        }

        /// <summary>
        /// GIF を読み込み、長辺を maxSize 以下に縮小したコマを最大 maxFrames 個返す。
        /// 途中で切れたファイルは読めたコマまで返し、1 コマも読めなければ InvalidDataException。
        /// </summary>
        public static GifImage Decode(byte[] data, int maxSize, int maxFrames)
        {
            if (!IsGif(data))
            {
                throw new InvalidDataException("Not a GIF file");
            }

            var reader = new Reader(data, 6);
            try
            {
                return DecodeBody(reader, maxSize, maxFrames);
            }
            catch (IndexOutOfRangeException)
            {
                // 範囲外の読み取りはファイルが途中で切れている
                throw new InvalidDataException("The GIF file is truncated");
            }
        }

        private static GifImage DecodeBody(Reader reader, int maxSize, int maxFrames)
        {
            // 画面の大きさと、全体の色表
            int width = reader.ReadUInt16();
            int height = reader.ReadUInt16();
            int packed = reader.ReadByte();
            reader.Skip(2);
            if (width <= 0 || height <= 0 || (long)width * height > MaxCanvasPixels)
            {
                throw new InvalidDataException($"Unsupported GIF size: {width}x{height}");
            }

            byte[] globalColors = (packed & 0x80) != 0 ? reader.ReadBytes(3 * (2 << (packed & 0x07))) : null;

            // 縮小後の大きさ（長辺を maxSize 以下にし、元より大きくはしない）
            float scale = Math.Min(1f, (float)maxSize / Math.Max(width, height));
            var image = new GifImage
            {
                Width = Math.Max(1, (int)(width * scale)),
                Height = Math.Max(1, (int)(height * scale)),
            };

            // 合成先の画面（最初は透明）と、表示時間・透過色・消し方（次の画像に効く）
            var canvas = new byte[width * height * 4];
            int delayMs = 0;
            int transparent = -1;
            int disposal = 0;

            while (image.Frames.Count < maxFrames && !reader.AtEnd)
            {
                byte block = reader.ReadByte();
                if (block == ExtensionIntroducer)
                {
                    // 表示時間・透過色・消し方は直後の画像だけに効く。それ以外の拡張（コメント等）は読み飛ばす
                    byte label = reader.ReadByte();
                    if (label == GraphicControlLabel && reader.PeekByte() >= 4)
                    {
                        reader.Skip(1);
                        int flags = reader.ReadByte();
                        delayMs = reader.ReadUInt16() * 10;
                        int index = reader.ReadByte();
                        transparent = (flags & 0x01) != 0 ? index : -1;
                        disposal = (flags >> 2) & 0x07;
                    }

                    reader.SkipSubBlocks();
                }
                else if (block == ImageSeparator)
                {
                    try
                    {
                        DecodeFrame(reader, canvas, width, height, globalColors, transparent, disposal, delayMs, image);
                    }
                    catch (Exception e) when (image.Frames.Count > 0
                        && (e is InvalidDataException || e is IndexOutOfRangeException))
                    {
                        // 途中のコマが壊れていれば、読めたコマまでを使う
                        break;
                    }

                    // 次の画像のために拡張の値を戻す
                    delayMs = 0;
                    transparent = -1;
                    disposal = 0;
                }
                else
                {
                    // 終端・未知のブロックで終わる（未知のブロックの後ろは読めない）
                    break;
                }
            }

            if (image.Frames.Count == 0)
            {
                throw new InvalidDataException("The GIF file has no image");
            }

            return image;
        }

        private static void DecodeFrame(Reader reader, byte[] canvas, int width, int height, byte[] globalColors,
            int transparent, int disposal, int delayMs, GifImage image)
        {
            // コマの位置・大きさと、コマ専用の色表（無ければ全体の色表）
            int left = reader.ReadUInt16();
            int top = reader.ReadUInt16();
            int frameWidth = reader.ReadUInt16();
            int frameHeight = reader.ReadUInt16();
            int packed = reader.ReadByte();
            bool interlaced = (packed & 0x40) != 0;
            byte[] colors = (packed & 0x80) != 0 ? reader.ReadBytes(3 * (2 << (packed & 0x07))) : globalColors;
            if (colors == null)
            {
                throw new InvalidDataException("The GIF file has no color table");
            }

            // 色番号の並びを LZW で展開する
            int minCodeSize = reader.ReadByte();
            byte[] indices = DecodeLzw(reader.ReadSubBlocks(), minCodeSize, frameWidth * frameHeight);

            // 「描く前の状態に戻す」コマは、描く前の画面を取っておく
            byte[] previous = disposal == DisposeToPrevious ? (byte[])canvas.Clone() : null;

            // 画面に重ねる（透過色と、色表に無い番号は描かない。画面の外は切り捨てる）
            int colorCount = colors.Length / 3;
            for (int row = 0; row < frameHeight; row++)
            {
                int y = top + (interlaced ? InterlacedRow(row, frameHeight) : row);
                if (y >= height)
                {
                    continue;
                }

                for (int column = 0; column < frameWidth; column++)
                {
                    int x = left + column;
                    int index = indices[row * frameWidth + column];
                    if (x >= width || index == transparent || index >= colorCount)
                    {
                        continue;
                    }

                    int offset = (y * width + x) * 4;
                    canvas[offset] = colors[index * 3];
                    canvas[offset + 1] = colors[index * 3 + 1];
                    canvas[offset + 2] = colors[index * 3 + 2];
                    canvas[offset + 3] = 255;
                }
            }

            // 縮小して 1 コマとして残す
            image.Frames.Add(new GifFrame
            {
                Rgba = Downscale(canvas, width, height, image.Width, image.Height),
                DelayMs = delayMs < MinDelayMs ? DefaultDelayMs : delayMs,
            });

            // 次のコマのために、指定どおりに消す
            if (disposal == DisposeToBackground)
            {
                ClearRect(canvas, width, height, left, top, frameWidth, frameHeight);
            }
            else if (previous != null)
            {
                Buffer.BlockCopy(previous, 0, canvas, 0, canvas.Length);
            }
        }

        private static int InterlacedRow(int row, int height)
        {
            // 4 回に分けて送られた行を、元の行の位置へ並べ直す
            for (int pass = 0; pass < InterlaceStart.Length; pass++)
            {
                int count = (height - InterlaceStart[pass] + InterlaceStep[pass] - 1) / InterlaceStep[pass];
                if (row < count)
                {
                    return InterlaceStart[pass] + row * InterlaceStep[pass];
                }

                row -= count;
            }

            return row;
        }

        private static void ClearRect(byte[] canvas, int width, int height, int left, int top, int rectWidth, int rectHeight)
        {
            // コマの範囲（画面の内側だけ）を透明にする
            int right = Math.Min(width, left + rectWidth);
            int bottom = Math.Min(height, top + rectHeight);
            for (int y = top; y < bottom; y++)
            {
                if (left < right)
                {
                    Array.Clear(canvas, (y * width + left) * 4, (right - left) * 4);
                }
            }
        }

        private static byte[] Downscale(byte[] canvas, int width, int height, int targetWidth, int targetHeight)
        {
            // 最近傍で縮小し、Unity のテクスチャに合わせて下の行から並べる
            var result = new byte[targetWidth * targetHeight * 4];
            for (int y = 0; y < targetHeight; y++)
            {
                int sourceY = Math.Min(height - 1, (int)((targetHeight - 1 - y + 0.5f) * height / targetHeight));
                for (int x = 0; x < targetWidth; x++)
                {
                    int sourceX = Math.Min(width - 1, (int)((x + 0.5f) * width / targetWidth));
                    Buffer.BlockCopy(canvas, (sourceY * width + sourceX) * 4, result, (y * targetWidth + x) * 4, 4);
                }
            }

            return result;
        }

        /// <summary>
        /// GIF の LZW（下位ビットから詰めた可変長の符号）を色番号の並びへ展開する。足りない分は 0（先頭の色）。
        /// </summary>
        public static byte[] DecodeLzw(byte[] data, int minCodeSize, int pixelCount)
        {
            if (minCodeSize < 1 || minCodeSize > 11)
            {
                throw new InvalidDataException($"Invalid LZW code size: {minCodeSize}");
            }

            // 辞書（各符号 = 前の符号 + 末尾の 1 文字）と、展開途中の文字を逆順に積むスタック
            var prefix = new short[MaxCodes];
            var suffix = new byte[MaxCodes];
            var stack = new byte[MaxCodes + 1];
            int clear = 1 << minCodeSize;
            int end = clear + 1;
            for (int code = 0; code < clear; code++)
            {
                suffix[code] = (byte)code;
            }

            var output = new byte[pixelCount];
            int written = 0;
            int codeSize = minCodeSize + 1;
            int next = clear + 2;
            int old = -1;
            byte first = 0;

            // 下位ビットから順に符号を取り出す
            int bitBuffer = 0;
            int bitCount = 0;
            int position = 0;
            while (written < pixelCount)
            {
                // 符号 1 つ分のビットを溜める（データが尽きたら終わり）
                while (bitCount < codeSize && position < data.Length)
                {
                    bitBuffer |= data[position++] << bitCount;
                    bitCount += 8;
                }

                if (bitCount < codeSize)
                {
                    break;
                }

                int code = bitBuffer & ((1 << codeSize) - 1);
                bitBuffer >>= codeSize;
                bitCount -= codeSize;

                // クリア符号で辞書を初期化し、終了符号で終わる
                if (code == clear)
                {
                    codeSize = minCodeSize + 1;
                    next = clear + 2;
                    old = -1;
                    continue;
                }

                if (code == end)
                {
                    break;
                }

                // クリア直後の最初の符号は 1 文字そのもの
                if (old < 0)
                {
                    if (code >= clear)
                    {
                        throw new InvalidDataException("Invalid LZW code");
                    }

                    output[written++] = (byte)code;
                    old = code;
                    first = (byte)code;
                    continue;
                }

                // 辞書にまだ無い符号は「前の符号 + 前の符号の先頭の文字」（同じ並びが続く場合）
                int current = code;
                int top = 0;
                if (code >= next)
                {
                    if (code > next)
                    {
                        throw new InvalidDataException("Invalid LZW code");
                    }

                    stack[top++] = first;
                    code = old;
                }

                // 符号をたどって文字を逆順に積む
                while (code > end)
                {
                    stack[top++] = suffix[code];
                    code = prefix[code];
                }

                first = suffix[code];
                stack[top++] = first;

                // 積んだ文字を正しい順で書き出す（画素数を超える分は捨てる）
                while (top > 0 && written < pixelCount)
                {
                    output[written++] = stack[--top];
                }

                // 辞書に「前の符号 + 今の先頭の文字」を足し、満ちたら符号のビット数を増やす
                if (next < MaxCodes)
                {
                    prefix[next] = (short)old;
                    suffix[next] = first;
                    next++;
                    if (next == 1 << codeSize && codeSize < MaxCodeBits)
                    {
                        codeSize++;
                    }
                }

                old = current;
            }

            return output;
        }

        /// <summary>
        /// バイト列を先頭から読む（範囲外は IndexOutOfRangeException。途中で切れたファイルとして扱う）。
        /// </summary>
        private sealed class Reader
        {
            private readonly byte[] _data;
            private int _position;

            public Reader(byte[] data, int position)
            {
                _data = data;
                _position = position;
            }

            public bool AtEnd => _position >= _data.Length;

            public byte ReadByte()
            {
                return _data[_position++];
            }

            public byte PeekByte()
            {
                return _data[_position];
            }

            public int ReadUInt16()
            {
                // GIF の数値は下位バイトが先
                int value = _data[_position] | (_data[_position + 1] << 8);
                _position += 2;
                return value;
            }

            public byte[] ReadBytes(int count)
            {
                if (_position + count > _data.Length)
                {
                    throw new IndexOutOfRangeException();
                }

                var result = new byte[count];
                Buffer.BlockCopy(_data, _position, result, 0, count);
                _position += count;
                return result;
            }

            public void Skip(int count)
            {
                _position += count;
            }

            public void SkipSubBlocks()
            {
                // 長さ付きのブロックを長さ 0 のブロックまで読み飛ばす
                for (int length = ReadByte(); length > 0; length = ReadByte())
                {
                    _position += length;
                }
            }

            public byte[] ReadSubBlocks()
            {
                // 長さ付きのブロックをつなげて 1 つのデータにする（途中で切れていれば読めた分まで）
                using (var stream = new MemoryStream())
                {
                    while (!AtEnd)
                    {
                        int length = ReadByte();
                        if (length == 0)
                        {
                            break;
                        }

                        int available = Math.Min(length, _data.Length - _position);
                        stream.Write(_data, _position, available);
                        _position += length;
                    }

                    return stream.ToArray();
                }
            }
        }
    }
}
