using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// 入力元のトラッカー（MediaPipe: vrcast_tracker.exe / OpenSeeFace: facetracker.exe）を管理する。アプリ全体で 1 つ。
    /// 既定は同梱した実行ファイル（ビルドでは StreamingAssets、エディターでは Trackers）を使い、trackingEnabled の間は選択カメラで自動起動・異常終了時は再起動する。
    /// どちらも同じ引数（一覧 "-l 1"、起動 "-c 番号 -i 127.0.0.1 -p ポート"）で扱い、カメラはデバイス名で保存して起動時に番号へ解決する。
    /// </summary>
    public class TrackerProcess : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "Tracker";

        // カメラ一覧取得の待ち時間上限（ミリ秒、MediaPipe 版は展開・読込に数秒かかる）
        private const int ListTimeoutMilliseconds = 30000;

        // 起動失敗・異常終了後に再起動するまでの間隔（秒、カメラ使用中等で連続起動しないように）
        private const float RestartInterval = 5f;

        // MediaPipe 版で手の推定を止める引数（腕・手を使わないときの CPU 負荷軽減）
        private const string NoHandsArgument = " --no-hands";

        // MediaPipe 版に親プロセス（VRCast）の PID を渡す引数（親の終了を検出して自分も終了する）
        private const string ParentPidArgument = " --parent-pid ";

        // 一覧の 1 行（"0: カメラ名"）
        private static readonly Regex CameraLine = new Regex(@"^\s*(\d+)\s*:\s*(.+?)\s*$");

        // バックグラウンドスレッドと共有する値の排他
        private readonly object _lock = new object();
        private List<string> _pendingCameras;
        private string _pendingError;
        private string _lastOutput = string.Empty;

        // 入力元ごとの同梱版のフルパス（起動時に 1 回だけ探す。無ければ null）
        private readonly Dictionary<TrackingSource, string> _bundledPaths = new Dictionary<TrackingSource, string>();

        private AppSettings _settings;
        private int _ownProcessId;
        private Process _process;
        private int _startedPort;
        private string _startedCamera;
        private TrackingSource _startedSource;
        private bool _startedHands;
        private float _nextStartTime;

        // 一覧を取得済み（または取得中）の入力元。null なら未取得
        private TrackingSource? _listedSource;
        private List<string> _cameras = new List<string>();

        /// <summary>
        /// 最後に取得したカメラ名（インデックス = トラッカーのカメラ番号）。
        /// </summary>
        public IReadOnlyList<string> Cameras => _cameras;

        public bool IsListing { get; private set; }

        public bool IsRunning => _process != null;

        /// <summary>
        /// 現在の入力元の同梱版が見つかったか（無ければ UI でパス入力を求める）。
        /// </summary>
        public bool HasBundled => _settings != null && _bundledPaths[_settings.trackingSource] != null;

        public string Status { get; private set; } = "Not started";

        public void Initialize(AppSettings settings)
        {
            _settings = settings;

            // トラッカーへ渡す自分の PID（起動ごとに取得しないよう保持）
            using (Process current = Process.GetCurrentProcess())
            {
                _ownProcessId = current.Id;
            }

            // 全入力元の同梱版を探しておく
            foreach (TrackingSource source in (TrackingSource[])Enum.GetValues(typeof(TrackingSource)))
            {
                _bundledPaths[source] = FindBundled(BundledRoot, source);
                VRCastLog.Info(LogCategory, $"Bundled {ExecutableOf(source)}: {_bundledPaths[source] ?? "not found"}");
            }
        }

        /// <summary>
        /// エディターで同梱版を置くフォルダ名（Unity プロジェクト直下、Assets の外）。
        /// トラッカーの DLL 群を Assets 内に置くと Unity がネイティブプラグインとして登録し、
        /// エディターのスクリプトコンパイルが OutOfMemoryException で失敗するため。ビルド時に StreamingAssets へコピーする。
        /// </summary>
        public const string EditorFolderName = "Trackers";

        /// <summary>
        /// 同梱版を探す起点。ビルドでは StreamingAssets、エディターではプロジェクト直下の Trackers。
        /// </summary>
        public static string BundledRoot => Application.isEditor
            ? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, EditorFolderName)
            : Application.streamingAssetsPath;

        /// <summary>
        /// 同梱版を置くフォルダ名（BundledRoot 内）。
        /// </summary>
        public static string FolderOf(TrackingSource source)
        {
            return source == TrackingSource.OpenSeeFace ? "OpenSeeFace" : "MediaPipeTracker";
        }

        /// <summary>
        /// トラッカーの実行ファイル名。
        /// </summary>
        public static string ExecutableOf(TrackingSource source)
        {
            return source == TrackingSource.OpenSeeFace ? "facetracker.exe" : "vrcast_tracker.exe";
        }

        /// <summary>
        /// トラッカーの一覧出力からカメラ名を番号順に取り出す（番号の欠けは空文字で埋める）。
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
            // 取得中なら何もしない（入力元が変わっていれば完了後に取り直す）
            if (IsListing)
            {
                return;
            }

            // 実行ファイル無しなら取得済み扱い（パスが設定されるまで繰り返さない）
            TrackingSource source = _settings.trackingSource;
            _listedSource = source;
            if (!TryGetExecutable(out string path))
            {
                return;
            }

            IsListing = true;
            Status = "Listing cameras...";
            bool utf8 = source == TrackingSource.MediaPipe;
            Task.Run(() => ListCameras(path, utf8));
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

            // 有効化後の初回と入力元の変更時は一覧を自動取得
            if (_listedSource != _settings.trackingSource)
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

            // 受信ポート・カメラ・入力元・（MediaPipe の）手の有無が変わったら起動し直す
            bool changed = _startedPort != _settings.trackingPort || _startedCamera != _settings.trackerCamera
                || _startedSource != _settings.trackingSource || _startedHands != UsesHands();
            if (changed)
            {
                Restart();
            }
        }

        private bool UsesHands()
        {
            // 手の推定を行うのは MediaPipe で腕・手が有効なときだけ
            return _settings.trackingSource == TrackingSource.MediaPipe && _settings.trackingHands;
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
                // ループバックの受信ポートへ送らせる（MediaPipe で腕・手が無効なら手の推定を止める）
                _startedPort = _settings.trackingPort;
                _startedCamera = _settings.trackerCamera;
                _startedSource = _settings.trackingSource;
                _startedHands = UsesHands();
                bool mediaPipe = _startedSource == TrackingSource.MediaPipe;
                string arguments = $"-c {camera} -i 127.0.0.1 -p {_startedPort}";
                if (mediaPipe && !_startedHands)
                {
                    arguments += NoHandsArgument;
                }

                // MediaPipe 版には自分の PID を渡し、VRCast が異常終了してもトラッカー（とカメラ）を残さない
                if (mediaPipe)
                {
                    arguments += ParentPidArgument + _ownProcessId;
                }

                var process = new Process { StartInfo = CreateStartInfo(path, arguments, mediaPipe) };

                // 出力を読み続けないとバッファが詰まって停止するため、最後の行だけ保持する
                process.OutputDataReceived += OnOutput;
                process.ErrorDataReceived += OnOutput;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                _process = process;

                Status = $"Running: {_startedCamera}";
                VRCastLog.Info(LogCategory, $"Started {_startedSource} camera {camera} ({_startedCamera}) -> port {_startedPort}");
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

        private void ListCameras(string path, bool utf8)
        {
            // バックグラウンドスレッドで実行（Unity API は使わない）
            List<string> cameras = new List<string>();
            string error = null;
            try
            {
                using (Process process = Process.Start(CreateStartInfo(path, "-l 1", utf8)))
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
            TrackingSource source = _settings.trackingSource;
            string executable = ExecutableOf(source);
            string custom = PathUtility.NormalizeInput(_settings.trackerPath);
            path = string.IsNullOrEmpty(custom) ? _bundledPaths[source] : custom;

            // 入力元に合った名前の実在する実行ファイルのみ受け付ける（入力元の切替後に別のトラッカーを起動しないように）
            bool valid = path != null
                && string.Equals(Path.GetFileName(path), executable, StringComparison.OrdinalIgnoreCase)
                && File.Exists(path);
            if (!valid)
            {
                Status = executable + " not found";
            }

            return valid;
        }

        /// <summary>
        /// (同梱版の起点)/(入力元のフォルダ) 以下からトラッカーの実行ファイルを探す（zip の展開階層に依存しない）。
        /// 複数あれば最も浅いもの。無ければ null。
        /// </summary>
        public static string FindBundled(string streamingAssetsPath, TrackingSource source)
        {
            // フォルダが無ければ同梱なし
            string root = Path.Combine(streamingAssetsPath, FolderOf(source));
            if (!Directory.Exists(root))
            {
                return null;
            }

            try
            {
                // 配下を再帰検索し、パスの短い（浅い）ものを優先
                string[] found = Directory.GetFiles(root, ExecutableOf(source), SearchOption.AllDirectories);
                Array.Sort(found, (a, b) => a.Length.CompareTo(b.Length));
                return found.Length > 0 ? found[0] : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // 読めないフォルダがあれば同梱なし扱い
                return null;
            }
        }

        private static ProcessStartInfo CreateStartInfo(string path, string arguments, bool utf8)
        {
            // モデル等を相対パスで読むため作業ディレクトリは exe の場所。コンソールは出さない
            var info = new ProcessStartInfo(path, arguments)
            {
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            // MediaPipe 版は UTF-8 で出力する（日本語のカメラ名を化けさせない）
            if (utf8)
            {
                info.StandardOutputEncoding = Encoding.UTF8;
                info.StandardErrorEncoding = Encoding.UTF8;
            }

            return info;
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
            // アプリ終了時にトラッカーを残さない
            StopProcess();
        }
    }
}
