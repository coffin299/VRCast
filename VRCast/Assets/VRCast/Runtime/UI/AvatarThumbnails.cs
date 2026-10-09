using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using VRCast.Avatars;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// 最近使ったアバターの一覧に出す画像（PNG / JPG / GIF）。選んだ画像は設定フォルダの Thumbnails へコピーし、
    /// 元のファイルを動かしても消えないようにする。読み込みは表示したときに裏で行い、GIF はアニメーションする。
    /// </summary>
    public class AvatarThumbnails
    {
        // ログのカテゴリ名
        private const string LogCategory = "Thumbnail";

        // 画像のコピー先（persistentDataPath の下）
        private const string FolderName = "Thumbnails";

        // 受け付ける画像の大きさの上限（バイト）と、縮小後の長辺（一覧の表示より大きめにして拡大表示でもぼけにくくする）
        public const long MaxFileBytes = 20L * 1024 * 1024;
        private const int MaxSize = 256;

        // GIF のコマ数の上限（縮小後でも 1 コマ 256 KB になるため）
        private const int MaxGifFrames = 300;

        // 一覧の画像として選べる拡張子（ファイル選択ダイアログの絞り込みにも使う）
        public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".gif" };

        private readonly AppSettings _settings;
        private readonly string _folder;

        // 読み込んだ画像（ファイル名 → 表示用の状態）
        private readonly Dictionary<string, Thumbnail> _cache = new Dictionary<string, Thumbnail>(StringComparer.OrdinalIgnoreCase);

        public AvatarThumbnails(AppSettings settings)
        {
            _settings = settings;
            _folder = Path.Combine(Application.persistentDataPath, FolderName);
            DeleteUnused();
        }

        /// <summary>
        /// 一覧の画像として使える拡張子なら true。存在確認はしない。
        /// </summary>
        public static bool IsImage(string path)
        {
            return Array.Exists(Extensions, extension => AvatarFiles.HasExtension(path, extension));
        }

        /// <summary>
        /// 複数のパス（ドロップされたファイル等）から最初の画像を返す。無ければ null。
        /// </summary>
        public static string FindImage(IEnumerable<string> paths)
        {
            if (paths == null)
            {
                return null;
            }

            foreach (string path in paths)
            {
                if (IsImage(path))
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>
        /// アバターの画像をコピーして設定する（前の画像は消す）。失敗したら理由（英語）を返し、成功なら null。
        /// </summary>
        public string Set(string avatarPath, string imagePath)
        {
            // 対応していない・見つからない・大きすぎる画像は断る
            if (!IsImage(imagePath))
            {
                return "Unsupported image (PNG, JPG and GIF only)";
            }

            var info = new FileInfo(imagePath);
            if (!info.Exists)
            {
                return "File not found";
            }

            if (info.Length > MaxFileBytes)
            {
                return $"The image is too large (max {MaxFileBytes / (1024 * 1024)} MB)";
            }

            // 同じ名前の別画像と取り違えないよう、毎回新しい名前でコピーする
            string fileName = Guid.NewGuid().ToString("N") + info.Extension.ToLowerInvariant();
            try
            {
                Directory.CreateDirectory(_folder);
                File.Copy(imagePath, Path.Combine(_folder, fileName));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                VRCastLog.Warning(LogCategory, $"Could not copy the image: {e.Message}");
                return e.Message;
            }

            // 記録の無いアバター（読み込んだことが無い）は対象外。コピーした画像は使わないので消す
            string previous = _settings.GetAvatarThumbnail(avatarPath);
            if (!_settings.SetAvatarThumbnail(avatarPath, fileName))
            {
                DeleteFile(fileName);
                return "Load the avatar once before setting an image";
            }

            Release(previous);
            AppBootstrap.Save();
            return null;
        }

        /// <summary>
        /// アバターの画像を外す（コピーした画像も消す）。
        /// </summary>
        public void Clear(string avatarPath)
        {
            string previous = _settings.GetAvatarThumbnail(avatarPath);
            if (string.IsNullOrEmpty(previous))
            {
                return;
            }

            _settings.SetAvatarThumbnail(avatarPath, string.Empty);
            Release(previous);
            AppBootstrap.Save();
        }

        /// <summary>
        /// 一覧から外したアバターの画像を消す（AppSettings.ForgetAvatar の前に呼ぶ）。
        /// </summary>
        public void Forget(string avatarPath)
        {
            Release(_settings.GetAvatarThumbnail(avatarPath));
        }

        /// <summary>
        /// 画像が設定されていれば true（読み込みが終わっていなくても true）。
        /// </summary>
        public bool Has(string avatarPath)
        {
            return !string.IsNullOrEmpty(_settings.GetAvatarThumbnail(avatarPath));
        }

        /// <summary>
        /// 表示する画像（GIF は今のコマ）。未設定・読込中・読めなかった画像は null。
        /// </summary>
        public Texture2D Get(string avatarPath)
        {
            string fileName = _settings.GetAvatarThumbnail(avatarPath);
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            // 初めて表示するときに裏で読み込みを始める
            if (!_cache.TryGetValue(fileName, out Thumbnail thumbnail))
            {
                thumbnail = new Thumbnail(Path.Combine(_folder, fileName));
                _cache.Add(fileName, thumbnail);
            }

            return thumbnail.Current();
        }

        private void Release(string fileName)
        {
            // 表示用のテクスチャとコピーした画像を消す
            if (string.IsNullOrEmpty(fileName))
            {
                return;
            }

            if (_cache.TryGetValue(fileName, out Thumbnail thumbnail))
            {
                thumbnail.Dispose();
                _cache.Remove(fileName);
            }

            DeleteFile(fileName);
        }

        private void DeleteFile(string fileName)
        {
            try
            {
                File.Delete(Path.Combine(_folder, fileName));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // 消せなかった画像は次の起動時に消す
                VRCastLog.Warning(LogCategory, $"Could not delete {fileName}: {e.Message}");
            }
        }

        private void DeleteUnused()
        {
            // 一覧から外れた（古くなって押し出された・強制終了で記録されなかった）アバターの画像を消す
            if (!Directory.Exists(_folder))
            {
                return;
            }

            HashSet<string> used = _settings.AvatarThumbnails();
            foreach (string path in Directory.GetFiles(_folder))
            {
                string fileName = Path.GetFileName(path);
                if (!used.Contains(fileName))
                {
                    DeleteFile(fileName);
                }
            }
        }

        /// <summary>
        /// 1 枚の画像の読み込みと表示（GIF はコマの切り替え）。
        /// </summary>
        private sealed class Thumbnail
        {
            // 裏で読んだファイルの中身（PNG / JPG）か、展開済みの GIF
            private readonly Task<(byte[] Bytes, GifImage Gif)> _loading;
            private Texture2D _texture;
            private GifImage _gif;
            private float _gifStart;
            private int _gifDuration;
            private int _shownFrame = -1;
            private bool _done;

            public Thumbnail(string path)
            {
                // ファイルの読み込みと GIF の展開はメインスレッドを止めないよう裏で行う
                _loading = Task.Run(() =>
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    return GifDecoder.IsGif(bytes)
                        ? ((byte[])null, GifDecoder.Decode(bytes, MaxSize, MaxGifFrames))
                        : (bytes, (GifImage)null);
                });
            }

            public Texture2D Current()
            {
                // 読み込みが終わったらテクスチャを作る（読めなかった画像は null のまま）
                if (!_done)
                {
                    // 縮小で描画先を切り替えるため、描画中（Repaint）は避けて配置の計算のときに作る
                    if (!_loading.IsCompleted || (Event.current != null && Event.current.type != EventType.Layout))
                    {
                        return null;
                    }

                    _done = true;
                    Finish();
                }

                if (_gif != null)
                {
                    ShowGifFrame();
                }

                return _texture;
            }

            public void Dispose()
            {
                if (_texture != null)
                {
                    UnityEngine.Object.Destroy(_texture);
                    _texture = null;
                }

                _gif = null;
            }

            private void Finish()
            {
                if (_loading.IsFaulted)
                {
                    VRCastLog.Warning(LogCategory, $"Could not read the image: {_loading.Exception?.GetBaseException().Message}");
                    return;
                }

                (byte[] bytes, GifImage gif) = _loading.Result;
                if (gif != null)
                {
                    // GIF は 1 枚のテクスチャへコマを書き込んで切り替える
                    _gif = gif;
                    _texture = new Texture2D(gif.Width, gif.Height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                    _gifStart = Time.unscaledTime;
                    foreach (GifFrame frame in gif.Frames)
                    {
                        _gifDuration += frame.DelayMs;
                    }

                    return;
                }

                // PNG / JPG は Unity で読み、大きい画像は縮小してメモリを抑える
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!source.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(source);
                    VRCastLog.Warning(LogCategory, "Could not decode the image");
                    return;
                }

                _texture = Shrink(source);
            }

            private void ShowGifFrame()
            {
                // 経過時間からコマを選び、変わったときだけ書き込む（1 コマなら 1 回だけ）
                int frame = 0;
                if (_gif.Frames.Count > 1 && _gifDuration > 0)
                {
                    int elapsed = (int)((Time.unscaledTime - _gifStart) * 1000f) % _gifDuration;
                    while (elapsed >= _gif.Frames[frame].DelayMs)
                    {
                        elapsed -= _gif.Frames[frame].DelayMs;
                        frame++;
                    }
                }

                if (frame == _shownFrame)
                {
                    return;
                }

                _shownFrame = frame;
                _texture.LoadRawTextureData(_gif.Frames[frame].Rgba);
                _texture.Apply(false);
            }

            private static Texture2D Shrink(Texture2D source)
            {
                // 長辺が MaxSize 以下ならそのまま使う
                float scale = (float)MaxSize / Mathf.Max(source.width, source.height);
                source.wrapMode = TextureWrapMode.Clamp;
                if (scale >= 1f)
                {
                    return source;
                }

                // GPU で縮小して読み戻す
                int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
                int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
                RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                RenderTexture active = RenderTexture.active;
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var result = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                result.Apply(false);
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(source);
                return result;
            }
        }
    }
}
