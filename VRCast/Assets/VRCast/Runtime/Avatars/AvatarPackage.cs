using System;
using VRCast.AvatarFormat;

namespace VRCast.Avatars
{
    /// <summary>
    /// 検証・展開済みの .vavatar。bundle はキャッシュ上のファイルを指す。
    /// </summary>
    public sealed class AvatarPackage
    {
        public AvatarManifest Manifest { get; }
        public string SourcePath { get; }
        public string BundlePath { get; }

        public AvatarPackage(AvatarManifest manifest, string sourcePath, string bundlePath)
        {
            Manifest = manifest;
            SourcePath = sourcePath;
            BundlePath = bundlePath;
        }
    }

    /// <summary>
    /// .vavatar が不正・読込不能な場合の例外。
    /// </summary>
    public class AvatarPackageException : Exception
    {
        public AvatarPackageException(string message)
            : base(message)
        {
        }

        public AvatarPackageException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
