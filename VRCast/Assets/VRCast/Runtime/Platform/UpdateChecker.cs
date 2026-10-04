using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using VRCast.Core;

namespace VRCast.Platform
{
    /// <summary>
    /// Web サイト（webpage ブランチの GitHub Pages）の version.json を取得し、新しいバージョンがあるかを調べる。アプリ全体で 1 つ。
    /// 起動時に 1 回だけ確認し、失敗（オフライン等）は静かに諦める。自動ダウンロード・自動更新はしない。
    /// </summary>
    public class UpdateChecker : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "Update";

        // 最新バージョンの情報（{"version": "1.2.0", "url": "...", "boothUrl": "..."}）
        public const string ManifestUrl = "https://coffin299.github.io/VRCast/version.json";

        // version.json に url / boothUrl が無い・許可外のときに開くダウンロードページ
        public const string DefaultGitHubUrl = "https://github.com/coffin299/VRCast/releases/";
        public const string DefaultBoothUrl = "https://coffin299.booth.pm/items/8933317";

        // 開いてよいダウンロードページの接頭辞（取得した JSON から任意の URL を開かないため）
        private static readonly string[] GitHubUrlPrefixes =
        {
            "https://github.com/coffin299/VRCast/",
            "https://coffin299.github.io/VRCast/",
        };

        private static readonly string[] BoothUrlPrefixes =
        {
            "https://coffin299.booth.pm/",
        };

        // 取得のタイムアウト（秒）
        private const int TimeoutSeconds = 10;

        /// <summary>
        /// version.json の形（フィールド名は JSON と一致させる）。
        /// </summary>
        [Serializable]
        private class Manifest
        {
            public string version;
            public string url;
            public string boothUrl;
        }

        private AppSettings _settings;

        /// <summary>
        /// 確認の状態。
        /// </summary>
        public enum CheckState
        {
            NotChecked,
            Checking,
            Done,
            Failed,
        }

        public CheckState State { get; private set; } = CheckState.NotChecked;

        /// <summary>
        /// 取得した最新バージョン（未取得なら空）。
        /// </summary>
        public string LatestVersion { get; private set; } = string.Empty;

        /// <summary>
        /// GitHub（Releases）のダウンロードページ。
        /// </summary>
        public string GitHubUrl { get; private set; } = DefaultGitHubUrl;

        /// <summary>
        /// BOOTH のダウンロードページ。
        /// </summary>
        public string BoothUrl { get; private set; } = DefaultBoothUrl;

        /// <summary>
        /// 今のバージョンより新しいものがあれば true（通知しないことにしたバージョンでも true）。
        /// </summary>
        public bool IsUpdateAvailable => State == CheckState.Done && VersionUtility.IsNewer(LatestVersion, Application.version);

        /// <summary>
        /// 通知を出すべきなら true（新しいバージョンがあり、通知しないことにしたバージョンではない）。
        /// </summary>
        public bool ShouldNotify => IsUpdateAvailable && LatestVersion != _settings.skippedVersion;

        public void Initialize(AppSettings settings)
        {
            _settings = settings;
        }

        private void Start()
        {
            // 設定で OFF なら確認しない（後で ON にしたら Check で確認できる）
            if (_settings != null && _settings.checkForUpdates)
            {
                Check();
            }
        }

        /// <summary>
        /// 確認を始める（確認中・確認済みなら何もしない）。
        /// </summary>
        public void Check()
        {
            if (State == CheckState.Checking || State == CheckState.Done)
            {
                return;
            }

            StartCoroutine(Fetch());
        }

        /// <summary>
        /// 取得した最新バージョンを通知しないことにする（次の新しいバージョンでまた通知する）。
        /// </summary>
        public void SkipLatest()
        {
            _settings.skippedVersion = LatestVersion;
        }

        /// <summary>
        /// GitHub のダウンロードページをブラウザで開く。
        /// </summary>
        public void OpenGitHub()
        {
            Application.OpenURL(GitHubUrl);
        }

        /// <summary>
        /// BOOTH のダウンロードページをブラウザで開く。
        /// </summary>
        public void OpenBooth()
        {
            Application.OpenURL(BoothUrl);
        }

        private IEnumerator Fetch()
        {
            State = CheckState.Checking;

            using (UnityWebRequest request = UnityWebRequest.Get(ManifestUrl))
            {
                // 古い情報を掴まないよう CDN のキャッシュを避ける
                request.SetRequestHeader("Cache-Control", "no-cache");
                request.timeout = TimeoutSeconds;
                yield return request.SendWebRequest();

                // 通信失敗・HTTP エラーは静かに諦める（ログのみ）
                if (request.result != UnityWebRequest.Result.Success)
                {
                    State = CheckState.Failed;
                    VRCastLog.Info(LogCategory, "Check failed: " + request.error);
                    yield break;
                }

                Apply(request.downloadHandler.text);
            }
        }

        private void Apply(string json)
        {
            Manifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<Manifest>(json);
            }
            catch (ArgumentException)
            {
                // JSON として読めない
                manifest = null;
            }

            // バージョン番号が読めなければ失敗扱い
            if (manifest == null || !VersionUtility.TryParse(manifest.version, out _))
            {
                State = CheckState.Failed;
                VRCastLog.Warning(LogCategory, "Invalid version.json");
                return;
            }

            // 先頭の "v" 等を除いた番号を記録し、許可されたページだけを開くようにする
            LatestVersion = manifest.version.Trim().TrimStart('v', 'V');
            GitHubUrl = IsAllowedUrl(manifest.url, GitHubUrlPrefixes) ? manifest.url : DefaultGitHubUrl;
            BoothUrl = IsAllowedUrl(manifest.boothUrl, BoothUrlPrefixes) ? manifest.boothUrl : DefaultBoothUrl;
            State = CheckState.Done;
            VRCastLog.Info(LogCategory, $"Latest {LatestVersion}, current {Application.version}");
        }

        private static bool IsAllowedUrl(string url, string[] prefixes)
        {
            // 空・許可外の接頭辞は使わない
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            foreach (string prefix in prefixes)
            {
                if (url.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
