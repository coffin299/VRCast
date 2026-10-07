using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VRCast.Core
{
    /// <summary>
    /// この PC の LAN のアドレス（スマートフォンのアプリに送信先として入力してもらうため）。
    /// </summary>
    public static class LocalNetwork
    {
        // 自動割り当て（DHCP 失敗時の 169.254.x.x）の先頭。ほかの機器から届かないため表示しない
        private const string LinkLocalPrefix = "169.254.";

        /// <summary>
        /// 動作中のネットワークアダプターの IPv4 アドレス（ループバック・トンネル・自動割り当てを除く）。取得できなければ空。
        /// アダプター一覧が取れない環境では、ホスト名から引いたアドレスで代用する。
        /// </summary>
        public static List<string> GetIPv4Addresses()
        {
            var addresses = new List<string>();
            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // 切断中・ループバック・トンネル（VPN の一部等）は対象外
                    if (adapter.OperationalStatus != OperationalStatus.Up
                        || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback
                        || adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    foreach (UnicastIPAddressInformation info in adapter.GetIPProperties().UnicastAddresses)
                    {
                        Add(addresses, info.Address);
                    }
                }
            }
            catch (Exception)
            {
                // ビルドの種類によっては未対応の例外になるため、ここでは握りつぶして下の代用へ進む
                addresses.Clear();
            }

            // 1 つも取れなければホスト名から引く（アダプターの状態は分からないが、表示しないよりよい）
            if (addresses.Count == 0)
            {
                try
                {
                    foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                    {
                        Add(addresses, address);
                    }
                }
                catch (Exception)
                {
                    // 取得できない環境では空のまま（UI は「ipconfig で確認」を案内する）
                }
            }

            return addresses;
        }

        private static void Add(List<string> addresses, IPAddress address)
        {
            // IPv4 で、ループバック・自動割り当てでなく、まだ無いものだけ
            string text = address.ToString();
            if (address.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(address)
                && !text.StartsWith(LinkLocalPrefix, StringComparison.Ordinal)
                && !addresses.Contains(text))
            {
                addresses.Add(text);
            }
        }
    }
}
