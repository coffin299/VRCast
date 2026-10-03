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

        [Test]
        public void FindPackage_MixedFiles_ReturnsFirstPackage()
        {
            string[] paths = { @"C:\a.png", @"C:\b.vrcaster", @"C:\c.vrcaster" };

            // 対象外を飛ばして最初の .vrcaster を返す
            Assert.AreEqual(@"C:\b.vrcaster", AvatarFiles.FindPackage(paths));
        }

        [Test]
        public void FindPackage_NoPackage_ReturnsNull()
        {
            // 対象が無ければ null（null の一覧も同様）
            Assert.IsNull(AvatarFiles.FindPackage(new[] { @"C:\a.png", @"C:\folder" }));
            Assert.IsNull(AvatarFiles.FindPackage(null));
        }
    }
}
