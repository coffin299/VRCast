#include "MediaSource.h"

using Microsoft::WRL::ComPtr;
using Microsoft::WRL::MakeAndInitialize;

namespace vrcast
{
    HRESULT MediaSource::RuntimeClassInitialize()
    {
        VRC_RETURN_IF_FAILED(MFCreateEventQueue(&_queue));
        VRC_RETURN_IF_FAILED(MFCreateAttributes(&_attributes, 4));
        VRC_RETURN_IF_FAILED(MakeAndInitialize<MediaStream>(&_stream, static_cast<IMFMediaSourceEx*>(this)));

        // ストリーム 1 本の説明を選択済みにしておく
        IMFStreamDescriptor* streams[] = { _stream->Descriptor() };
        VRC_RETURN_IF_FAILED(MFCreatePresentationDescriptor(1, streams, &_descriptor));
        VRC_RETURN_IF_FAILED(_descriptor->SelectStream(0));
        return S_OK;
    }

    IFACEMETHODIMP MediaSource::BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state)
    {
        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        return _queue->BeginGetEvent(callback, state);
    }

    IFACEMETHODIMP MediaSource::EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event)
    {
        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        return _queue->EndGetEvent(result, event);
    }

    IFACEMETHODIMP MediaSource::GetEvent(DWORD flags, IMFMediaEvent** event)
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

    IFACEMETHODIMP MediaSource::QueueEvent(MediaEventType type, REFGUID extendedType, HRESULT status,
        const PROPVARIANT* eventValue)
    {
        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        return _queue->QueueEventParamVar(type, extendedType, status, eventValue);
    }

    IFACEMETHODIMP MediaSource::CreatePresentationDescriptor(IMFPresentationDescriptor** descriptor)
    {
        if (descriptor == nullptr)
        {
            return E_POINTER;
        }

        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        return _descriptor->Clone(descriptor);
    }

    IFACEMETHODIMP MediaSource::GetCharacteristics(DWORD* characteristics)
    {
        if (characteristics == nullptr)
        {
            return E_POINTER;
        }

        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        *characteristics = MFMEDIASOURCE_IS_LIVE;
        return S_OK;
    }

    IFACEMETHODIMP MediaSource::Pause()
    {
        // ライブのカメラは一時停止できない
        return MF_E_INVALID_STATE_TRANSITION;
    }

    IFACEMETHODIMP MediaSource::Shutdown()
    {
        auto lock = _lock.Lock();
        if (_shutdown)
        {
            return MF_E_SHUTDOWN;
        }

        _shutdown = true;
        if (_stream != nullptr)
        {
            _stream->Shutdown();
        }

        if (_queue != nullptr)
        {
            _queue->Shutdown();
        }

        return S_OK;
    }

    IFACEMETHODIMP MediaSource::Start(IMFPresentationDescriptor* descriptor, const GUID* timeFormat,
        const PROPVARIANT* startPosition)
    {
        if (descriptor == nullptr || startPosition == nullptr)
        {
            return E_INVALIDARG;
        }

        if (timeFormat != nullptr && *timeFormat != GUID_NULL)
        {
            return MF_E_UNSUPPORTED_TIME_FORMAT;
        }

        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());

        DWORD count = 0;
        VRC_RETURN_IF_FAILED(descriptor->GetStreamDescriptorCount(&count));
        for (DWORD i = 0; i < count; i++)
        {
            BOOL selected = FALSE;
            ComPtr<IMFStreamDescriptor> stream;
            VRC_RETURN_IF_FAILED(descriptor->GetStreamDescriptorByIndex(i, &selected, &stream));
            DWORD id = 0;
            VRC_RETURN_IF_FAILED(stream->GetStreamIdentifier(&id));
            if (id != MediaStream::StreamId)
            {
                continue;
            }

            if (!selected)
            {
                // 選択を外されたら止める
                if (_streamStarted)
                {
                    _stream->Stop();
                }

                continue;
            }

            // 選ばれた形式でストリームを開始し、ストリームを渡す（2 回目以降は更新の通知）
            ComPtr<IMFMediaTypeHandler> handler;
            VRC_RETURN_IF_FAILED(stream->GetMediaTypeHandler(&handler));
            ComPtr<IMFMediaType> type;
            VRC_RETURN_IF_FAILED(handler->GetCurrentMediaType(&type));
            VRC_RETURN_IF_FAILED(_queue->QueueEventParamUnk(_streamStarted ? MEUpdatedStream : MENewStream,
                GUID_NULL, S_OK, static_cast<IMFMediaStream2*>(_stream.Get())));
            VRC_RETURN_IF_FAILED(_stream->Start(type.Get()));
            _streamStarted = true;
        }

        PROPVARIANT time;
        PropVariantInit(&time);
        time.vt = VT_I8;
        time.hVal.QuadPart = MFGetSystemTime();
        return _queue->QueueEventParamVar(MESourceStarted, GUID_NULL, S_OK, &time);
    }

    IFACEMETHODIMP MediaSource::Stop()
    {
        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        if (_streamStarted)
        {
            _stream->Stop();
        }

        return _queue->QueueEventParamVar(MESourceStopped, GUID_NULL, S_OK, nullptr);
    }

    IFACEMETHODIMP MediaSource::GetSourceAttributes(IMFAttributes** attributes)
    {
        if (attributes == nullptr)
        {
            return E_POINTER;
        }

        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        return _attributes.CopyTo(attributes);
    }

    IFACEMETHODIMP MediaSource::GetStreamAttributes(DWORD streamId, IMFAttributes** attributes)
    {
        if (attributes == nullptr)
        {
            return E_POINTER;
        }

        auto lock = _lock.Lock();
        VRC_RETURN_IF_FAILED(CheckShutdown());
        if (streamId != MediaStream::StreamId)
        {
            return MF_E_INVALIDSTREAMNUMBER;
        }

        IMFAttributes* stream = _stream->Attributes();
        stream->AddRef();
        *attributes = stream;
        return S_OK;
    }

    IFACEMETHODIMP MediaSource::SetD3DManager(IUnknown*)
    {
        // システムメモリのサンプルを使うため不要
        return S_OK;
    }

    IFACEMETHODIMP MediaSource::GetService(REFGUID, REFIID, LPVOID* result)
    {
        if (result != nullptr)
        {
            *result = nullptr;
        }

        return MF_E_UNSUPPORTED_SERVICE;
    }

    IFACEMETHODIMP MediaSource::KsProperty(PKSPROPERTY, ULONG, LPVOID, ULONG, ULONG*)
    {
        return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
    }

    IFACEMETHODIMP MediaSource::KsMethod(PKSMETHOD, ULONG, LPVOID, ULONG, ULONG*)
    {
        return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
    }

    IFACEMETHODIMP MediaSource::KsEvent(PKSEVENT, ULONG, LPVOID, ULONG, ULONG*)
    {
        return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
    }
}
