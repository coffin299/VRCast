using System;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// manifest.json の内容。JsonUtility でシリアライズする。
    /// </summary>
    [Serializable]
    public class AvatarManifest
    {
        // アバター名の最大文字数
        public const int MaxNameLength = 128;

        // SHA-256 の 16 進文字列長
        private const int Sha256HexLength = 64;

        public int formatVersion = AvatarPackageLayout.FormatVersion;
        public string name = string.Empty;
        public string unityVersion = string.Empty;
        public string bundleSha256 = string.Empty;
        public long bundleSize;
        public string createdAt = string.Empty;

        /// <summary>
        /// 内容を検証し、問題があればエラーメッセージを、無ければ null を返す。
        /// </summary>
        public string Validate()
        {
            // 未対応のフォーマットバージョンは読めない
            if (formatVersion != AvatarPackageLayout.FormatVersion)
            {
                return $"Unsupported formatVersion: {formatVersion} (expected {AvatarPackageLayout.FormatVersion}).";
            }

            // 名前は表示とキャッシュ識別に使うため必須
            if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
            {
                return $"Invalid name (1-{MaxNameLength} chars required).";
            }

            // AssetBundle はバージョン依存なので作成時の Unity バージョンは必須
            if (string.IsNullOrWhiteSpace(unityVersion))
            {
                return "unityVersion is missing.";
            }

            // ハッシュはキャッシュのディレクトリ名にも使うため 16 進 64 文字に限定する
            if (!IsLowerHex(bundleSha256, Sha256HexLength))
            {
                return "bundleSha256 must be 64 lowercase hex characters.";
            }

            // サイズ 0 以下の bundle は不正
            if (bundleSize <= 0)
            {
                return "bundleSize must be positive.";
            }

            return null;
        }

        private static bool IsLowerHex(string value, int length)
        {
            // null または長さ不一致は不正
            if (value == null || value.Length != length)
            {
                return false;
            }

            // 0-9 / a-f 以外が含まれていれば不正（パス文字の混入も防ぐ）
            foreach (char c in value)
            {
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
