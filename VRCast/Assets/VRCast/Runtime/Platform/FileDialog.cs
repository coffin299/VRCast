using System;
using System.IO;
using System.Runtime.InteropServices;

namespace VRCast.Platform
{
    /// <summary>
    /// Windows 標準の「ファイルを開く」ダイアログ（GetOpenFileNameW）。
    /// 閉じるまで呼び出し元（メインスレッド）を止めるモーダル表示。
    /// </summary>
    public static class FileDialog
    {
        // 選択パスの最大長（文字）
        private const int MaxPathLength = 32768;

        // 既存ファイルのみ・既存フォルダのみ・カレントディレクトリを変えない・エクスプローラー形式
        private const int Flags = 0x00001000 | 0x00000800 | 0x00000008 | 0x00080000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int structSize;
            public IntPtr owner;
            public IntPtr instance;
            public string filter;
            public IntPtr customFilter;
            public int maxCustomFilter;
            public int filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            public string initialDirectory;
            public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public string defaultExtension;
            public IntPtr customData;
            public IntPtr hook;
            public IntPtr templateName;
            public IntPtr reserved;
            public int reservedValue;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetOpenFileNameW(ref OpenFileName dialog);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        /// <summary>
        /// この環境でダイアログを使えるなら true。
        /// </summary>
        public static bool IsSupported
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// ファイルを 1 つ選ばせてフルパスを返す。キャンセル・非対応環境では null。
        /// </summary>
        /// <param name="title">ダイアログのタイトル</param>
        /// <param name="filterName">種類の表示名（例: "VRCast avatar"）</param>
        /// <param name="extension">拡張子（例: ".vrcaster"）</param>
        /// <param name="initialPath">最初に開くフォルダを決めるパス（ファイル・フォルダ・空可）</param>
        public static string OpenFile(string title, string filterName, string extension, string initialPath)
        {
            // 非対応環境では開かない
            if (!IsSupported)
            {
                return null;
            }

            // 結果を受け取るバッファ（先頭を空文字にしておく）
            IntPtr buffer = Marshal.AllocHGlobal(MaxPathLength * sizeof(char));
            try
            {
                Marshal.WriteInt16(buffer, 0);
                string pattern = "*" + extension;
                var dialog = new OpenFileName
                {
                    structSize = Marshal.SizeOf(typeof(OpenFileName)),
                    owner = GetActiveWindow(),
                    filter = $"{filterName} ({pattern})\0{pattern}\0\0",
                    filterIndex = 1,
                    file = buffer,
                    maxFile = MaxPathLength,
                    initialDirectory = ResolveDirectory(initialPath),
                    title = title,
                    flags = Flags,
                    defaultExtension = extension.TrimStart('.'),
                };

                // キャンセル・エラー時は false
                return GetOpenFileNameW(ref dialog) ? Marshal.PtrToStringUni(buffer) : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string ResolveDirectory(string path)
        {
            // 空・不正なパスは指定しない（ダイアログの既定の場所）
            if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return null;
            }

            // フォルダならそのまま、ファイルなら親フォルダ（存在するものだけ）
            if (Directory.Exists(path))
            {
                return path;
            }

            string parent = Path.GetDirectoryName(path);
            return !string.IsNullOrEmpty(parent) && Directory.Exists(parent) ? parent : null;
        }
    }
}
