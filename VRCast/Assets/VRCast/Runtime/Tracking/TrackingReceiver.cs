using System.Net;
using System.Net.Sockets;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// トラッカーの UDP 出力を受信する Provider。アプリ全体で 1 つ。
    /// 設定の入力元に合わせて OpenSeeFace（バイナリ、顔のみ）と MediaPipe（JSON、顔・腕・手）を解析し分ける。
    /// 外部からの入力を受けないよう 127.0.0.1 にのみ bind し、スレッドを使わず Update でポーリングする。
    /// </summary>
    public class TrackingReceiver : MonoBehaviour, IFaceTrackingProvider, IBodyTrackingProvider
    {
        // ログのカテゴリ名
        private const string LogCategory = "Tracking";

        // この秒数フレームが来なければ途絶とみなす
        private const float StaleSeconds = 0.5f;

        // bind 失敗時の再試行間隔（秒）
        private const float RetryInterval = 3f;

        // 1 フレームで処理するパケット数の上限（溜まった古いパケットで固まらないように）
        private const int MaxPacketsPerUpdate = 64;

        // 受信バッファ（UDP の最大長）
        private readonly byte[] _buffer = new byte[65536];
        private AppSettings _settings;
        private Socket _socket;
        private int _boundPort = -1;
        private TrackingSource _boundSource;
        private float _nextRetryTime;

        // 最新の顔・腕手フレームと受信時刻（顔が映らず腕だけのフレームもあるため別々に持つ）
        private FaceTrackingFrame _latestFace;
        private float _faceTime = float.NegativeInfinity;
        private BodyTrackingFrame _latestBody;
        private float _bodyTime = float.NegativeInfinity;

        // 受信レート計測（1 秒ごとに更新）
        private int _framesThisSecond;
        private float _rateWindowStart;
        private int _framesPerSecond;

        public string Status { get; private set; } = "Disabled";

        public void Initialize(AppSettings settings)
        {
            _settings = settings;
        }

        public bool TryGetFrame(out FaceTrackingFrame frame)
        {
            // 受信中かつ途絶していなければ最新フレームを返す
            frame = _latestFace;
            return IsFresh(_faceTime);
        }

        public bool TryGetBody(out BodyTrackingFrame frame)
        {
            // 顔と同様（OpenSeeFace では一度も更新されないため常に false）
            frame = _latestBody;
            return IsFresh(_bodyTime);
        }

        private bool IsFresh(float time)
        {
            // ソケットが開いていて、最後の受信から途絶判定の時間内
            return _socket != null && Time.unscaledTime - time <= StaleSeconds;
        }

        private void Update()
        {
            // 未初期化なら何もしない
            if (_settings == null)
            {
                return;
            }

            // 無効化されたら閉じ、再有効化時はすぐ開けるようにする
            if (!_settings.trackingEnabled)
            {
                Close("Disabled");
                _nextRetryTime = 0f;
                return;
            }

            // ポート・入力元の変更時は即時（前の入力元の値を捨てる）、未 bind 時は間隔を空けて（再）bind
            bool changed = _socket != null
                && (_boundPort != _settings.trackingPort || _boundSource != _settings.trackingSource);
            if (changed || (_socket == null && Time.unscaledTime >= _nextRetryTime))
            {
                Open(_settings.trackingPort, _settings.trackingSource);
            }

            // 溜まっているパケットを処理
            if (_socket != null)
            {
                Receive();
                UpdateStatus();
            }
        }

        private void Open(int port, TrackingSource source)
        {
            Close("Stopped");
            _nextRetryTime = Time.unscaledTime + RetryInterval;

            try
            {
                // ループバックのみで待ち受け（ブロックしない）
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
                _socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                _boundPort = port;
                _boundSource = source;
                Status = $"Listening on 127.0.0.1:{port}";
                VRCastLog.Info(LogCategory, $"{Status} ({source})");
            }
            catch (SocketException e)
            {
                // ポート使用中など。間隔を空けて再試行
                Close($"Failed to listen on {port}: {e.SocketErrorCode}");
                VRCastLog.Warning(LogCategory, Status);
            }
        }

        private void Receive()
        {
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                for (int i = 0; i < MaxPacketsPerUpdate && _socket.Available > 0; i++)
                {
                    // 1 パケット受信して入力元の形式で解析（不正なものは捨てる）
                    int length = _socket.ReceiveFrom(_buffer, ref remote);
                    if (Parse(length))
                    {
                        _framesThisSecond++;
                    }
                }
            }
            catch (SocketException e)
            {
                // 受信エラーは閉じて再試行に任せる
                Close($"Receive error: {e.SocketErrorCode}");
                VRCastLog.Warning(LogCategory, Status);
            }
        }

        private bool Parse(int length)
        {
            float now = Time.unscaledTime;

            // OpenSeeFace は顔のみ
            if (_boundSource == TrackingSource.OpenSeeFace)
            {
                if (!OpenSeeFacePacket.TryParse(_buffer, 0, length, out FaceTrackingFrame frame))
                {
                    return false;
                }

                _latestFace = frame;
                _faceTime = now;
                return true;
            }

            // MediaPipe は顔（映っていれば）と腕・手（映っていない部分は「無し」として毎回更新）
            if (!MediaPipePacket.TryParse(_buffer, length, out bool hasFace, out FaceTrackingFrame face,
                    out BodyTrackingFrame body))
            {
                return false;
            }

            if (hasFace)
            {
                _latestFace = face;
                _faceTime = now;
            }

            _latestBody = body;
            _bodyTime = now;
            return true;
        }

        private void UpdateStatus()
        {
            // 1 秒ごとに受信レートを確定
            float now = Time.unscaledTime;
            if (now - _rateWindowStart >= 1f)
            {
                _framesPerSecond = _framesThisSecond;
                _framesThisSecond = 0;
                _rateWindowStart = now;
            }

            // 顔を受信中ならレート、パケットはあるが顔が映っていなければその旨、途絶中なら待機表示
            if (TryGetFrame(out _))
            {
                Status = $"Receiving on {_boundPort} ({_framesPerSecond} fps)";
            }
            else if (TryGetBody(out _))
            {
                Status = $"Receiving on {_boundPort} ({_framesPerSecond} fps, no face)";
            }
            else
            {
                Status = $"Listening on 127.0.0.1:{_boundPort} (no data)";
            }
        }

        private void Close(string status)
        {
            // ソケットがあれば閉じる
            if (_socket != null)
            {
                _socket.Close();
                _socket = null;
            }

            _boundPort = -1;
            _faceTime = float.NegativeInfinity;
            _bodyTime = float.NegativeInfinity;
            Status = status;
        }

        private void OnDestroy()
        {
            // 終了時にポートを解放
            Close("Stopped");
        }
    }
}
