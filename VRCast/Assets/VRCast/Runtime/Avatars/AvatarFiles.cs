using System;
using System.Collections.Generic;
using System.IO;
using VRCast.AvatarFormat;

namespace VRCast.Avatars
{
    /// <summary>
    /// 読み込み対象のファイル（.vrcaster / .vrm）かどうかの判定。ドロップ・ファイル選択・パス入力で共通。
    /// </summary>
    public static class AvatarFiles
    {
        // VRM ファイルの拡張子
        public const string VrmExtension = ".vrm";

        // 読み込める拡張子（ファイル選択ダイアログの絞り込みにも使う）
        public static readonly string[] Extensions = { AvatarPackageLayout.Extension, VrmExtension };

        /// <summary>
        /// 拡張子が .vrcaster（大文字小文字を区別しない）なら true。存在確認はしない。
        /// </summary>
        public static bool IsPackage(string path)
        {
            return HasExtension(path, AvatarPackageLayout.Extension);
        }

        /// <summary>
        /// 拡張子が .vrm（大文字小文字を区別しない）なら true。存在確認はしない。
        /// </summary>
        public static bool IsVrm(string path)
        {
            return HasExtension(path, VrmExtension);
        }

        /// <summary>
        /// 読み込めるファイル（.vrcaster / .vrm）なら true。存在確認はしない。
        /// </summary>
        public static bool IsSupported(string path)
        {
            return IsPackage(path) || IsVrm(path);
        }

        /// <summary>
        /// 複数のパス（ドロップされたファイル等）から最初の読み込めるファイルを返す。無ければ null。
        /// </summary>
        public static string FindSupported(IEnumerable<string> paths)
        {
            // null の一覧は空扱い
            if (paths == null)
            {
                return null;
            }

            foreach (string path in paths)
            {
                if (IsSupported(path))
                {
                    return path;
                }
            }

            return null;
        }

        private static bool HasExtension(string path, string extension)
        {
            // 空・不正な文字を含むパスは対象外
            if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return false;
            }

            return string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);
        }
    }
}
