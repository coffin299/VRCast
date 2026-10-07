#include "MediaStream.h"

using Microsoft::WRL::ComPtr;

namespace vrcast
{
    namespace
    {
        // 30fps（1 フレームの長さは 100ns 単位）
        constexpr UINT32 FrameRate = 30;
        constexpr LONGLONG FrameDuration = 10000000LL / FrameRate;

        // 同時に貸し出せるサンプル数
        constexpr DWORD SampleCount = 10;

        // 出せる形式（先頭が既定）
        struct Format
        {
            const GUID* Subtype;
            UINT32 Width;
            UINT32 Height;
        };

        const Format Formats[] =
        {
            { &MFVideoFormat_NV12, 1920, 1080 },
            { &MFVideoFormat_NV12, 1280, 720 },
            { &MFVideoFormat_RGB32, 1920, 1080 },
            { &MFVideoFormat_RGB32, 1280, 720 },
        };

        HRESULT CreateMediaType(const Format& format, IMFMediaType** result)
        {
            ComPtr<IMFMediaType> type;
            VRC_RETURN_IF_FAILED(MFCreateMediaType(&type));

            // 1 行のバイト数と 1 フレームの大きさ（NV12 は輝度 + 半分の大きさの色差）
            const bool nv12 = *format.Subtype == MFVideoFormat_NV12;
            const UINT32 stride = nv12 ? format.Width : format.Width * 4;
            const UINT32 size = nv12 ? format.Width * format.Height * 3 / 2 : format.Width * format.Height * 4;

            VRC_RETURN_IF_FAILED(type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video));
            VRC_RETURN_IF_FAILED(type->SetGUID(MF_MT_SUBTYPE, *format.Subtype));
            VRC_RETURN_IF_FAILED(MFSetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, format.Width, format.Height));
            VRC_RETURN_IF_FAILED(MFSetAttributeRatio(type.Get(), MF_MT_FRAME_RATE, FrameRate, 1));
            VRC_RETURN_IF_FAILED(MFSetAttributeRatio(type.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1));
            VRC_RETURN_IF_FAILED(type->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
            VRC_RETURN_IF_FAILED(type->SetUINT32(MF_MT_ALL_SAMPLES_INDEPENDENT, TRUE));
            VRC_RETURN_IF_FAILED(type->SetUINT32(MF_MT_FIXED_SIZE_SAMPLES, TRUE));
            VRC_RETURN_IF_FAILED(type->SetUINT32(MF_MT_DEFAULT_STRIDE, stride));
            VRC_RETURN_IF_FAILED(type->SetUINT32(MF_MT_SAMPLE_SIZE, size));
            VRC_RETURN_IF_FAILED(type->SetUINT32(MF_MT_AVG_BITRATE, size * 8 * FrameRate));
            *result = type.Detach();
            return S_OK;
        }

