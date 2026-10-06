using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using VRCast.Core;

namespace VRCast.Output
{
    /// <summary>
    /// カメラの描画結果を Spout2 で送る（OBS の Spout2 プラグイン等で受け取る）。メインカメラに付け、有効な間だけ毎フレーム送る。
    /// GPU 上でテクスチャを共有するため CPU への読み戻しが無く、透過（アルファ）もそのまま渡る。操作パネルは映らない。
    /// 送信は KlakSpout（Unlicense）のネイティブプラグイン KlakSpout.dll を直接呼ぶ（Tools/Spout/fetch.ps1 で配置）。
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class SpoutOutput : MonoBehaviour
    {
        /// <summary>
        /// 受け取る側に表示される送信元の名前。
        /// </summary>
        public const string SenderName = "VRCast";

        // ログのカテゴリ名
        private const string LogCategory = "Spout";

        // KlakSpout.dll の描画イベント番号（KlakSpout::EventID と同じ値）
        private const int UpdateSenderEvent = 0;
        private const int CloseSenderEvent = 2;

        // 描画スレッドが読み終えるまで、閉じた送信先のイベントデータを解放せずに待つフレーム数
        private const int ReleaseDelayFrames = 3;

        // 描画イベントに渡すデータ（KlakSpout::EventData と同じ並び）
        [StructLayout(LayoutKind.Sequential)]
        private struct EventData
        {
            public IntPtr Instance;
            public IntPtr Texture;
        }

        [DllImport("KlakSpout")]
        private static extern IntPtr GetRenderEventCallback();

        [DllImport("KlakSpout", CharSet = CharSet.Ansi)]
        private static extern IntPtr CreateSender(string name, int width, int height);

        // 閉じた送信先のイベントデータと、解放してよいフレーム
        private readonly List<KeyValuePair<IntPtr, int>> _retired = new List<KeyValuePair<IntPtr, int>>();

        private AppSettings _settings;
        private CommandBuffer _commands;

        // 送るテクスチャ（描画結果を毎フレーム写す）と、送信先・そのイベントデータ
        private RenderTexture _buffer;
        private IntPtr _sender;
        private IntPtr _eventData;

        // プラグインが見つからない・描画 API が非対応なら以降は送らない
        private bool _unavailable;

        public string Status { get; private set; } = "Off";

        /// <summary>
        /// Spout2 へ送るなら true（設定に保存する）。
        /// </summary>
        public bool Enabled
        {
            get => _settings != null && _settings.spoutEnabled;
            set
            {
                // 未初期化なら何もしない
                if (_settings == null)
                {
                    return;
                }

                _settings.spoutEnabled = value;
                enabled = value;
            }
        }

        public void Initialize(AppSettings settings)
        {
            _settings = settings;

            // 無効な間は OnRenderImage を呼ばせない（描画の中間テクスチャを作らせない）
            enabled = settings.spoutEnabled;
        }

        private void OnEnable()
        {
            // 最初の描画で送信先を作るまでの表示（使えない環境なら理由を残す）
            if (!_unavailable)
            {
                Status = "Starting...";
            }
        }

        private void Update()
        {
            // 描画スレッドが使い終えた古いイベントデータを解放する
            FreeRetired(false);
        }

        private void OnDisable()
        {
            // 送信をやめて受け取る側から消す
            CloseSender();
            ReleaseBuffer();
            Status = "Off";
        }

        private void OnDestroy()
        {
            CloseSender();
            ReleaseBuffer();
            FreeRetired(true);
            _commands?.Release();
            _commands = null;
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            // 画面への表示はそのまま
            Graphics.Blit(source, destination);

            // 未初期化・使えない環境なら送らない
            if (_settings == null || _unavailable)
            {
                return;
            }

            // 共有テクスチャは Direct3D 11 / 12 のみ
            GraphicsDeviceType device = SystemInfo.graphicsDeviceType;
            if (device != GraphicsDeviceType.Direct3D11 && device != GraphicsDeviceType.Direct3D12)
            {
                Fail("Error: Direct3D 11 or 12 is required");
                return;
            }

            try
            {
                // 描画結果を送信用テクスチャへ写し（アルファも保つ）、大きさが変わったら送信先を作り直す
                PrepareBuffer(source.width, source.height);
                Graphics.Blit(source, _buffer);
                if (_sender == IntPtr.Zero && !OpenSender())
                {
                    return;
                }

                // 描画スレッドで共有テクスチャへコピーさせる
                IssueEvent(UpdateSenderEvent, _eventData);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                // 同梱されていない（開発環境で取得していない等）
                Fail("Error: KlakSpout.dll not found");
            }
        }

        private void PrepareBuffer(int width, int height)
        {
            // 大きさが同じなら使い回す
            if (_buffer != null && _buffer.width == width && _buffer.height == height)
            {
                return;
            }

            // 送信先は作成時の大きさに固定されるため、作り直す
            CloseSender();
            ReleaseBuffer();
            _buffer = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "VRCast Spout",
            };
            _buffer.Create();
        }

