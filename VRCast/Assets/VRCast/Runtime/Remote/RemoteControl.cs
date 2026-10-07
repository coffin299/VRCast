using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;

namespace VRCast.Remote
{
    /// <summary>
    /// 外部（Stream Deck・OSC アプリ・curl 等）からの表情の操作を受ける。アプリ全体で 1 つ。
    /// OSC（UDP）と HTTP を 127.0.0.1 にのみ bind し、スレッドを使わず Update でポーリングする（操作はメインスレッドで行う）。
    /// </summary>
    public class RemoteControl : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "Remote";

        // bind 失敗時の再試行間隔（秒）
        private const float RetryInterval = 3f;

        // 1 フレームで処理する OSC パケット数の上限
        private const int MaxPacketsPerUpdate = 64;

        // 同時に扱う HTTP 接続の数・ヘッダーの最大長・届くまで待つ秒数・送信のタイムアウト（ミリ秒）
        private const int MaxHttpClients = 8;
        private const int MaxHeaderBytes = 4096;
        private const float HttpTimeoutSeconds = 2f;
        private const int SendTimeoutMilliseconds = 500;

        // HTTP の待ち受けで溜めておく接続の数
        private const int ListenBacklog = 8;

        // 受信途中の HTTP 接続（届いたバイトと期限）
        private sealed class PendingRequest
        {
            public Socket Socket;
            public readonly byte[] Buffer = new byte[MaxHeaderBytes];
            public int Length;
            public float Deadline;
        }

        // HTTP の応答の本文（JSON）。current は選択中の表情名（空 = ニュートラル）
        [Serializable]
        private class Response
        {
            public bool ok;
            public string error = string.Empty;
            public string current = string.Empty;
            public bool manual;
            public string[] expressions = new string[0];
        }

        private readonly byte[] _buffer = new byte[65536];
        private readonly List<OscMessage> _messages = new List<OscMessage>();
        private readonly List<PendingRequest> _clients = new List<PendingRequest>();

        private AvatarSession _session;
        private AppSettings _settings;
        private Socket _osc;
        private Socket _http;
        private int _oscPort = -1;
        private int _httpPort = -1;
        private float _nextRetryTime;

        /// <summary>
        /// OSC の待ち受けの状態（表示用）。
        /// </summary>
        public string OscStatus { get; private set; } = "Disabled";

        /// <summary>
        /// HTTP の待ち受けの状態（表示用）。
        /// </summary>
        public string HttpStatus { get; private set; } = "Disabled";

        public void Initialize(AvatarSession session, AppSettings settings)
        {
            _session = session;
            _settings = settings;
        }

        private void Update()
        {
            // 未初期化なら何もしない
            if (_settings == null)
            {
                return;
            }

            // 無効化されたら閉じ、再有効化時はすぐ開けるようにする
            if (!_settings.remoteControlEnabled)
            {
                CloseOsc("Disabled");
                CloseHttp("Disabled");
                _nextRetryTime = 0f;
                return;
            }

            // ポートが変わったら閉じて、すぐ開き直す
            if (_osc != null && _oscPort != _settings.remoteOscPort)
            {
                CloseOsc("Stopped");
                _nextRetryTime = 0f;
            }

            if (_http != null && _httpPort != _settings.remoteHttpPort)
            {
                CloseHttp("Stopped");
                _nextRetryTime = 0f;
            }

            // 開いていないものを間隔を空けて（再）bind する
            if ((_osc == null || _http == null) && Time.unscaledTime >= _nextRetryTime)
            {
                _nextRetryTime = Time.unscaledTime + RetryInterval;
                OpenOsc();
                OpenHttp();
            }

            PollOsc();
            PollHttp();
        }

        private void OpenOsc()
        {
            // 既に開いていれば何もしない
            if (_osc != null)
            {
                return;
            }

            int port = _settings.remoteOscPort;
            try
            {
                // ループバックのみで待ち受け（ブロックしない）
                _osc = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
                _osc.Bind(new IPEndPoint(IPAddress.Loopback, port));
                _oscPort = port;
                OscStatus = $"Listening on 127.0.0.1:{port} (UDP)";
                VRCastLog.Info(LogCategory, $"OSC: {OscStatus}");
            }
            catch (SocketException e)
            {
                // ポート使用中など。間隔を空けて再試行
                CloseOsc($"Failed to listen on {port}: {e.SocketErrorCode}");
                VRCastLog.Warning(LogCategory, $"OSC: {OscStatus}");
            }
        }

