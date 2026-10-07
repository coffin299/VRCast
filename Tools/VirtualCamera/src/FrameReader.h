// 受け取る側（Frame Server 内のメディアソース）: 共有メモリを用意し、VRCast が書いた BGRA を出力形式へ変換する。
#pragma once

#include <vector>
#include "Common.h"

namespace vrcast
{
    class FrameReader
    {
    public:
        FrameReader() = default;
        FrameReader(const FrameReader&) = delete;
        FrameReader& operator=(const FrameReader&) = delete;
        ~FrameReader() { Close(); }

        // 共有メモリとロックを作る（VRCast が開いて書き込めるよう、対話ユーザーにも書き込みを許す）
        HRESULT Open();
        void Close();

        // subtype（NV12 / RGB32）の width x height の画像を dest（1 行 pitch バイト）へ書く。
        // VRCast から届いていなければ黒で埋める
        void Fill(const GUID& subtype, BYTE* dest, LONG pitch, UINT32 width, UINT32 height);

    private:
        bool CopyLatest();

        HANDLE _mapping = nullptr;
        HANDLE _mutex = nullptr;
        FrameHeader* _header = nullptr;

        // ロック中に写した最新の画像（変換はロックの外で行う）
        std::vector<uint8_t> _frame;
        uint32_t _width = 0;
        uint32_t _height = 0;
        bool _bottomUp = false;

        // 横方向の拡大縮小で使う元の列（出力の幅が変わったら作り直す）
        std::vector<uint32_t> _columns;
        uint32_t _columnsSourceWidth = 0;
    };
}
