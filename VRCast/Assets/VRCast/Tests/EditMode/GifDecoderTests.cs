using System;
using System.IO;
using NUnit.Framework;
using VRCast.UI;

namespace VRCast.Tests
{
    /// <summary>
    /// 一覧の画像に使う GIF の読み込みと、画像ファイルの判定を検証する。
    /// </summary>
    public class GifDecoderTests
    {
        // 1x1 の透明な GIF（透過色付き）
        private const string TransparentPixel = "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

        // 2x2 で、上の行が赤・緑、下の行が青・白の GIF（全体の色表 4 色）
        private static readonly byte[] FourColors =
        {
            (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
            2, 0, 2, 0, 0x81, 0, 0,
            255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255,
            0x2C, 0, 0, 0, 0, 2, 0, 2, 0, 0,
            2, 3, 0x44, 0x34, 0x05, 0,
            0x3B,
        };

        [Test]
        public void Decode_TransparentPixel_IsTransparent()
        {
            GifImage image = GifDecoder.Decode(Convert.FromBase64String(TransparentPixel), 256, 10);

            // 1 コマで、透過色の画素は透明になること
            Assert.That(image.Width, Is.EqualTo(1));
            Assert.That(image.Frames.Count, Is.EqualTo(1));
            Assert.That(image.Frames[0].Rgba[3], Is.EqualTo(0));
        }

        [Test]
        public void Decode_FourColors_RowsAreBottomUp()
        {
            GifImage image = GifDecoder.Decode(FourColors, 256, 10);
            byte[] rgba = image.Frames[0].Rgba;

            // Unity のテクスチャと同じく下の行（青・白）から並ぶこと
            Assert.That(new[] { rgba[0], rgba[1], rgba[2], rgba[3] }, Is.EqualTo(new byte[] { 0, 0, 255, 255 }));
            Assert.That(new[] { rgba[4], rgba[5], rgba[6] }, Is.EqualTo(new byte[] { 255, 255, 255 }));
            Assert.That(new[] { rgba[8], rgba[9], rgba[10] }, Is.EqualTo(new byte[] { 255, 0, 0 }));
            Assert.That(new[] { rgba[12], rgba[13], rgba[14] }, Is.EqualTo(new byte[] { 0, 255, 0 }));

            // 表示時間の無いコマは 0.1 秒になること
            Assert.That(image.Frames[0].DelayMs, Is.EqualTo(100));
        }

        [Test]
        public void Decode_MaxSize_Downscales()
        {
            GifImage image = GifDecoder.Decode(FourColors, 1, 10);

            // 長辺が指定以下に縮むこと
            Assert.That(image.Width, Is.EqualTo(1));
            Assert.That(image.Height, Is.EqualTo(1));
            Assert.That(image.Frames[0].Rgba.Length, Is.EqualTo(4));
        }

        [Test]
        public void Decode_BrokenData_Throws()
        {
            // GIF でない・途中で切れたデータは InvalidDataException になること
            Assert.Throws<InvalidDataException>(() => GifDecoder.Decode(new byte[] { 1, 2, 3 }, 256, 10));
            Assert.Throws<InvalidDataException>(() => GifDecoder.Decode(new[] { (byte)'G', (byte)'I', (byte)'F',
                (byte)'8', (byte)'9', (byte)'a', (byte)1 }, 256, 10));
        }

        [TestCase(@"C:\a.png", true)]
        [TestCase(@"C:\a.JPG", true)]
        [TestCase(@"C:\a.jpeg", true)]
        [TestCase(@"C:\a.gif", true)]
        [TestCase(@"C:\a.bmp", false)]
        [TestCase(@"C:\a.vrm", false)]
        [TestCase(null, false)]
        public void IsImage_ChecksExtension(string path, bool expected)
        {
            // PNG / JPG / GIF だけを一覧の画像として受け付けること
            Assert.That(AvatarThumbnails.IsImage(path), Is.EqualTo(expected));
        }

        [Test]
        public void FindImage_SkipsOtherFiles()
        {
            // 画像以外を飛ばして最初の画像を返すこと
            Assert.That(AvatarThumbnails.FindImage(new[] { @"C:\a.txt", @"C:\b.gif", @"C:\c.png" }), Is.EqualTo(@"C:\b.gif"));
            Assert.That(AvatarThumbnails.FindImage(new[] { @"C:\a.txt" }), Is.Null);
        }
    }
}
