using System;
using System.Collections.Generic;
using System.IO;
using VRCast.AvatarFormat;

namespace VRCast.Avatars
{
    /// <summary>
    /// 読み込み対象のファイル（.vrcaster）かどうかの判定。ドロップ・ファイル選択・パス入力で共通。
    /// </summary>
    public static class AvatarFiles
    {
        /// <summary>
        /// 拡張子が .vrcaster（大文字小文字を区別しない）なら true。存在確認はしない。
        /// </summary>
        public static bool IsPackage(string path)
        {
            // 空・不正な文字を含むパスは対象外
            if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return false;
            }

            return string.Equals(Path.GetExtension(path), AvatarPackageLayout.Extension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 複数のパス（ドロップされたファイル等）から最初の .vrcaster を返す。無ければ null。
        /// </summary>
        public static string FindPackage(IEnumerable<string> paths)
        {
            // null の一覧は空扱い
            if (paths == null)
            {
                return null;
            }

            foreach (string path in paths)
            {
                if (IsPackage(path))
                {
                    return path;
                }
            }

            return null;
        }
    }
}
