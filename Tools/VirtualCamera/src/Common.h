// Media Foundation・WRL の共通ヘッダー。
#pragma once

#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mferror.h>
#include <mfvirtualcamera.h>
#include <ks.h>
#include <ksproxy.h>
#include <ksmedia.h>
#include <wrl/client.h>
#include <wrl/implements.h>
#include <wrl/module.h>
#include <wrl/wrappers/corewrappers.h>

#include "Shared.h"

// 失敗したら HRESULT を返して抜ける
#define VRC_RETURN_IF_FAILED(expr) \
    do { HRESULT hr_ = (expr); if (FAILED(hr_)) { return hr_; } } while (0)
