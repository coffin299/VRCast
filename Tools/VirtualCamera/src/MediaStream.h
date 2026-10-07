// 仮想カメラの映像ストリーム（1 本）。要求されるたびに VRCast から届いた最新の画像をサンプルにして返す。
#pragma once

#include "Common.h"
#include "FrameReader.h"

namespace vrcast
{
    class MediaStream : public Microsoft::WRL::RuntimeClass<
        Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
        Microsoft::WRL::ChainInterfaces<IMFMediaStream2, IMFMediaStream, IMFMediaEventGenerator>,
        IKsControl>
    {
    public:
        // ストリーム番号（1 本だけ）
        static constexpr DWORD StreamId = 0;

        HRESULT RuntimeClassInitialize(IMFMediaSource* parent);

        // 親（メディアソース）から呼ぶ
        HRESULT Start(IMFMediaType* type);
        HRESULT Stop();
        void Shutdown();
        IMFStreamDescriptor* Descriptor() const { return _descriptor.Get(); }
        IMFAttributes* Attributes() const { return _attributes.Get(); }

        // IMFMediaEventGenerator
        IFACEMETHODIMP BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state) override;
        IFACEMETHODIMP EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event) override;
        IFACEMETHODIMP GetEvent(DWORD flags, IMFMediaEvent** event) override;
        IFACEMETHODIMP QueueEvent(MediaEventType type, REFGUID extendedType, HRESULT status,
            const PROPVARIANT* eventValue) override;

        // IMFMediaStream
        IFACEMETHODIMP GetMediaSource(IMFMediaSource** source) override;
        IFACEMETHODIMP GetStreamDescriptor(IMFStreamDescriptor** descriptor) override;
        IFACEMETHODIMP RequestSample(IUnknown* token) override;

        // IMFMediaStream2
        IFACEMETHODIMP SetStreamState(MF_STREAM_STATE state) override;
        IFACEMETHODIMP GetStreamState(MF_STREAM_STATE* state) override;

        // IKsControl（独自のプロパティは無い）
        IFACEMETHODIMP KsProperty(PKSPROPERTY property, ULONG propertyLength, LPVOID data, ULONG dataLength,
            ULONG* bytesReturned) override;
        IFACEMETHODIMP KsMethod(PKSMETHOD method, ULONG methodLength, LPVOID data, ULONG dataLength,
            ULONG* bytesReturned) override;
        IFACEMETHODIMP KsEvent(PKSEVENT event, ULONG eventLength, LPVOID data, ULONG dataLength,
            ULONG* bytesReturned) override;

    private:
        HRESULT CheckShutdown() const { return _shutdown ? MF_E_SHUTDOWN : S_OK; }

        Microsoft::WRL::Wrappers::CriticalSection _lock;

        // 親は親がこのストリームを持つため弱参照（Shutdown で外す）
        IMFMediaSource* _parent = nullptr;

        Microsoft::WRL::ComPtr<IMFMediaEventQueue> _queue;
        Microsoft::WRL::ComPtr<IMFStreamDescriptor> _descriptor;
        Microsoft::WRL::ComPtr<IMFAttributes> _attributes;
        Microsoft::WRL::ComPtr<IMFVideoSampleAllocatorEx> _allocator;

        // 選ばれた出力形式
        GUID _subtype = GUID_NULL;
        UINT32 _width = 0;
        UINT32 _height = 0;

        MF_STREAM_STATE _state = MF_STREAM_STATE_STOPPED;
        bool _shutdown = false;
        FrameReader _reader;
    };
}
