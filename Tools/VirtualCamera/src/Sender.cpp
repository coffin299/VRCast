// 送る側（VRCast 本体から P/Invoke で呼ぶ）: 仮想カメラの作成・削除と、共有メモリへの画像の書き込み。
#include "Common.h"

#include <cstring>
#include <mutex>
#include <thread>

using Microsoft::WRL::ComPtr;

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace
{
    // MFCreateVirtualCamera（Windows 11 の mfsensorgroup.dll のみ。古い Windows では読み込まない）
    using CreateVirtualCameraFunction = HRESULT(WINAPI*)(MFVirtualCameraType, MFVirtualCameraLifetime,
        MFVirtualCameraAccess, LPCWSTR, LPCWSTR, const GUID*, ULONG, IMFVirtualCamera**);

    // 送信の結果（C# の MediaFoundationCamera と同じ値）
    constexpr int SendOk = 0;
    constexpr int SendNoReader = 1;
    constexpr int SendBusy = 2;
    constexpr int SendInvalid = -1;

    // 受け取る側の共有メモリを探し直す間隔とロックを待つ最長時間（ミリ秒）
    constexpr ULONGLONG ReopenInterval = 1000;
    constexpr DWORD LockTimeout = 20;

    std::mutex g_lock;
    ComPtr<IMFVirtualCamera> g_camera;
    bool g_mtaReady = false;

    HANDLE g_mapping = nullptr;
    HANDLE g_mutex = nullptr;
    vrcast::FrameHeader* g_header = nullptr;
    ULONGLONG g_nextOpen = 0;

    CreateVirtualCameraFunction LoadCreateFunction()
    {
        HMODULE module = LoadLibraryExW(L"mfsensorgroup.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        return module != nullptr
            ? reinterpret_cast<CreateVirtualCameraFunction>(GetProcAddress(module, "MFCreateVirtualCamera"))
            : nullptr;
    }

    template <typename Function>
    HRESULT RunOnMta(Function function)
    {
        // Unity のメインスレッドは STA のため、MTA の別スレッドで呼んで終わるまで待つ
        HRESULT result = E_FAIL;
        std::thread worker([&]() { result = function(); });
        worker.join();
        return result;
    }

    void CloseShared()
    {
        if (g_header != nullptr)
        {
            UnmapViewOfFile(g_header);
            g_header = nullptr;
        }

        if (g_mutex != nullptr)
        {
            CloseHandle(g_mutex);
            g_mutex = nullptr;
        }

        if (g_mapping != nullptr)
        {
            CloseHandle(g_mapping);
            g_mapping = nullptr;
        }
    }

    bool OpenShared(const wchar_t* mappingName, const wchar_t* mutexName)
    {
        g_mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, mappingName);
        g_mutex = g_mapping != nullptr ? OpenMutexW(SYNCHRONIZE | MUTEX_MODIFY_STATE, FALSE, mutexName) : nullptr;
        g_header = g_mutex != nullptr
            ? static_cast<vrcast::FrameHeader*>(
                MapViewOfFile(g_mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, vrcast::MappingSize))
            : nullptr;
        if (g_header == nullptr || g_header->Magic != vrcast::FrameMagic)
        {
            CloseShared();
            return false;
        }

        return true;
    }

    bool EnsureShared(ULONGLONG now)
    {
        // 受け取る側（カメラを開いたアプリ）が現れるまでは間隔を空けて探す
        if (g_header != nullptr)
        {
            return true;
        }

        if (now < g_nextOpen)
        {
            return false;
        }

        g_nextOpen = now + ReopenInterval;
        return OpenShared(vrcast::GlobalMappingName, vrcast::GlobalMutexName)
            || OpenShared(vrcast::LocalMappingName, vrcast::LocalMutexName);
    }
}

// MFCreateVirtualCamera を使える（Windows 11 以降）なら 1
extern "C" int __stdcall VRCastVCam_IsSupported()
{
    return LoadCreateFunction() != nullptr ? 1 : 0;
}

