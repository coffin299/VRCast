using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using VRCast.Core;

namespace VRCast.Platform
{
    /// <summary>
    /// CPU のコア構成。次の 2 種類を見分け、VRCast 本体と同梱トラッカーに使わせるコアのマスクを決める。
    /// - L3 の大きさが違う CCD がある CPU（Ryzen 7950X3D / 9950X3D 等、片方の CCD だけ 3D V-Cache）: ゲーム検知時に AMD のドライバーが
    ///   キャッシュの無い側を休ませ、VRCast もゲームと同じコアに集まるため、L3 の小さい側のコアを使わせる。
    /// - 性能の違うコアがある CPU（Intel 12 世代以降・Core Ultra 等の P コア / E コア）: 設定に従い E コアだけ / P コアだけを使わせる。
    /// 性能の違うコアがある CPU は前者として扱わない（L3 の大きさがコアの種類で違うだけのため）。
    /// プロセッサグループが 1 つ（64 論理コア以下）の PC だけ扱う。
    /// </summary>
    public static class CpuTopology
    {
        // GetLogicalProcessorInformationEx の種類（RelationAll / RelationProcessorCore / RelationCache）とエラー番号
        private const int RelationAll = 0xFFFF;
        private const int RelationProcessorCore = 0;
        private const int RelationCache = 2;
        private const int ErrorInsufficientBuffer = 122;

        // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX 内の位置（共通: Relationship / Size）
        private const int RelationshipOffset = 0;
        private const int SizeOffset = 4;

        // PROCESSOR_RELATIONSHIP 内の位置（EfficiencyClass / GroupMask[0].Mask / GroupMask[0].Group）
        private const int EfficiencyClassOffset = 9;
        private const int CoreMaskOffset = 32;
        private const int CoreGroupOffset = 40;

        // CACHE_RELATIONSHIP 内の位置（Level / CacheSize / GroupMask.Mask / GroupMask.Group）
        private const int LevelOffset = 8;
        private const int CacheSizeOffset = 12;
        private const int CacheMaskOffset = 40;
        private const int CacheGroupOffset = 48;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);

        // 判定結果（初回だけ調べる）
        private static bool _detected;
        private static ulong _smallCacheMask;
        private static ulong _performanceMask;
        private static ulong _efficiencyMask;
        private static string _description = "unknown";

        /// <summary>
        /// L3 の大きさが違う CCD がある（2 CCD の X3D 等）。
        /// </summary>
        public static bool HasAsymmetricCache
        {
            get
            {
                Detect();
                return _smallCacheMask != 0;
            }
        }

        /// <summary>
        /// 性能の違うコアがある（P コア / E コア）。
        /// </summary>
        public static bool IsHybrid
        {
            get
            {
                Detect();
                return _efficiencyMask != 0;
            }
        }

        /// <summary>
        /// ログ用の構成（例: "L3 96MB, 32MB" / "L3 36MB; P 16, E 16"）。
        /// </summary>
        public static string Description
        {
            get
            {
                Detect();
                return _description;
            }
        }

        /// <summary>
        /// 設定（avoidCacheCcd / hybridCores）に対応する、使わせるコアのマスク。制限しない（全コア）なら 0。
        /// </summary>
        public static ulong CoreMaskFor(AppSettings settings)
        {
            return CoreMaskFor(settings.avoidCacheCcd, settings.hybridCores);
        }

        /// <summary>
        /// 使わせるコアのマスク。制限しない（全コア）なら 0。
        /// </summary>
        public static ulong CoreMaskFor(bool avoidCacheCcd, HybridCoreSelection hybridCores)
        {
            Detect();

            // P コア / E コアの CPU は選択に従う
            if (IsHybrid)
            {
                switch (hybridCores)
                {
                    case HybridCoreSelection.EfficiencyOnly:
                        return _efficiencyMask;
                    case HybridCoreSelection.PerformanceOnly:
                        return _performanceMask;
                    default:
                        return 0;
                }
            }

            // 2 CCD の X3D は L3 の小さい側
            return avoidCacheCcd ? _smallCacheMask : 0;
        }

