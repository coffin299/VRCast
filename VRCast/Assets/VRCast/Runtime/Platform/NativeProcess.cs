using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace VRCast.Platform
{
    /// <summary>
    /// CreateProcessW で外部プログラムを起動する。IL2CPP の System.Diagnostics.Process.Start（UseShellExecute = false）は
    /// 起動に失敗する（"Native error= Success"）ため、トラッカーの起動・VRCast の起動し直しはこちらを使う。
    /// 出力を受け取る場合は標準出力と標準エラー出力を 1 本のパイプにまとめ、行ごとに別スレッドから通知する。
    /// </summary>
    public sealed class NativeProcess : IDisposable
    {
        // コンソールを出さない、標準入出力を指定する
        private const uint CreateNoWindow = 0x08000000;
        private const int UseStdHandles = 0x00000100;

        // 待機の結果・ハンドルの継承フラグ
        private const uint WaitObject0 = 0;
        private const uint HandleFlagInherit = 0x1;

        // 出力の読み取り単位（バイト）
        private const int ReadBufferSize = 4096;

        // 強制終了時の終了コード
        private const uint KilledExitCode = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfo
        {
            public int Size;
            public IntPtr Reserved;
            public IntPtr Desktop;
            public IntPtr Title;
            public int X;
            public int Y;
            public int XSize;
            public int YSize;
            public int XCountChars;
            public int YCountChars;
            public int FillAttribute;
            public int Flags;
            public short ShowWindow;
            public short Reserved2Size;
            public IntPtr Reserved2;
            public IntPtr StdInput;
            public IntPtr StdOutput;
            public IntPtr StdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr Process;
            public IntPtr Thread;
            public int ProcessId;
            public int ThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            public int Length;
            public IntPtr SecurityDescriptor;
            public int InheritHandle;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine,
            IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
            IntPtr environment, string currentDirectory, ref StartupInfo startupInfo, out ProcessInformation information);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(out IntPtr readPipe, out IntPtr writePipe,
            ref SecurityAttributes attributes, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr file, byte[] buffer, int toRead, out int read, IntPtr overlapped);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private IntPtr _handle;
        private Thread _reader;

        private NativeProcess(IntPtr handle, int id)
        {
            _handle = handle;
            Id = id;
        }

        /// <summary>
        /// プロセス ID。
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// 終了していれば true。
        /// </summary>
        public bool HasExited => WaitForSingleObject(_handle, 0) == WaitObject0;

        /// <summary>
        /// 終了コード（Windows の異常終了コードは負の値になる）。
        /// </summary>
        public int ExitCode => GetExitCodeProcess(_handle, out uint code) ? unchecked((int)code) : -1;

        /// <summary>
        /// 起動する。onLine を渡すとコンソールを出さずに出力を行ごとに受け取る（別スレッドから呼ばれる）。
        /// null なら出力はそのまま（GUI アプリの起動用）。起動できなければ Win32Exception。
        /// </summary>
        public static NativeProcess Start(string path, string arguments, string workingDirectory,
            Encoding outputEncoding = null, Action<string> onLine = null)
        {
            // 実行ファイル名は空白を含んでもよいよう引用符で囲む（CreateProcessW は書き換え可能なバッファを要求する）
            var commandLine = new StringBuilder($"\"{path}\"");
            if (!string.IsNullOrEmpty(arguments))
            {
                commandLine.Append(' ').Append(arguments);
            }

            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
            IntPtr readPipe = IntPtr.Zero;
            IntPtr writePipe = IntPtr.Zero;
            bool redirect = onLine != null;
            if (redirect)
            {
                // 子に継承させるパイプを作り、読む側だけ継承を外す
                var attributes = new SecurityAttributes
                {
                    Length = Marshal.SizeOf<SecurityAttributes>(),
                    InheritHandle = 1,
                };
                if (!CreatePipe(out readPipe, out writePipe, ref attributes, 0))
                {
                    throw Failure("CreatePipe");
                }

                SetHandleInformation(readPipe, HandleFlagInherit, 0);

                // 標準出力・標準エラー出力の両方を同じパイプへ（標準入力は渡さない）
                startup.Flags = UseStdHandles;
                startup.StdOutput = writePipe;
                startup.StdError = writePipe;
            }

            bool started = CreateProcessW(path, commandLine, IntPtr.Zero, IntPtr.Zero, redirect,
                redirect ? CreateNoWindow : 0, IntPtr.Zero,
                string.IsNullOrEmpty(workingDirectory) ? null : workingDirectory,
                ref startup, out ProcessInformation information);
            Win32Exception error = started ? null : Failure("CreateProcess");

            // 書く側は子だけが持つ（親が閉じないと子の終了後も読み取りが終わらない）
            if (writePipe != IntPtr.Zero)
            {
                CloseHandle(writePipe);
            }

            if (!started)
            {
                if (readPipe != IntPtr.Zero)
                {
                    CloseHandle(readPipe);
                }

                throw error;
            }

            // スレッドのハンドルは使わない
            CloseHandle(information.Thread);
            var process = new NativeProcess(information.Process, information.ProcessId);
            if (redirect)
            {
                // 出力は終了まで別スレッドで読み続ける（読まないとパイプが詰まって子が止まる）
                Encoding encoding = outputEncoding ?? Encoding.UTF8;
                process._reader = new Thread(() => ReadLines(readPipe, encoding, onLine))
                {
                    IsBackground = true,
                    Name = "VRCast process output",
                };
                process._reader.Start();
            }

            return process;
        }

        /// <summary>
        /// 終了を待つ。時間内に終了すれば true。
        /// </summary>
        public bool WaitForExit(int milliseconds)
        {
            return WaitForSingleObject(_handle, (uint)Math.Max(0, milliseconds)) == WaitObject0;
        }

        /// <summary>
        /// 出力を最後まで読み終えるのを待つ（終了後に呼ぶ）。時間内に読み終えれば true。
        /// </summary>
        public bool WaitForOutput(int milliseconds)
        {
            return _reader == null || _reader.Join(milliseconds);
        }

        /// <summary>
        /// 強制終了する（既に終了していれば何もしない）。
        /// </summary>
        public void Kill()
        {
            if (!HasExited)
            {
                TerminateProcess(_handle, KilledExitCode);
            }
        }

        public void Dispose()
        {
            // プロセスのハンドルを閉じる（プロセス自体は終了させない）
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }

        private static void ReadLines(IntPtr pipe, Encoding encoding, Action<string> onLine)
        {
            var buffer = new byte[ReadBufferSize];
            var chars = new char[encoding.GetMaxCharCount(ReadBufferSize)];
            Decoder decoder = encoding.GetDecoder();
            var line = new StringBuilder();
            try
            {
                // 子と孫がパイプを閉じる（終了する）まで読む
                while (ReadFile(pipe, buffer, buffer.Length, out int read, IntPtr.Zero) && read > 0)
                {
                    // マルチバイト文字が読み取り単位をまたいでも化けないよう Decoder で続けて変換する
                    int count = decoder.GetChars(buffer, 0, read, chars, 0);
                    for (int i = 0; i < count; i++)
                    {
                        if (chars[i] == '\n')
                        {
                            onLine(line.ToString().TrimEnd('\r'));
                            line.Clear();
                        }
                        else
                        {
                            line.Append(chars[i]);
                        }
                    }
                }

                // 改行で終わらない最後の行
                if (line.Length > 0)
                {
                    onLine(line.ToString().TrimEnd('\r'));
                }
            }
            finally
            {
                CloseHandle(pipe);
            }
        }

        private static Win32Exception Failure(string function)
        {
            // エラー番号をメッセージに含める（IL2CPP では説明文が取れない場合がある）
            int code = Marshal.GetLastWin32Error();
            return new Win32Exception(code, $"{function} failed (Windows error {code})");
        }
    }
}
