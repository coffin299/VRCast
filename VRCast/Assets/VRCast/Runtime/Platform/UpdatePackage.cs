using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace VRCast.Platform
{
    /// <summary>
    /// 自動更新の配布 zip（GitHub Releases の VRCast-x.y.z-win64.zip）の検証・展開と、アップデーターへ渡す引数。
    /// zip には VRCast/（本体）と VRCast-Converter/（書き出しツール）が入っており、本体の分だけを展開する。
    /// </summary>
    public static class UpdatePackage
    {
        // ダウンロードを許可する URL の接頭辞（取得した version.json から任意の場所を取りに行かないため）
        public const string DownloadUrlPrefix = "https://github.com/coffin299/VRCast/releases/download/";

        // zip 内の本体のフォルダ（この中身をインストール先へ置く）
        public const string AppFolderPrefix = "VRCast/";

        // SHA-256 の 16 進表記の長さ
        private const int Sha256HexLength = 64;

        /// <summary>
        /// version.json の update の内容が自動更新に使えれば true（許可された URL・正の大きさ・SHA-256）。
        /// </summary>
        public static bool IsValid(string zipUrl, long size, string sha256)
        {
            return IsAllowedUrl(zipUrl) && size > 0 && IsSha256(sha256);
        }

        /// <summary>
        /// GitHub Releases の VRCast のダウンロード URL で、上位参照などを含まなければ true。
        /// </summary>
        public static bool IsAllowedUrl(string url)
        {
            // 接頭辞の後ろで別の場所へ抜けられないよう、上位参照・クエリ・空白を拒否する
            return !string.IsNullOrEmpty(url)
                && url.StartsWith(DownloadUrlPrefix, StringComparison.Ordinal)
                && url.Length > DownloadUrlPrefix.Length
                && url.IndexOf("..", StringComparison.Ordinal) < 0
                && url.IndexOfAny(new[] { '?', '#', '\\', ' ' }) < 0;
        }

        /// <summary>
        /// 64 桁の 16 進数なら true（大文字小文字は問わない）。
        /// </summary>
        public static bool IsSha256(string text)
        {
            if (text == null || text.Length != Sha256HexLength)
            {
                return false;
            }

            foreach (char c in text)
            {
                // 0-9・a-f・A-F 以外は不可
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// zip のエントリ名から本体フォルダ内の相対パス（区切りは OS のもの）を得る。
        /// 本体以外（書き出しツール）・フォルダのエントリは false。不正な名前（上位参照・絶対パス等）は InvalidDataException。
        /// </summary>
        public static bool TryGetAppRelativePath(string entryName, out string relative)
        {
            relative = null;

            // 本体フォルダの外（VRCast-Converter/ 等）は展開しない
            if (string.IsNullOrEmpty(entryName) || !entryName.StartsWith(AppFolderPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            // フォルダのエントリはファイルの展開時に作るので飛ばす
            string path = entryName.Substring(AppFolderPrefix.Length);
            if (path.Length == 0 || path.EndsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            // zip slip（展開先の外へ書く名前）を拒否する: バックスラッシュ・ドライブ・先頭の区切り・上位参照
            if (path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0 || path.StartsWith("/", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Invalid entry: {entryName}");
            }

            foreach (string part in path.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == "..")
                {
                    throw new InvalidDataException($"Invalid entry: {entryName}");
                }
            }

            relative = path.Replace('/', Path.DirectorySeparatorChar);
            return true;
        }

        /// <summary>
        /// zip の本体フォルダの中身を destination へ展開する（destination は作り直す）。展開したファイル数を返す。
        /// </summary>
        public static int ExtractApp(string zipPath, string destination)
        {
            // 前回の残りと混ざらないよう消してから作る
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }

            Directory.CreateDirectory(destination);
            string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            int count = 0;
            using (FileStream stream = File.OpenRead(zipPath))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (!TryGetAppRelativePath(entry.FullName, out string relative))
                    {
                        continue;
                    }

                    // 名前の検査に加え、解決後のパスが展開先の中にあることも確かめる
                    string path = Path.GetFullPath(Path.Combine(root, relative));
                    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"Invalid entry: {entry.FullName}");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);
                    using (Stream input = entry.Open())
                    using (FileStream output = File.Create(path))
                    {
                        input.CopyTo(output);
                    }

                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 展開した本体フォルダに exe と (exe 名)_Data があれば true。
        /// </summary>
        public static bool HasAppFiles(string folder, string exeName)
        {
            string data = Path.GetFileNameWithoutExtension(exeName) + "_Data";
            return File.Exists(Path.Combine(folder, exeName)) && Directory.Exists(Path.Combine(folder, data));
        }

        /// <summary>
        /// アップデーター（Tools/Updater）の起動引数。
        /// </summary>
        public static string BuildUpdaterArguments(int pid, string source, string target, string exeName,
            string logPath, string resultPath)
        {
            var builder = new StringBuilder();
            builder.Append("--pid ").Append(pid);
            builder.Append(" --source ").Append(Quote(source));
            builder.Append(" --target ").Append(Quote(target));
            builder.Append(" --exe ").Append(Quote(exeName));
            builder.Append(" --log ").Append(Quote(logPath));
            builder.Append(" --result ").Append(Quote(resultPath));
            return builder.ToString();
        }

        /// <summary>
        /// コマンドラインの 1 引数として引用符で囲む（末尾の \ は閉じの " を打ち消さないよう 2 つにする）。
        /// </summary>
        public static string Quote(string value)
        {
            string text = value ?? string.Empty;
            int trailing = 0;
            while (trailing < text.Length && text[text.Length - 1 - trailing] == '\\')
            {
                trailing++;
            }

            return "\"" + text + new string('\\', trailing) + "\"";
        }
    }
}
