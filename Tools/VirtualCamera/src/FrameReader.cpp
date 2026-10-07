#include "FrameReader.h"

#include <sddl.h>
#include <cstring>

namespace vrcast
{
    namespace
    {
        // SYSTEM・管理者・LOCAL SERVICE（Frame Server）・作成者・対話ユーザー（VRCast）に読み書きを許す
        constexpr wchar_t SharedSddl[] = L"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;LS)(A;;GA;;;OW)(A;;GA;;;IU)";

        // ロックを待つ最長時間（ミリ秒。送る側の書き込みは 1 フレーム分のコピーだけ）
        constexpr DWORD LockTimeout = 15;

        // BT.601（リミテッドレンジ）の YUV
        inline uint8_t ToY(int r, int g, int b) { return uint8_t(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16); }
        inline uint8_t ToU(int r, int g, int b) { return uint8_t(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128); }
        inline uint8_t ToV(int r, int g, int b) { return uint8_t(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128); }
    }

    HRESULT FrameReader::Open()
    {
        if (_header != nullptr)
        {
            return S_OK;
        }

        PSECURITY_DESCRIPTOR descriptor = nullptr;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(SharedSddl, SDDL_REVISION_1, &descriptor, nullptr))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        SECURITY_ATTRIBUTES attributes = { sizeof(attributes), descriptor, FALSE };
        const DWORD high = DWORD(uint64_t(MappingSize) >> 32);
        const DWORD low = DWORD(uint64_t(MappingSize) & 0xFFFFFFFFu);

        // サービス（別セッション）から VRCast へ見せるため Global を優先する
        _mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, &attributes, PAGE_READWRITE, high, low, GlobalMappingName);
        const wchar_t* mutexName = GlobalMutexName;
        if (_mapping == nullptr)
        {
            _mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, &attributes, PAGE_READWRITE, high, low, LocalMappingName);
            mutexName = LocalMutexName;
        }

        if (_mapping != nullptr)
        {
            _mutex = CreateMutexW(&attributes, FALSE, mutexName);
        }

        HRESULT hr = (_mapping != nullptr && _mutex != nullptr) ? S_OK : HRESULT_FROM_WIN32(GetLastError());
        LocalFree(descriptor);
        if (FAILED(hr))
        {
            Close();
            return hr;
        }

        _header = static_cast<FrameHeader*>(MapViewOfFile(_mapping, FILE_MAP_ALL_ACCESS, 0, 0, MappingSize));
        if (_header == nullptr)
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
            Close();
            return hr;
        }

        // 開いた時点から読んでいる扱いにする（最初の要求の前に送る側が閉じてしまわないように）
        _header->Magic = FrameMagic;
        InterlockedExchange64(&_header->ReaderTick, LONG64(GetTickCount64()));
        _frame.resize(size_t(MaxWidth) * MaxHeight * 4);
        return S_OK;
    }

    void FrameReader::Close()
    {
        if (_header != nullptr)
        {
            UnmapViewOfFile(_header);
            _header = nullptr;
        }

        if (_mutex != nullptr)
        {
            CloseHandle(_mutex);
            _mutex = nullptr;
        }

        if (_mapping != nullptr)
        {
            CloseHandle(_mapping);
            _mapping = nullptr;
        }

        _width = 0;
        _height = 0;
    }

    bool FrameReader::CopyLatest()
    {
        if (_header == nullptr)
        {
            return false;
        }

        // 読んでいることを送る側へ知らせる（止まると送る側は送信をやめる）
        const ULONGLONG now = GetTickCount64();
        InterlockedExchange64(&_header->ReaderTick, LONG64(now));

        const DWORD wait = WaitForSingleObject(_mutex, LockTimeout);
        if (wait != WAIT_OBJECT_0 && wait != WAIT_ABANDONED)
        {
            // 書き込み中で待てなければ前回の画像を使う
            return _width != 0;
        }

        // 送る側が止まっていれば黒にする
        const uint32_t width = _header->Width;
        const uint32_t height = _header->Height;
        const bool fresh = now - ULONGLONG(_header->SenderTick) < StaleMilliseconds;
        const bool valid = _header->Magic == FrameMagic && width > 0 && height > 0
            && width <= MaxWidth && height <= MaxHeight && fresh;
        if (valid)
        {
            std::memcpy(_frame.data(), reinterpret_cast<const uint8_t*>(_header + 1), size_t(width) * height * 4);
            _width = width;
            _height = height;
            _bottomUp = _header->BottomUp != 0;
        }
        else
        {
            _width = 0;
            _height = 0;
        }

        ReleaseMutex(_mutex);
        return valid;
    }

    void FrameReader::Fill(const GUID& subtype, BYTE* dest, LONG pitch, UINT32 width, UINT32 height)
    {
        // pitch は負（下から上の並び）のこともあるため符号付きで計算する
        const bool nv12 = subtype == MFVideoFormat_NV12;
        const ptrdiff_t step = pitch;
        BYTE* chroma = dest + step * ptrdiff_t(height);

        // 届いていなければ黒
        if (!CopyLatest())
        {
            for (UINT32 y = 0; y < height; y++)
            {
                std::memset(dest + step * ptrdiff_t(y), nv12 ? 16 : 0, size_t(nv12 ? width : width * 4));
            }

            if (nv12)
            {
                for (UINT32 y = 0; y < height / 2; y++)
                {
                    std::memset(chroma + step * ptrdiff_t(y), 128, width);
                }
            }

            return;
        }

        // 出力の各列に対応する元の列（最近傍で拡大縮小）
        if (_columns.size() != width || _columnsSourceWidth != _width)
        {
            _columnsSourceWidth = _width;
            _columns.resize(width);
            for (UINT32 x = 0; x < width; x++)
            {
                _columns[x] = x * _width / width;
            }
        }

        for (UINT32 y = 0; y < height; y++)
        {
            // 出力は上から下。元が下から上の並びなら行を反転して読む
            uint32_t sourceY = y * _height / height;
            if (_bottomUp)
            {
                sourceY = _height - 1 - sourceY;
            }

            const uint8_t* source = _frame.data() + size_t(sourceY) * _width * 4;
            BYTE* row = dest + step * ptrdiff_t(y);
            BYTE* uv = nv12 && (y & 1) == 0 ? chroma + step * ptrdiff_t(y / 2) : nullptr;
            for (UINT32 x = 0; x < width; x++)
            {
                const uint8_t* pixel = source + size_t(_columns[x]) * 4;
                const int b = pixel[0];
                const int g = pixel[1];
                const int r = pixel[2];
                if (!nv12)
                {
                    // RGB32（B, G, R, 未使用）
                    row[x * 4 + 0] = uint8_t(b);
                    row[x * 4 + 1] = uint8_t(g);
                    row[x * 4 + 2] = uint8_t(r);
                    row[x * 4 + 3] = 255;
                    continue;
                }

                row[x] = ToY(r, g, b);

                // 色差は 2x2 画素ごとに左上の画素から求める
                if (uv != nullptr && (x & 1) == 0)
                {
                    uv[x] = ToU(r, g, b);
                    uv[x + 1] = ToV(r, g, b);
                }
            }
        }
    }
}
