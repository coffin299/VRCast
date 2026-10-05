using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VRCast.Platform
{
    /// <summary>
    /// DXGI が列挙する GPU（アダプター）1 つ分。Index は Unity の起動引数（-force-device-index / -adapter）に渡す番号。
    /// </summary>
    public readonly struct GpuAdapter
    {
        public readonly int Index;
        public readonly string Name;

        public GpuAdapter(int index, string name)
        {
            Index = index;
            Name = name;
        }
    }

    /// <summary>
    /// DXGI で GPU を列挙する。COM のインターフェース定義に依存しないよう、関数テーブル（vtable）を直接呼ぶ。
    /// 列挙順は Windows の「グラフィックの設定」（GpuPreference）の影響を受けるため、番号ではなく名前で保存して起動時に番号へ解決する。
    /// </summary>
    public static class GpuAdapters
    {
        // IDXGIFactory1 のインターフェース ID
        private static readonly Guid FactoryId = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");

        // vtable の位置（IUnknown 3 + IDXGIObject 4 + IDXGIFactory 5 → EnumAdapters1、IUnknown 3 + IDXGIObject 4 + IDXGIAdapter 3 → GetDesc1）
        private const int ReleaseSlot = 2;
        private const int EnumAdapters1Slot = 12;
        private const int GetDesc1Slot = 10;

        // ソフトウェア描画のアダプター（Microsoft Basic Render Driver）の印
        private const uint SoftwareFlag = 0x2;

        // 列挙の上限（壊れたドライバーで終わらない場合の保険）
        private const int MaxAdapters = 16;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct AdapterDesc1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;

            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public UIntPtr DedicatedVideoMemory;
            public UIntPtr DedicatedSystemMemory;
            public UIntPtr SharedSystemMemory;
            public uint LuidLowPart;
            public int LuidHighPart;
            public uint Flags;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumAdapters1Fn(IntPtr self, uint index, out IntPtr adapter);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDesc1Fn(IntPtr self, out AdapterDesc1 desc);

        [DllImport("dxgi.dll")]
        private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

        /// <summary>
        /// ハードウェアの GPU を列挙順に返す（ソフトウェア描画は除く。番号は除いた分も数えたまま）。失敗時は空。
        /// </summary>
        public static List<GpuAdapter> Enumerate()
        {
            var result = new List<GpuAdapter>();
            IntPtr factory;
            try
            {
                // DXGI のファクトリーを作る（失敗なら空）
                Guid id = FactoryId;
                if (CreateDXGIFactory1(ref id, out factory) < 0 || factory == IntPtr.Zero)
                {
                    return result;
                }
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                // DXGI が無い環境（Windows 以外）
                return result;
            }

            try
            {
                var enumAdapters = Method<EnumAdapters1Fn>(factory, EnumAdapters1Slot);
                for (uint i = 0; i < MaxAdapters; i++)
                {
                    // DXGI_ERROR_NOT_FOUND 等（負の値）で列挙終了
                    if (enumAdapters(factory, i, out IntPtr adapter) < 0 || adapter == IntPtr.Zero)
                    {
                        break;
                    }

                    try
                    {
                        // 名前を読み、ソフトウェア描画以外を追加
                        var getDesc = Method<GetDesc1Fn>(adapter, GetDesc1Slot);
                        if (getDesc(adapter, out AdapterDesc1 desc) >= 0 && (desc.Flags & SoftwareFlag) == 0)
                        {
                            result.Add(new GpuAdapter((int)i, desc.Description.Trim()));
                        }
                    }
                    finally
                    {
                        Release(adapter);
                    }
                }
            }
            finally
            {
                Release(factory);
            }

            return result;
        }

        /// <summary>
        /// 名前が一致する最初の GPU の番号。見つからなければ -1。
        /// </summary>
        public static int IndexOf(IReadOnlyList<GpuAdapter> adapters, string name)
        {
            foreach (GpuAdapter adapter in adapters)
            {
                // 名前は完全一致（ドライバーが返す名前そのまま）
                if (adapter.Name == name)
                {
                    return adapter.Index;
                }
            }

            return -1;
        }

        private static T Method<T>(IntPtr comObject, int slot) where T : Delegate
        {
            // オブジェクトの先頭が vtable へのポインター、vtable の slot 番目が関数
            IntPtr vtable = Marshal.ReadIntPtr(comObject);
            IntPtr function = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer<T>(function);
        }

        private static void Release(IntPtr comObject)
        {
            // 参照カウントを減らす
            Method<ReleaseFn>(comObject, ReleaseSlot)(comObject);
        }
    }
}
