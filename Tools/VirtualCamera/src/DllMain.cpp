// COM の入口（クラスの生成・regsvr32 での登録と解除）。
#include "Activator.h"

#include <string>

using namespace Microsoft::WRL;

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace vrcast
{
    CoCreatableClass(Activator);
}

namespace
{
    // HKLM\SOFTWARE\Classes\CLSID\{...}（Frame Server は HKLM の登録を読む）
    std::wstring ClassKey()
    {
        return std::wstring(L"SOFTWARE\\Classes\\CLSID\\") + vrcast::CLSID_VirtualCameraSourceString;
    }

    LSTATUS SetString(const std::wstring& key, const wchar_t* name, const std::wstring& value)
    {
        return RegSetKeyValueW(HKEY_LOCAL_MACHINE, key.c_str(), name, REG_SZ, value.c_str(),
            DWORD((value.size() + 1) * sizeof(wchar_t)));
    }
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
    }

    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** result)
{
    return Module<InProc>::GetModule().GetClassObject(clsid, riid, result);
}

STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().Terminate() ? S_OK : S_FALSE;
}

STDAPI DllRegisterServer()
{
    // この DLL 自身のパスを登録する（管理者権限が必要）
    wchar_t path[MAX_PATH];
    const DWORD length = GetModuleFileNameW(reinterpret_cast<HMODULE>(&__ImageBase), path, MAX_PATH);
    if (length == 0 || length >= MAX_PATH)
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    const std::wstring key = ClassKey();
    const std::wstring server = key + L"\\InprocServer32";
    LSTATUS status = SetString(key, nullptr, L"VRCast Virtual Camera Source");
    if (status == ERROR_SUCCESS)
    {
        status = SetString(server, nullptr, path);
    }

    if (status == ERROR_SUCCESS)
    {
        status = SetString(server, L"ThreadingModel", L"Both");
    }

    return HRESULT_FROM_WIN32(status);
}

STDAPI DllUnregisterServer()
{
    const LSTATUS status = RegDeleteTreeW(HKEY_LOCAL_MACHINE, ClassKey().c_str());
    return status == ERROR_SUCCESS || status == ERROR_FILE_NOT_FOUND ? S_OK : HRESULT_FROM_WIN32(status);
}
