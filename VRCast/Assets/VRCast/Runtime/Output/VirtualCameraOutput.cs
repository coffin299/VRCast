using System;
using System.Runtime.InteropServices;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Output
{
    /// <summary>
    /// 仮想カメラへの送信状態（UI で表示言語の文言にする）。
    /// </summary>
    public enum VirtualCameraState
    {
        Off,
        Starting,
        Sending,
        WaitingForApp,
        Error,
    }

    /// <summary>
    /// カメラの描画結果を仮想カメラ（UnityCapture）へ送る。メインカメラに付け、有効な間だけ毎フレーム送る。
    /// 送るのはカメラの描画結果のみで、IMGUI の操作パネルは映らない。解像度は受け取る側に合わせて拡大縮小する。
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class VirtualCameraOutput : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "VirtualCamera";

        // 送信が止まったと受け取る側が判断するまでの時間（ミリ秒）
        private const int TimeoutMilliseconds = 1000;

        // 使うデバイス番号（登録する仮想カメラは 1 台）
        private const int DeviceIndex = 0;

        // プラグインの引数・戻り値（UnityCapturePlugin.dll と同じ値）
        private enum ResizeMode
        {
            Disabled = 0,
            Linear = 1,
        }

        private enum MirrorMode
        {
            Disabled = 0,
        }

        private enum SendResult
        {
            Success = 0,
            WarningFrameSkip = 1,
            WarningCaptureInactive = 2,
            ErrorUnsupportedGraphicsDevice = 100,
            ErrorParameter = 101,
            ErrorTooLargeResolution = 102,
            ErrorTextureFormat = 103,
            ErrorReadTexture = 104,
            ErrorInvalidCaptureInstance = 200,
        }

        [DllImport("UnityCapturePlugin")]
        private static extern IntPtr CaptureCreateInstance(int deviceIndex);

        [DllImport("UnityCapturePlugin")]
        private static extern void CaptureDeleteInstance(IntPtr instance);

        [DllImport("UnityCapturePlugin")]
        private static extern SendResult CaptureSendTexture(
            IntPtr instance, IntPtr nativeTexture, int timeout, bool useDoubleBuffering, ResizeMode resizeMode,
            MirrorMode mirrorMode, bool isLinearColorSpace);

        private AppSettings _settings;
        private IntPtr _instance;

        // プラグインが見つからなかったら以降は送らない
        private bool _pluginMissing;

        // 前回の送信結果（変わったときだけ状態表示・ログを更新する。未送信は null）
        private SendResult? _lastResult;

        /// <summary>
        /// 同梱ドライバーのフォルダ（無ければ null）。
        /// </summary>
        public string BundledFolder { get; private set; }

        /// <summary>
        /// 状態の英語の説明（ログ用。エラーのときは原因を含む）。
        /// </summary>
        public string Status { get; private set; } = "Off";

        public VirtualCameraState State { get; private set; } = VirtualCameraState.Off;

        /// <summary>
        /// 仮想カメラへ送るなら true（設定に保存する）。
        /// </summary>
        public bool Enabled
        {
            get => _settings != null && _settings.virtualCameraEnabled;
            set
            {
                // 未初期化なら何もしない
                if (_settings == null)
                {
                    return;
                }

                _settings.virtualCameraEnabled = value;
                enabled = value;
            }
        }

        public void Initialize(AppSettings settings)
        {
            _settings = settings;
            BundledFolder = VirtualCameraInstaller.FindBundled(Application.streamingAssetsPath);
            VRCastLog.Info(LogCategory, $"Bundled driver: {BundledFolder ?? "not found"}");

            // 無効な間は OnRenderImage を呼ばせない（描画の中間テクスチャを作らせない）
            enabled = settings.virtualCameraEnabled;
        }

        /// <summary>
        /// 同梱ドライバーと比べた現在の登録状態。
        /// </summary>
        public VirtualCameraRegistration GetRegistration()
        {
            return VirtualCameraInstaller.GetRegistration(BundledFolder);
        }

        private void OnEnable()
        {
            // プラグインが無いと分かっていればエラーのまま（送信しないため結果が出ない）
            if (_pluginMissing)
            {
                State = VirtualCameraState.Error;
                Status = "Error: UnityCapturePlugin.dll not found";
                return;
            }

            // 最初の送信結果が出るまでは開始中として表示する
            State = VirtualCameraState.Starting;
            Status = "Starting";
        }

        private void OnDisable()
        {
            // 送信を止めて受け取る側へ停止を伝える
            Close();
            State = VirtualCameraState.Off;
            Status = "Off";
        }

        private void OnDestroy()
        {
            Close();
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            // 画面への表示はそのまま
            Graphics.Blit(source, destination);

            // 未初期化・プラグイン無しなら送らない
            if (_settings == null || _pluginMissing)
            {
                return;
            }

            try
            {
                // 初回に送信先を作り、描画結果のテクスチャを渡す（GPU 上でコピーされる）
                if (_instance == IntPtr.Zero)
                {
                    _instance = CaptureCreateInstance(DeviceIndex);
                }

                bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                SendResult result = CaptureSendTexture(_instance, source.GetNativeTexturePtr(), TimeoutMilliseconds,
                    false, ResizeMode.Linear, MirrorMode.Disabled, linear);
                Report(result);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                // 同梱されていない（開発環境で取得していない等）
                _pluginMissing = true;
                State = VirtualCameraState.Error;
                Status = "Error: UnityCapturePlugin.dll not found";
                VRCastLog.Warning(LogCategory, Status);
            }
        }

        private void Report(SendResult result)
        {
            // 同じ結果が続く間は何もしない
            if (_lastResult == result)
            {
                return;
            }

            _lastResult = result;
            State = StateOf(result);
            Status = Describe(result);

            // エラーだけログに残す（受け取るアプリが無い・フレーム落ちは通常の状態）
            if ((int)result >= (int)SendResult.ErrorUnsupportedGraphicsDevice)
            {
                VRCastLog.Warning(LogCategory, Status);
            }
        }

        private static VirtualCameraState StateOf(SendResult result)
        {
            // 送れた・間引いただけなら送信中、受け取る側が無ければ待機、それ以外はエラー
            switch (result)
            {
                case SendResult.Success:
                case SendResult.WarningFrameSkip:
                    return VirtualCameraState.Sending;
                case SendResult.WarningCaptureInactive:
                    return VirtualCameraState.WaitingForApp;
                default:
                    return VirtualCameraState.Error;
            }
        }

        private static string Describe(SendResult result)
        {
            // 状態表示の文言
            switch (result)
            {
                case SendResult.Success:
                case SendResult.WarningFrameSkip:
                    return $"Sending to '{VirtualCameraInstaller.DeviceName}'";
                case SendResult.WarningCaptureInactive:
                    return $"Waiting for an app to open '{VirtualCameraInstaller.DeviceName}'";
                case SendResult.ErrorUnsupportedGraphicsDevice:
                    return "Error: Direct3D 11 is required";
                case SendResult.ErrorTooLargeResolution:
                    return "Error: resolution too large (max 3840x2160)";
                case SendResult.ErrorTextureFormat:
                    return "Error: unsupported render texture format";
                default:
                    return "Error: " + result;
            }
        }

        private void Close()
        {
            // 送信先があれば破棄
            if (_instance != IntPtr.Zero && !_pluginMissing)
            {
                CaptureDeleteInstance(_instance);
            }

            _instance = IntPtr.Zero;
            _lastResult = null;
        }
    }
}
