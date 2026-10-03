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
    /// OpenSeeFace（facetracker.exe）を管理する。アプリ全体で 1 つ。
    /// 既定は StreamingAssets に同梱した facetracker を使い、trackingEnabled の間は選択カメラで自動起動・異常終了時は再起動する。
    /// カメラ一覧は「facetracker.exe -l 1」の出力から取得し、カメラはデバイス名で保存して起動時に番号へ解決する。
    /// </summary>
    public class FaceTrackerProcess : MonoBehaviour
    {
        /// <summary>
        /// 同梱版を探す StreamingAssets からの相対パス（リリース zip の展開直下・Binary 配下の両方に対応）。
        /// </summary>
        public static readonly string[] BundledRelativePaths =
        {
            "OpenSeeFace/facetracker.exe",
            "OpenSeeFace/Binary/facetracker.exe",
        };

        // ログのカテゴリ名
        private const string LogCategory = "Tracker";

        // カメラ一覧取得の待ち時間上限（ミリ秒）
        private const int ListTimeoutMilliseconds = 15000;

        // 起動失敗・異常終了後に再起動するまでの間隔（秒、カメラ使用中等で連続起動しないように）
        private const float RestartInterval = 5f;

        // 一覧の 1 行（"0: カメラ名"）
        private static readonly Regex CameraLine = new Regex(@"^\s*(\d+)\s*:\s*(.+?)\s*$");

        // バックグラウンドスレッドと共有する値の排他
        private readonly object _lock = new object();
        private List<string> _pendingCameras;
        private string _pendingError;
        private string _lastOutput = string.Empty;

        private AppSettings _settings;

        // 同梱版のフルパス（起動時に 1 回だけ探す。無ければ null）
        private string _bundledPath;
        private Process _process;
        private int _startedPort;
        private string _startedCamera;
        private float _nextStartTime;
        private bool _listRequested;
        private List<string> _cameras = new List<string>();

        /// <summary>
        /// 最後に取得したカメラ名（インデックス = facetracker のカメラ番号）。
        /// </summary>
        public IReadOnlyList<string> Cameras => _cameras;

        public bool IsListing { get; private set; }

        public bool IsRunning => _process != null;

        /// <summary>
        /// 同梱版の facetracker.exe が見つかったか（無ければ UI でパス入力を求める）。
        /// </summary>
        public bool HasBundled => _bundledPath != null;

        public string Status { get; private set; } = "Not started";

        public void Initialize(AppSettings settings)
        {
            _settings = settings;
            _bundledPath = FindBundled();
            VRCastLog.Info(LogCategory, "Bundled facetracker: " + (_bundledPath ?? "not found"));
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
            _listRequested = true;

            // 取得中・実行ファイル無しなら何もしない
            if (IsListing || !TryGetExecutable(out string path))
            {
                return;
            }

            IsListing = true;
            Status = "Listing cameras...";
            Task.Run(() => ListCameras(path));
        }

        /// <summary>
        /// 起動中なら終了し、すぐに起動し直す（カメラ・ポート変更時や手動の再起動）。
        /// </summary>
        public void Restart()
        {
            StopProcess();
            _nextStartTime = 0f;
        }

        private void Update()
        {
            // 未初期化なら何もしない
            if (_settings == null)
            {
                return;
            }

            ApplyPendingCameras();

            // 受信を無効にしたら停止
            if (!_settings.trackingEnabled)
            {
                if (IsRunning)
                {
                    StopProcess();
                    Status = "Stopped";
                }

                return;
            }

            // 有効化後の初回は一覧を自動取得
            if (!_listRequested)
            {
                RefreshCameras();
            }

            // 起動中なら終了・設定変更を監視、停止中なら間隔を空けて起動
            if (IsRunning)
            {
                Monitor();
            }
            else if (!IsListing && Time.unscaledTime >= _nextStartTime)
            {
                StartTracker();
            }
        }

        private void Monitor()
        {
            // 自然終了（カメラが開けない等）は最後の出力を表示して、間隔を空けて再起動
            if (_process.HasExited)
            {
                int code = _process.ExitCode;
                StopProcess();
                _nextStartTime = Time.unscaledTime + RestartInterval;
                lock (_lock)
                {
                    Status = $"Tracker exited ({code}): {_lastOutput}";
                }

                VRCastLog.Warning(LogCategory, Status);
                return;
            }

            // 受信ポート・カメラが変わったら起動し直す
            if (_startedPort != _settings.trackingPort || _startedCamera != _settings.trackerCamera)
            {
                Restart();
            }
        }

        private void StartTracker()
        {
            // 次の試行は間隔を空ける（成功時は Monitor が終了を検出するまで使われない）
            _nextStartTime = Time.unscaledTime + RestartInterval;

            // 実行ファイル無しなら何もしない
            if (!TryGetExecutable(out string path))
            {
                return;
            }

            // 保存済みのカメラ名を現在の番号へ解決
            int camera = string.IsNullOrEmpty(_settings.trackerCamera) ? -1 : _cameras.IndexOf(_settings.trackerCamera);
            if (camera < 0)
            {
                Status = _cameras.Count == 0 ? "No camera found (Refresh cameras)" : "Select a camera";
                return;
            }

            try
            {
                // ループバックの受信ポートへ送らせる
                _startedPort = _settings.trackingPort;
                _startedCamera = _settings.trackerCamera;
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

                Status = $"Running: {_startedCamera}";
                VRCastLog.Info(LogCategory, $"Started camera {camera} ({_startedCamera}) -> port {_startedPort}");
            }
            catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            {
                // 起動失敗（権限・壊れた実行ファイル等）
                Status = "Failed to start: " + e.Message;
                VRCastLog.Warning(LogCategory, Status);
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

            // 未選択なら先頭の有効なカメラを既定にする（保存済みの名前は外れていても上書きしない）
            if (string.IsNullOrEmpty(_settings.trackerCamera))
            {
                _settings.trackerCamera = _cameras.Find(name => !string.IsNullOrEmpty(name)) ?? string.Empty;
            }

            IsListing = false;
            _nextStartTime = 0f;
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
                        // 0 件なら原因調査用に出力内容（エラー出力優先）を添える
                        cameras = ParseCameraList(output.Result);
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
            // 利用者指定のパスを優先し、無ければ同梱版
            string custom = PathUtility.NormalizeInput(_settings.trackerPath);
            path = string.IsNullOrEmpty(custom) ? _bundledPath : custom;

            // 実在する .exe のみ受け付ける
            bool valid = path != null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
            if (!valid)
            {
                Status = "facetracker.exe not found";
            }

            return valid;
        }

        private static string FindBundled()
        {
            foreach (string relative in BundledRelativePaths)
            {
                // StreamingAssets（ビルドでは VRCast_Data/StreamingAssets）配下の候補
                string path = Path.Combine(Application.streamingAssetsPath, relative);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
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
