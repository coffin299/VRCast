using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// パッケージ検証用のハッシュ計算。
    /// </summary>
    public static class HashUtility
    {
        public static string ComputeSha256Hex(string filePath)
        {
            // ファイル全体をストリームで読みながらハッシュを計算
            using (FileStream stream = File.OpenRead(filePath))
            using (SHA256 sha = SHA256.Create())
            {
                return ToLowerHex(sha.ComputeHash(stream));
            }
        }

        private static string ToLowerHex(byte[] bytes)
        {
            // 1 バイトを 2 桁の小文字 16 進へ変換して連結
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
