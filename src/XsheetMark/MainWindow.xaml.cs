using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XsheetMark;

public partial class MainWindow : Window
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

    private const int ResizeGripThickness = 8;

    private const double MinScale = 0.02;
    private const double MaxScale = 16.0;
    private const double ImageGap = 50;

    private enum Tool { Pen, Eraser, Move }

    private static readonly string[] SupportedExtensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif" };

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

    private int _strokeCount;
    private Tool _currentTool = Tool.Pen;

    private bool _panning;
    private Point _panStartScreen;
    private double _panStartTranslateX, _panStartTranslateY;

    private Image? _draggingImage;
    private Point _dragStartCursorWorld;
    private double _dragStartImageLeft, _dragStartImageTop;

    public MainWindow()
    {
        InitializeComponent();
        Ink.StrokeCollected += OnStrokeCollected;
        Ink.DefaultDrawingAttributes.Color = Color.FromRgb(0x20, 0x20, 0x20);
        Ink.DefaultDrawingAttributes.Width = 2;
        Ink.DefaultDrawingAttributes.Height = 2;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_NOACTIVATE));

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
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

    private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ReleaseCapture();
        SendMessage(new WindowInteropHelper(this).Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private Point ScreenToWorld(Point screen) =>
        new((screen.X - WorldTranslate.X) / WorldScale.ScaleX,
            (screen.Y - WorldTranslate.Y) / WorldScale.ScaleY);

    private void Viewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var cursor = e.GetPosition(Viewport);
        var factor = Math.Pow(1.1, e.Delta / 120.0);
        ZoomAt(cursor, factor);
        e.Handled = true;
    }

    private void ZoomAt(Point screenPoint, double factor)
    {
        var worldBefore = ScreenToWorld(screenPoint);
        var newScale = Math.Clamp(WorldScale.ScaleX * factor, MinScale, MaxScale);
        if (newScale == WorldScale.ScaleX) return;

        WorldScale.ScaleX = newScale;
        WorldScale.ScaleY = newScale;

        var worldAfter = ScreenToWorld(screenPoint);
        WorldTranslate.X += (worldAfter.X - worldBefore.X) * newScale;
        WorldTranslate.Y += (worldAfter.Y - worldBefore.Y) * newScale;

        StatusText.Text = $"Zoom: {newScale * 100:F0}%";
    }

    private void Viewport_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _panning = true;
            _panStartScreen = e.GetPosition(Viewport);
            _panStartTranslateX = WorldTranslate.X;
            _panStartTranslateY = WorldTranslate.Y;
            Viewport.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (_currentTool == Tool.Move && e.ChangedButton == MouseButton.Left)
        {
            if (e.OriginalSource is Image img && ImageLayer.Children.Contains(img))
            {
                _draggingImage = img;
                _dragStartCursorWorld = e.GetPosition(ImageLayer);
                _dragStartImageLeft = Canvas.GetLeft(img);
                if (double.IsNaN(_dragStartImageLeft)) _dragStartImageLeft = 0;
                _dragStartImageTop = Canvas.GetTop(img);
                if (double.IsNaN(_dragStartImageTop)) _dragStartImageTop = 0;
                Viewport.CaptureMouse();
                e.Handled = true;
            }
        }
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_panning)
        {
            var current = e.GetPosition(Viewport);
            WorldTranslate.X = _panStartTranslateX + (current.X - _panStartScreen.X);
            WorldTranslate.Y = _panStartTranslateY + (current.Y - _panStartScreen.Y);
            return;
        }

        if (_draggingImage != null)
        {
            var current = e.GetPosition(ImageLayer);
            var dx = current.X - _dragStartCursorWorld.X;
            var dy = current.Y - _dragStartCursorWorld.Y;
            Canvas.SetLeft(_draggingImage, _dragStartImageLeft + dx);
            Canvas.SetTop(_draggingImage, _dragStartImageTop + dy);
        }
    }

    private void Viewport_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning && e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _panning = false;
            Viewport.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (_draggingImage != null && e.ChangedButton == MouseButton.Left)
        {
            _draggingImage = null;
            Viewport.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnToolChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tagName) return;
        if (Ink is null || StatusText is null) return;

        _currentTool = tagName switch
        {
            "Eraser" => Tool.Eraser,
            "Move" => Tool.Move,
            _ => Tool.Pen,
        };

        switch (_currentTool)
        {
            case Tool.Pen:
                Ink.EditingMode = InkCanvasEditingMode.Ink;
                Ink.IsHitTestVisible = true;
                break;
            case Tool.Eraser:
                Ink.EditingMode = InkCanvasEditingMode.EraseByStroke;
                Ink.IsHitTestVisible = true;
                break;
            case Tool.Move:
                Ink.EditingMode = InkCanvasEditingMode.None;
                Ink.IsHitTestVisible = false;
                break;
        }

        StatusText.Text = $"Tool: {_currentTool}";
    }

    private void OnColorChanged(object sender, RoutedEventArgs e)
    {
        if (Ink is null) return;
        if (sender is RadioButton rb && rb.Background is SolidColorBrush brush)
        {
            Ink.DefaultDrawingAttributes.Color = brush.Color;
        }
    }

    private void OnWidthChanged(object sender, RoutedEventArgs e)
    {
        if (Ink is null) return;
        if (sender is RadioButton rb
            && rb.Tag is string tag
            && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
        {
            Ink.DefaultDrawingAttributes.Width = width;
            Ink.DefaultDrawingAttributes.Height = width;
        }
    }

    private void FitAll_Click(object sender, RoutedEventArgs e)
    {
        Rect? bounds = null;
        foreach (UIElement el in ImageLayer.Children)
        {
            if (el is not Image img) continue;
            double left = Canvas.GetLeft(img);
            if (double.IsNaN(left)) left = 0;
            double top = Canvas.GetTop(img);
            if (double.IsNaN(top)) top = 0;
            var rect = new Rect(left, top, img.Width, img.Height);
            bounds = bounds.HasValue ? Rect.Union(bounds.Value, rect) : rect;
        }
        if (bounds.HasValue) FitToBounds(bounds.Value);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        bool wasEmpty = ImageLayer.Children.Count == 0;

        int loaded = 0, failed = 0;
        Rect? unionBounds = null;
        string? lastFile = null;
        int lastW = 0, lastH = 0;

        foreach (var file in files)
        {
            if (TryAddImage(file, out var w, out var h, out var rect))
            {
                loaded++;
                lastFile = Path.GetFileName(file);
                lastW = w;
                lastH = h;
                unionBounds = unionBounds.HasValue ? Rect.Union(unionBounds.Value, rect) : rect;
            }
            else
            {
                failed++;
            }
        }

        if (wasEmpty && unionBounds.HasValue)
        {
            FitToBounds(unionBounds.Value);
        }

        var fitSuffix = wasEmpty && loaded > 0
            ? $"   fit at {WorldScale.ScaleX * 100:F0}%"
            : "";
        StatusText.Text = loaded switch
        {
            0 => $"Drop failed: {failed} file(s), unsupported or unreadable",
            1 => $"Loaded: {lastFile} ({lastW}×{lastH})" + (failed > 0 ? $"   [{failed} skipped]" : "") + fitSuffix,
            _ => $"Loaded {loaded} images" + (failed > 0 ? $"   [{failed} skipped]" : "") + fitSuffix,
        };
    }

    private bool TryAddImage(string path, out int pixelWidth, out int pixelHeight, out Rect bounds)
    {
        pixelWidth = pixelHeight = 0;
        bounds = default;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (Array.IndexOf(SupportedExtensions, ext) < 0) return false;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();

            var img = new Image
            {
                Source = bmp,
                Width = bmp.PixelWidth,
                Height = bmp.PixelHeight,
                Stretch = Stretch.Fill,
            };

            double x = GetNextImageX();
            double y = 0;
            Canvas.SetLeft(img, x);
            Canvas.SetTop(img, y);
            ImageLayer.Children.Add(img);

            pixelWidth = bmp.PixelWidth;
            pixelHeight = bmp.PixelHeight;
            bounds = new Rect(x, y, pixelWidth, pixelHeight);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private double GetNextImageX()
    {
        double maxRight = 0;
        foreach (UIElement el in ImageLayer.Children)
        {
            if (el is not Image img) continue;
            double left = Canvas.GetLeft(img);
            if (double.IsNaN(left)) left = 0;
            double right = left + img.Width + ImageGap;
            if (right > maxRight) maxRight = right;
        }
        return maxRight;
    }

    private void FitToBounds(Rect bounds)
    {
        if (Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0) return;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        const double padding = 20;
        double scaleX = (Viewport.ActualWidth - padding * 2) / bounds.Width;
        double scaleY = (Viewport.ActualHeight - padding * 2) / bounds.Height;
        double scale = Math.Min(scaleX, scaleY);
        scale = Math.Min(scale, 1.0);
        scale = Math.Clamp(scale, MinScale, MaxScale);

        WorldScale.ScaleX = scale;
        WorldScale.ScaleY = scale;

        double centerX = bounds.X + bounds.Width / 2;
        double centerY = bounds.Y + bounds.Height / 2;
        WorldTranslate.X = Viewport.ActualWidth / 2 - centerX * scale;
        WorldTranslate.Y = Viewport.ActualHeight / 2 - centerY * scale;
    }

    private void OnStrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        _strokeCount++;

        var pts = e.Stroke.StylusPoints;
        float minP = 1f, maxP = 0f;
        foreach (var p in pts)
        {
            if (p.PressureFactor < minP) minP = p.PressureFactor;
            if (p.PressureFactor > maxP) maxP = p.PressureFactor;
        }

        var flat = Math.Abs(maxP - minP) < 0.001f;
        StatusText.Text =
            $"Strokes: {_strokeCount}   pressure: {minP:F2}–{maxP:F2} {(flat ? "(flat)" : "(varies)")}   zoom: {WorldScale.ScaleX * 100:F0}%";
    }
}
