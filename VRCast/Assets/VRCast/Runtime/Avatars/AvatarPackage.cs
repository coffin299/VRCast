using System;
using VRCast.AvatarFormat;

namespace VRCast.Avatars
{
    /// <summary>
    /// 検証・展開済みの .vrcaster。bundle はキャッシュ上のファイルを指す。
    /// </summary>
    public sealed class AvatarPackage
    {
        public AvatarManifest Manifest { get; }
        public string SourcePath { get; }
        public string BundlePath { get; }

        // 表情プリセット（無い・不正な場合は空）
        public ExpressionSet Expressions { get; }

        // リップシンク・まぶた設定（無い・不正な場合は空）
        public AvatarDescriptorData Descriptor { get; }

        // 揺れもの設定（無い・不正な場合は空）
        public PhysBoneSet PhysBones { get; }

        // Constraint 設定（無い・不正な場合は空）
        public ConstraintSet Constraints { get; }

        // BlendShape の同期（Modular Avatar の Blendshape Sync。無い・不正な場合は空）
        public BlendShapeSyncSet BlendShapeSync { get; }

        public AvatarPackage(
            AvatarManifest manifest, string sourcePath, string bundlePath,
            ExpressionSet expressions, AvatarDescriptorData descriptor, PhysBoneSet physBones, ConstraintSet constraints,
            BlendShapeSyncSet blendShapeSync)
        {
            BlendShapeSync = blendShapeSync ?? new BlendShapeSyncSet();
            Manifest = manifest;
            SourcePath = sourcePath;
            BundlePath = bundlePath;
            Expressions = expressions ?? new ExpressionSet();
            Descriptor = descriptor ?? new AvatarDescriptorData();
            PhysBones = physBones ?? new PhysBoneSet();
            Constraints = constraints ?? new ConstraintSet();
        }
    }

    /// <summary>
    /// .vrcaster が不正・読込不能な場合の例外。
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
