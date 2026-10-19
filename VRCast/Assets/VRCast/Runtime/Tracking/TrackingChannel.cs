using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// トラッカーの UDP 出力を受信する口 1 つ分。入力元 1 つ（OpenSeeFace・MediaPipe・VMC・iFacialMocap）の形式で解析する。
    /// 同梱トラッカーは 127.0.0.1 にのみ bind し、外部アプリ（iPhone 等から LAN 経由で届く）だけ全アドレスで待ち受ける。
    /// iFacialMocap はデータが来ない間、設定の iPhone へ送信開始の合図を同じソケットから送り続ける。
    /// スレッドを使わず、TrackingReceiver の Update から毎フレーム呼ばれてポーリングする。
    /// </summary>
    public sealed class TrackingChannel
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

        // VMC の値の保持（VMC は状態を送り続ける形式のため、パケットをまたいで値を持つ）
        private readonly VmcPacket _vmc = new VmcPacket();

        // iFacialMocap: データが来ない間に送信開始の合図を送る間隔（秒）と、次に送ってよい時刻・合図の中身
        private const float StartCommandInterval = 2f;
        private float _nextStartCommandTime;
        private static readonly byte[] StartCommandBytes = Encoding.ASCII.GetBytes(IFacialMocapPacket.StartCommand);

        // iFacialMocap: 直前に合図を送ろうとした宛先（変わったらすぐ送り直す）
        private string _lastStartAddress = string.Empty;

        // iFacialMocap: 合図の送信失敗を警告する最短間隔（秒）と、次に警告してよい時刻
        private const float SendWarningInterval = 10f;
        private float _nextSendWarningTime;

        // Windows で UDP の送信先が閉じているときに、次の受信がエラー（接続のリセット）になるのを止める制御コード
        private const int SioUdpConnReset = -1744830452;

        private AppSettings _settings;
        private Socket _socket;
        private int _boundPort = -1;
        private TrackingSource _boundSource;
        private IPAddress _boundAddress = IPAddress.Loopback;
        private float _nextRetryTime;

        // 最新の顔・腕手フレームと受信時刻（顔が映らず腕だけのフレームもあるため別々に持つ）
        private FaceTrackingFrame _latestFace;
        private float _faceTime = float.NegativeInfinity;
        private BodyTrackingFrame _latestBody;
        private float _bodyTime = float.NegativeInfinity;
        private int _bodySequence;

        // 最新の全身の姿勢（VMC のみ）と受信時刻
        private readonly HumanoidPoseFrame _latestPose = new HumanoidPoseFrame();
        private float _poseTime = float.NegativeInfinity;

        // 受信レート計測（1 秒ごとに更新）
        private int _framesThisSecond;
        private float _rateWindowStart;
        private int _framesPerSecond;

        // 診断: 受信統計を詳細ログへ出す間隔（秒）、途絶を警告するまでの秒数、不正パケットの警告の最短間隔（秒）
        private const float SummaryInterval = 5f;
        private const float SilenceWarningSeconds = 3f;
        private const float InvalidWarningInterval = 10f;

        // 診断: 顔を見失ったと記録するまでの秒数
        private const float FaceLostSeconds = 1f;

        // 不正パケットの警告に添える先頭部分の最大文字数
        private const int PreviewLength = 80;

        // 診断: 集計期間内の件数（パケット・不正・顔・腕・左手・右手）とバイト数。整数の加算だけにして毎パケットの負荷を抑える
        private int _statPackets;
        private int _statInvalid;
        private int _statFaces;
        private int _statArms;
        private int _statLeftHands;
        private int _statRightHands;
        private long _statBytes;
        private float _statStart;

        // 診断: データが届いている状態か・最後の受信時刻・顔が映っている状態か・次に不正パケットを警告してよい時刻
        private bool _receiving;
        private float _lastPacketTime = float.NegativeInfinity;
        private bool _faceVisible;
        private float _nextInvalidWarningTime;

        // 診断: 待ち受けを始めてから一度もデータが来ないことを警告するまでの秒数と、待ち受け開始時刻・受信したか・警告済みか
        private const float NoDataWarningSeconds = 15f;
        private float _listenStart;
        private bool _receivedSinceOpen;
        private bool _warnedNoData;

        public string Status { get; private set; } = "Disabled";

        /// <summary>
        /// 待ち受け中の入力元（未 bind の間は最後に待ち受けた入力元）。
        /// </summary>
        public TrackingSource BoundSource => _boundSource;

        // 途絶した後に前回のレートが残らないよう、受信中のときだけ返す
        public int FramesPerSecond => IsFresh(_faceTime) || IsFresh(_bodyTime) || IsFresh(_poseTime) ? _framesPerSecond : 0;

        public bool TryGetFrame(out FaceTrackingFrame frame)
        {
            // 受信中かつ途絶していなければ最新フレームを返す
            frame = _latestFace;
            return IsFresh(_faceTime);
        }

        public bool TryGetBody(out BodyTrackingFrame frame)
        {
            // 顔と同様（MediaPipe 以外では一度も更新されないため常に false）
            frame = _latestBody;
            return IsFresh(_bodyTime);
        }

        public bool TryGetPose(out HumanoidPoseFrame pose)
        {
            // 顔と同様（VMC 以外では一度も更新されないため常に false）
            pose = _latestPose;
            return IsFresh(_poseTime);
        }

        private bool IsFresh(float time)
        {
            // ソケットが開いていて、最後の受信から途絶判定の時間内
            return _socket != null && Time.unscaledTime - time <= StaleSeconds;
        }

        /// <summary>
        /// 入力元 source として待ち受け・受信する（毎フレーム呼ぶ）。ポート・入力元が変わったら開き直す。
        /// </summary>
        public void Update(AppSettings settings, TrackingSource source)
        {
            _settings = settings;

            // ポート・入力元の変更時は即時（前の入力元の値を捨てる）、未 bind 時は間隔を空けて（再）bind
            int port = TrackingSourceInfo.PortOf(settings, source);
            bool changed = _socket != null && (_boundPort != port || _boundSource != source);
            if (changed || (_socket == null && Time.unscaledTime >= _nextRetryTime))
            {
                Open(port, source);
            }

            // 溜まっているパケットを処理（iFacialMocap は先に送信開始の合図を送る）
            if (_socket != null)
            {
                SendStartCommandIfNeeded();
                Receive();
                UpdateStatus();
                UpdateDiagnostics();
            }
        }

        /// <summary>
        /// 待ち受けをやめる（無効化・使わない入力元への切り替え）。次の Update ですぐ開き直せるようにする。
        /// </summary>
        public void Stop(string status)
        {
            Close(status);
            _nextRetryTime = 0f;
        }

        private void Open(int port, TrackingSource source)
        {
            Close("Stopped");
            _nextRetryTime = Time.unscaledTime + RetryInterval;

            try
            {
                // 同梱トラッカーはループバックのみ、外部アプリは LAN から届くため全アドレスで待ち受け（ブロックしない）
                bool network = TrackingSourceInfo.ReceivesFromNetwork(source);
                IPAddress address = network ? IPAddress.Any : IPAddress.Loopback;
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
                if (network)
                {
                    DisableConnectionReset(_socket);
                }

                _socket.Bind(new IPEndPoint(address, port));
                _boundPort = port;
                _boundSource = source;
                _boundAddress = address;
                _listenStart = Time.unscaledTime;
                _receivedSinceOpen = false;
                _warnedNoData = false;
                _nextStartCommandTime = 0f;
                _lastStartAddress = string.Empty;

                // 前の待ち受けで受け取った VMC の値を持ち越さない
                _vmc.Reset();
                Status = $"Listening on {address}:{port}";
                VRCastLog.Info(LogCategory, $"{Status} ({source})");
            }
            catch (SocketException e)
            {
                // ポート使用中など。間隔を空けて再試行
                Close($"Failed to listen on {port}: {e.SocketErrorCode}");
                VRCastLog.Warning(LogCategory, Status);
            }
        }

        private static void DisableConnectionReset(Socket socket)
        {
            // 合図の宛先が閉じていると Windows は次の受信をエラーにするため止める（Windows 以外・未対応の環境では何もしない）
            try
            {
                socket.IOControl(SioUdpConnReset, new byte[4], null);
            }
            catch (System.Exception)
            {
                // 止められなくても受信側でリセットのエラーを無視する
            }
        }

        private void SendStartCommandIfNeeded()
        {
            // iFacialMocap 以外は送らない
            if (_boundSource != TrackingSource.IFacialMocap)
            {
                return;
            }

            // iPhone の IP が変わったら間隔を待たずすぐ送り直す
            string address = (_settings.iFacialMocapAddress ?? string.Empty).Trim();
            if (!string.Equals(address, _lastStartAddress, StringComparison.Ordinal))
            {
                _lastStartAddress = address;
                _nextStartCommandTime = 0f;
            }

            // 受信が途絶えている間だけ間隔を空けて送る
            float now = Time.unscaledTime;
            if (now < _nextStartCommandTime || now - _lastPacketTime <= StaleSeconds)
            {
                return;
            }

            _nextStartCommandTime = now + StartCommandInterval;

            // iPhone の IP アドレスが未設定・不正なら送らない（状態表示で入力を促す）
            if (!IPAddress.TryParse(address, out IPAddress phone)
                || phone.AddressFamily != AddressFamily.InterNetwork)
            {
                return;
            }

            try
            {
                // 受信と同じポートから送ると、iFacialMocap はこの PC のそのポートへ送り返してくる
                _socket.SendTo(StartCommandBytes, new IPEndPoint(phone, AppSettings.IFacialMocapPort));
            }
            catch (SocketException e)
            {
                // 宛先に届かない（別のネットワーク等）。毎回は警告しない
                if (now >= _nextSendWarningTime)
                {
                    _nextSendWarningTime = now + SendWarningInterval;
                    VRCastLog.Warning(LogCategory, $"Could not send the start command to {phone}: {e.SocketErrorCode}");
                }
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
                    _statPackets++;
                    _statBytes += length;
                    _lastPacketTime = Time.unscaledTime;

                    // 途絶後・待ち受け開始後の最初のパケットだけ送信元を記録（毎回は文字列を作らない）
                    if (!_receiving)
                    {
                        _receiving = true;
                        _receivedSinceOpen = true;
                        VRCastLog.Info(LogCategory, $"Receiving {_boundSource} data from {remote}");
                    }

                    if (Parse(length, out bool completedFrame))
                    {
                        // VMC は 1 フレームが複数パケットに分かれることがあるため、確定したフレームだけ数える
                        if (completedFrame)
                        {
                            _framesThisSecond++;
                        }
                    }
                    else
                    {
                        _statInvalid++;
                        WarnInvalidPacket(length);
                    }
                }
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
            {
                // 送った合図の宛先が閉じていた通知（iPhone のアプリが未起動等）。待ち受けは続ける
            }
            catch (SocketException e)
            {
                // 受信エラーは閉じて再試行に任せる
                Close($"Receive error: {e.SocketErrorCode}");
                VRCastLog.Warning(LogCategory, Status);
            }
        }

        private bool Parse(int length, out bool completedFrame)
        {
            float now = Time.unscaledTime;
            completedFrame = true;

            // OpenSeeFace・iFacialMocap は顔のみ（1 パケット 1 フレーム）
            if (_boundSource == TrackingSource.OpenSeeFace || _boundSource == TrackingSource.IFacialMocap)
            {
                FaceTrackingFrame frame;
                bool parsed = _boundSource == TrackingSource.OpenSeeFace
                    ? OpenSeeFacePacket.TryParse(_buffer, 0, length, out frame)
                    : IFacialMocapPacket.TryParse(_buffer, length, out frame);
                if (!parsed)
                {
                    return false;
                }

                _latestFace = frame;
                _faceTime = now;
                _statFaces++;
                return true;
            }

            // VMC は顔（Apply で 1 フレーム確定したときだけ更新）と全身の姿勢（ボーン・ルートが届くたびに更新）
            if (_boundSource == TrackingSource.Vmc)
            {
                if (!_vmc.Read(_buffer, length, out completedFrame, out FaceTrackingFrame frame, VRCastLog.DetailEnabled))
                {
                    return false;
                }

                // 送信内容（ARKit 名か・Head があるか・どのボーンが届くか）を確かめられるよう、初めてのアドレスを詳細ログへ
                foreach (string address in _vmc.NewAddresses)
                {
                    VRCastLog.Detail(LogCategory, $"VMC message: {address}");
                }

                if (completedFrame)
                {
                    _latestFace = frame;
                    _faceTime = now;
                    _statFaces++;
                }

                // 全身の姿勢は Apply を送らない送信元もあるため、ボーン・ルートが届いたパケットごとに写す
                if (_vmc.PoseChanged)
                {
                    _latestPose.CopyFrom(_vmc.Pose);
                    _latestPose.Sequence++;
                    _poseTime = now;
                }

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
                _statFaces++;
            }

            body.Sequence = ++_bodySequence;
            _latestBody = body;
            _bodyTime = now;

            // 腕（どちらか）・左手・右手が映っていたフレーム数
            if (body.Left.HasArm || body.Right.HasArm)
            {
                _statArms++;
            }

            if (body.Left.HasHand)
            {
                _statLeftHands++;
            }

            if (body.Right.HasHand)
            {
                _statRightHands++;
            }

            return true;
        }

        private void WarnInvalidPacket(int length)
        {
            // 同じ原因で毎フレーム警告しないよう間隔を空ける（件数は受信統計に出る）
            float now = Time.unscaledTime;
            if (now < _nextInvalidWarningTime)
            {
                return;
            }

            _nextInvalidWarningTime = now + InvalidWarningInterval;
            VRCastLog.Warning(LogCategory,
                $"Ignored invalid {_boundSource} packet ({length} bytes): {DescribeInvalid(length)}");
        }

        private string DescribeInvalid(int length)
        {
            // 先頭が '{' なら MediaPipe 版の JSON、'/' か '#' なら OSC、それ以外はバイナリ（OpenSeeFace）とみなして原因を推定する
            bool json = length > 0 && _buffer[0] == (byte)'{';
            bool osc = length > 0 && (_buffer[0] == (byte)'/' || _buffer[0] == (byte)'#');
            if (_boundSource == TrackingSource.Vmc)
            {
                return osc
                    ? "OSC data without VMC messages (the sender is not using the VMC protocol?)"
                    : "not OSC data (set the sender to the VMC protocol)";
            }

            if (_boundSource == TrackingSource.IFacialMocap)
            {
                string preview = Encoding.ASCII.GetString(_buffer, 0, Mathf.Min(length, PreviewLength));
                return $"no ARKit values in the text (not iFacialMocap data?): {preview}";
            }

            if (_boundSource == TrackingSource.MediaPipe)
            {
                if (!json)
                {
                    return length >= OpenSeeFacePacket.FrameSize
                        ? "looks like OpenSeeFace data. Set the input source to OpenSeeFace"
                        : "not MediaPipe JSON";
                }

                // JSON なのに読めない = 送信側と形式が違う（古い / 新しいトラッカー）可能性が高い
                string preview = Encoding.UTF8.GetString(_buffer, 0, Mathf.Min(length, PreviewLength));
                return $"JSON could not be read (tracker version mismatch? expected v{MediaPipePacket.ProtocolVersion}): {preview}";
            }

            if (json)
            {
                return "looks like MediaPipe data. Set the input source to MediaPipe";
            }

            return length < OpenSeeFacePacket.FrameSize
                ? $"too short for OpenSeeFace ({length} < {OpenSeeFacePacket.FrameSize} bytes)"
                : "contains invalid values";
        }

        private void UpdateDiagnostics()
        {
            float now = Time.unscaledTime;

            // 受信していたのに一定時間届かなければ途絶として警告（トラッカーの停止・カメラの切断・ポート違い等）
            if (_receiving && now - _lastPacketTime > SilenceWarningSeconds)
            {
                _receiving = false;

                // OpenSeeFace は顔が映っていない間は何も送らないため、その可能性も添える。VMC は送信側アプリの停止・スリープ
                string hint = _boundSource == TrackingSource.OpenSeeFace
                    ? "face out of view, tracker stopped, or camera disconnected?"
                    : TrackingSourceInfo.ReceivesFromNetwork(_boundSource)
                        ? "sender app stopped, phone asleep, or Wi-Fi disconnected?"
                        : "tracker stopped or camera disconnected?";
                VRCastLog.Warning(LogCategory, $"No {_boundSource} data for {SilenceWarningSeconds:F0} s ({hint})");
            }

            // 待ち受けを始めてから一度もデータが来ない（トラッカーが起動していない・カメラが開けない・ポート違い）
            if (!_receivedSinceOpen && !_warnedNoData && now - _listenStart > NoDataWarningSeconds)
            {
                _warnedNoData = true;
                string hint = _boundSource == TrackingSource.Vmc
                    ? "wrong IP address or port in the sender app, a different network, or blocked by Windows Firewall?"
                    : _boundSource == TrackingSource.IFacialMocap
                        ? "wrong iPhone IP address, iFacialMocap not open, a different network, or blocked by Windows Firewall?"
                        : "tracker not running, camera not opened, or port mismatch?";
                VRCastLog.Warning(LogCategory,
                    $"No {_boundSource} data on port {_boundPort} for {NoDataWarningSeconds:F0} s since listening started " +
                    $"({hint})");
            }

            // 顔が映った / 見失った瞬間（受信中のみ。詳細ログ。一瞬の見失いで交互に並ばないよう長めの猶予で判定）
            bool faceVisible = _receiving && now - _faceTime <= FaceLostSeconds;
            if (faceVisible != _faceVisible)
            {
                _faceVisible = faceVisible;
                if (_receiving)
                {
                    VRCastLog.Detail(LogCategory, $"{_boundSource}: " + (faceVisible ? "Face detected" : "Face lost"));
                }
            }

            // 一定間隔で受信統計を詳細ログへ出して集計を戻す（詳細ログ OFF なら文字列を作らない）
            if (now - _statStart < SummaryInterval)
            {
                return;
            }

            if (VRCastLog.DetailEnabled && _statPackets > 0)
            {
                VRCastLog.Detail(LogCategory, FormatSummary(now - _statStart));
            }

            ResetStatistics(now);
        }

        private string FormatSummary(float seconds)
        {
            // パケットのレート・平均サイズ・不正件数と、映っていた割合（MediaPipe は腕・手も）
            int valid = Mathf.Max(1, _statPackets - _statInvalid);
            string summary = $"{_boundSource}: {_statPackets / seconds:F1} packets/s, " +
                $"{_statBytes / Mathf.Max(1, _statPackets)} bytes/packet, invalid {_statInvalid}, " +
                $"face {Percent(_statFaces, valid)}";
            if (_boundSource == TrackingSource.MediaPipe)
            {
                summary += $", arms {Percent(_statArms, valid)}, " +
                    $"left hand {Percent(_statLeftHands, valid)}, right hand {Percent(_statRightHands, valid)}";
            }

            // VMC は届いた ARKit 名の数と頭の向きの有無、ボーンの数と全身の有無（パーフェクトシンク・頭・全身が使えるか）
            if (_boundSource == TrackingSource.Vmc)
            {
                summary += $", ARKit shapes {_vmc.ArKitShapeCount}, head {(_vmc.HasHead ? "yes" : "no")}, " +
                    $"bones {_vmc.Pose.ReceivedCount}, full body {(_vmc.Pose.HasBody ? "yes" : "no")}";
            }

            return summary;
        }

        private static string Percent(int count, int total)
        {
            // 0〜100% の整数表記
            return $"{count * 100 / total}%";
        }

        private void ResetStatistics(float now)
        {
            // 集計期間を始め直す
            _statPackets = 0;
            _statInvalid = 0;
            _statFaces = 0;
            _statArms = 0;
            _statLeftHands = 0;
            _statRightHands = 0;
            _statBytes = 0;
            _statStart = now;
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
            else if (TryGetPose(out _))
            {
                // 顔の値（Blend/Apply）を送らない送信元は、全身の姿勢だけを受信中と出す
                Status = $"Receiving on {_boundPort} (body only, no face)";
            }
            else if (_boundSource == TrackingSource.IFacialMocap && !IPAddress.TryParse(_settings.iFacialMocapAddress, out _))
            {
                // 合図を送れないので、iPhone の IP アドレスの入力を促す
                Status = $"Listening on {_boundAddress}:{_boundPort} (enter the iPhone's IP address)";
            }
            else
            {
                Status = $"Listening on {_boundAddress}:{_boundPort} (no data)";
            }
        }

        /// <summary>
        /// ソケットを閉じる（終了時にポートを解放する）。
        /// </summary>
        public void Close(string status)
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
            _poseTime = float.NegativeInfinity;
            Status = status;

            // 診断の状態も始め直す（次に開いたときの最初のパケットを記録するため）
            _receiving = false;
            _faceVisible = false;
            ResetStatistics(Time.unscaledTime);
        }
    }
}