        HRESULT SetDeviceStreamAttributes(IMFAttributes* attributes)
        {
            // Frame Server にカラーの映像キャプチャのストリームとして見せる
            VRC_RETURN_IF_FAILED(attributes->SetGUID(MF_DEVICESTREAM_STREAM_CATEGORY, PINNAME_VIDEO_CAPTURE));
            VRC_RETURN_IF_FAILED(attributes->SetUINT32(MF_DEVICESTREAM_STREAM_ID, MediaStream::StreamId));
            VRC_RETURN_IF_FAILED(attributes->SetUINT32(MF_DEVICESTREAM_FRAMESERVER_SHARED, 1));
            VRC_RETURN_IF_FAILED(attributes->SetUINT32(MF_DEVICESTREAM_ATTRIBUTE_FRAMESOURCE_TYPES,
                MFFrameSourceTypes_Color));
            return S_OK;
        }
    }

    HRESULT MediaStream::RuntimeClassInitialize(IMFMediaSource* parent)
    {
        _parent = parent;
        VRC_RETURN_IF_FAILED(MFCreateEventQueue(&_queue));
        VRC_RETURN_IF_FAILED(MFCreateAttributes(&_attributes, 8));
        VRC_RETURN_IF_FAILED(SetDeviceStreamAttributes(_attributes.Get()));

        // 出せる形式の一覧からストリームの説明を作り、先頭を既定にする
        constexpr DWORD count = ARRAYSIZE(Formats);
        ComPtr<IMFMediaType> types[count];
        IMFMediaType* raw[count] = {};
        for (DWORD i = 0; i < count; i++)
        {
            VRC_RETURN_IF_FAILED(CreateMediaType(Formats[i], &types[i]));
            raw[i] = types[i].Get();
        }

        VRC_RETURN_IF_FAILED(MFCreateStreamDescriptor(StreamId, count, raw, &_descriptor));
        ComPtr<IMFMediaTypeHandler> handler;
        VRC_RETURN_IF_FAILED(_descriptor->GetMediaTypeHandler(&handler));
        VRC_RETURN_IF_FAILED(handler->SetCurrentMediaType(raw[0]));
        VRC_RETURN_IF_FAILED(SetDeviceStreamAttributes(_descriptor.Get()));
        return S_OK;
    }

    HRESULT MediaStream::Start(IMFMediaType* type)
    {
        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        if (type == nullptr)
        {
            return E_INVALIDARG;
        }

        // 選ばれた形式でサンプルの貸し出し元を用意する
        VRC_RETURN_IF_FAILED(type->GetGUID(MF_MT_SUBTYPE, &_subtype));
        VRC_RETURN_IF_FAILED(MFGetAttributeSize(type, MF_MT_FRAME_SIZE, &_width, &_height));
        if (_allocator == nullptr)
        {
            VRC_RETURN_IF_FAILED(MFCreateVideoSampleAllocatorEx(IID_PPV_ARGS(&_allocator)));
        }
        else
        {
            _allocator->UninitializeSampleAllocator();
        }

        VRC_RETURN_IF_FAILED(_allocator->InitializeSampleAllocator(SampleCount, type));

        // 共有メモリを用意できなくても黒い画像は出す（VRCast からは届かない）
        _reader.Open();
        _state = MF_STREAM_STATE_RUNNING;

        PROPVARIANT time;
        PropVariantInit(&time);
        time.vt = VT_I8;
        time.hVal.QuadPart = MFGetSystemTime();
        return _queue->QueueEventParamVar(MEStreamStarted, GUID_NULL, S_OK, &time);
    }

    HRESULT MediaStream::Stop()
    {
        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        _state = MF_STREAM_STATE_STOPPED;
        _reader.Close();
        if (_allocator != nullptr)
        {
            _allocator->UninitializeSampleAllocator();
        }

        return _queue->QueueEventParamVar(MEStreamStopped, GUID_NULL, S_OK, nullptr);
    }

    void MediaStream::Shutdown()
    {
        auto lock = _lock.Lock();
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;
        _state = MF_STREAM_STATE_STOPPED;
        _reader.Close();
        if (_queue != nullptr)
        {
            _queue->Shutdown();
        }

        if (_allocator != nullptr)
        {
            _allocator->UninitializeSampleAllocator();
            _allocator.Reset();
        }

        _parent = nullptr;
    }

    IFACEMETHODIMP MediaStream::BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state)
    {
        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        return _queue->BeginGetEvent(callback, state);
    }

    IFACEMETHODIMP MediaStream::EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event)
    {
        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        return _queue->EndGetEvent(result, event);
    }

    IFACEMETHODIMP MediaStream::GetEvent(DWORD flags, IMFMediaEvent** event)
    {
        // 待つ可能性があるためロックの外で呼ぶ
        ComPtr<IMFMediaEventQueue> queue;
        {
            auto lock = _lock.Lock();
            VRC_RETURN_IF_FAILED(CheckShutdown());
            queue = _queue;
        }

        return queue->GetEvent(flags, event);
    }

    IFACEMETHODIMP MediaStream::QueueEvent(MediaEventType type, REFGUID extendedType, HRESULT status,
        const PROPVARIANT* eventValue)
    {
        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        return _queue->QueueEventParamVar(type, extendedType, status, eventValue);
    }

    IFACEMETHODIMP MediaStream::GetMediaSource(IMFMediaSource** source)
    {
        if (source == nullptr)
        {
            return E_POINTER;
        }

        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        if (_parent == nullptr)
        {
            return MF_E_SHUTDOWN;
        }

        _parent->AddRef();
        *source = _parent;
        return S_OK;
    }

    IFACEMETHODIMP MediaStream::GetStreamDescriptor(IMFStreamDescriptor** descriptor)
    {
        if (descriptor == nullptr)
        {
            return E_POINTER;
        }

        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        return _descriptor.CopyTo(descriptor);
    }

    IFACEMETHODIMP MediaStream::RequestSample(IUnknown* token)
    {
        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        if (_state != MF_STREAM_STATE_RUNNING)
        {
            return MF_E_INVALIDREQUEST;
        }

        ComPtr<IMFSample> sample;
        VRC_RETURN_IF_FAILED(_allocator->AllocateSample(&sample));
        ComPtr<IMFMediaBuffer> buffer;
        VRC_RETURN_IF_FAILED(sample->GetBufferByIndex(0, &buffer));

        // 2D バッファなら実際の行のバイト数で書く（無ければ詰めた並びとして書く）
        ComPtr<IMF2DBuffer2> buffer2d;
        if (SUCCEEDED(buffer.As(&buffer2d)))
        {
            BYTE* scanline = nullptr;
            LONG pitch = 0;
            BYTE* start = nullptr;
            DWORD length = 0;
            VRC_RETURN_IF_FAILED(buffer2d->Lock2DSize(MF2DBuffer_LockFlags_Write, &scanline, &pitch, &start, &length));
            _reader.Fill(_subtype, scanline, pitch, _width, _height);
            buffer2d->Unlock2D();
        }
        else
        {
            BYTE* data = nullptr;
            DWORD maxLength = 0;
            VRC_RETURN_IF_FAILED(buffer->Lock(&data, &maxLength, nullptr));
            const bool nv12 = _subtype == MFVideoFormat_NV12;
            const LONG pitch = LONG(nv12 ? _width : _width * 4);
            _reader.Fill(_subtype, data, pitch, _width, _height);
            buffer->Unlock();
            buffer->SetCurrentLength(nv12 ? _width * _height * 3 / 2 : _width * _height * 4);
        }

        // 時刻は Frame Server と同じシステム時刻。要求の目印（token）があれば付けて返す
        VRC_RETURN_IF_FAILED(sample->SetSampleTime(MFGetSystemTime()));
        VRC_RETURN_IF_FAILED(sample->SetSampleDuration(FrameDuration));
        if (token != nullptr)
        {
            VRC_RETURN_IF_FAILED(sample->SetUnknown(MFSampleExtension_Token, token));
        }

        return _queue->QueueEventParamUnk(MEMediaSample, GUID_NULL, S_OK, sample.Get());
    }

    IFACEMETHODIMP MediaStream::SetStreamState(MF_STREAM_STATE state)
    {
        // 状態が変わるときだけ開始・停止する（一時停止は止めずに状態だけ持つ）
        MF_STREAM_STATE current;
        {
            auto lock = _lock.Lock();
            VRC_RETURN_IF_FAILED(CheckShutdown());
            current = _state;
            if (state == MF_STREAM_STATE_PAUSED)
            {
                _state = state;
                return S_OK;
            }
        }

        if (state == current)
        {
            return S_OK;
        }

        if (state == MF_STREAM_STATE_RUNNING)
        {
            ComPtr<IMFMediaTypeHandler> handler;
            VRC_RETURN_IF_FAILED(_descriptor->GetMediaTypeHandler(&handler));
            ComPtr<IMFMediaType> type;
            VRC_RETURN_IF_FAILED(handler->GetCurrentMediaType(&type));
            return Start(type.Get());
        }

        return Stop();
    }

    IFACEMETHODIMP MediaStream::GetStreamState(MF_STREAM_STATE* state)
    {
        if (state == nullptr)
        {
            return E_POINTER;
        }

        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        *state = _state;
        return S_OK;
    }

    IFACEMETHODIMP MediaStream::KsProperty(PKSPROPERTY, ULONG, LPVOID, ULONG, ULONG*)
    {
        return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
    }

    IFACEMETHODIMP MediaStream::KsMethod(PKSMETHOD, ULONG, LPVOID, ULONG, ULONG*)
    {
        return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
    }

    IFACEMETHODIMP MediaStream::KsEvent(PKSEVENT, ULONG, LPVOID, ULONG, ULONG*)
    {
        return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
    }
}
