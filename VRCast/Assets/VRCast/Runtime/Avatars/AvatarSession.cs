using System;
using System.Collections;
using System.IO;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Avatars
{
    /// <summary>
    /// 現在表示中のアバター 1 体のロード・リロード・アンロードを管理する。
    /// </summary>
    public class AvatarSession : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "AvatarSession";

        // 展開した bundle のキャッシュフォルダ名
        private const string CacheFolderName = "avatars";

        public LoadedAvatar Current { get; private set; }
        public bool IsLoading { get; private set; }
        public string LastError { get; private set; }

        /// <summary>
        /// アバターの読込が完了したときに通知する。
        /// </summary>
        public event Action<LoadedAvatar> AvatarLoaded;

        private string CacheRoot => Path.Combine(Application.temporaryCachePath, CacheFolderName);

        public void Load(string packagePath)
        {
            // 読込中の多重実行は無視
            if (IsLoading)
            {
                return;
            }

            // 空パスはエラーとして表示
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                LastError = "Avatar path is empty.";
                return;
            }

            StartCoroutine(LoadRoutine(PathUtility.NormalizeInput(packagePath)));
        }

        public void Reload()
        {
            // 表示中のアバターがあれば同じファイルを読み直す
            if (Current != null)
            {
                Load(Current.SourcePath);
            }
        }

        public void Unload()
        {
            // 表示中のアバターと bundle を解放
            Current?.Dispose();
            Current = null;
        }

        private IEnumerator LoadRoutine(string packagePath)
        {
            IsLoading = true;
            LastError = null;

            // 同一 bundle の二重読込は Unity がエラーにするため先に解放
            Unload();

            // パッケージを検証・展開（失敗理由は UI に表示）
            AvatarPackage package = null;
            try
            {
                package = AvatarPackageReader.Extract(packagePath, CacheRoot);
            }
            catch (Exception e) when (e is AvatarPackageException || e is IOException || e is UnauthorizedAccessException)
            {
                LastError = e.Message;
            }

            // 展開できた場合のみ bundle を読み込む
            if (package != null)
            {
                yield return AvatarLoader.Load(package, transform, OnLoaded, OnError);
            }

            // エラーはログにも残す
            if (LastError != null)
            {
                VRCastLog.Error(LogCategory, $"Failed to load '{packagePath}': {LastError}");
            }

            IsLoading = false;
        }

        private void OnLoaded(LoadedAvatar avatar)
        {
            // 現在のアバターとして保持し、購読者へ通知
            Current = avatar;
            AvatarLoaded?.Invoke(avatar);
        }

        private void OnError(string message)
        {
            LastError = message;
        }

        private void OnDestroy()
        {
            // 終了時に bundle を解放
            Unload();
        }
    }
}
