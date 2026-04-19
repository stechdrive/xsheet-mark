using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XsheetMark.Interop;
using XsheetMark.Tools;
using XsheetMark.Viewport;

namespace XsheetMark;

public partial class MainWindow : Window
{
    private const double ImageGap = 50;

    private static readonly string[] SupportedExtensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif" };

    private readonly CanvasViewport _viewport;
    private readonly InkToolState _inkTools;

    private int _strokeCount;

    private Image? _draggingImage;
    private Point _dragStartCursorWorld;
    private double _dragStartImageLeft;
    private double _dragStartImageTop;

    public MainWindow()
    {
        InitializeComponent();

        _viewport = new CanvasViewport(Viewport, WorldScale, WorldTranslate);
        _inkTools = new InkToolState(Ink)
        {
            Color = Color.FromRgb(0x20, 0x20, 0x20),
        };
        WindowChromeInterop.Attach(this);

        Ink.StrokeCollected += OnStrokeCollected;
    }

    private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        WindowChromeInterop.BeginTitleBarDrag(this);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Viewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var cursor = e.GetPosition(Viewport);
        var factor = Math.Pow(1.1, e.Delta / 120.0);
        _viewport.ZoomAt(cursor, factor);
        StatusText.Text = $"Zoom: {_viewport.Scale * 100:F0}%";
        e.Handled = true;
    }

    private void Viewport_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _viewport.BeginPan(e.GetPosition(Viewport));
            Viewport.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (_inkTools.Tool == Tool.Move && e.ChangedButton == MouseButton.Left
            && e.OriginalSource is Image img && ImageLayer.Children.Contains(img))
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

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_viewport.IsPanning)
        {
            _viewport.UpdatePan(e.GetPosition(Viewport));
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
        if (_viewport.IsPanning && e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _viewport.EndPan();
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
        if (_inkTools is null || StatusText is null) return;
        if (sender is not RadioButton rb || rb.Tag is not string tagName) return;

        var tool = tagName switch
        {
            "Eraser" => Tool.Eraser,
            "Move" => Tool.Move,
            _ => Tool.Pen,
        };
        _inkTools.Tool = tool;
        SyncWidthSelection();
        StatusText.Text = $"Tool: {tool}";
    }

    private void SyncWidthSelection()
    {
        if (WidthThin is null || WidthMedium is null || WidthThick is null) return;
        var w = _inkTools.Width;
        WidthThin.IsChecked = Math.Abs(w - 2) < 1e-6;
        WidthMedium.IsChecked = Math.Abs(w - 5) < 1e-6;
        WidthThick.IsChecked = Math.Abs(w - 10) < 1e-6;
    }

    private void OnColorChanged(object sender, RoutedEventArgs e)
    {
        if (_inkTools is null) return;
        if (sender is RadioButton rb && rb.Background is SolidColorBrush brush)
        {
            _inkTools.Color = brush.Color;
        }
    }

    private void OnWidthChanged(object sender, RoutedEventArgs e)
    {
        if (_inkTools is null) return;
        if (sender is RadioButton rb
            && rb.Tag is string tag
            && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
        {
            _inkTools.Width = width;
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
        if (bounds.HasValue) _viewport.FitToBounds(bounds.Value);
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
            _viewport.FitToBounds(unionBounds.Value);
        }

        var fitSuffix = wasEmpty && loaded > 0
            ? $"   fit at {_viewport.Scale * 100:F0}%"
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
            $"Strokes: {_strokeCount}   pressure: {minP:F2}–{maxP:F2} {(flat ? "(flat)" : "(varies)")}   zoom: {_viewport.Scale * 100:F0}%";
    }
}
