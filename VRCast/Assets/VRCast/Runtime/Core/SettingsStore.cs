using System;
using System.IO;
using UnityEngine;

namespace VRCast.Core
{
    /// <summary>
    /// AppSettings を JSON ファイルへ読み書きする。
    /// ファイルが無い・壊れている場合は既定値にフォールバックする。
    /// </summary>
    public class SettingsStore
    {
        // ログのカテゴリ名
        private const string LogCategory = "Settings";

        // 既定の設定ファイル名
        public const string DefaultFileName = "settings.json";

        public string FilePath { get; }

        public SettingsStore(string filePath)
        {
            // 空パスは保存先として無効なので即座に弾く
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("Settings file path is empty.", nameof(filePath));
            }

            FilePath = filePath;
        }

        /// <summary>
        /// persistentDataPath 配下の既定パスを使うストアを生成する。
        /// </summary>
        public static SettingsStore CreateDefault()
        {
            // OS ごとのユーザーデータ領域に保存する
            return new SettingsStore(Path.Combine(Application.persistentDataPath, DefaultFileName));
        }

        public AppSettings Load()
        {
            // 初回起動などでファイルが無ければ既定値を返す
            if (!File.Exists(FilePath))
            {
                VRCastLog.Info(LogCategory, $"No settings file. Using defaults: {FilePath}");
                return new AppSettings();
            }

            try
            {
                // ファイル全体を読み込んで JSON から復元
                string json = File.ReadAllText(FilePath);
                AppSettings settings = JsonUtility.FromJson<AppSettings>(json);

                // 空ファイル等で null になった場合は既定値を使う
                if (settings == null)
                {
                    VRCastLog.Warning(LogCategory, $"Settings file is empty. Using defaults: {FilePath}");
                    return new AppSettings();
                }

                // 範囲外の値を補正してから返す
                settings.Sanitize();
                VRCastLog.Info(LogCategory, $"Loaded settings: {FilePath}");
                return settings;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                // 読込失敗や JSON 破損では起動を止めず既定値で続行
                VRCastLog.Warning(LogCategory, $"Failed to load settings ({e.Message}). Using defaults.");
                return new AppSettings();
            }
        }

        public bool Save(AppSettings settings)
        {
            // null を保存しようとした場合は呼び出し側のバグとして扱う
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            try
            {
                // 保存先ディレクトリが無ければ作成
                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 書き込み途中で落ちても既存ファイルを壊さないよう一時ファイル経由で置き換える
                string tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, JsonUtility.ToJson(settings, true));

                // 既存ファイルがあれば置換、無ければ移動
                if (File.Exists(FilePath))
                {
                    File.Replace(tempPath, FilePath, null);
                }
                else
                {
                    File.Move(tempPath, FilePath);
                }

                // 保存成功を記録して Player.log から追えるようにする
                VRCastLog.Info(LogCategory, $"Saved settings: {FilePath}");
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // 保存失敗はアプリを止めずに通知のみ
                VRCastLog.Error(LogCategory, $"Failed to save settings: {e.Message}");
                return false;
            }
        }
    }
}
