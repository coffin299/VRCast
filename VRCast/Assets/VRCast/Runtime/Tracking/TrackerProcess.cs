using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using VRCast.Core;
using VRCast.Output;
using VRCast.Platform;

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

        // トラッカー自身の出力（標準出力・エラー出力）のカテゴリ名
        private const string OutputLogCategory = "TrackerOutput";

        // カメラ一覧取得の待ち時間上限（ミリ秒、MediaPipe 版は展開・読込に数秒かかる）
        private const int ListTimeoutMilliseconds = 30000;

        // 起動失敗・異常終了後に再起動するまでの間隔（秒、カメラ使用中等で連続起動しないように）
        private const float RestartInterval = 5f;

        // MediaPipe 版で手の推定を止める引数（腕・手を使わないときの CPU 負荷軽減）
        private const string NoHandsArgument = " --no-hands";

        // MediaPipe 版に親プロセス（VRCast）の PID を渡す引数（親の終了を検出して自分も終了する）
        private const string ParentPidArgument = " --parent-pid ";

        // 軽量モードの引数（MediaPipe 版: 推定を毎秒 20 回までに間引く / OpenSeeFace: 既定（3）より軽いモデル）
        private const string LowLoadMediaPipeArgument = " --max-fps 20";
        private const string LowLoadOpenSeeFaceArgument = " --model 2";

        // 一覧の 1 行（"0: カメラ名"）
        private static readonly Regex CameraLine = new Regex(@"^\s*(\d+)\s*:\s*(.+?)\s*$");

        // 顔を映せない（仮想カメラ・赤外線カメラ）とみなすカメラ名に含まれる語（大文字・小文字は区別しない）。
        // VRCast 自身の仮想カメラを選ぶと自分の出力（未出力なら黒）を読み、顔も体も検出されない
        private static readonly string[] UnusableCameraKeywords =
        {
            VirtualCameraInstaller.DeviceName, "Unity Video Capture", "Virtual", "VCam", "IR Camera", "Infrared",
        };

        // glog（MediaPipe・TensorFlow Lite の内部ログ）の行頭（重要度 I/W/E/F + 月日 4 桁）
        private static readonly Regex GlogLine = new Regex(@"^([IWEF])\d{4}\s");

        // トラッカー出力をデバッグログへ残す上限（行/秒。超えた INFO 行は捨てて件数だけ報告し、ログ採取の負荷を抑える）
        private const int MaxOutputLinesPerSecond = 30;

        // 捨てた出力行の件数を報告する最短間隔（秒）
        private const float SuppressedReportInterval = 5f;

        // Windows の異常終了コード（DLL が見つからない / メモリアクセス違反）
        private const int StatusDllNotFound = unchecked((int)0xC0000135);
        private const int StatusAccessViolation = unchecked((int)0xC0000005);

        // バックグラウンドスレッドと共有する値の排他
        private readonly object _lock = new object();
        private List<string> _pendingCameras;
        private string _pendingError;
        private double _pendingListSeconds;
        private string _lastOutput = string.Empty;

        // 出力の流量制限（OnOutput は別スレッドのため Environment.TickCount で 1 秒の窓を計る。_lock で保護）
        private int _outputWindowStart;
        private int _outputLinesInWindow;
        private int _suppressedOutputLines;
        private float _nextSuppressedReportTime;

        // 同じ警告（カメラ未選択・実行ファイル無し等）を再試行のたびに記録しないよう、最後に記録した状態
        private string _lastWarnedStatus;

        // 起動時刻（終了時に動作時間を記録する）
        private float _startTime;

        // 入力元ごとの同梱版のフルパス（起動時に 1 回だけ探す。無ければ null）
        private readonly Dictionary<TrackingSource, string> _bundledPaths = new Dictionary<TrackingSource, string>();

        private AppSettings _settings;
        private int _ownProcessId;
        private NativeProcess _process;
        private int _startedPort;
        private string _startedCamera;
        private TrackingSource _startedSource;
        private bool _startedHands;
        private bool _startedLowLoad;
        private ProcessPriority _appliedPriority;
        private ulong _appliedCoreMask;
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
        /// 起動中のトラッカーの PID（起動していなければ 0）。
        /// </summary>
        public int ProcessId => _process != null ? _process.Id : 0;

        /// <summary>
        /// 現在の入力元の同梱版が見つかったか（無ければ UI でパス入力を求める）。
        /// </summary>
        public bool HasBundled => _settings != null
            && _bundledPaths.TryGetValue(_settings.trackingSource, out string bundled) && bundled != null;

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
                // 外部アプリから受信する入力元には同梱版が無い
                if (!TrackingSourceInfo.UsesBundledTracker(source))
                {
                    continue;
                }

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
        /// 顔を映せないカメラ（仮想カメラ・赤外線カメラ）らしい名前なら true。
        /// </summary>
        public static bool IsLikelyUnusableCamera(string name)
        {
            // 空の名前は判定しない
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            foreach (string keyword in UnusableCameraKeywords)
            {
                // 名前のどこかに含まれていれば該当
                if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 既定に選ぶカメラ名（顔を映せるカメラを優先し、無ければ先頭の名前。一覧が空なら空文字）。
        /// </summary>
        public static string ChooseDefaultCamera(IReadOnlyList<string> cameras)
        {
            string fallback = string.Empty;
            foreach (string name in cameras)
            {
                // 番号の欠けた空の名前は飛ばす
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // 実カメラらしい最初のものを採用
                if (!IsLikelyUnusableCamera(name))
                {
                    return name;
                }

                // 仮想カメラしか無いときのために先頭を覚えておく
                if (fallback.Length == 0)
                {
                    fallback = name;
                }
            }

            return fallback;
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
            ReportSuppressedOutput();

            // 受信を無効にしたら停止
            if (!_settings.trackingEnabled)
            {
                if (IsRunning)
                {
                    StopProcess();
                    Status = "Stopped";
                    VRCastLog.Info(LogCategory, "Tracking disabled; tracker stopped");
                }

                return;
            }

            // 外部アプリ（VMC）から受信する間は同梱トラッカーを使わない（起動中なら止め、カメラも開かない）
            if (!TrackingSourceInfo.UsesBundledTracker(_settings.trackingSource))
            {
                if (IsRunning)
                {
                    StopProcess();
                    VRCastLog.Info(LogCategory, "Switched to an external app; tracker stopped");
                }

                Status = "Not used (receiving from an external app)";
                _listedSource = null;
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

                // 終了コードの意味と動作時間を添える（起動直後の終了ならカメラ・DLL の問題の可能性が高い）
                float seconds = Time.unscaledTime - _startTime;
                VRCastLog.Warning(LogCategory,
                    $"{Status} [{DescribeExitCode(_startedSource, code)}, ran {seconds:F1} s, " +
                    $"restarting in {RestartInterval:F0} s]");
                return;
            }

            // 受信ポート・カメラ・入力元・（MediaPipe の）手の有無・軽量モードが変わったら起動し直す
            bool changed = _startedPort != _settings.trackingPort || _startedCamera != _settings.trackerCamera
                || _startedSource != _settings.trackingSource || _startedHands != UsesHands()
                || _startedLowLoad != _settings.lowLoadMode;
            if (changed)
            {
                VRCastLog.Info(LogCategory, "Tracking settings changed; restarting tracker");
                Restart();
                return;
            }

            // 優先度・使うコアは再起動せずに変更できる
            if (_appliedPriority != _settings.processPriority)
            {
                ApplyPriority();
            }

            if (_appliedCoreMask != CpuTopology.CoreMaskFor(_settings))
            {
                ApplyCoreMask();
            }
        }

        private void ApplyCoreMask()
        {
            // 試した値は記録して毎フレーム再試行しない（起動時は VRCast から引き継いだ制限に関係なく設定の値にする）
            _appliedCoreMask = CpuTopology.CoreMaskFor(_settings);
            if (!ProcessTuning.SetCoreMask(_process.Id, _appliedCoreMask))
            {
                VRCastLog.Warning(LogCategory, "Could not change the CPU cores the tracker runs on");
            }
        }

        private void ApplyPriority()
        {
            // 失敗しても同じ値で毎フレーム再試行しないよう、試した値を記録する
            _appliedPriority = _settings.processPriority;
            if (!ProcessTuning.SetPriority(_process.Id, _appliedPriority))
            {
                VRCastLog.Warning(LogCategory, $"Could not set the tracker priority to {_appliedPriority}");
            }
        }

        /// <summary>
        /// トラッカーの終了コードの意味（不明なら "unknown"）。
        /// </summary>
        public static string DescribeExitCode(TrackingSource source, int code)
        {
            // Windows の異常終了は入力元に関係なく判定
            if (code == StatusDllNotFound)
            {
                return "a required DLL was not found (install the Visual C++ Redistributable?)";
            }

            if (code == StatusAccessViolation)
            {
                return "crashed (access violation)";
            }

            // 0 は正常終了（親プロセスの終了検出など）
            if (code == 0)
            {
                return "exited normally";
            }

            // 同梱の MediaPipe 版が返す終了コード（vrcast_tracker.py と一致させる）
            if (source == TrackingSource.MediaPipe)
            {
                switch (code)
                {
                    case 1:
                        return "camera could not be opened or tracker error";
                    case 2:
                        return "camera stopped delivering frames";
                }
            }

            return "unknown";
        }

        private void WarnStatus(string status)
        {
            // 状態を表示し、前回と違うときだけ警告として記録（再試行のたびに同じ警告を並べない）
            Status = status;
            if (status == _lastWarnedStatus)
            {
                return;
            }

            _lastWarnedStatus = status;
            VRCastLog.Warning(LogCategory, status);
        }

        private bool UsesHands()
        {
            // 手の推定を行うのは腕・手を受信できる入力元で腕・手が有効なときだけ
            return TrackingSourceInfo.HasArms(_settings.trackingSource) && _settings.trackingHands;
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
                // 保存済みのカメラが一覧に無いときは名前も添える（抜き差しで名前が変わった等の調査用）
                WarnStatus(_cameras.Count == 0
                    ? "No camera found (Refresh cameras)"
                    : string.IsNullOrEmpty(_settings.trackerCamera)
                        ? "Select a camera"
                        : $"Select a camera (saved camera \"{_settings.trackerCamera}\" is not in the list)");
                return;
            }

            try
            {
                // ループバックの受信ポートへ送らせる（MediaPipe で腕・手が無効なら手の推定を止める）
                _startedPort = _settings.trackingPort;
                _startedCamera = _settings.trackerCamera;
                _startedSource = _settings.trackingSource;
                _startedHands = UsesHands();
                _startedLowLoad = _settings.lowLoadMode;
                bool mediaPipe = _startedSource == TrackingSource.MediaPipe;
                string arguments = $"-c {camera} -i 127.0.0.1 -p {_startedPort}";
                if (mediaPipe && !_startedHands)
                {
                    arguments += NoHandsArgument;
                }

                // 軽量モードでは入力元に合わせて処理を軽くする
                if (_startedLowLoad)
                {
                    arguments += mediaPipe ? LowLoadMediaPipeArgument : LowLoadOpenSeeFaceArgument;
                }

                // MediaPipe 版には自分の PID を渡し、VRCast が異常終了してもトラッカー（とカメラ）を残さない
                if (mediaPipe)
                {
                    arguments += ParentPidArgument + _ownProcessId;
                }

                // 出力を読み続けないとバッファが詰まって停止するため、最後の行だけ保持する
                NativeProcess process = StartProcess(path, arguments, mediaPipe, OnOutput);
                _process = process;
                _startTime = Time.unscaledTime;
                _lastWarnedStatus = null;

                // VRCast が背面にある間に Windows がトラッカーの CPU 速度を落とし、推定が遅れて手を見失わないようにする
                if (!ProcessTuning.DisablePowerThrottling(process.Id))
                {
                    VRCastLog.Info(LogCategory, "Could not opt the tracker out of Windows power throttling");
                }

                // 設定の優先度・使うコアにする（以降の変更は Monitor が再起動せずに反映）
                ApplyPriority();
                ApplyCoreMask();

                // 起動したコマンドライン全体と PID を残す（手動で同じ引数を試せるように）
                Status = $"Running: {_startedCamera}";
                VRCastLog.Info(LogCategory,
                    $"Started {_startedSource} camera {camera} ({_startedCamera}) -> port {_startedPort}, " +
                    $"pid {process.Id}: \"{path}\" {arguments}");

                // 仮想カメラ・赤外線カメラを選んでいると顔も体も検出されないため、起動のたびに警告する
                if (IsLikelyUnusableCamera(_startedCamera))
                {
                    VRCastLog.Warning(LogCategory,
                        $"\"{_startedCamera}\" looks like a virtual or infrared camera. " +
                        "Your face will not be detected; select your real webcam");
                }
            }
            catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            {
                // 起動失敗（権限・壊れた実行ファイル・ウイルス対策ソフトによるブロック等）
                WarnStatus("Failed to start: " + e.Message);
            }
        }

        private void ApplyPendingCameras()
        {
            bool listFailed;
            double listSeconds;
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
                listFailed = _pendingError != null;
                listSeconds = _pendingListSeconds;
            }

            // 未選択なら実カメラらしいものを既定にする（保存済みの名前は外れていても上書きしない）
            if (string.IsNullOrEmpty(_settings.trackerCamera))
            {
                _settings.trackerCamera = ChooseDefaultCamera(_cameras);
            }

            IsListing = false;
            _nextStartTime = 0f;

            // カメラが見つからない・一覧の取得に失敗したときは警告にして、デバッグログで目立たせる（所要時間も添える）
            string message = $"{Status} in {listSeconds:F1} s: {string.Join(", ", _cameras)}";
            if (listFailed)
            {
                VRCastLog.Warning(LogCategory, message);
            }
            else
            {
                VRCastLog.Info(LogCategory, message);
            }
        }

        private void ListCameras(string path, bool utf8)
        {
            // バックグラウンドスレッドで実行（Unity API は使わない）
            List<string> cameras = new List<string>();
            string error = null;
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                // 標準出力・エラー出力をまとめて読み切る（読まないと詰まって終了しない）
                var output = new StringBuilder();
                using (NativeProcess process = StartProcess(path, "-l 1", utf8, line =>
                       {
                           lock (output)
                           {
                               output.Append(line).Append('\n');
                           }
                       }))
                {
                    if (process.WaitForExit(ListTimeoutMilliseconds) && process.WaitForOutput(ListTimeoutMilliseconds))
                    {
                        // 0 件なら原因調査用に出力内容を添える
                        string text;
                        lock (output)
                        {
                            text = output.ToString();
                        }

                        cameras = ParseCameraList(text);
                        error = cameras.Count == 0 ? "No camera found: " + text.Trim() : null;
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
                _pendingListSeconds = watch.Elapsed.TotalSeconds;
            }
        }

        private bool TryGetExecutable(out string path)
        {
            // 利用者指定のパスを優先し、無ければ同梱版
            TrackingSource source = _settings.trackingSource;
            string executable = ExecutableOf(source);
            string custom = PathUtility.NormalizeInput(_settings.trackerPath);
            _bundledPaths.TryGetValue(source, out string bundled);
            path = string.IsNullOrEmpty(custom) ? bundled : custom;

            // 入力元に合った名前の実在する実行ファイルのみ受け付ける（入力元の切替後に別のトラッカーを起動しないように）
            bool valid = path != null
                && string.Equals(Path.GetFileName(path), executable, StringComparison.OrdinalIgnoreCase)
                && File.Exists(path);
            if (!valid)
            {
                WarnStatus($"{executable} not found ({path ?? "no path"})");
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

        private static NativeProcess StartProcess(string path, string arguments, bool utf8, Action<string> onLine)
        {
            // "/" 混じりのパスを "\" 区切りの絶対パスにそろえる
            path = Path.GetFullPath(path);

            // モデル等を相対パスで読むため作業ディレクトリは exe の場所。MediaPipe 版は UTF-8 で出力する（日本語のカメラ名を化けさせない）
            return NativeProcess.Start(path, arguments, Path.GetDirectoryName(path),
                utf8 ? Encoding.UTF8 : Encoding.Default, onLine);
        }

        private void OnOutput(string data)
        {
            // 空行は無視し、最後の行をエラー表示用に保持（別スレッドから呼ばれる）
            if (string.IsNullOrWhiteSpace(data))
            {
                return;
            }

            string line = data.Trim();
            LogLevel level = ClassifyOutput(line);

            // 定期統計は終了理由の表示に使わず、詳細ログ OFF なら排他も取らずに捨てる
            if (level == LogLevel.Debug)
            {
                if (LogBuffer.DetailEnabled)
                {
                    LogBuffer.Add(level, OutputLogCategory, line);
                }

                return;
            }

            int now = Environment.TickCount;
            lock (_lock)
            {
                _lastOutput = line;

                // 1 秒の窓ごとに行数を数え直す（TickCount の折り返しも差分なら正しく扱える）
                if (now - _outputWindowStart >= 1000)
                {
                    _outputWindowStart = now;
                    _outputLinesInWindow = 0;
                }

                // 上限を超えた INFO 行は捨てて件数だけ数える（警告・エラーは原因調査に必要なので常に残す）
                _outputLinesInWindow++;
                if (level == LogLevel.Info && _outputLinesInWindow > MaxOutputLinesPerSecond)
                {
                    _suppressedOutputLines++;
                    return;
                }
            }

            // カメラが開けない等の原因を追えるよう、デバッグログタブに残す（量が多いため Player.log には書かない）
            LogBuffer.Add(level, OutputLogCategory, line);
        }

        /// <summary>
        /// トラッカーの出力行の重要度（行頭の "ERROR"/"WARN"/"STATS"、glog の重要度、Python の例外から判定。それ以外は INFO）。
        /// </summary>
        public static LogLevel ClassifyOutput(string line)
        {
            // 同梱の MediaPipe 版の定期統計は詳細ログ（DEBUG。詳細ログ OFF なら記録しない）
            if (line.StartsWith("STATS:", StringComparison.Ordinal))
            {
                return LogLevel.Debug;
            }

            // glog は行頭 1 文字が重要度（I 情報 / W 警告 / E エラー / F 致命的）
            Match glog = GlogLine.Match(line);
            if (glog.Success)
            {
                char severity = glog.Groups[1].Value[0];
                return severity == 'I' ? LogLevel.Info : severity == 'W' ? LogLevel.Warning : LogLevel.Error;
            }

            // 同梱トラッカーのエラー行・Python の例外
            if (line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Traceback", StringComparison.Ordinal)
                || line.StartsWith("Tracker error", StringComparison.Ordinal)
                || line.StartsWith("Failed", StringComparison.Ordinal)
                || line.IndexOf("Exception", StringComparison.Ordinal) >= 0)
            {
                return LogLevel.Error;
            }

            // 同梱トラッカーの警告行・Python の警告
            if (line.StartsWith("WARN", StringComparison.OrdinalIgnoreCase)
                || line.IndexOf("Warning:", StringComparison.Ordinal) >= 0)
            {
                return LogLevel.Warning;
            }

            return LogLevel.Info;
        }

        private void ReportSuppressedOutput()
        {
            // 報告は間隔を空ける（大量出力が続く間に警告だけで埋まらないように）
            if (Time.unscaledTime < _nextSuppressedReportTime)
            {
                return;
            }

            int suppressed;
            lock (_lock)
            {
                suppressed = _suppressedOutputLines;
                _suppressedOutputLines = 0;
            }

            if (suppressed == 0)
            {
                return;
            }

            _nextSuppressedReportTime = Time.unscaledTime + SuppressedReportInterval;
            VRCastLog.Warning(LogCategory,
                $"Skipped {suppressed} tracker output line(s) (over {MaxOutputLinesPerSecond} lines/s)");
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