        private bool OpenSender()
        {
            // 送信先を作る（同名の送信元が既にある等で失敗したら次のフレームで再試行）
            _sender = CreateSender(SenderName, _buffer.width, _buffer.height);
            if (_sender == IntPtr.Zero)
            {
                Status = "Error: could not create the Spout sender";
                return false;
            }

            // 描画スレッドが参照するデータはネイティブメモリに置く（GC に動かされないように）
            _eventData = Marshal.AllocHGlobal(Marshal.SizeOf<EventData>());
            Marshal.StructureToPtr(new EventData { Instance = _sender, Texture = _buffer.GetNativeTexturePtr() },
                _eventData, false);
            Status = $"Sending as '{SenderName}' ({_buffer.width}x{_buffer.height})";
            VRCastLog.Info(LogCategory, Status);
            return true;
        }

        private void CloseSender()
        {
            // 送信先が無ければ何もしない
            if (_sender == IntPtr.Zero)
            {
                return;
            }

            // 描画スレッドで閉じさせ、そのデータは読み終わるまで解放を待つ
            try
            {
                IssueEvent(CloseSenderEvent, _eventData);
                _retired.Add(new KeyValuePair<IntPtr, int>(_eventData, Time.frameCount + ReleaseDelayFrames));
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                // 作成できていた以上ここには来ないが、念のためデータだけ解放する
                Marshal.FreeHGlobal(_eventData);
            }

            _sender = IntPtr.Zero;
            _eventData = IntPtr.Zero;
        }

        private void IssueEvent(int eventId, IntPtr data)
        {
            // 描画スレッドで KlakSpout.dll のイベント処理を呼ばせる
            if (_commands == null)
            {
                _commands = new CommandBuffer { name = "VRCast Spout" };
            }

            _commands.Clear();
            _commands.IssuePluginEventAndData(GetRenderEventCallback(), eventId, data);
            Graphics.ExecuteCommandBuffer(_commands);
        }

        private void ReleaseBuffer()
        {
            // 送信用テクスチャを破棄
            if (_buffer != null)
            {
                _buffer.Release();
                Destroy(_buffer);
                _buffer = null;
            }
        }

        private void FreeRetired(bool all)
        {
            // 待ち時間を過ぎた（または終了時は全部の）イベントデータを解放する
            for (int i = _retired.Count - 1; i >= 0; i--)
            {
                if (all || Time.frameCount >= _retired[i].Value)
                {
                    Marshal.FreeHGlobal(_retired[i].Key);
                    _retired.RemoveAt(i);
                }
            }
        }

        private void Fail(string status)
        {
            // 以降は送らず、理由を表示・記録する
            _unavailable = true;
            Status = status;
            VRCastLog.Warning(LogCategory, status);
        }
    }
}
