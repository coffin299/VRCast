using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// 利用者が指定した OpenSeeFace（facetracker.exe）を起動・停止する。アプリ全体で 1 つ。
    /// カメラ一覧は「facetracker.exe -l 1」の出力から取得し、カメラはデバイス名で保存して起動時に番号へ解決する。
    /// </summary>
    public class FaceTrackerProcess : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "Tracker";

        // カメラ一覧取得の待ち時間上限（ミリ秒）
        private const int ListTimeoutMilliseconds = 15000;

        // 一覧の 1 行（"0: カメラ名"）
        private static readonly Regex CameraLine = new Regex(@"^\s*(\d+)\s*:\s*(.+?)\s*$");

        // バックグラウンドスレッドと共有する値の排他
        private readonly object _lock = new object();
        private List<string> _pendingCameras;
        private string _pendingError;
        private string _lastOutput = string.Empty;

        private AppSettings _settings;
        private Process _process;
        private int _startedPort;
        private List<string> _cameras = new List<string>();

        /// <summary>
        /// 最後に取得したカメラ名（インデックス = facetracker のカメラ番号）。
        /// </summary>
        public IReadOnlyList<string> Cameras => _cameras;

        public bool IsListing { get; private set; }

        public bool IsRunning => _process != null;

        public string Status { get; private set; } = "Not started";

        public void Initialize(AppSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// facetracker の一覧出力からカメラ名を番号順に取り出す（番号の欠けは空文字で埋める）。
        /// </summary>
        public static List<string> ParseCameraList(string output)
        {
            var cameras = new List<string>();

            // 空出力は 0 件
            if (string.IsNullOrEmpty(output))
            {
                return cameras;
            }

            foreach (string line in output.Split('\n'))
            {
                // "番号: 名前" 以外の行（見出し等）は無視
                Match match = CameraLine.Match(line);
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out int index) || index > 255)
                {
                    continue;
                }

                // 番号の位置へ格納
                while (cameras.Count <= index)
                {
                    cameras.Add(string.Empty);
                }

                cameras[index] = match.Groups[2].Value;
            }

            return cameras;
        }

        /// <summary>
        /// カメラ一覧をバックグラウンドで取得する（結果は Update で反映）。
        /// </summary>
        public void RefreshCameras()
        {
            // 取得中・実行ファイル未設定なら何もしない
            if (IsListing || !TryGetExecutable(out string path))
            {
                return;
            }

            IsListing = true;
            Status = "Listing cameras...";
            Task.Run(() => ListCameras(path));
        }

        /// <summary>
        /// 選択中のカメラで facetracker を起動し、受信も有効にする。
        /// </summary>
        public void StartTracker()
        {
            // 実行中・実行ファイル未設定なら何もしない
            if (IsRunning || !TryGetExecutable(out string path))
            {
                return;
            }

            // 保存済みのカメラ名を現在の番号へ解決
            int camera = _cameras.IndexOf(_settings.trackerCamera);
            if (camera < 0 || string.IsNullOrEmpty(_settings.trackerCamera))
            {
                Status = "Select a camera (Refresh cameras)";
                return;
            }

            try
            {
                // ループバックの受信ポートへ送らせる
                _startedPort = _settings.trackingPort;
                var process = new Process
                {
                    StartInfo = CreateStartInfo(path, $"-c {camera} -i 127.0.0.1 -p {_startedPort}"),
                };

                // 出力を読み続けないとバッファが詰まって停止するため、最後の行だけ保持する
                process.OutputDataReceived += OnOutput;
                process.ErrorDataReceived += OnOutput;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                _process = process;

                _settings.trackingEnabled = true;
                Status = $"Running: {_settings.trackerCamera}";
                VRCastLog.Info(LogCategory, $"Started camera {camera} ({_settings.trackerCamera}) -> port {_startedPort}");
            }
            catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            {
                // 起動失敗（権限・壊れた実行ファイル等）
                Status = "Failed to start: " + e.Message;
                VRCastLog.Warning(LogCategory, Status);
            }
        }

        /// <summary>
        /// 起動中の facetracker を終了する。
        /// </summary>
        public void StopTracker()
        {
            StopProcess();
            Status = "Stopped";
        }

        private void Update()
        {
            // 未初期化なら何もしない
            if (_settings == null)
            {
                return;
            }

            ApplyPendingCameras();

            // 起動中でなければ以降の監視は不要
            if (_process == null)
            {
                return;
            }

            // 受信を無効にしたら停止
            if (!_settings.trackingEnabled)
            {
                StopTracker();
                return;
            }

            // 自然終了（カメラが開けない等）を検出して最後の出力を表示
            if (_process.HasExited)
            {
                int code = _process.ExitCode;
                StopProcess();
                lock (_lock)
                {
                    Status = $"Tracker exited ({code}): {_lastOutput}";
                }

                VRCastLog.Warning(LogCategory, Status);
                return;
            }

            // 受信ポートが変わったら送信先を合わせて再起動
            if (_startedPort != _settings.trackingPort)
            {
                StopProcess();
                StartTracker();
            }
        }

        private void ApplyPendingCameras()
        {
            lock (_lock)
            {
                // バックグラウンドの取得結果が無ければ何もしない
                if (_pendingCameras == null)
                {
                    return;
                }

                _cameras = _pendingCameras;
                _pendingCameras = null;
                Status = _pendingError ?? $"{_cameras.Count} camera(s) found";
            }

            IsListing = false;
            VRCastLog.Info(LogCategory, $"{Status}: {string.Join(", ", _cameras)}");
        }

        private void ListCameras(string path)
        {
            // バックグラウンドスレッドで実行（Unity API は使わない）
            List<string> cameras = new List<string>();
            string error = null;
            try
            {
                using (Process process = Process.Start(CreateStartInfo(path, "-l 1")))
                {
                    // 標準出力・エラー出力の両方を読み切る（片方が詰まると終了しない）
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    Task<string> errors = process.StandardError.ReadToEndAsync();
                    if (process.WaitForExit(ListTimeoutMilliseconds))
                    {
                        cameras = ParseCameraList(output.Result);
                        // 0 件なら原因調査用に出力内容（エラー出力優先）を添える
                        string detail = string.IsNullOrWhiteSpace(errors.Result) ? output.Result : errors.Result;
                        error = cameras.Count == 0 ? "No camera found: " + detail.Trim() : null;
                    }
                    else
                    {
                        process.Kill();
                        error = "Listing cameras timed out";
                    }
                }
            }
            catch (Exception e)
            {
                // 起動失敗等はメッセージとして表示
                error = "Failed to list cameras: " + e.Message;
            }

            lock (_lock)
            {
                _pendingCameras = cameras;
                _pendingError = error;
            }
        }

        private bool TryGetExecutable(out string path)
        {
            // 実在する .exe のみ受け付ける
            path = PathUtility.NormalizeInput(_settings.trackerPath);
            bool valid = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
            if (!valid)
            {
                Status = "facetracker.exe not found";
            }

            return valid;
        }

        private static ProcessStartInfo CreateStartInfo(string path, string arguments)
        {
            // モデル等を相対パスで読むため作業ディレクトリは exe の場所。コンソールは出さない
            return new ProcessStartInfo(path, arguments)
            {
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
        }

        private void OnOutput(object sender, DataReceivedEventArgs e)
        {
            // 空行は無視し、最後の行をエラー表示用に保持（別スレッドから呼ばれる）
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            lock (_lock)
            {
                _lastOutput = e.Data.Trim();
            }
        }

        private void StopProcess()
        {
            // 起動していなければ何もしない
            if (_process == null)
            {
                return;
            }

            try
            {
                // 動作中なら強制終了
                if (!_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            {
                // 既に終了している、または終了処理中
                VRCastLog.Info(LogCategory, "Stop: " + e.Message);
            }

            _process.Dispose();
            _process = null;
        }

        private void OnDestroy()
        {
            // アプリ終了時に facetracker を残さない
            StopProcess();
        }
    }
}
