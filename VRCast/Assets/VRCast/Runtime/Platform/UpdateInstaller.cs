using System;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Platform
{
    /// <summary>
    /// 自動更新。利用者が「今すぐ更新」を押したら、GitHub Releases の配布 zip をダウンロードして大きさと SHA-256 を確かめ、
    /// 本体フォルダの分を展開してからアップデーター（StreamingAssets/Updater/VRCastUpdater.exe）を起動して VRCast を終了する。
    /// ファイルの差し替えと起動し直しはアップデーターが行い、結果を次の起動時にここで読む。
    /// 作業フォルダは %LOCALAPPDATA%\VRCast\update。インストール先に書き込めない場合（Program Files 等）は自動更新しない。
    /// </summary>
    public class UpdateInstaller : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "Update";

        // 同梱のアップデーター（StreamingAssets 内のフォルダとファイル名）
        public const string UpdaterFolder = "Updater";
        public const string UpdaterFileName = "VRCastUpdater.exe";

        // 作業フォルダ内の名前（ダウンロードした zip・展開先・アップデーターのログと結果）
        private const string WorkFolderName = "update";
        private const string DownloadFileName = "VRCast-update.zip";
        private const string StagingFolderName = "VRCast";
        private const string LogFileName = "updater.log";
        private const string ResultFileName = "result.txt";

        // アップデーターが旧ファイルを退避するフォルダ（インストール先の直下。Tools/Updater と同じ名前）
        private const string BackupFolderName = ".vrcast-update-backup";

        // インストール先に書き込めるかを確かめる一時ファイル
        private const string WriteTestFileName = ".vrcast-write-test";

        // ダウンロードが進まないまま諦めるまでの秒数
        private const float StallSeconds = 60f;

        /// <summary>
        /// 更新の進み具合。
        /// </summary>
        public enum InstallState
        {
            Idle,
            Downloading,
            Preparing,
            Restarting,
            Failed,
        }

        /// <summary>
        /// 自動更新できるか（できない理由）。
        /// </summary>
        public enum Availability
        {
            Available,

            // version.json に自動更新の情報が無い・不正
            NoPackage,

            // Unity エディターで実行中
            Editor,

            // アップデーターが同梱されていない
            UpdaterMissing,

            // インストール先に書き込めない（Program Files 等）
            NotWritable,
        }

        /// <summary>
        /// 失敗の種類（UI で表示言語の文言にする）。
        /// </summary>
        public enum Failure
        {
            None,
            Download,
            Corrupted,
            Extract,
            Launch,
            Cancelled,
        }

        /// <summary>
        /// 前回の更新の結果（アップデーターが残したもの）。
        /// </summary>
        public enum PreviousResult
        {
            None,
            Succeeded,
            Failed,
        }

        private UpdateChecker _updates;
        private Availability? _availability;
        private bool _cancel;
        private Task _cleanup;

        public InstallState State { get; private set; } = InstallState.Idle;

        /// <summary>
        /// 直前の失敗の種類と、原因（英語。ログと UI に添える）。
        /// </summary>
        public Failure LastFailure { get; private set; } = Failure.None;
        public string FailureDetail { get; private set; } = string.Empty;

        /// <summary>
        /// ダウンロード済み・全体のバイト数。
        /// </summary>
        public long DownloadedBytes { get; private set; }
        public long TotalBytes { get; private set; }

        /// <summary>
        /// ダウンロードの進み具合（0〜1）。
        /// </summary>
        public float Progress => TotalBytes > 0 ? Mathf.Clamp01((float)DownloadedBytes / TotalBytes) : 0f;

        /// <summary>
        /// ダウンロード・準備・終了の途中なら true（もう一度押せないようにする）。
        /// </summary>
        public bool IsBusy => State == InstallState.Downloading || State == InstallState.Preparing
            || State == InstallState.Restarting;

        /// <summary>
        /// 前回の更新の結果と、失敗時の理由（英語）。表示を閉じたら None に戻す。
        /// </summary>
        public PreviousResult Previous { get; private set; } = PreviousResult.None;
        public string PreviousDetail { get; private set; } = string.Empty;

        // %LOCALAPPDATA%\VRCast\update
        private static string WorkFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCast", WorkFolderName);

        // 同梱のアップデーター
        private static string UpdaterSource =>
            Path.Combine(Application.streamingAssetsPath, UpdaterFolder, UpdaterFileName);

        public void Initialize(UpdateChecker updates)
        {
            _updates = updates;
        }

        private void Start()
        {
            // エディターでは更新しないので、結果の読み込み・掃除もしない
            if (Application.isEditor)
            {
                return;
            }

            ReadPreviousResult();

            // 前回の更新の残り（展開したファイル・zip・退避した旧ファイル）を裏で消す
            string installFolder = InstallFolder();
            string workFolder = WorkFolder;
            _cleanup = Task.Run(() => CleanUp(installFolder, workFolder));
        }

        /// <summary>
        /// 自動更新できるか。新しいバージョンの確認が済んでから呼ぶ（書き込めるかの確認は 1 回だけ行う）。
        /// </summary>
        public Availability GetAvailability()
        {
            // 確認前・情報なしは都度判定する（後から確認が終わることがある）
            if (Application.isEditor)
            {
                return Availability.Editor;
            }

            if (_updates == null || _updates.Package == null)
            {
                return Availability.NoPackage;
            }

            // ファイルの確認は一度だけ
            if (!_availability.HasValue)
            {
                string folder = InstallFolder();
                if (!File.Exists(UpdaterSource))
                {
                    _availability = Availability.UpdaterMissing;
                }
                else if (folder == null || !CanWrite(folder))
                {
                    _availability = Availability.NotWritable;
                }
                else
                {
                    _availability = Availability.Available;
                }

                VRCastLog.Info(LogCategory, $"Auto-update: {_availability.Value}");
            }

            return _availability.Value;
        }

        /// <summary>
        /// ダウンロードから始めて更新する（成功すると VRCast は終了し、更新後に起動し直す）。
        /// </summary>
        public void Install()
        {
            // 実行中・更新できない状態なら何もしない
            if (IsBusy || GetAvailability() != Availability.Available)
            {
                return;
            }

            _cancel = false;
            StartCoroutine(Run(_updates.Package));
        }

        /// <summary>
        /// ダウンロードを中止する（準備・終了の途中では止めない）。
        /// </summary>
        public void Cancel()
        {
            if (State == InstallState.Downloading)
            {
                _cancel = true;
            }
        }

        /// <summary>
        /// 前回の更新の結果の表示を閉じる。
        /// </summary>
        public void DismissPrevious()
        {
            Previous = PreviousResult.None;
            PreviousDetail = string.Empty;
        }

        private IEnumerator Run(UpdateChecker.PackageInfo package)
        {
            State = InstallState.Downloading;
            LastFailure = Failure.None;
            FailureDetail = string.Empty;
            DownloadedBytes = 0;
            TotalBytes = package.size;
            VRCastLog.Info(LogCategory, $"Downloading {package.zipUrl} ({package.size} bytes)");

            // 起動時の掃除と同じフォルダを使うため、終わるのを待つ
            while (_cleanup != null && !_cleanup.IsCompleted)
            {
                yield return null;
            }

            string work = WorkFolder;
            string zipPath = Path.Combine(work, DownloadFileName);
            string error = CreateFolder(work);
            if (error != null)
            {
                Fail(Failure.Download, error);
                yield break;
            }

            // ダウンロード（GitHub は別ドメインへ転送するが、中身は後で SHA-256 で確かめる）
            using (var request = new UnityWebRequest(package.zipUrl, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(zipPath) { removeFileOnAbort = true };
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                ulong lastBytes = 0;
                float lastProgress = Time.unscaledTime;
                bool stalled = false;
                while (!operation.isDone)
                {
                    // 中止の指示
                    if (_cancel)
                    {
                        request.Abort();
                        break;
                    }

                    // 一定時間まったく進まなければ諦める（全体の時間では切らない。回線が遅くても進んでいれば待つ）
                    ulong bytes = request.downloadedBytes;
                    if (bytes != lastBytes)
                    {
                        lastBytes = bytes;
                        lastProgress = Time.unscaledTime;
                    }
                    else if (Time.unscaledTime - lastProgress > StallSeconds)
                    {
                        stalled = true;
                        request.Abort();
                        break;
                    }

                    DownloadedBytes = (long)bytes;
                    yield return null;
                }

                if (_cancel)
                {
                    Fail(Failure.Cancelled, "Cancelled");
                    yield break;
                }

                if (stalled || request.result != UnityWebRequest.Result.Success)
                {
                    Fail(Failure.Download, stalled ? "The download stopped" : request.error);
                    yield break;
                }

                DownloadedBytes = TotalBytes;
            }

            // 大きさ・SHA-256 の確認と展開は時間がかかるので別スレッドで行う
            State = InstallState.Preparing;
            string exe = GpuSelection.ExecutablePath();
            string exeName = Path.GetFileName(exe ?? string.Empty);
            string staging = Path.Combine(work, StagingFolderName);
            Task<string> prepare = Task.Run(() => Prepare(zipPath, package.size, package.sha256, staging, exeName));
            while (!prepare.IsCompleted)
            {
                yield return null;
            }

            if (prepare.IsFaulted)
            {
                Fail(Failure.Extract, prepare.Exception?.GetBaseException().Message ?? "Unknown error");
                yield break;
            }

            if (prepare.Result != null)
            {
                Fail(Failure.Corrupted, prepare.Result);
                yield break;
            }

            // アップデーターを作業フォルダへ写して起動する（インストール先の分も差し替えられるように）
            error = Launch(work, staging, exeName);
            if (error != null)
            {
                Fail(Failure.Launch, error);
                yield break;
            }

            // 設定を保存して終了する（アップデーターが終了を待って差し替え、起動し直す）
            State = InstallState.Restarting;
            VRCastLog.Info(LogCategory, $"Quitting to install {_updates.LatestVersion}");
            AppBootstrap.Save();
            Application.Quit();
        }

        private static string Prepare(string zipPath, long size, string sha256, string staging, string exeName)
        {
            // 大きさ・SHA-256 が version.json と一致しなければ使わない（途中で切れた・改ざんされた）
            long actual = new FileInfo(zipPath).Length;
            if (actual != size)
            {
                return $"Size mismatch ({actual} bytes, expected {size})";
            }

            string hash = HashUtility.ComputeSha256Hex(zipPath);
            if (!string.Equals(hash, sha256, StringComparison.OrdinalIgnoreCase))
            {
                return "SHA-256 mismatch";
            }

            // 本体の分を展開し、起動に必要なものがそろっているか確かめる
            int count = UpdatePackage.ExtractApp(zipPath, staging);
            VRCastLog.Info(LogCategory, $"Extracted {count} files to {staging}");
            return UpdatePackage.HasAppFiles(staging, exeName) ? null : $"The package does not contain {exeName}";
        }

        private string Launch(string work, string staging, string exeName)
        {
            string updater = Path.Combine(work, UpdaterFileName);
            string resultPath = Path.Combine(work, ResultFileName);
            try
            {
                // 前回の結果を消してから、同梱のアップデーターを写して起動する
                File.Copy(UpdaterSource, updater, true);
                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }

                string arguments = UpdatePackage.BuildUpdaterArguments(NativeProcess.CurrentProcessId, staging,
                    InstallFolder(), exeName, Path.Combine(work, LogFileName), resultPath);
                NativeProcess.Start(updater, arguments, work).Dispose();
                return null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is Win32Exception)
            {
                return e.Message;
            }
        }

        private void Fail(Failure failure, string detail)
        {
            // 失敗を記録し、もう一度押せる状態に戻す（中止は失敗として残さない）
            State = failure == Failure.Cancelled ? InstallState.Idle : InstallState.Failed;
            LastFailure = failure == Failure.Cancelled ? Failure.None : failure;
            FailureDetail = detail ?? string.Empty;
            DownloadedBytes = 0;
            VRCastLog.Warning(LogCategory, $"Update {failure}: {detail}");
        }

        private void ReadPreviousResult()
        {
            // アップデーターの結果（1 行目 ok / failed、2 行目に理由）を読んで消す
            string path = Path.Combine(WorkFolder, ResultFileName);
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                string[] lines = File.ReadAllLines(path);
                File.Delete(path);
                Previous = lines.Length > 0 && lines[0].Trim() == "ok" ? PreviousResult.Succeeded : PreviousResult.Failed;
                PreviousDetail = lines.Length > 1 ? lines[1].Trim() : string.Empty;
                VRCastLog.Info(LogCategory, $"Previous update: {Previous} {PreviousDetail}");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                VRCastLog.Info(LogCategory, "Could not read the update result: " + e.Message);
            }
        }

        private static void CleanUp(string installFolder, string workFolder)
        {
            // 展開したファイルと zip、退避した旧ファイル（他のアプリが読み込み中の DLL は消せないので次回に持ち越す）
            DeleteFolder(Path.Combine(workFolder, StagingFolderName));
            DeleteFile(Path.Combine(workFolder, DownloadFileName));
            if (installFolder != null)
            {
                DeleteFolder(Path.Combine(installFolder, BackupFolderName));
            }
        }

        private static void DeleteFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                VRCastLog.Info(LogCategory, $"Could not delete {folder}: {e.Message}");
            }
        }

        private static void DeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                VRCastLog.Info(LogCategory, $"Could not delete {path}: {e.Message}");
            }
        }

        private static string CreateFolder(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                return null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return e.Message;
            }
        }

        private static bool CanWrite(string folder)
        {
            // 一時ファイルを作って消せれば書き込める（Program Files 等は管理者権限が無いと失敗する）
            string path = Path.Combine(folder, WriteTestFileName);
            try
            {
                File.WriteAllBytes(path, Array.Empty<byte>());
                File.Delete(path);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string InstallFolder()
        {
            // 実行中の exe のフォルダ（取得できなければ null）
            string exe = GpuSelection.ExecutablePath();
            return exe != null ? Path.GetDirectoryName(exe) : null;
        }
    }
}
