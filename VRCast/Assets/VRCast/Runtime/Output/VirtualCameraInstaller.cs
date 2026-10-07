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
    /// 同梱の仮想カメラドライバーの検出・登録・解除。従来方式は StreamingAssets/UnityCapture の 32 / 64 bit フィルター DLL、
    /// Windows 11 の方式（Media Foundation）は VRCastVirtualCamera.dll を Program Files へコピーして登録する
    /// （Frame Server のサービスはユーザーのフォルダを読めないため）。
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

        // Media Foundation 版のメディアソースの COM 登録（Tools/VirtualCamera/src/Shared.h の CLSID と同じ）
        private const string MediaFoundationServerKey =
            @"SOFTWARE\Classes\CLSID\{7D3F6B2A-4C1E-4F8B-9A57-2E6C1D0B8F41}\InprocServer32";

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
            // 同梱の 64 bit DLL と同じパスなら VRCast が登録したもの
            return Compare(ReadRegisteredPath(ServerKey),
                bundledFolder != null ? Path.Combine(bundledFolder, Filter64) : null);
        }

        /// <summary>
        /// Media Foundation 版の登録先フォルダ（Program Files\VRCast\VirtualCamera）。
        /// </summary>
        public static string MediaFoundationInstallFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VRCast", "VirtualCamera");

        /// <summary>
        /// Media Foundation 版の現在の登録状態（登録先フォルダの DLL なら登録済み）。
        /// </summary>
        public static VirtualCameraRegistration GetMediaFoundationRegistration()
        {
            return Compare(ReadRegisteredPath(MediaFoundationServerKey),
                Path.Combine(MediaFoundationInstallFolder, MediaFoundationCamera.PluginFileName));
        }

        private static VirtualCameraRegistration Compare(string registered, string expected)
        {
            // 未登録
            if (string.IsNullOrEmpty(registered))
            {
                return VirtualCameraRegistration.NotInstalled;
            }

            bool same = expected != null && string.Equals(
                Path.GetFullPath(registered), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
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
            return Task.Run(() => RunElevated(arguments, owner, install, ServerKey));
        }

        /// <summary>
        /// Media Foundation 版の登録（installFolder へ sourceDll をコピーして regsvr32）・解除（regsvr32 /u して削除）の引数。
        /// 使用中の DLL は上書き・削除できないため、失敗したときだけ Frame Server を止めてからやり直す
        /// （Frame Server は次にカメラを開いたときに自動で起動する）。
        /// </summary>
        public static string BuildMediaFoundationArguments(string sourceDll, string installFolder, bool install)
        {
            string target = Path.Combine(installFolder, MediaFoundationCamera.PluginFileName);
            const string stopService = "net stop FrameServer /y >nul 2>&1";
            string command;
            if (install)
            {
                string copy = $"copy /y \"{sourceDll}\" \"{target}\" >nul";
                command = $"(if not exist \"{installFolder}\" mkdir \"{installFolder}\")"
                    + $" & ({copy} || ({stopService} & {copy})) && regsvr32 /s \"{target}\"";
            }
            else
            {
                // 登録の解除はレジストリで確かめるため、ファイルやフォルダが残っても終了コードは 0 にする
                string delete = $"del /f /q \"{target}\" >nul 2>&1";
                command = $"regsvr32 /s /u \"{target}\" & {delete}"
                    + $" & (if exist \"{target}\" ({stopService} & {delete}))"
                    + $" & (rmdir \"{installFolder}\" >nul 2>&1)"
                    + $" & (rmdir \"{Path.GetDirectoryName(installFolder)}\" >nul 2>&1) & exit /b 0";
            }

            // /s: 外側の引用符だけを外して残りをそのままコマンドとして実行させる
            return $"/s /c \"{command}\"";
        }

        /// <summary>
        /// Media Foundation 版の登録（install = true）または解除を管理者権限で実行する。成功なら null、失敗なら理由を返す。
        /// </summary>
        public static Task<string> RunMediaFoundationElevatedAsync(string sourceDll, bool install)
        {
            string arguments = BuildMediaFoundationArguments(sourceDll, MediaFoundationInstallFolder, install);
            IntPtr owner = NativeProcess.ActiveWindow;
            return Task.Run(() => RunElevated(arguments, owner, install, MediaFoundationServerKey));
        }

        private static string RunElevated(string arguments, IntPtr owner, bool install, string serverKey)
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
            bool registered = !string.IsNullOrEmpty(ReadRegisteredPath(serverKey));
            if (registered != install)
            {
                return install ? "The driver was not registered" : "The driver was not unregistered";
            }

            return null;
        }

        private static string ReadRegisteredPath(string serverKey)
        {
            // 既定値（値の名前 null）を文字列として読む。キーが無ければ未登録
            var buffer = new StringBuilder(MaxPathLength);
            uint size = MaxPathLength * sizeof(char);
            try
            {
                int result = RegGetValueW(LocalMachine, serverKey, null, StringValueOnly, IntPtr.Zero, buffer, ref size);
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
