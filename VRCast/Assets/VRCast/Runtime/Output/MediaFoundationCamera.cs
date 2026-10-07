using System;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCast.Output
{
    /// <summary>
    /// Windows 11 の Media Foundation 仮想カメラ（Tools/VirtualCamera でビルドする VRCastVirtualCamera.dll）への送信。
    /// 描画結果を決まった大きさへ写して GPU から非同期に読み戻し、DLL 経由で共有メモリへ書く
    /// （Frame Server に読み込まれたメディアソースが読んで、カメラを開いたアプリへ渡す）。
    /// </summary>
    public sealed class MediaFoundationCamera
    {
        /// <summary>
        /// カメラを使うアプリに表示されるデバイス名（従来方式の VRCast Camera と区別する）。
        /// </summary>
        public const string DeviceName = "VRCast Camera (MF)";

        /// <summary>
        /// DLL のファイル名（ドライバー登録時に Program Files へコピーする）。
        /// </summary>
        public const string PluginFileName = "VRCastVirtualCamera.dll";

        private const string Plugin = "VRCastVirtualCamera";

        // 送る大きさ（受け取る側の 1080p / 720p へは DLL 内で縮小する）と送る間隔（30fps）
        private const int Width = 1920;
        private const int Height = 1080;
        private const float FrameInterval = 1f / 30f;

        // 読み戻した画像は先頭の行が画像の下端（Unity のテクスチャの並び）
        private const int BottomUp = 1;

        // VRCastVCam_Send の結果（Sender.cpp と同じ値）
        private const int SendOk = 0;
        private const int SendBusy = 2;

        // DLL のパスの最大長（文字）
        private const int MaxPathLength = 1024;

        [DllImport(Plugin)]
        private static extern int VRCastVCam_IsSupported();

        [DllImport(Plugin, CharSet = CharSet.Unicode)]
        private static extern int VRCastVCam_Start(string friendlyName);

        [DllImport(Plugin)]
        private static extern void VRCastVCam_Stop();

        [DllImport(Plugin)]
        private static extern int VRCastVCam_Send(IntPtr pixels, int width, int height, int bottomUp);

        [DllImport(Plugin, CharSet = CharSet.Unicode)]
        private static extern int VRCastVCam_GetModulePath(StringBuilder buffer, int length);

        // DLL の有無・対応 OS の判定結果（初回だけ調べる）
        private static bool _checked;
        private static bool _pluginFound;
        private static bool _supported;
        private static string _modulePath;

        private RenderTexture _target;
        private bool _readbackPending;
        private float _nextCapture;

        /// <summary>
        /// DLL が同梱されていれば true。
        /// </summary>
        public static bool PluginFound
        {
            get
            {
                Check();
                return _pluginFound;
            }
        }

        /// <summary>
        /// DLL があり、OS が Media Foundation の仮想カメラに対応（Windows 11 以降）していれば true。
        /// </summary>
        public static bool IsSupported
        {
            get
            {
                Check();
                return _supported;
            }
        }

        /// <summary>
        /// 読み込んだ DLL のフルパス（ドライバー登録時のコピー元。無ければ null）。
        /// </summary>
        public static string ModulePath
        {
            get
            {
                Check();
                return _modulePath;
            }
        }

        /// <summary>
        /// 仮想カメラを作成済みなら true。
        /// </summary>
        public bool Started { get; private set; }

        /// <summary>
        /// カメラを開いているアプリがあり、画像を渡せていれば true。
        /// </summary>
        public bool HasReader { get; private set; }

        /// <summary>
        /// 仮想カメラを作って開始する（作成済みなら何もしない）。結果は HRESULT（負なら失敗）。
        /// </summary>
        public int Start()
        {
            if (Started)
            {
                return 0;
            }

            int result = VRCastVCam_Start(DeviceName);
            Started = result >= 0;
            return result;
        }

        /// <summary>
        /// 仮想カメラを消し、読み戻し用のテクスチャを解放する。
        /// </summary>
        public void Stop()
        {
            HasReader = false;
            if (Started)
            {
                VRCastVCam_Stop();
                Started = false;
            }

            if (_target != null)
            {
                _target.Release();
                UnityEngine.Object.Destroy(_target);
                _target = null;
            }
        }

        /// <summary>
        /// 描画結果を写して読み戻しを始める（30fps まで。前の読み戻しが終わるまでは次を始めない）。
        /// </summary>
        public void Capture(RenderTexture source)
        {
            if (!Started || _readbackPending || Time.unscaledTime < _nextCapture)
            {
                return;
            }

            _nextCapture = Time.unscaledTime + FrameInterval;
            if (_target == null)
            {
                // sRGB で書き込ませ、Linear の描画結果も見た目どおりの色で読み戻す
                _target = new RenderTexture(Width, Height, 0, RenderTextureFormat.BGRA32, RenderTextureReadWrite.sRGB)
                {
                    name = "VRCast Virtual Camera (MF)",
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            // 縦横比が違う場合は受け取る側の大きさへ引き伸ばす（従来方式と同じ）
            Graphics.Blit(source, _target);
            _readbackPending = true;
            AsyncGPUReadback.Request(_target, 0, OnReadback);
        }

        private unsafe void OnReadback(AsyncGPUReadbackRequest request)
        {
            _readbackPending = false;

            // 停止後・失敗した読み戻しは捨てる
            if (!Started || request.hasError)
            {
                return;
            }

            NativeArray<byte> data = request.GetData<byte>();
            if (data.Length < Width * Height * 4)
            {
                return;
            }

            // 書き込み中で見送ったときは、受け取る側がいる状態のまま
            var pixels = (IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data);
            int result = VRCastVCam_Send(pixels, Width, Height, BottomUp);
            HasReader = result == SendOk || (result == SendBusy && HasReader);
        }

        private static void Check()
        {
            if (_checked)
            {
                return;
            }

            _checked = true;
            try
            {
                // 対応 OS の判定と、ドライバー登録時に使う DLL のパス
                _supported = VRCastVCam_IsSupported() != 0;
                _pluginFound = true;
                var buffer = new StringBuilder(MaxPathLength);
                _modulePath = VRCastVCam_GetModulePath(buffer, MaxPathLength) > 0 ? buffer.ToString() : null;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                // 同梱されていない（開発環境で Tools/VirtualCamera/build.ps1 を実行していない等）
                _pluginFound = false;
                _supported = false;
                _modulePath = null;
            }
        }
    }
}
