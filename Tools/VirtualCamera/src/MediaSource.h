// 仮想カメラのメディアソース（Frame Server が読み込む）。映像ストリームを 1 本だけ持つ。
#pragma once

#include "Common.h"
#include "MediaStream.h"

namespace vrcast
{
    class MediaSource : public Microsoft::WRL::RuntimeClass<
        Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
        Microsoft::WRL::ChainInterfaces<IMFMediaSourceEx, IMFMediaSource, IMFMediaEventGenerator>,
        IMFGetService,
        IKsControl>
    {
    public:
        HRESULT RuntimeClassInitialize();

        // IMFMediaEventGenerator
        IFACEMETHODIMP BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state) override;
        IFACEMETHODIMP EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event) override;
        IFACEMETHODIMP GetEvent(DWORD flags, IMFMediaEvent** event) override;
        IFACEMETHODIMP QueueEvent(MediaEventType type, REFGUID extendedType, HRESULT status,
            const PROPVARIANT* eventValue) override;

        // IMFMediaSource
        IFACEMETHODIMP CreatePresentationDescriptor(IMFPresentationDescriptor** descriptor) override;
        IFACEMETHODIMP GetCharacteristics(DWORD* characteristics) override;
        IFACEMETHODIMP Pause() override;
        IFACEMETHODIMP Shutdown() override;
        IFACEMETHODIMP Start(IMFPresentationDescriptor* descriptor, const GUID* timeFormat,
            const PROPVARIANT* startPosition) override;
        IFACEMETHODIMP Stop() override;

        // IMFMediaSourceEx
        IFACEMETHODIMP GetSourceAttributes(IMFAttributes** attributes) override;
        IFACEMETHODIMP GetStreamAttributes(DWORD streamId, IMFAttributes** attributes) override;
        IFACEMETHODIMP SetD3DManager(IUnknown* manager) override;

        // IMFGetService
        IFACEMETHODIMP GetService(REFGUID service, REFIID riid, LPVOID* result) override;

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
        Microsoft::WRL::ComPtr<IMFMediaEventQueue> _queue;
        Microsoft::WRL::ComPtr<IMFAttributes> _attributes;
        Microsoft::WRL::ComPtr<IMFPresentationDescriptor> _descriptor;
        Microsoft::WRL::ComPtr<MediaStream> _stream;

        // ストリームを一度でも開始したか（2 回目以降は MEUpdatedStream を送る）
        bool _streamStarted = false;
        bool _shutdown = false;
    };
}
