using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using VRCast.Core;

namespace VRCast.Platform
{
    /// <summary>
    /// 公式 Discord サーバーのウィジェット（widget.json）と、オンラインのメンバーのアイコンを取得する。
    /// 呼び出し側（Discord タブ）が表示中に Poll を呼んだときだけ通信し、一定間隔で取り直す。失敗（オフライン等）は静かに諦める。
    /// </summary>
    public sealed class DiscordWidget : IDisposable
    {
        // ログのカテゴリ名
        private const string LogCategory = "Discord";

        // サーバーのウィジェットの情報（サーバー設定でウィジェットを有効にしてあるもの）
        public const string WidgetUrl = "https://discord.com/api/guilds/1558139661933879329/widget.json";

        // 招待コードと、参加ボタンで開く招待（期限なし）。widget.json の instant_invite は期限付きのことがあるので使わない
        public const string InviteCode = "vM5RH52HdF";
        public const string InviteUrl = "https://discord.gg/" + InviteCode;

        // アイコンを取りに行ってよい場所（取得した JSON から任意の URL へ通信しないため）
        private const string AvatarUrlPrefix = "https://cdn.discordapp.com/";

        // アイコンの大きさ（px。Discord の CDN に小さい画像を頼む）
        private const int AvatarSize = 64;

        // 表示・アイコン取得の対象にするメンバー数の上限
        private const int MaxMembers = 50;

        // 取得のタイムアウト（秒）と、取り直す間隔（秒）
        private const int TimeoutSeconds = 10;
        private const float RefreshSeconds = 60f;

        /// <summary>
        /// widget.json の形（フィールド名は JSON と一致させる）。
        /// </summary>
        [Serializable]
        private class Widget
        {
            public string name;
            public Member[] members;
            public int presence_count;
        }

        /// <summary>
        /// オンラインのメンバー（status は online / idle / dnd）。
        /// </summary>
        [Serializable]
        public class Member
        {
            public string username;
            public string status;
            public string avatar_url;
        }

        /// <summary>
        /// 取得の状態。
        /// </summary>
        public enum FetchState
        {
            NotFetched,
            Fetching,
            Done,
            Failed,
        }

        private readonly List<Member> _members = new List<Member>();
        private readonly Dictionary<string, Texture2D> _avatars = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly Queue<string> _pendingAvatars = new Queue<string>();
        private UnityWebRequest _request;
        private UnityWebRequest _avatarRequest;
        private string _avatarRequestUrl;
        private float _fetchedAt;

        public FetchState State { get; private set; } = FetchState.NotFetched;

        /// <summary>
        /// サーバー名（未取得なら空）。
        /// </summary>
        public string ServerName { get; private set; } = string.Empty;

        /// <summary>
        /// オンラインの人数（一覧に出ない人も含む）。
        /// </summary>
        public int OnlineCount { get; private set; }

        /// <summary>
        /// オンラインのメンバー（上限まで）。
        /// </summary>
        public IReadOnlyList<Member> Members => _members;

        /// <summary>
        /// メンバーのアイコン（未取得・取得失敗なら null）。
        /// </summary>
        public Texture2D GetAvatar(Member member)
        {
            return member != null && member.avatar_url != null && _avatars.TryGetValue(member.avatar_url, out Texture2D texture)
                ? texture
                : null;
        }

        /// <summary>
        /// 取得の進行・完了の処理と、間隔が空いたときの取り直しを行う（表示中に毎回呼ぶ）。
        /// </summary>
        public void Poll()
        {
            // 情報の取得が終わったら読み取る
            if (_request != null && _request.isDone)
            {
                FinishFetch();
            }

            // 未取得、または前回から間隔が空いたら取り直す（失敗時も同じ間隔で再試行）
            bool stale = Time.unscaledTime - _fetchedAt >= RefreshSeconds;
            if (_request == null && (State == FetchState.NotFetched || stale))
            {
                StartFetch();
            }

            PollAvatars();
        }

        /// <summary>
        /// すぐに取り直す（取得中なら何もしない）。
        /// </summary>
        public void Refresh()
        {
            if (_request == null)
            {
                StartFetch();
            }
        }

        public void Dispose()
        {
            // 通信を止め、作ったテクスチャを破棄する
            _request?.Dispose();
            _request = null;
            _avatarRequest?.Dispose();
            _avatarRequest = null;
            _pendingAvatars.Clear();
            foreach (Texture2D texture in _avatars.Values)
            {
                UnityEngine.Object.Destroy(texture);
            }

            _avatars.Clear();
        }

