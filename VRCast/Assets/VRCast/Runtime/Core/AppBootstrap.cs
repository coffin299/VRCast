using System.IO;
using UnityEngine;

namespace VRCast.Core
{
    /// <summary>
    /// 起動時に設定を読み込み、終了時に保存する。シーン配置不要。
    /// </summary>
    public static class AppBootstrap
    {
        // ログのカテゴリ名
        private const string LogCategory = "Bootstrap";

        private static SettingsStore _store;

        /// <summary>
        /// 起動時に読み込んだ現在の設定。起動前は null。
        /// </summary>
        public static AppSettings Settings { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            // 起動情報を出力して Player.log から環境を追えるようにする
            VRCastLog.Info(LogCategory, $"VRCast {Application.version} / Unity {Application.unityVersion} / {Application.platform}");

            // 既定パスの設定ファイルを読み込む
            _store = SettingsStore.CreateDefault();
            Settings = _store.Load();

            // 初回起動時は既定値でファイルを作成し、終了処理に頼らず編集可能な状態にする
            if (!File.Exists(_store.FilePath))
            {
                _store.Save(Settings);
            }

            // ウィンドウサイズの変更はスタンドアロン実行時のみ行う（Editor の Game View は触らない）
            if (!Application.isEditor)
            {
                Screen.SetResolution(Settings.windowWidth, Settings.windowHeight, FullScreenMode.Windowed);
            }

            // 終了時に保存する。Editor の Domain Reload 無効時の二重登録を防ぐため一度外す
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting()
        {
            // 未初期化の場合は保存対象が無い
            if (_store == null || Settings == null)
            {
                return;
            }

            // 現在の設定を保存
            _store.Save(Settings);
        }
    }
}