        private void OpenHttp()
        {
            // 既に開いていれば何もしない
            if (_http != null)
            {
                return;
            }

            int port = _settings.remoteHttpPort;
            try
            {
                // ループバックのみで待ち受け（接続の受け付けもブロックしない）
                _http = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { Blocking = false };
                _http.Bind(new IPEndPoint(IPAddress.Loopback, port));
                _http.Listen(ListenBacklog);
                _httpPort = port;
                HttpStatus = $"Listening on http://127.0.0.1:{port}/";
                VRCastLog.Info(LogCategory, $"HTTP: {HttpStatus}");
            }
            catch (SocketException e)
            {
                // ポート使用中など。間隔を空けて再試行
                CloseHttp($"Failed to listen on {port}: {e.SocketErrorCode}");
                VRCastLog.Warning(LogCategory, $"HTTP: {HttpStatus}");
            }
        }

        private void PollOsc()
        {
            // 開いていなければ何もしない
            if (_osc == null)
            {
                return;
            }

            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                for (int i = 0; i < MaxPacketsPerUpdate && _osc.Available > 0; i++)
                {
                    // 1 パケット受信して、含まれるメッセージを順に操作として行う（読めないものは捨てる）
                    int length = _osc.ReceiveFrom(_buffer, ref remote);
                    _messages.Clear();
                    OscPacket.Parse(_buffer, 0, length, _messages);
                    foreach (OscMessage message in _messages)
                    {
                        if (RemoteCommand.TryParseOsc(message, out RemoteCommand command))
                        {
                            Run(command, $"OSC {message.Address}");
                        }
                    }
                }
            }
            catch (SocketException e)
            {
                // 受信エラーは閉じて再試行に任せる
                CloseOsc($"Receive error: {e.SocketErrorCode}");
                VRCastLog.Warning(LogCategory, $"OSC: {OscStatus}");
            }
        }

        private void PollHttp()
        {
            // 開いていなければ何もしない
            if (_http == null)
            {
                return;
            }

            try
            {
                // 待っている接続を受け付ける（同時接続の上限まで）
                while (_clients.Count < MaxHttpClients && _http.Poll(0, SelectMode.SelectRead))
                {
                    Socket socket = _http.Accept();
                    socket.Blocking = false;
                    _clients.Add(new PendingRequest { Socket = socket, Deadline = Time.unscaledTime + HttpTimeoutSeconds });
                }
            }
            catch (SocketException e)
            {
                // 待ち受けのエラーは閉じて再試行に任せる
                CloseHttp($"Accept error: {e.SocketErrorCode}");
                VRCastLog.Warning(LogCategory, $"HTTP: {HttpStatus}");
                return;
            }

            // 受信途中の接続を進める（終わったものは一覧から外す）
            for (int i = _clients.Count - 1; i >= 0; i--)
            {
                if (ServeClient(_clients[i]))
                {
                    CloseClient(_clients[i]);
                    _clients.RemoveAt(i);
                }
            }
        }

        private bool ServeClient(PendingRequest client)
        {
            try
            {
                // 届いている分を読み足す（切断されていれば終わり）
                if (client.Socket.Available > 0)
                {
                    int read = client.Socket.Receive(client.Buffer, client.Length, client.Buffer.Length - client.Length, SocketFlags.None);
                    if (read <= 0)
                    {
                        return true;
                    }

                    client.Length += read;
                }

                // ヘッダーの終わり（空行）まで届いたら応答する
                int end = FindHeaderEnd(client.Buffer, client.Length);
                if (end >= 0)
                {
                    Respond(client.Socket, Encoding.UTF8.GetString(client.Buffer, 0, end));
                    return true;
                }

                // ヘッダーが長すぎる・期限切れなら打ち切る
                if (client.Length >= client.Buffer.Length)
                {
                    Send(client.Socket, 431, "Request Header Fields Too Large", Error("header too large"));
                    return true;
                }

                return Time.unscaledTime > client.Deadline;
            }
            catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
            {
                // 相手が切断した等。この接続だけ捨てる
                return true;
            }
        }

        private static int FindHeaderEnd(byte[] buffer, int length)
        {
            // "\r\n\r\n" の手前の位置（無ければ -1）
            for (int i = 0; i + 3 < length; i++)
            {
                if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                {
                    return i;
                }
            }

            return -1;
        }

        private void Respond(Socket socket, string head)
        {
            // リクエスト行が読めなければ拒否
            if (!HttpRequest.TryParse(head, out HttpRequest request))
            {
                Send(socket, 400, "Bad Request", Error("bad request"));
                return;
            }

            // Web ページからの送信は拒否（閲覧中のページに勝手に操作されないように）
            if (request.FromWebPage)
            {
                Send(socket, 403, "Forbidden", Error("requests from web pages are not allowed"));
                return;
            }

            // 読み取り（GET）と操作用の POST だけ受け付ける
            if (request.Method != "GET" && request.Method != "POST")
            {
                Send(socket, 405, "Method Not Allowed", Error("use GET or POST"));
                return;
            }

            // 未知のパス・引数不足
            if (!RemoteCommand.TryParseHttp(request, out RemoteCommand command))
            {
                Send(socket, 404, "Not Found", Error("unknown command"));
                return;
            }

            // 操作して、結果と今の状態を返す
            bool ok = Run(command, $"HTTP {request.Path}", out string error);
            Response response = StatusResponse();
            response.ok = ok;
            response.error = error ?? string.Empty;
            Send(socket, ok ? 200 : 404, ok ? "OK" : "Not Found", JsonUtility.ToJson(response));
        }

        private void Run(RemoteCommand command, string source)
        {
            Run(command, source, out _);
        }

        private bool Run(RemoteCommand command, string source, out string error)
        {
            // 表示中のアバターの表情に対して行い、結果を詳細ログへ残す
            bool ok = command.Execute(CurrentExpressions(), out error);
            if (ok)
            {
                VRCastLog.Detail(LogCategory, $"{source}: {command.Action} {command.Name ?? command.Index.ToString()}");
            }
            else
            {
                VRCastLog.Warning(LogCategory, $"{source}: {error}");
            }

            return ok;
        }

        private ExpressionController CurrentExpressions()
        {
            // 表示中のアバターが無ければ null
            GameObject instance = _session != null && _session.Current != null ? _session.Current.Instance : null;
            return instance != null ? instance.GetComponent<ExpressionController>() : null;
        }

        private Response StatusResponse()
        {
            // 選択中の表情・手動で固定中か・表情の一覧（Pose タブの並び順。番号はこの位置 + 1）
            var response = new Response();
            ExpressionController expressions = CurrentExpressions();
            if (expressions == null)
            {
                return response;
            }

            response.manual = expressions.IsManual;
            response.current = expressions.Current >= 0 ? expressions.Names[expressions.Current] : string.Empty;
            var names = new string[expressions.Names.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = expressions.Names[i];
            }

            response.expressions = names;
            return response;
        }

        private static string Error(string message)
        {
            // 失敗の応答の本文
            return JsonUtility.ToJson(new Response { ok = false, error = message });
        }

        private static void Send(Socket socket, int status, string reason, string json)
        {
            // JSON の本文と最小限のヘッダーを返す（Web ページから結果を読めないよう CORS のヘッダーは付けない）
            byte[] body = Encoding.UTF8.GetBytes(json);
            string header = $"HTTP/1.1 {status} {reason}\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n\r\n";
            byte[] head = Encoding.ASCII.GetBytes(header);

            // 応答は小さいので、短いタイムアウト付きでまとめて送る
            socket.Blocking = true;
            socket.SendTimeout = SendTimeoutMilliseconds;
            socket.Send(head);
            socket.Send(body);
        }

        private static void CloseClient(PendingRequest client)
        {
            // 送り終えた・打ち切った接続を閉じる（既に切れていても構わない）
            try
            {
                client.Socket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
            {
                // 相手が先に切断していれば何もしない
            }

            client.Socket.Close();
        }

        private void CloseOsc(string status)
        {
            // ソケットがあれば閉じる
            if (_osc != null)
            {
                _osc.Close();
                _osc = null;
            }

            _oscPort = -1;
            OscStatus = status;
        }

        private void CloseHttp(string status)
        {
            // 受信途中の接続と待ち受けを閉じる
            foreach (PendingRequest client in _clients)
            {
                CloseClient(client);
            }

            _clients.Clear();
            if (_http != null)
            {
                _http.Close();
                _http = null;
            }

            _httpPort = -1;
            HttpStatus = status;
        }

        private void OnDestroy()
        {
            // 終了時にポートを解放
            CloseOsc("Stopped");
            CloseHttp("Stopped");
        }
    }
}
