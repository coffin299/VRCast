using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace VRCast.Platform
{
    /// <summary>
    /// VMC / iFacialMocap など LAN からの UDP 受信向けに、Windows ファイアウォールの受信許可を付け直す。
    /// 初回の確認ウィンドウが出ない・更新後に届かなくなったとき用（UAC の確認が 1 回出る）。
    /// </summary>
    public static class WindowsFirewall
    {
        // ルール名（作り直しのときに同じ名前で削除する）
        public const string RuleName = "VRCast LAN tracking";

        // UAC で拒否されたときの Win32 エラー
        private const int ErrorCancelled = 1223;

        /// <summary>
        /// 現在の exe への UDP 受信許可（プライベート・ドメイン）を作り直す。成功なら null、失敗なら理由。
        /// UAC の確認で呼び出し元が止まらないよう別スレッドで実行する。
        /// </summary>
        public static Task<string> AllowUdpInboundAsync()
        {
            // Unity API とウィンドウハンドルはメインスレッドで取る
            if (Application.isEditor)
            {
                return Task.FromResult("Not available in the editor");
            }

            string exe = GpuSelection.ExecutablePath();
            IntPtr owner = NativeProcess.ActiveWindow;
            return Task.Run(() => AllowUdpInbound(exe, owner));
        }

        private static string AllowUdpInbound(string exe, IntPtr owner)
        {
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                return "Could not find VRCast.exe";
            }

            // 同名ルールを消してから追加（無いときは delete が失敗するので無視する）
            string delete = $"netsh advfirewall firewall delete rule name=\"{RuleName}\" >nul 2>&1";
            string add = "netsh advfirewall firewall add rule "
                + $"name=\"{RuleName}\" dir=in action=allow program=\"{exe}\" "
                + "enable=yes profile=private,domain protocol=udp";
            string arguments = $"/s /c \"{delete} & {add}\"";

            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string cmd = string.IsNullOrEmpty(system) ? "cmd.exe" : Path.Combine(system, "cmd.exe");
            int exitCode;
            try
            {
                exitCode = NativeProcess.RunElevated(cmd, arguments, owner);
            }
            catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
            {
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

            return exitCode == 0 ? null : $"netsh failed (exit code {exitCode})";
        }
    }
}
