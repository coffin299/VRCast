#include "Activator.h"

using Microsoft::WRL::MakeAndInitialize;

namespace vrcast
{
    HRESULT Activator::RuntimeClassInitialize()
    {
        return MFCreateAttributes(&_attributes, 1);
    }

    IFACEMETHODIMP Activator::ActivateObject(REFIID riid, void** result)
    {
        if (result == nullptr)
        {
            return E_POINTER;
        }

        // 何度呼ばれても同じメディアソースを渡す
        auto lock = _lock.Lock();
        if (_source == nullptr)
        {
            VRC_RETURN_IF_FAILED(MakeAndInitialize<MediaSource>(&_source));
        }

        return _source.CopyTo(riid, result);
    }

    IFACEMETHODIMP Activator::ShutdownObject()
    {
        auto lock = _lock.Lock();
        if (_source != nullptr)
        {
            _source->Shutdown();
            _source.Reset();
        }

        return S_OK;
    }

    IFACEMETHODIMP Activator::DetachObject()
    {
        auto lock = _lock.Lock();
        _source.Reset();
        return S_OK;
    }
}
