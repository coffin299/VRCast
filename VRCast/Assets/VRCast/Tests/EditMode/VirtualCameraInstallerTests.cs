using System.IO;
using NUnit.Framework;
using VRCast.Output;

namespace VRCast.Tests
{
    public class VirtualCameraInstallerTests
    {
        [Test]
        public void BuildArguments_Install_RegistersBothFiltersWithDeviceName()
        {
            string folder = Path.Combine("C:", "VRCast", VirtualCameraInstaller.FolderName);
            string arguments = VirtualCameraInstaller.BuildArguments(folder, true);

            // 64 bit → 32 bit の順に、引用符付きのパスとデバイス名で登録すること
            string option = $"\"/i:UnityCaptureName={VirtualCameraInstaller.DeviceName}\"";
            string expected = $"/s /c \"regsvr32 /s \"{Path.Combine(folder, VirtualCameraInstaller.Filter64)}\" {option}"
                + $" && regsvr32 /s \"{Path.Combine(folder, VirtualCameraInstaller.Filter32)}\" {option}\"";
            Assert.AreEqual(expected, arguments);
        }

        [Test]
        public void BuildArguments_Uninstall_UnregistersWithoutName()
        {
            string folder = Path.Combine("C:", "VRCast", VirtualCameraInstaller.FolderName);
            string arguments = VirtualCameraInstaller.BuildArguments(folder, false);

            // 解除はデバイス名を付けず /u で両方
            StringAssert.DoesNotContain("UnityCaptureName", arguments);
            StringAssert.Contains($"regsvr32 /s /u \"{Path.Combine(folder, VirtualCameraInstaller.Filter64)}\"", arguments);
            StringAssert.Contains($"regsvr32 /s /u \"{Path.Combine(folder, VirtualCameraInstaller.Filter32)}\"", arguments);
        }

        [Test]
        public void FindBundled_RequiresBothFilters()
        {
            // 64 bit だけを置いた一時 StreamingAssets
            string root = Path.Combine(Path.GetTempPath(), "VRCastTests_" + System.Guid.NewGuid().ToString("N"));
            string folder = Path.Combine(root, VirtualCameraInstaller.FolderName);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, VirtualCameraInstaller.Filter64), string.Empty);

            try
            {
                // 片方だけでは見つからず、両方そろうとフォルダのフルパスを返すこと
                Assert.IsNull(VirtualCameraInstaller.FindBundled(root));
                File.WriteAllText(Path.Combine(folder, VirtualCameraInstaller.Filter32), string.Empty);
                Assert.AreEqual(Path.GetFullPath(folder), VirtualCameraInstaller.FindBundled(root));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void FindBundled_EmptyPath_ReturnsNull()
        {
            Assert.IsNull(VirtualCameraInstaller.FindBundled(string.Empty));
        }
    }
}
