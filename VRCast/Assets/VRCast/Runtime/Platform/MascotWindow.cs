using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace VRCast.Platform
{
    /// <summary>
    /// マスコット用の別ウィンドウ。枠なし・最前面・アルファ付きで、透明な部分はクリックを通す。
    /// 本体の Unity ウィンドウは触らない。
    /// </summary>
    public static class MascotWindow
    {
        public const int BaseWidth = 480;
        public const int BaseHeight = 700;
        public const float MinScale = 0.25f;
        public const float MaxScale = 3f;
        private const string ClassName = "VRCastMascotWindow";

        private const int WsPopup = unchecked((int)0x80000000);
        private const int WsVisible = 0x10000000;
        private const int ExLayered = 0x00080000;
        private const int ExTopmost = 0x00000008;
        private const int ExToolWindow = 0x00000080;

        private const uint UlwAlpha = 0x00000002;
        private const byte AcSrcOver = 0;
        private const byte AcSrcAlpha = 1;

        private const int WmNcHitTest = 0x0084;
        private const int WmDestroy = 0x0002;
        private const int WmMouseMove = 0x0200;
        private const int WmRButtonDown = 0x0204;
        private const int WmRButtonUp = 0x0205;
        private const int WmCaptureChanged = 0x0215;
        private const int HtTransparent = -1;
        private const int HtCaption = 2;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const int OpaqueAlpha = 24;

        private const uint SpiGetWorkArea = 0x0030;

        private static readonly WndProc Proc = OnMessage;
        private static bool _classReady;
        private static IntPtr _hwnd;
        private static IntPtr _screenDc;
        private static IntPtr _memDc;
        private static IntPtr _dib;
        private static IntPtr _previous;
        private static IntPtr _bits;
        private static byte[] _alpha = Array.Empty<byte>();
        private static int _alphaWidth;
        private static int _alphaHeight;
        private static int _width = BaseWidth;
        private static int _height = BaseHeight;
        private static bool _draggingScale;
        private static int _dragStartY;
        private static float _dragStartScale;

        public static bool IsOpen => _hwnd != IntPtr.Zero;

        public static int PixelWidth => _width;

        public static int PixelHeight => _height;

        /// <summary>今の表示倍率。1 で 480×700。</summary>
        public static float Scale { get; private set; } = 1f;

        /// <summary>アバター上を右ドラッグして大きさを変えている間。</summary>
        public static bool DraggingScale => _draggingScale;

        /// <summary>壊れた値は 100%、それ以外は 25%〜300% に収め、1% 刻みにする。</summary>
        public static float ClampScale(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
            {
                return 1f;
            }

            if (scale < MinScale)
            {
                scale = MinScale;
            }
            else if (scale > MaxScale)
            {
                scale = MaxScale;
            }

            return (float)Math.Round(scale, 2);
        }

        /// <summary>
        /// ウィンドウの左上（画面座標、左上原点）と大きさ。開いていなければ空。
        /// </summary>
        public static Rect ScreenRect
        {
            get
            {
                if (_hwnd == IntPtr.Zero || !GetWindowRect(_hwnd, out RectNative rect))
                {
                    return new Rect();
                }

                return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }
        }

        public static bool TryCursor(out int x, out int y)
        {
            if (GetCursorPos(out PointNative point))
            {
                x = point.X;
                y = point.Y;
                return true;
            }

            x = 0;
            y = 0;
            return false;
        }

        /// <summary>
        /// ウィンドウを作る。x, y が負なら作業領域の右下に置く。
        /// </summary>
        public static bool Open(int x, int y, float scale)
        {
            if (_hwnd != IntPtr.Zero)
            {
                return true;
            }

            if (!EnsureClass())
            {
                return false;
            }

            ApplyScale(scale);
            if (x < 0 || y < 0)
            {
                PlaceDefault(out x, out y);
            }

            _hwnd = CreateWindowExW(
                ExLayered | ExTopmost | ExToolWindow,
                ClassName,
                "VRCast",
                WsPopup | WsVisible,
                x,
                y,
                _width,
                _height,
                IntPtr.Zero,
                IntPtr.Zero,
                GetModuleHandleW(null),
                IntPtr.Zero);
            if (_hwnd == IntPtr.Zero || !EnsureBitmap())
            {
                Close();
                return false;
            }

            return true;
        }

        public static void Close()
        {
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            FreeBitmap();
            _alpha = Array.Empty<byte>();
        }

        /// <summary>
        /// 今の位置を画面座標で返す。閉じていれば false。
        /// </summary>
        public static bool TryPosition(out int x, out int y)
        {
            if (_hwnd != IntPtr.Zero && GetWindowRect(_hwnd, out RectNative rect))
            {
                x = rect.Left;
                y = rect.Top;
                return true;
            }

            x = 0;
            y = 0;
            return false;
        }

        /// <summary>
        /// RGBA（下から上、1 ピクセル 4 バイト）をウィンドウへ出す。大きさは PixelWidth x PixelHeight。
        /// </summary>
        public static void Present(byte[] rgba)
        {
            if (_hwnd == IntPtr.Zero || rgba == null || _bits == IntPtr.Zero)
            {
                return;
            }

            int count = _width * _height;
            if (rgba.Length < count * 4)
            {
                return;
            }

            if (_alpha.Length != count)
            {
                _alpha = new byte[count];
                _alphaWidth = _width;
                _alphaHeight = _height;
            }

            // DIB は下から上。読み取りも下から上なのでそのまま BGRA（乗算済み）へ並べる
            unsafe
            {
                byte* dest = (byte*)_bits;
                for (int i = 0; i < count; i++)
                {
                    int s = i * 4;
                    byte a = rgba[s + 3];
                    _alpha[i] = a;
                    dest[0] = (byte)(rgba[s + 2] * a / 255);
                    dest[1] = (byte)(rgba[s + 1] * a / 255);
                    dest[2] = (byte)(rgba[s] * a / 255);
                    dest[3] = a;
                    dest += 4;
                }
            }

            var origin = new PointNative();
            var size = new SizeNative { Cx = _width, Cy = _height };
            var blend = new BlendFunction { BlendOp = AcSrcOver, SourceConstantAlpha = 255, AlphaFormat = AcSrcAlpha };
            UpdateLayeredWindow(_hwnd, IntPtr.Zero, IntPtr.Zero, ref size, _memDc, ref origin, 0, ref blend, UlwAlpha);
        }

        private static void PlaceDefault(out int x, out int y)
        {
            var work = new RectNative();
            if (!SystemParametersInfoW(SpiGetWorkArea, 0, ref work, 0))
            {
                x = 80;
                y = 80;
                return;
            }

            x = Math.Max(work.Left, work.Right - _width - 24);
            y = Math.Max(work.Top, work.Bottom - _height - 24);
        }

        /// <summary>
        /// 倍率に合わせてウィンドウとビットマップを変える。開いていれば中心を保つ。
        /// </summary>
        public static void ApplyScale(float scale)
        {
            scale = ClampScale(scale);
            int width = Math.Max(1, (int)Math.Round(BaseWidth * scale));
            int height = Math.Max(1, (int)Math.Round(BaseHeight * scale));
            Scale = scale;
            if (_hwnd == IntPtr.Zero)
            {
                _width = width;
                _height = height;
                return;
            }

            if (width == _width && height == _height)
            {
                return;
            }

            int x = 0;
            int y = 0;
            if (GetWindowRect(_hwnd, out RectNative rect))
            {
                x = rect.Left + (_width - width) / 2;
                y = rect.Top + (_height - height) / 2;
            }

            _width = width;
            _height = height;
            SetWindowPos(_hwnd, IntPtr.Zero, x, y, width, height, SwpNoZOrder | SwpNoActivate);
            if (!EnsureBitmap())
            {
                return;
            }

            _alpha = Array.Empty<byte>();
            _alphaWidth = 0;
            _alphaHeight = 0;
        }

        private static bool EnsureClass()
        {
            if (_classReady)
            {
                return true;
            }

            var info = new WindowClass
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
                hInstance = GetModuleHandleW(null),
                hCursor = LoadCursorW(IntPtr.Zero, 32512),
                lpszClassName = ClassName,
            };
            _classReady = RegisterClassW(ref info) != 0 || Marshal.GetLastWin32Error() == 1410;
            return _classReady;
        }

        private static bool EnsureBitmap()
        {
            FreeBitmap();
            _screenDc = GetDC(IntPtr.Zero);
            _memDc = CreateCompatibleDC(_screenDc);
            var infoHeader = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = _width,
                Height = _height,
                Planes = 1,
                BitCount = 32,
            };
            var header = new BitmapInfo { Header = infoHeader };
            _dib = CreateDIBSection(_screenDc, ref header, 0, out _bits, IntPtr.Zero, 0);
            if (_dib == IntPtr.Zero || _bits == IntPtr.Zero)
            {
                return false;
            }

            _previous = SelectObject(_memDc, _dib);
            return true;
        }

        private static void FreeBitmap()
        {
            if (_memDc != IntPtr.Zero && _previous != IntPtr.Zero)
            {
                SelectObject(_memDc, _previous);
                _previous = IntPtr.Zero;
            }

            if (_dib != IntPtr.Zero)
            {
                DeleteObject(_dib);
                _dib = IntPtr.Zero;
                _bits = IntPtr.Zero;
            }

            if (_memDc != IntPtr.Zero)
            {
                DeleteDC(_memDc);
                _memDc = IntPtr.Zero;
            }

            if (_screenDc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, _screenDc);
                _screenDc = IntPtr.Zero;
            }
        }

        [MonoPInvokeCallback(typeof(WndProc))]
        private static IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == WmRButtonDown && TryCursor(out int cursorX, out int cursorY) && OpaqueAt(cursorX, cursorY))
            {
                _draggingScale = true;
                _dragStartY = cursorY;
                _dragStartScale = Scale;
                SetCapture(hwnd);
                return IntPtr.Zero;
            }

            if (message == WmMouseMove && _draggingScale && TryCursor(out _, out int dragY))
            {
                // 上へドラッグすると大きく、下へドラッグすると小さく
                ApplyScale(_dragStartScale + (_dragStartY - dragY) / (float)BaseHeight);
                return IntPtr.Zero;
            }

            if (message == WmRButtonUp && _draggingScale)
            {
                _draggingScale = false;
                ReleaseCapture();
                return IntPtr.Zero;
            }

            if (message == WmCaptureChanged)
            {
                _draggingScale = false;
            }

            if (message == WmNcHitTest)
            {
                // 透明な画素は後ろのウィンドウへ。それ以外は掴んで移動できる
                int packed = unchecked((int)lParam.ToInt64());
                int screenX = unchecked((short)(packed & 0xFFFF));
                int screenY = unchecked((short)((packed >> 16) & 0xFFFF));
                if (OpaqueAt(screenX, screenY))
                {
                    return (IntPtr)HtCaption;
                }

                return (IntPtr)HtTransparent;
            }

            if (message == WmDestroy)
            {
                _hwnd = IntPtr.Zero;
            }

            return DefWindowProcW(hwnd, message, wParam, lParam);
        }

        private static bool OpaqueAt(int screenX, int screenY)
        {
            if (_hwnd == IntPtr.Zero || _alpha.Length == 0 || !ScreenToClient(_hwnd, ref screenX, ref screenY))
            {
                return false;
            }

            // クライアントは左上原点。アルファ配列は下から上
            if (screenX < 0 || screenY < 0 || screenX >= _alphaWidth || screenY >= _alphaHeight)
            {
                return false;
            }

            int row = _alphaHeight - 1 - screenY;
            return _alpha[row * _alphaWidth + screenX] >= OpaqueAlpha;
        }

        private static bool ScreenToClient(IntPtr hwnd, ref int x, ref int y)
        {
            var point = new PointNative { X = x, Y = y };
            if (!ScreenToClient(hwnd, ref point))
            {
                return false;
            }

            x = point.X;
            y = point.Y;
            return true;
        }

        private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            public uint Style;
            public IntPtr lpfnWndProc;
            public int ClsExtra;
            public int WndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PointNative
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SizeNative
        {
            public int Cx;
            public int Cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RectNative
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public int Size;
            public int Width;
            public int Height;
            public short Planes;
            public short BitCount;
            public int Compression;
            public int SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public int ClrUsed;
            public int ClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader Header;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassW(ref WindowClass windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(
            int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RectNative rect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCapture(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out PointNative point);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hwnd, ref PointNative point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SystemParametersInfoW(uint action, uint param, ref RectNative rect, uint winIni);

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(
            IntPtr hwnd, IntPtr destDc, IntPtr destPoint, ref SizeNative size,
            IntPtr sourceDc, ref PointNative sourcePoint, uint colorKey, ref BlendFunction blend, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(
            IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string module);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadCursorW(IntPtr instance, int cursor);
    }
}
