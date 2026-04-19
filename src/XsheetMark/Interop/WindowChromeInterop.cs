using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace XsheetMark.Interop;

/// <summary>
/// Keeps the window from stealing focus (WS_EX_NOACTIVATE + MA_NOACTIVATE) and
/// provides edge-resize hit-testing for a chrome-less, transparent WPF window.
/// Also exposes a title-bar-drag helper that works without activating the window.
/// </summary>
public static class WindowChromeInterop
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 0x0003;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    public static int ResizeGripThickness { get; set; } = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// Attaches the focus-non-theft + edge-resize behavior to a Window.
    /// Safe to call from the window's constructor; hooks run once on SourceInitialized.
    /// </summary>
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SourceInitialized += (_, _) => OnSourceInitialized(window);
    }

    /// <summary>
    /// Starts a native title-bar drag on the window without activating it.
    /// Call from the custom drag-bar's MouseLeftButtonDown handler.
    /// </summary>
    public static void BeginTitleBarDrag(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ReleaseCapture();
        SendMessage(new WindowInteropHelper(window).Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    private static void OnSourceInitialized(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_NOACTIVATE));
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)MA_NOACTIVATE;
        }
        if (msg == WM_NCHITTEST)
        {
            var hit = HitTestEdges(hwnd, lParam);
            if (hit != 0)
            {
                handled = true;
                return (IntPtr)hit;
            }
        }
        return IntPtr.Zero;
    }

    private static int HitTestEdges(IntPtr hwnd, IntPtr lParam)
    {
        if (!GetWindowRect(hwnd, out var r)) return 0;

        var lp = unchecked((int)lParam.ToInt64());
        int sx = (short)(lp & 0xFFFF);
        int sy = (short)((lp >> 16) & 0xFFFF);

        int wx = sx - r.Left;
        int wy = sy - r.Top;
        int width = r.Right - r.Left;
        int height = r.Bottom - r.Top;

        bool onLeft = wx >= 0 && wx < ResizeGripThickness;
        bool onRight = wx >= width - ResizeGripThickness && wx < width;
        bool onTop = wy >= 0 && wy < ResizeGripThickness;
        bool onBottom = wy >= height - ResizeGripThickness && wy < height;

        if (onTop && onLeft) return HTTOPLEFT;
        if (onTop && onRight) return HTTOPRIGHT;
        if (onBottom && onLeft) return HTBOTTOMLEFT;
        if (onBottom && onRight) return HTBOTTOMRIGHT;
        if (onLeft) return HTLEFT;
        if (onRight) return HTRIGHT;
        if (onTop) return HTTOP;
        if (onBottom) return HTBOTTOM;

        return 0;
    }
}
