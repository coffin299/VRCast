using NUnit.Framework;
using VRCast.Avatars;

namespace VRCast.Tests
{
    public class AvatarFilesTests
    {
        [TestCase(@"C:\Avatars\Test.vrcaster")]
        [TestCase(@"C:\Avatars\Test.VRCaster")]
        [TestCase("Test.vrcaster")]
        public void IsPackage_VrcasterExtension_ReturnsTrue(string path)
        {
            // 大文字小文字を問わず .vrcaster は対象
            Assert.IsTrue(AvatarFiles.IsPackage(path));
        }

        [TestCase(@"C:\Avatars\Test.unitypackage")]
        [TestCase(@"C:\Avatars\Test.vrcaster.zip")]
        [TestCase(@"C:\Avatars")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("C:\\Avatars\\Te|st.vrcaster")]
        public void IsPackage_OtherPaths_ReturnsFalse(string path)
        {
            // 拡張子違い・フォルダ・空・不正な文字は対象外
            Assert.IsFalse(AvatarFiles.IsPackage(path));
        }

        [TestCase(@"C:\Avatars\Test.vrm")]
        [TestCase(@"C:\Avatars\Test.VRM")]
        public void IsVrm_VrmExtension_ReturnsTrue(string path)
        {
            // 大文字小文字を問わず .vrm は対象で、読み込めるファイルでもある
            Assert.IsTrue(AvatarFiles.IsVrm(path));
            Assert.IsTrue(AvatarFiles.IsSupported(path));
            Assert.IsFalse(AvatarFiles.IsPackage(path));
        }

        [TestCase(@"C:\Avatars\Test.vrm.zip")]
        [TestCase(@"C:\Avatars\Test.glb")]
        [TestCase(null)]
        public void IsSupported_OtherPaths_ReturnsFalse(string path)
        {
            // .vrcaster / .vrm 以外は対象外
            Assert.IsFalse(AvatarFiles.IsSupported(path));
        }

        [Test]
        public void FindSupported_MixedFiles_ReturnsFirstSupported()
        {
            string[] paths = { @"C:\a.png", @"C:\b.vrm", @"C:\c.vrcaster" };

            // 対象外を飛ばして最初の読み込めるファイルを返す
            Assert.AreEqual(@"C:\b.vrm", AvatarFiles.FindSupported(paths));
        }

        [Test]
        public void FindSupported_NoSupported_ReturnsNull()
        {
            // 対象が無ければ null（null の一覧も同様）
            Assert.IsNull(AvatarFiles.FindSupported(new[] { @"C:\a.png", @"C:\folder" }));
            Assert.IsNull(AvatarFiles.FindSupported(null));
        }
    }
}
