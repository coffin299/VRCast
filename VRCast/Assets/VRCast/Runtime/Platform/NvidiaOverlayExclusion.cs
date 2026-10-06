using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Platform
{
    /// <summary>
    /// NVIDIA のオーバーレイ（ShadowPlay / インスタントリプレイ）に VRCast をゲームとして検知させないための、ドライバー設定（DRS）の操作。
    /// NVIDIA は公式の手段を用意していないため、広く使われている非公開の設定（0x809D5F60 = 0x10000000）を、
    /// VRCast.exe 用のプロファイル「VRCast」に書く。ドライバーや NVIDIA App の更新で効かなくなる可能性がある。
    /// 設定は起動時にしか読まれないので、反映には VRCast の再起動が要る。NVIDIA の GPU が無い PC では使えない。
    /// </summary>
    public static class NvidiaOverlayExclusion
    {
        // ログのカテゴリ名
        private const string LogCategory = "NVIDIA";

        // 作るプロファイルの名前
        private const string ProfileName = "VRCast";

        // オーバーレイを外す非公開の設定と値（NVIDIA Profile Inspector 等で使われているもの）
        private const uint OverlaySettingId = 0x809D5F60;
        private const uint OverlayDisabledValue = 0x10000000;

        // NvAPI の関数 ID（nvapi_QueryInterface に渡す。公開ヘッダー nvapi_interface.h の値）
        private const uint InitializeId = 0x0150E828;
        private const uint CreateSessionId = 0x0694D52E;
        private const uint DestroySessionId = 0xDAD9CFF8;
        private const uint LoadSettingsId = 0x375DBD6B;
        private const uint SaveSettingsId = 0xFCBC7E14;
        private const uint CreateProfileId = 0xCC176068;
        private const uint DeleteProfileId = 0x17093206;
        private const uint FindProfileByNameId = 0x7E4A9A0B;
        private const uint CreateApplicationId = 0x4347A9DE;
        private const uint FindApplicationByNameId = 0xEEE566B2;
        private const uint SetSettingId = 0x577DD202;
        private const uint GetSettingId = 0x73BF8338;

        // NvAPI の戻り値（0 = 成功）
        private const int Ok = 0;
        private const int ProfileNotFound = -163;
        private const int SettingNotFound = -160;
        private const int ExecutableNotFound = -166;
        private const int ExecutableAlreadyInUse = -167;

        // NvAPI_UnicodeString の長さ（文字）と、構造体のバイト数（公開ヘッダーの NVDRS_PROFILE_V1 / NVDRS_APPLICATION_V1 / NVDRS_SETTING_V1）
        private const int UnicodeStringLength = 2048;
        private const int UnicodeStringBytes = UnicodeStringLength * 2;
        private const int ProfileSize = 4 + UnicodeStringBytes + 4 * 4;
        private const int ApplicationSize = 4 + 4 + UnicodeStringBytes * 3;
        private const int SettingValueSize = 4100;
        private const int SettingSize = 4 + UnicodeStringBytes + 4 * 5 + SettingValueSize * 2;

        // NVDRS_SETTING 内の位置（設定 ID・種類・現在値）と、DWORD の種類
        private const int SettingIdOffset = 4 + UnicodeStringBytes;
        private const int SettingTypeOffset = SettingIdOffset + 4;
        private const int CurrentValueOffset = SettingIdOffset + 4 * 5 + SettingValueSize;
        private const int DwordType = 0;

        // NVDRS_APPLICATION 内のアプリ名の位置
        private const int AppNameOffset = 8;

        /// <summary>
        /// 現在の状態。
        /// </summary>
        public enum State
        {
            Unavailable,
            Excluded,
            NotExcluded,
        }

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
        private delegate int ProfileOnly(IntPtr session, IntPtr profile);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ProfileData(IntPtr session, IntPtr profile, IntPtr data);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ApplicationByName(IntPtr session, IntPtr name, out IntPtr profile, IntPtr application);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetSettingFn(IntPtr session, IntPtr profile, uint settingId, IntPtr setting);

        /// <summary>
        /// 今の状態を調べる（NVIDIA の GPU・ドライバーが無ければ Unavailable）。
        /// </summary>
        public static State Query()
        {
            State state = State.Unavailable;
            Run(session =>
            {
                // プロファイルが無ければ未設定
                int status = FindProfile(session, out IntPtr profile);
                if (status == ProfileNotFound)
                {
                    state = State.NotExcluded;
                    return Ok;
                }

                if (status != Ok)
                {
                    return status;
                }

                // 設定の現在値がオーバーレイを外す値なら設定済み
                IntPtr setting = Marshal.AllocHGlobal(SettingSize);
                try
                {
                    Clear(setting, SettingSize);
                    Marshal.WriteInt32(setting, 0, Version(SettingSize));
                    status = Get<GetSettingFn>(GetSettingId)(session, profile, OverlaySettingId, setting);
                    bool excluded = status == Ok
                        && unchecked((uint)Marshal.ReadInt32(setting, CurrentValueOffset)) == OverlayDisabledValue;
                    state = excluded ? State.Excluded : State.NotExcluded;
                    return status == SettingNotFound ? Ok : status;
                }
                finally
                {
                    Marshal.FreeHGlobal(setting);
                }
            });
            return state;
        }

        /// <summary>
        /// VRCast.exe をオーバーレイの対象から外す。失敗したら理由（成功なら null）。
        /// </summary>
        public static string Exclude()
        {
            // エディターでは Unity.exe を登録してしまうので使わない
            string exe = GpuSelection.ExecutablePath();
            if (Application.isEditor || exe == null)
            {
                return "Only available in the built app.";
            }

            string appName = Path.GetFileName(exe);
            return Run(session =>
            {
                // VRCast のプロファイルを探し、無ければ作る
                int status = FindProfile(session, out IntPtr profile);
                if (status == ProfileNotFound)
                {
                    status = CreateProfile(session, out profile);
                }

                if (status != Ok)
                {
                    return status;
                }

                // VRCast.exe がどのプロファイルにも無ければ VRCast のプロファイルへ登録する
                status = FindApplication(session, appName, out IntPtr owner);
                if (status == ExecutableNotFound)
                {
                    status = AddApplication(session, profile, appName);
                }
                else if (status == Ok && owner != profile)
                {
                    // 既に別のプロファイル（NVIDIA の既定等）に入っていれば、それを書き換えない
                    VRCastLog.Warning(LogCategory, $"{appName} already belongs to another driver profile.");
                    return ExecutableAlreadyInUse;
                }

                if (status != Ok)
                {
                    return status;
                }

                // オーバーレイを外す値を書いて保存する
                status = SetDword(session, profile, OverlaySettingId, OverlayDisabledValue);
                return status == Ok ? Get<SessionOnly>(SaveSettingsId)(session) : status;
            }, "exclude");
        }

        /// <summary>
        /// オーバーレイの対象へ戻す（作ったプロファイルを消す）。失敗したら理由（成功なら null）。
        /// </summary>
        public static string Restore()
        {
            return Run(session =>
            {
                // プロファイルが無ければ戻す必要はない
                int status = FindProfile(session, out IntPtr profile);
                if (status == ProfileNotFound)
                {
                    return Ok;
                }

                if (status != Ok)
                {
                    return status;
                }

                // プロファイルごと消して保存する
                status = Get<ProfileOnly>(DeleteProfileId)(session, profile);
                return status == Ok ? Get<SessionOnly>(SaveSettingsId)(session) : status;
            }, "restore");
        }

        private static string Run(Func<IntPtr, int> action, string operation = null)
        {
            IntPtr session = IntPtr.Zero;
            try
            {
                // NvAPI を初期化してセッションを作り、今のドライバー設定を読み込む
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

                // 失敗したら番号を記録して返す（状態の確認は記録しない）
                if (status != Ok && operation != null)
                {
                    VRCastLog.Warning(LogCategory, $"Failed to {operation} the overlay setting (NvAPI error {status}).");
                }

                return status == Ok ? null : $"NvAPI error {status}";
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                // NVIDIA のドライバーが無い
                return "NVIDIA driver not found.";
            }
            finally
            {
                // セッションは必ず閉じる
                if (session != IntPtr.Zero)
                {
                    Get<SessionOnly>(DestroySessionId)(session);
                }
            }
        }

        private static T Get<T>(uint id) where T : Delegate
        {
            // 関数が見つからない（古いドライバー等）ときは DLL 不在と同じ扱いにする
            IntPtr pointer = QueryInterface(id);
            if (pointer == IntPtr.Zero)
            {
                throw new EntryPointNotFoundException($"NvAPI function 0x{id:X8}");
            }

            return Marshal.GetDelegateForFunctionPointer<T>(pointer);
        }

        private static int FindProfile(IntPtr session, out IntPtr profile)
        {
            // 名前は NvAPI_UnicodeString（固定長）で渡す
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
            // NVDRS_PROFILE: バージョンと名前だけ入れる
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
            // 見つかったアプリの情報は使わないが、バージョンを入れた入れ物が要る
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
            // NVDRS_APPLICATION: バージョンとアプリ名（exe のファイル名）だけ入れる
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
            // NVDRS_SETTING: バージョン・設定 ID・種類（DWORD）・現在値だけ入れる
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

        private static int Version(int size)
        {
            // NvAPI の構造体バージョン（バイト数 | バージョン番号 1 << 16）
            return size | (1 << 16);
        }

        private static IntPtr UnicodeString(string text)
        {
            // 固定長の NvAPI_UnicodeString を確保して書く（呼び出し側で解放）
            IntPtr buffer = Marshal.AllocHGlobal(UnicodeStringBytes);
            Clear(buffer, UnicodeStringBytes);
            WriteString(buffer, 0, text);
            return buffer;
        }

        private static void WriteString(IntPtr buffer, int offset, string text)
        {
            // 終端の 0 を残すため最大長 - 1 文字まで書く（残りは Clear 済みの 0）
            int length = Math.Min(text.Length, UnicodeStringLength - 1);
            for (int i = 0; i < length; i++)
            {
                Marshal.WriteInt16(buffer, offset + i * 2, text[i]);
            }
        }

        private static void Clear(IntPtr buffer, int size)
        {
            // AllocHGlobal は中身が不定なので 0 で埋める
            Marshal.Copy(new byte[size], 0, buffer, size);
        }
    }
}
