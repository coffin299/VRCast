using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using VRCast.Platform;

namespace VRCast.Output
{
    /// <summary>
    /// 仮想カメラのドライバー（UnityCapture の DirectShow フィルター）の登録状態。
    /// </summary>
    public enum VirtualCameraRegistration
    {
        NotInstalled,
        Installed,

        // 別の場所の DLL が登録されている（VRCast のフォルダを移動した、または他のアプリが登録した）
        InstalledElsewhere,
    }

    /// <summary>
    /// 同梱の仮想カメラドライバー（StreamingAssets/UnityCapture の 32 / 64 bit フィルター DLL）の検出・登録・解除。
    /// 登録・解除は regsvr32 を管理者権限で実行する（UAC の確認が 1 回出る）。
    /// </summary>
    public static class VirtualCameraInstaller
    {
        // カメラを使うアプリに表示されるデバイス名
        public const string DeviceName = "VRCast Camera";

        // 同梱フォルダ名（StreamingAssets 内）とフィルター DLL 名
        public const string FolderName = "UnityCapture";
        public const string Filter64 = "UnityCaptureFilter64.dll";
        public const string Filter32 = "UnityCaptureFilter32.dll";

        // 64 bit フィルターの COM 登録（既定値 = 登録された DLL のパス）
        private const string ServerKey =
            @"SOFTWARE\Classes\CLSID\{5C2CD55C-92AD-4999-8666-912BD3E70010}\InprocServer32";

        // HKEY_LOCAL_MACHINE、RegGetValue の文字列型指定、UAC で「いいえ」を選んだときのエラー番号
        private static readonly IntPtr LocalMachine = new IntPtr(unchecked((int)0x80000002));
        private const uint StringValueOnly = 0x00000002;
        private const int ErrorCancelled = 1223;

        // レジストリのパスの最大長（文字）
        private const int MaxPathLength = 1024;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegGetValueW(
            IntPtr key, string subKey, string valueName, uint flags, IntPtr type, StringBuilder data, ref uint size);

        /// <summary>
        /// 同梱フォルダ（32 / 64 bit の両方がそろっている場合のみ）のフルパス。無ければ null。
        /// </summary>
        public static string FindBundled(string streamingAssetsPath)
        {
            // StreamingAssets が無い環境では見つからない扱い
            if (string.IsNullOrEmpty(streamingAssetsPath))
            {
                return null;
            }

            string folder = Path.Combine(streamingAssetsPath, FolderName);
            bool complete = File.Exists(Path.Combine(folder, Filter64)) && File.Exists(Path.Combine(folder, Filter32));
            return complete ? Path.GetFullPath(folder) : null;
        }

        /// <summary>
        /// 同梱フォルダ（null 可）と比べた現在の登録状態。
        /// </summary>
        public static VirtualCameraRegistration GetRegistration(string bundledFolder)
        {
            // 未登録
            string registered = ReadRegisteredPath();
            if (string.IsNullOrEmpty(registered))
            {
                return VirtualCameraRegistration.NotInstalled;
            }

            // 同梱の 64 bit DLL と同じパスなら VRCast が登録したもの
            bool same = bundledFolder != null && string.Equals(
                Path.GetFullPath(registered), Path.Combine(bundledFolder, Filter64), StringComparison.OrdinalIgnoreCase);
            return same ? VirtualCameraRegistration.Installed : VirtualCameraRegistration.InstalledElsewhere;
        }

        /// <summary>
        /// 管理者権限の cmd.exe に渡す引数（64 bit → 32 bit の順に regsvr32 を実行）。
        /// </summary>
        public static string BuildArguments(string bundledFolder, bool install)
        {
            // 登録はデバイス名を指定、解除はそのまま
            string option = install ? $" \"/i:UnityCaptureName={DeviceName}\"" : string.Empty;
            string mode = install ? string.Empty : "/u ";
            string first = $"regsvr32 /s {mode}\"{Path.Combine(bundledFolder, Filter64)}\"{option}";
            string second = $"regsvr32 /s {mode}\"{Path.Combine(bundledFolder, Filter32)}\"{option}";

            // /s: 外側の引用符だけを外して残りをそのままコマンドとして実行させる
            return $"/s /c \"{first} && {second}\"";
        }

        /// <summary>
        /// 登録（install = true）または解除を管理者権限で実行する。成功なら null、失敗なら理由を返す。
        /// UAC の確認で呼び出し元が止まらないよう別スレッドで実行する。
        /// </summary>
        public static Task<string> RunElevatedAsync(string bundledFolder, bool install)
        {
            // UAC の確認を VRCast のウィンドウの前に出すため、呼び出し元（メインスレッド）でウィンドウを取っておく
            string arguments = BuildArguments(bundledFolder, install);
            IntPtr owner = NativeProcess.ActiveWindow;
            return Task.Run(() => RunElevated(arguments, owner, install));
        }

        private static string RunElevated(string arguments, IntPtr owner, bool install)
        {
            // IL2CPP の Process.Start（runas）は起動できないため Windows の API で直接起動する
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string cmd = string.IsNullOrEmpty(system) ? "cmd.exe" : Path.Combine(system, "cmd.exe");
            int exitCode;
            try
            {
                exitCode = NativeProcess.RunElevated(cmd, arguments, owner);
            }
            catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
            {
                // UAC で拒否された
                return "Cancelled";
            }
            catch (Win32Exception e)
            {
                return e.Message;
            }
            catch (EntryPointNotFoundException)
            {
                return "Not supported on this OS";
            }
            catch (DllNotFoundException)
            {
                return "Not supported on this OS";
            }

            if (exitCode != 0)
            {
                return $"regsvr32 failed (exit code {exitCode})";
            }

            // 終了コードが 0 でも登録されていなければ失敗として伝える
            bool registered = !string.IsNullOrEmpty(ReadRegisteredPath());
            if (registered != install)
            {
                return install ? "The driver was not registered" : "The driver was not unregistered";
            }

            return null;
        }

        private static string ReadRegisteredPath()
        {
            // 既定値（値の名前 null）を文字列として読む。キーが無ければ未登録
            var buffer = new StringBuilder(MaxPathLength);
            uint size = MaxPathLength * sizeof(char);
            try
            {
                int result = RegGetValueW(LocalMachine, ServerKey, null, StringValueOnly, IntPtr.Zero, buffer, ref size);
                return result == 0 ? buffer.ToString() : null;
            }
            catch (DllNotFoundException)
            {
                // Windows 以外（レジストリが無い）
                return null;
            }
        }
    }
}
