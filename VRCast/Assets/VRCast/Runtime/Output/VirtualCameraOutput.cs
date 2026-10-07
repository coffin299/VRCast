using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
    /// カメラの描画結果を仮想カメラへ送る。メインカメラに付け、有効な間だけ毎フレーム送る。
    /// 方式は従来の UnityCapture（DirectShow）と、設定で選べる Windows 11 の Media Foundation（MediaFoundationCamera）。
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

        // Media Foundation 版の作成に失敗したときに作り直すまでの間隔（ドライバーを登録すると次の試行で始まる）
        private const float MediaFoundationRetrySeconds = 5f;

        private AppSettings _settings;
        private IntPtr _instance;

        // プラグインが見つからなかったら以降は送らない
        private bool _pluginMissing;

        // 前回の送信結果（変わったときだけ状態表示・ログを更新する。未送信は null）
        private SendResult? _lastResult;

        // Media Foundation 版の送信先、送信中の方式（切り替えたら前の方式を閉じる）、作成を次に試す時刻
        private readonly MediaFoundationCamera _mediaFoundation = new MediaFoundationCamera();
        private bool _sendingMediaFoundation;
        private float _nextMediaFoundationStart;

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

        /// <summary>
        /// Windows 11 の方式（Media Foundation）で送るなら true（設定は対応環境でのみ有効）。
        /// </summary>
        public bool UseMediaFoundation
        {
            get => _settings != null && _settings.virtualCameraMediaFoundation && MediaFoundationCamera.IsSupported;
            set
            {
                // 未初期化なら何もしない（送信の切り替えは次の描画で行う）
                if (_settings != null)
                {
                    _settings.virtualCameraMediaFoundation = value;
                }
            }
        }

        /// <summary>
        /// 使用中の方式でカメラを使うアプリに表示されるデバイス名。
        /// </summary>
        public string DeviceName => UseMediaFoundation ? MediaFoundationCamera.DeviceName : VirtualCameraInstaller.DeviceName;

        /// <summary>
        /// 使用中の方式のドライバーが同梱されていれば true。
        /// </summary>
        public bool HasDriverFiles => UseMediaFoundation ? MediaFoundationCamera.ModulePath != null : BundledFolder != null;

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
            return UseMediaFoundation
                ? VirtualCameraInstaller.GetMediaFoundationRegistration()
                : VirtualCameraInstaller.GetRegistration(BundledFolder);
        }

        /// <summary>
        /// 使用中の方式のドライバーを管理者権限で登録（install = true）または解除する。成功なら null、失敗なら理由。
        /// </summary>
        public Task<string> RunDriverAsync(bool install)
        {
            if (!UseMediaFoundation)
            {
                return VirtualCameraInstaller.RunElevatedAsync(BundledFolder, install);
            }

            // 自分のカメラが DLL を使っていると上書き・削除できないため、いったん消す（次のフレームから作り直す）
            Close();
            _nextMediaFoundationStart = Time.unscaledTime + MediaFoundationRetrySeconds;
            return VirtualCameraInstaller.RunMediaFoundationElevatedAsync(MediaFoundationCamera.ModulePath, install);
        }

        private void OnEnable()
        {
            // 従来方式でプラグインが無いと分かっていればエラーのまま（送信しないため結果が出ない）
            if (_pluginMissing && !UseMediaFoundation)
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
            // 方式を切り替えたら前の方式を閉じて開始からやり直す（全設定のリセットも含む）
            bool mediaFoundation = UseMediaFoundation;
            if (mediaFoundation != _sendingMediaFoundation)
            {
                Close();
                _sendingMediaFoundation = mediaFoundation;
                _nextMediaFoundationStart = 0f;
                State = VirtualCameraState.Starting;
                Status = "Starting";

                // 従来方式へ戻したがプラグインが無いと分かっていれば、送信しないためここでエラーにする
                if (!mediaFoundation && _pluginMissing)
                {
                    SetState(VirtualCameraState.Error, "Error: UnityCapturePlugin.dll not found");
                }
            }

            if (mediaFoundation && _settings != null)
            {
                SendMediaFoundation(source);
            }

            // 画面への表示はそのまま
            Graphics.Blit(source, destination);

            // 未初期化・プラグイン無し・Media Foundation で送っているなら UnityCapture へは送らない
            if (_settings == null || _pluginMissing || mediaFoundation)
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

        private void SendMediaFoundation(RenderTexture source)
        {
            // カメラを作る（失敗したら間隔を空けて作り直す。ドライバー未登録など）
            if (!_mediaFoundation.Started)
            {
                if (Time.unscaledTime < _nextMediaFoundationStart)
                {
                    return;
                }

                _nextMediaFoundationStart = Time.unscaledTime + MediaFoundationRetrySeconds;
                int result = _mediaFoundation.Start();
                if (result < 0)
                {
                    SetState(VirtualCameraState.Error,
                        $"Error: could not create '{MediaFoundationCamera.DeviceName}' (0x{result:X8}). Install the driver");
                    return;
                }

                VRCastLog.Info(LogCategory, $"Created '{MediaFoundationCamera.DeviceName}'");
            }

            // 描画結果を読み戻して渡す（受け取る側がいるかは前回の読み戻しの結果）
            _mediaFoundation.Capture(source);
            SetState(_mediaFoundation.HasReader ? VirtualCameraState.Sending : VirtualCameraState.WaitingForApp,
                _mediaFoundation.HasReader
                    ? $"Sending to '{MediaFoundationCamera.DeviceName}'"
                    : $"Waiting for an app to open '{MediaFoundationCamera.DeviceName}'");
        }

        private void SetState(VirtualCameraState state, string status)
        {
            // 変わったときだけ更新し、エラーはログに残す
            if (State == state && Status == status)
            {
                return;
            }

            State = state;
            Status = status;
            if (state == VirtualCameraState.Error)
            {
                VRCastLog.Warning(LogCategory, status);
            }
        }

        private void Report(SendResult result)
        {
            // 同じ結果が続く間は何もしない
            if (_lastResult == result)
            {
                return;
            }

            // エラーだけログに残す（受け取るアプリが無い・フレーム落ちは通常の状態）
            _lastResult = result;
            SetState(StateOf(result), Describe(result));
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

            // Media Foundation 版のカメラと読み戻し用のテクスチャも消す（作っていなければ DLL は呼ばない）
            _mediaFoundation.Stop();
        }
    }
}
