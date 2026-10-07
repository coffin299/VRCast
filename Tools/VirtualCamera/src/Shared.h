// VRCast の Media Foundation 仮想カメラ（Windows 11 以降）の共通定義。
// メディアソース（Frame Server のサービス内）と送信側（VRCast 本体）が同じ共有メモリを使う。
#pragma once

#include <windows.h>
#include <cstdint>

namespace vrcast
{
    // メディアソースの CLSID（regsvr32 で HKLM に登録し、MFCreateVirtualCamera の sourceId に渡す）
    // {7D3F6B2A-4C1E-4F8B-9A57-2E6C1D0B8F41}
    inline constexpr GUID CLSID_VirtualCameraSource =
        { 0x7d3f6b2a, 0x4c1e, 0x4f8b, { 0x9a, 0x57, 0x2e, 0x6c, 0x1d, 0x0b, 0x8f, 0x41 } };
    inline constexpr wchar_t CLSID_VirtualCameraSourceString[] = L"{7D3F6B2A-4C1E-4F8B-9A57-2E6C1D0B8F41}";

    // 共有メモリとロックの名前（サービスは別セッションのため Global を優先し、作れなければ Local）
    inline constexpr wchar_t GlobalMappingName[] = L"Global\\VRCastVirtualCameraFrame";
    inline constexpr wchar_t GlobalMutexName[] = L"Global\\VRCastVirtualCameraLock";
    inline constexpr wchar_t LocalMappingName[] = L"Local\\VRCastVirtualCameraFrame";
    inline constexpr wchar_t LocalMutexName[] = L"Local\\VRCastVirtualCameraLock";

    // 共有メモリの識別子（形式を変えたら値を変える）
    inline constexpr uint32_t FrameMagic = 0x56435231; // "VCR1"

    // 送れる最大の大きさ（BGRA）
    inline constexpr uint32_t MaxWidth = 1920;
    inline constexpr uint32_t MaxHeight = 1080;

    // 受け取る側が止まった・送る側が止まったとみなす時間（ミリ秒）
    inline constexpr ULONGLONG StaleMilliseconds = 3000;

    // 共有メモリの先頭（後ろに BGRA の画素が続く）
    struct FrameHeader
    {
        uint32_t Magic;
        uint32_t Width;       // 0 = まだ届いていない
        uint32_t Height;
        uint32_t BottomUp;    // 1 = 先頭の行が画像の下端
        volatile LONG64 FrameNumber;
        volatile LONG64 SenderTick;  // 送る側が最後に書いた時刻（GetTickCount64）
        volatile LONG64 ReaderTick;  // 受け取る側が最後に読んだ時刻（GetTickCount64）
    };

    inline constexpr size_t MappingSize = sizeof(FrameHeader) + size_t(MaxWidth) * MaxHeight * 4;
}
