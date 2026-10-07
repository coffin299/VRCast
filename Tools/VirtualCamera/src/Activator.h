// Frame Server が CLSID から作る起動用オブジェクト。ActivateObject でメディアソースを作って渡す。
#pragma once

#include "Common.h"
#include "MediaSource.h"

namespace vrcast
{
    class DECLSPEC_UUID("7D3F6B2A-4C1E-4F8B-9A57-2E6C1D0B8F41") Activator : public Microsoft::WRL::RuntimeClass<
        Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
        Microsoft::WRL::ChainInterfaces<IMFActivate, IMFAttributes>>
    {
    public:
        HRESULT RuntimeClassInitialize();

        // IMFActivate
        IFACEMETHODIMP ActivateObject(REFIID riid, void** result) override;
        IFACEMETHODIMP ShutdownObject() override;
        IFACEMETHODIMP DetachObject() override;

        // IMFAttributes（属性の保存先へそのまま渡す）
        IFACEMETHODIMP GetItem(REFGUID key, PROPVARIANT* item) override { return _attributes->GetItem(key, item); }
        IFACEMETHODIMP GetItemType(REFGUID key, MF_ATTRIBUTE_TYPE* type) override { return _attributes->GetItemType(key, type); }
        IFACEMETHODIMP CompareItem(REFGUID key, REFPROPVARIANT item, BOOL* result) override { return _attributes->CompareItem(key, item, result); }
        IFACEMETHODIMP Compare(IMFAttributes* other, MF_ATTRIBUTES_MATCH_TYPE type, BOOL* result) override { return _attributes->Compare(other, type, result); }
        IFACEMETHODIMP GetUINT32(REFGUID key, UINT32* item) override { return _attributes->GetUINT32(key, item); }
        IFACEMETHODIMP GetUINT64(REFGUID key, UINT64* item) override { return _attributes->GetUINT64(key, item); }
        IFACEMETHODIMP GetDouble(REFGUID key, double* item) override { return _attributes->GetDouble(key, item); }
        IFACEMETHODIMP GetGUID(REFGUID key, GUID* item) override { return _attributes->GetGUID(key, item); }
        IFACEMETHODIMP GetStringLength(REFGUID key, UINT32* length) override { return _attributes->GetStringLength(key, length); }
        IFACEMETHODIMP GetString(REFGUID key, LPWSTR item, UINT32 size, UINT32* length) override { return _attributes->GetString(key, item, size, length); }
        IFACEMETHODIMP GetAllocatedString(REFGUID key, LPWSTR* item, UINT32* length) override { return _attributes->GetAllocatedString(key, item, length); }
        IFACEMETHODIMP GetBlobSize(REFGUID key, UINT32* size) override { return _attributes->GetBlobSize(key, size); }
        IFACEMETHODIMP GetBlob(REFGUID key, UINT8* buffer, UINT32 size, UINT32* blobSize) override { return _attributes->GetBlob(key, buffer, size, blobSize); }
        IFACEMETHODIMP GetAllocatedBlob(REFGUID key, UINT8** buffer, UINT32* size) override { return _attributes->GetAllocatedBlob(key, buffer, size); }
        IFACEMETHODIMP GetUnknown(REFGUID key, REFIID riid, LPVOID* item) override { return _attributes->GetUnknown(key, riid, item); }
        IFACEMETHODIMP SetItem(REFGUID key, REFPROPVARIANT item) override { return _attributes->SetItem(key, item); }
        IFACEMETHODIMP DeleteItem(REFGUID key) override { return _attributes->DeleteItem(key); }
        IFACEMETHODIMP DeleteAllItems() override { return _attributes->DeleteAllItems(); }
        IFACEMETHODIMP SetUINT32(REFGUID key, UINT32 item) override { return _attributes->SetUINT32(key, item); }
        IFACEMETHODIMP SetUINT64(REFGUID key, UINT64 item) override { return _attributes->SetUINT64(key, item); }
        IFACEMETHODIMP SetDouble(REFGUID key, double item) override { return _attributes->SetDouble(key, item); }
        IFACEMETHODIMP SetGUID(REFGUID key, REFGUID item) override { return _attributes->SetGUID(key, item); }
        IFACEMETHODIMP SetString(REFGUID key, LPCWSTR item) override { return _attributes->SetString(key, item); }
        IFACEMETHODIMP SetBlob(REFGUID key, const UINT8* buffer, UINT32 size) override { return _attributes->SetBlob(key, buffer, size); }
        IFACEMETHODIMP SetUnknown(REFGUID key, IUnknown* item) override { return _attributes->SetUnknown(key, item); }
        IFACEMETHODIMP LockStore() override { return _attributes->LockStore(); }
        IFACEMETHODIMP UnlockStore() override { return _attributes->UnlockStore(); }
        IFACEMETHODIMP GetCount(UINT32* count) override { return _attributes->GetCount(count); }
        IFACEMETHODIMP GetItemByIndex(UINT32 index, GUID* key, PROPVARIANT* item) override { return _attributes->GetItemByIndex(index, key, item); }
        IFACEMETHODIMP CopyAllItems(IMFAttributes* destination) override { return _attributes->CopyAllItems(destination); }

    private:
        Microsoft::WRL::Wrappers::CriticalSection _lock;
        Microsoft::WRL::ComPtr<IMFAttributes> _attributes;
        Microsoft::WRL::ComPtr<MediaSource> _source;
    };
}