        private void StartFetch()
        {
            _request = UnityWebRequest.Get(WidgetUrl);
            _request.timeout = TimeoutSeconds;
            _request.SendWebRequest();
            _fetchedAt = Time.unscaledTime;
            // 取得済みの内容は取り直し中も表示し続ける
            if (State != FetchState.Done)
            {
                State = FetchState.Fetching;
            }
        }

        private void FinishFetch()
        {
            // 通信失敗・HTTP エラー（ウィジェットが無効等）は失敗扱い。取得済みの内容があれば残す
            bool success = _request.result == UnityWebRequest.Result.Success;
            string json = success ? _request.downloadHandler.text : null;
            string error = _request.error;
            _request.Dispose();
            _request = null;
            Widget widget = success ? Parse(json) : null;
            if (widget == null)
            {
                State = State == FetchState.Done ? FetchState.Done : FetchState.Failed;
                VRCastLog.Info(LogCategory, "Widget fetch failed: " + (error ?? "invalid JSON"));
                return;
            }

            ServerName = widget.name ?? string.Empty;
            OnlineCount = Mathf.Max(0, widget.presence_count);
            SetMembers(widget.members);
            State = FetchState.Done;
        }

        private static Widget Parse(string json)
        {
            try
            {
                return JsonUtility.FromJson<Widget>(json);
            }
            catch (ArgumentException)
            {
                // JSON として読めない
                return null;
            }
        }

        private void SetMembers(Member[] members)
        {
            // 壊れた項目を除いて上限まで並べる
            _members.Clear();
            foreach (Member member in members ?? Array.Empty<Member>())
            {
                if (member == null || _members.Count >= MaxMembers)
                {
                    continue;
                }

                member.username ??= string.Empty;
                // 許可外の場所のアイコンは取りに行かない
                member.avatar_url = IsAllowedAvatarUrl(member.avatar_url) ? member.avatar_url : null;
                _members.Add(member);
            }

            // 一覧から消えた人のアイコンを破棄し、新しい人のアイコンを取得待ちにする
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (Member member in _members)
            {
                if (member.avatar_url != null)
                {
                    used.Add(member.avatar_url);
                }
            }

            var removed = new List<string>();
            foreach (KeyValuePair<string, Texture2D> pair in _avatars)
            {
                if (!used.Contains(pair.Key))
                {
                    removed.Add(pair.Key);
                }
            }

            foreach (string url in removed)
            {
                UnityEngine.Object.Destroy(_avatars[url]);
                _avatars.Remove(url);
            }

            _pendingAvatars.Clear();
            foreach (string url in used)
            {
                // 取得済み・取得中のものは頼み直さない
                if (!_avatars.ContainsKey(url) && url != _avatarRequestUrl)
                {
                    _pendingAvatars.Enqueue(url);
                }
            }
        }

        private void PollAvatars()
        {
            // 取得の終わったアイコンを読み込む（1 枚ずつ順に取る）
            if (_avatarRequest != null && _avatarRequest.isDone)
            {
                if (_avatarRequest.result == UnityWebRequest.Result.Success)
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                    // 読めない画像は捨てる（アイコンなしで表示）
                    if (texture.LoadImage(_avatarRequest.downloadHandler.data))
                    {
                        _avatars[_avatarRequestUrl] = texture;
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(texture);
                    }
                }

                _avatarRequest.Dispose();
                _avatarRequest = null;
                _avatarRequestUrl = null;
            }

            // 次のアイコンを頼む
            if (_avatarRequest == null && _pendingAvatars.Count > 0)
            {
                _avatarRequestUrl = _pendingAvatars.Dequeue();
                string separator = _avatarRequestUrl.Contains("?") ? "&" : "?";
                _avatarRequest = UnityWebRequest.Get($"{_avatarRequestUrl}{separator}size={AvatarSize}");
                _avatarRequest.timeout = TimeoutSeconds;
                _avatarRequest.SendWebRequest();
            }
        }

        private static bool IsAllowedAvatarUrl(string url)
        {
            return !string.IsNullOrEmpty(url) && url.StartsWith(AvatarUrlPrefix, StringComparison.Ordinal);
        }
    }
}