// 仮想カメラを作って開始する（VRCast の終了・Stop で消える）。結果は HRESULT
extern "C" HRESULT __stdcall VRCastVCam_Start(const wchar_t* friendlyName)
{
    std::lock_guard<std::mutex> lock(g_lock);
    if (g_camera != nullptr)
    {
        return S_OK;
    }

    CreateVirtualCameraFunction create = LoadCreateFunction();
    if (create == nullptr)
    {
        return HRESULT_FROM_WIN32(ERROR_CALL_NOT_IMPLEMENTED);
    }

    // 呼び出し元が COM を初期化していないスレッドでも MTA として扱えるようにしておく
    if (!g_mtaReady)
    {
        CO_MTA_USAGE_COOKIE cookie;
        VRC_RETURN_IF_FAILED(CoIncrementMTAUsage(&cookie));
        g_mtaReady = true;
    }

    return RunOnMta([&]() -> HRESULT
    {
        VRC_RETURN_IF_FAILED(MFStartup(MF_VERSION, MFSTARTUP_LITE));
        ComPtr<IMFVirtualCamera> camera;
        VRC_RETURN_IF_FAILED(create(MFVirtualCameraType_SoftwareCameraSource, MFVirtualCameraLifetime_Session,
            MFVirtualCameraAccess_CurrentUser, friendlyName, vrcast::CLSID_VirtualCameraSourceString,
            nullptr, 0, &camera));
        const HRESULT hr = camera->Start(nullptr);
        if (FAILED(hr))
        {
            camera->Shutdown();
            return hr;
        }

        g_camera = camera;
        return S_OK;
    });
}

// 仮想カメラを消す
extern "C" void __stdcall VRCastVCam_Stop()
{
    std::lock_guard<std::mutex> lock(g_lock);
    CloseShared();
    g_nextOpen = 0;
    if (g_camera == nullptr)
    {
        return;
    }

    RunOnMta([&]() -> HRESULT
    {
        g_camera->Remove();
        g_camera->Shutdown();
        g_camera.Reset();
        return S_OK;
    });
}

// BGRA の画像を受け取る側へ渡す。0 = 渡した、1 = 受け取る側がいない、2 = 書き込み中で今回は見送り、-1 = 引数が不正
extern "C" int __stdcall VRCastVCam_Send(const void* pixels, int width, int height, int bottomUp)
{
    if (pixels == nullptr || width <= 0 || height <= 0
        || UINT32(width) > vrcast::MaxWidth || UINT32(height) > vrcast::MaxHeight)
    {
        return SendInvalid;
    }

    std::lock_guard<std::mutex> lock(g_lock);
    const ULONGLONG now = GetTickCount64();
    if (!EnsureShared(now))
    {
        return SendNoReader;
    }

    // 受け取る側が読まなくなったら閉じる（カメラを閉じたアプリの共有メモリを持ち続けない）
    if (now - ULONGLONG(g_header->ReaderTick) > vrcast::StaleMilliseconds)
    {
        CloseShared();
        return SendNoReader;
    }

    const DWORD wait = WaitForSingleObject(g_mutex, LockTimeout);
    if (wait != WAIT_OBJECT_0 && wait != WAIT_ABANDONED)
    {
        return SendBusy;
    }

    std::memcpy(g_header + 1, pixels, size_t(width) * height * 4);
    g_header->Width = UINT32(width);
    g_header->Height = UINT32(height);
    g_header->BottomUp = bottomUp != 0 ? 1 : 0;
    InterlockedExchange64(&g_header->SenderTick, LONG64(now));
    InterlockedIncrement64(&g_header->FrameNumber);
    ReleaseMutex(g_mutex);
    return SendOk;
}

// この DLL のフルパス（ドライバーとして登録するときのコピー元）。書いた文字数、失敗は 0
extern "C" int __stdcall VRCastVCam_GetModulePath(wchar_t* buffer, int length)
{
    if (buffer == nullptr || length <= 0)
    {
        return 0;
    }

    const DWORD written = GetModuleFileNameW(reinterpret_cast<HMODULE>(&__ImageBase), buffer, DWORD(length));
    return written > 0 && written < DWORD(length) ? int(written) : 0;
}
