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
        public void BuildMediaFoundationArguments_Install_CopiesThenRegistersCopy()
        {
            string source = Path.Combine("C:", "VRCast", MediaFoundationCamera.PluginFileName);
            string folder = Path.Combine("C:", "Program Files", "VRCast", "VirtualCamera");
            string target = Path.Combine(folder, MediaFoundationCamera.PluginFileName);
            string arguments = VirtualCameraInstaller.BuildMediaFoundationArguments(source, folder, true);

            // コピー先（Program Files）の DLL を登録し、元の場所の DLL は登録しないこと
            StringAssert.StartsWith("/s /c \"", arguments);
            StringAssert.Contains($"copy /y \"{source}\" \"{target}\"", arguments);
            StringAssert.Contains($"&& regsvr32 /s \"{target}\"", arguments);
            StringAssert.DoesNotContain($"regsvr32 /s \"{source}\"", arguments);
        }

        [Test]
        public void BuildMediaFoundationArguments_Uninstall_UnregistersAndDeletesCopy()
        {
            string folder = Path.Combine("C:", "Program Files", "VRCast", "VirtualCamera");
            string target = Path.Combine(folder, MediaFoundationCamera.PluginFileName);
            string arguments = VirtualCameraInstaller.BuildMediaFoundationArguments("unused.dll", folder, false);

            // 解除してからコピーを消す（登録の成否はレジストリで確かめる）
            StringAssert.Contains($"regsvr32 /s /u \"{target}\"", arguments);
            StringAssert.Contains($"del /f /q \"{target}\"", arguments);
            StringAssert.DoesNotContain("copy", arguments);
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
