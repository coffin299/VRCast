using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Platform
{
    /// <summary>
    /// NVIDIA コントロールパネルの「垂直同期」を、VRCast.exe 用のドライバープロファイルへ書く。
    /// Fast Sync は Unity では出せないため、DRS の VSYNCMODE（VIRTUAL = Fast Sync）を使う。
    /// オーバーレイ除外と同じ「VRCast」プロファイルへ追記する。設定は次回起動から効くことが多い。
    /// </summary>
    public static class NvidiaVerticalSync
    {
        private const string LogCategory = "NVIDIA";
        private const string ProfileName = "VRCast";

        // NvApiDriverSettings.h の Vertical Sync / G-SYNC（アプリ単位）
        private const uint VSyncModeId = 0x00A879CF;
        private const uint VSyncPassive = 0x60925292; // アプリ側の設定に任せる
        private const uint VSyncFastSync = 0x18888888; // VSYNCMODE_VIRTUAL = Fast Sync
        private const uint VrrAppOverrideId = 0x10A879CF;
        private const uint VrrAppAllow = 0; // VRR_APP_OVERRIDE_ALLOW

        private const uint InitializeId = 0x0150E828;
        private const uint CreateSessionId = 0x0694D52E;
        private const uint DestroySessionId = 0xDAD9CFF8;
        private const uint LoadSettingsId = 0x375DBD6B;
        private const uint SaveSettingsId = 0xFCBC7E14;
        private const uint CreateProfileId = 0xCC176068;
        private const uint FindProfileByNameId = 0x7E4A9A0B;
        private const uint CreateApplicationId = 0x4347A9DE;
        private const uint FindApplicationByNameId = 0xEEE566B2;
        private const uint SetSettingId = 0x577DD202;

        private const int Ok = 0;
        private const int ProfileNotFound = -163;
        private const int ExecutableNotFound = -166;
        private const int ExecutableAlreadyInUse = -167;

        private const int UnicodeStringLength = 2048;
        private const int UnicodeStringBytes = UnicodeStringLength * 2;
        private const int ProfileSize = 4 + UnicodeStringBytes + 4 * 4;
        private const int ApplicationSize = 4 + 4 + UnicodeStringBytes * 3;
        private const int SettingValueSize = 4100;
        private const int SettingSize = 4 + UnicodeStringBytes + 4 * 5 + SettingValueSize * 2;
        private const int SettingIdOffset = 4 + UnicodeStringBytes;
        private const int SettingTypeOffset = SettingIdOffset + 4;
        private const int CurrentValueOffset = SettingIdOffset + 4 * 5 + SettingValueSize;
        private const int DwordType = 0;
        private const int AppNameOffset = 8;

        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr QueryInterface(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NoArgs();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SessionOut(out IntPtr session);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SessionOnly(IntPtr session);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ProfileByName(IntPtr session, IntPtr name, out IntPtr profile);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int CreateProfileFn(IntPtr session, IntPtr info, out IntPtr profile);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ProfileData(IntPtr session, IntPtr profile, IntPtr data);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ApplicationByName(IntPtr session, IntPtr name, out IntPtr profile, IntPtr application);

        /// <summary>
        /// NVIDIA のドライバーがあり Fast Sync を書けるなら true。
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                try
                {
                    return Get<NoArgs>(InitializeId)() == Ok;
                }
                catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// VRCast.exe のプロファイルへ垂直同期 / VRR を書く。失敗したら理由（成功なら null）。
        /// Fast Sync・VRR のドライバー上書きは次回起動から効くことが多い。
        /// </summary>
        public static string Apply(VSyncMode mode)
        {
            // Fast Sync だけ NVIDIA 必須。VRR は非 NVIDIA でも Unity 側の同期オフだけで足る（ドライバー書き込みは NVIDIA のみ）
            if (mode == VSyncMode.Vrr && !IsAvailable)
            {
                return null;
            }

            string exe = GpuSelection.ExecutablePath();
            if (Application.isEditor || exe == null)
            {
                return mode == VSyncMode.FastSync || mode == VSyncMode.Vrr
                    ? "Only available in the built app."
                    : null;
            }

            string appName = Path.GetFileName(exe);
            bool needsDriver = mode == VSyncMode.FastSync || mode == VSyncMode.Vrr;
            return Run(session =>
            {
                int status = FindProfile(session, out IntPtr profile);
                if (status == ProfileNotFound)
                {
                    if (!needsDriver)
                    {
                        return Ok;
                    }

                    status = CreateProfile(session, out profile);
                }

                if (status != Ok)
                {
                    return status;
                }

                status = FindApplication(session, appName, out IntPtr owner);
                if (status == ExecutableNotFound)
                {
                    if (!needsDriver)
                    {
                        return Ok;
                    }

                    status = AddApplication(session, profile, appName);
                }
                else if (status == Ok && owner != profile)
                {
                    VRCastLog.Warning(LogCategory, $"{appName} already belongs to another driver profile.");
                    return needsDriver ? ExecutableAlreadyInUse : Ok;
                }

                if (status != Ok)
                {
                    return status;
                }

                // Fast Sync は Vertical Sync = VIRTUAL。それ以外はアプリ制御へ戻す
                uint vsync = mode == VSyncMode.FastSync ? VSyncFastSync : VSyncPassive;
                status = SetDword(session, profile, VSyncModeId, vsync);
                if (status != Ok)
                {
                    return status;
                }

                // VRR 選択時だけ G-SYNC をこのアプリで許可（モニター・全体の G-SYNC 設定はユーザー側）
                if (mode == VSyncMode.Vrr)
                {
                    status = SetDword(session, profile, VrrAppOverrideId, VrrAppAllow);
                    if (status != Ok)
                    {
                        return status;
                    }
                }

                return Get<SessionOnly>(SaveSettingsId)(session);
            }, mode == VSyncMode.FastSync ? "set Fast Sync" : mode == VSyncMode.Vrr ? "set VRR" : "clear display sync");
        }

        private static string Run(Func<IntPtr, int> action, string operation)
        {
            IntPtr session = IntPtr.Zero;
            try
            {
                int status = Get<NoArgs>(InitializeId)();
                if (status == Ok)
                {
                    status = Get<SessionOut>(CreateSessionId)(out session);
                }

                if (status == Ok)
                {
                    status = Get<SessionOnly>(LoadSettingsId)(session);
                }

                if (status == Ok)
                {
                    status = action(session);
                }

                if (status != Ok)
                {
                    VRCastLog.Warning(LogCategory, $"Failed to {operation} (NvAPI error {status}).");
                }

                return status == Ok ? null : $"NvAPI error {status}";
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                return "NVIDIA driver not found.";
            }
            finally
            {
                if (session != IntPtr.Zero)
                {
                    Get<SessionOnly>(DestroySessionId)(session);
                }
            }
        }

        private static T Get<T>(uint id) where T : Delegate
        {
            IntPtr pointer = QueryInterface(id);
            if (pointer == IntPtr.Zero)
            {
                throw new EntryPointNotFoundException($"NvAPI function 0x{id:X8}");
            }

            return Marshal.GetDelegateForFunctionPointer<T>(pointer);
        }

        private static int FindProfile(IntPtr session, out IntPtr profile)
        {
            IntPtr name = UnicodeString(ProfileName);
            try
            {
                return Get<ProfileByName>(FindProfileByNameId)(session, name, out profile);
            }
            finally
            {
                Marshal.FreeHGlobal(name);
            }
        }

        private static int CreateProfile(IntPtr session, out IntPtr profile)
        {
            IntPtr info = Marshal.AllocHGlobal(ProfileSize);
            try
            {
                Clear(info, ProfileSize);
                Marshal.WriteInt32(info, 0, Version(ProfileSize));
                WriteString(info, 4, ProfileName);
                return Get<CreateProfileFn>(CreateProfileId)(session, info, out profile);
            }
            finally
            {
                Marshal.FreeHGlobal(info);
            }
        }

        private static int FindApplication(IntPtr session, string appName, out IntPtr owner)
        {
            IntPtr name = UnicodeString(appName);
            IntPtr application = Marshal.AllocHGlobal(ApplicationSize);
            try
            {
                Clear(application, ApplicationSize);
                Marshal.WriteInt32(application, 0, Version(ApplicationSize));
                return Get<ApplicationByName>(FindApplicationByNameId)(session, name, out owner, application);
            }
            finally
            {
                Marshal.FreeHGlobal(name);
                Marshal.FreeHGlobal(application);
            }
        }

        private static int AddApplication(IntPtr session, IntPtr profile, string appName)
        {
            IntPtr application = Marshal.AllocHGlobal(ApplicationSize);
            try
            {
                Clear(application, ApplicationSize);
                Marshal.WriteInt32(application, 0, Version(ApplicationSize));
                WriteString(application, AppNameOffset, appName);
                return Get<ProfileData>(CreateApplicationId)(session, profile, application);
            }
            finally
            {
                Marshal.FreeHGlobal(application);
            }
        }

        private static int SetDword(IntPtr session, IntPtr profile, uint settingId, uint value)
        {
            IntPtr setting = Marshal.AllocHGlobal(SettingSize);
            try
            {
                Clear(setting, SettingSize);
                Marshal.WriteInt32(setting, 0, Version(SettingSize));
                Marshal.WriteInt32(setting, SettingIdOffset, unchecked((int)settingId));
                Marshal.WriteInt32(setting, SettingTypeOffset, DwordType);
                Marshal.WriteInt32(setting, CurrentValueOffset, unchecked((int)value));
                return Get<ProfileData>(SetSettingId)(session, profile, setting);
            }
            finally
            {
                Marshal.FreeHGlobal(setting);
            }
        }

        private static int Version(int size) => size | (1 << 16);

        private static IntPtr UnicodeString(string text)
        {
            IntPtr buffer = Marshal.AllocHGlobal(UnicodeStringBytes);
            Clear(buffer, UnicodeStringBytes);
            WriteString(buffer, 0, text);
            return buffer;
        }

        private static void WriteString(IntPtr buffer, int offset, string text)
        {
            int length = Math.Min(text.Length, UnicodeStringLength - 1);
            for (int i = 0; i < length; i++)
            {
                Marshal.WriteInt16(buffer, offset + i * 2, text[i]);
            }
        }

        private static void Clear(IntPtr buffer, int size)
        {
            Marshal.Copy(new byte[size], 0, buffer, size);
        }
    }
}