        private static void Detect()
        {
            // 一度だけ調べる
            if (_detected)
            {
                return;
            }

            _detected = true;
            var caches = new List<(ulong mask, ulong size)>();
            var cores = new List<(ulong mask, int efficiency)>();
            if (!Read(caches, cores))
            {
                return;
            }

            var parts = new List<string>();
            DetectHybrid(cores, parts);
            DetectCache(caches, parts);
            _description = parts.Count > 0 ? string.Join("; ", parts) : "unknown";
        }

        private static void DetectHybrid(List<(ulong mask, int efficiency)> cores, List<string> parts)
        {
            // EfficiencyClass は大きいほど高性能。最大の種類を P コア、それ以外を E コアとする
            int highest = int.MinValue;
            int lowest = int.MaxValue;
            foreach (var (_, efficiency) in cores)
            {
                highest = Math.Max(highest, efficiency);
                lowest = Math.Min(lowest, efficiency);
            }

            // 全部同じ種類なら P コア / E コアの CPU ではない
            if (cores.Count == 0 || highest == lowest)
            {
                return;
            }

            int performanceCount = 0;
            int efficiencyCount = 0;
            foreach (var (mask, efficiency) in cores)
            {
                if (efficiency == highest)
                {
                    _performanceMask |= mask;
                    performanceCount++;
                }
                else
                {
                    _efficiencyMask |= mask;
                    efficiencyCount++;
                }
            }

            parts.Add($"P {performanceCount}, E {efficiencyCount}");
        }

        private static void DetectCache(List<(ulong mask, ulong size)> caches, List<string> parts)
        {
            if (caches.Count == 0)
            {
                return;
            }

            // L3 の大きさの最小と最大
            ulong smallest = ulong.MaxValue;
            ulong largest = 0;
            var sizes = new List<string>();
            foreach (var (_, size) in caches)
            {
                smallest = Math.Min(smallest, size);
                largest = Math.Max(largest, size);
                sizes.Add($"{size / (1024 * 1024)}MB");
            }

            parts.Insert(0, "L3 " + string.Join(", ", sizes));

            // 全部同じ大きさ（X3D でない、1 CCD）や、P コア / E コアの CPU なら対象外
            if (smallest == largest || _efficiencyMask != 0)
            {
                return;
            }

            // 小さい L3 を持つ CCD のコアを集める
            foreach (var (mask, size) in caches)
            {
                if (size == smallest)
                {
                    _smallCacheMask |= mask;
                }
            }
        }

        private static bool Read(List<(ulong mask, ulong size)> caches, List<(ulong mask, int efficiency)> cores)
        {
            // 必要なバッファの大きさを聞いてから取得する
            uint length = 0;
            if (GetLogicalProcessorInformationEx(RelationAll, IntPtr.Zero, ref length)
                || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            {
                return false;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!GetLogicalProcessorInformationEx(RelationAll, buffer, ref length))
                {
                    return false;
                }

                // 可変長の要素を Size ずつ進めて、コアと L3 だけ拾う
                int offset = 0;
                while (offset < length)
                {
                    IntPtr item = buffer + offset;
                    int relationship = Marshal.ReadInt32(item, RelationshipOffset);
                    int size = Marshal.ReadInt32(item, SizeOffset);
                    if (size <= 0)
                    {
                        break;
                    }

                    if (relationship == RelationProcessorCore)
                    {
                        // グループが複数ある PC（64 論理コア超）はマスク 1 つで表せないので扱わない
                        if (Marshal.ReadInt16(item, CoreGroupOffset) != 0)
                        {
                            return false;
                        }

                        cores.Add((unchecked((ulong)Marshal.ReadInt64(item, CoreMaskOffset)),
                            Marshal.ReadByte(item, EfficiencyClassOffset)));
                    }
                    else if (relationship == RelationCache && Marshal.ReadByte(item, LevelOffset) == 3)
                    {
                        if (Marshal.ReadInt16(item, CacheGroupOffset) != 0)
                        {
                            return false;
                        }

                        caches.Add((unchecked((ulong)Marshal.ReadInt64(item, CacheMaskOffset)),
                            unchecked((uint)Marshal.ReadInt32(item, CacheSizeOffset))));
                    }

                    offset += size;
                }

                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
