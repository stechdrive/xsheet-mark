using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XsheetMark.Commands;
using XsheetMark.Psd;
using XsheetMark.Tga;

namespace XsheetMark.Workspace;

/// <summary>
/// Owns the image collection on the canvas, the image-move gesture, and
/// PSD export. Every loaded image keeps a reference to its source path
/// so export can either re-use the original PSD's layer structure or
/// build a new PSD wrapping the raster image.
/// </summary>
public class ImageWorkspace
{
    private const double ImageGap = 50;

    private static readonly string[] SupportedExtensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".psd", ".psb", ".tga" };

    private static readonly string[] PsdExtensions = { ".psd", ".psb" };
    private static readonly string[] TgaExtensions = { ".tga" };

    private readonly Canvas _imageLayer;
    private readonly InkCanvas _inkCanvas;
    private readonly UndoStack? _undoStack;
    private readonly Dictionary<Image, ImageOrigin> _origins = new();

    private Image? _draggingImage;
    private Point _dragStartCursorWorld;
    private double _dragStartImageLeft;
    private double _dragStartImageTop;
    private double _lastAppliedDx;
    private double _lastAppliedDy;
    private Stroke[] _followingStrokes = Array.Empty<Stroke>();

    public bool IsMoving => _draggingImage != null;
    public bool HasImages => _imageLayer.Children.Count > 0;
    public bool HasStrokes => _inkCanvas.Strokes.Count > 0;

    public ImageWorkspace(Canvas imageLayer, InkCanvas inkCanvas, UndoStack? undoStack = null)
    {
        ArgumentNullException.ThrowIfNull(imageLayer);
        ArgumentNullException.ThrowIfNull(inkCanvas);
        _imageLayer = imageLayer;
        _inkCanvas = inkCanvas;
        _undoStack = undoStack;
    }

    private sealed class ImageOrigin
    {
        public required string SourcePath { get; init; }
        public required bool IsPsd { get; init; }
    }

    public readonly record struct ExportOutcome(int Success, int Failed);

    public bool TryAddImage(string path, out int pixelWidth, out int pixelHeight, out Rect bounds)
    {
        pixelWidth = pixelHeight = 0;
        bounds = default;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (Array.IndexOf(SupportedExtensions, ext) < 0) return false;

        bool isPsd = Array.IndexOf(PsdExtensions, ext) >= 0;
        bool isTga = Array.IndexOf(TgaExtensions, ext) >= 0;
        var source =
            isPsd ? PsdIO.TryLoadComposite(path) :
            isTga ? TgaIO.TryLoad(path) :
            TryLoadStandardBitmap(path);
        if (source is null) return false;

        var img = new Image
        {
            Source = source,
            Width = source.PixelWidth,
            Height = source.PixelHeight,
            Stretch = Stretch.Fill,
        };

        double x = GetNextImageX();
        double y = 0;
        Canvas.SetLeft(img, x);
        Canvas.SetTop(img, y);
        _imageLayer.Children.Add(img);
        var origin = new ImageOrigin { SourcePath = path, IsPsd = isPsd };
        _origins[img] = origin;

        _undoStack?.Push(new LambdaCommand(
            redo: () =>
            {
                if (!_imageLayer.Children.Contains(img))
                {
                    _imageLayer.Children.Add(img);
                }
                _origins[img] = origin;
            },
            undo: () =>
            {
                _imageLayer.Children.Remove(img);
                _origins.Remove(img);
            }));

        pixelWidth = source.PixelWidth;
        pixelHeight = source.PixelHeight;
        bounds = new Rect(x, y, pixelWidth, pixelHeight);
        return true;
    }

    private static BitmapSource? TryLoadStandardBitmap(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public Rect? GetAllBounds()
    {
        Rect? bounds = null;
        foreach (UIElement el in _imageLayer.Children)
        {
            if (el is not Image img) continue;
            var rect = new Rect(GetLeftOrZero(img), GetTopOrZero(img), img.Width, img.Height);
            bounds = bounds.HasValue ? Rect.Union(bounds.Value, rect) : rect;
        }
        return bounds;
    }

    public bool TryBeginMove(Image image, Point cursorWorld)
    {
        if (!_imageLayer.Children.Contains(image)) return false;

        _draggingImage = image;
        _dragStartCursorWorld = cursorWorld;
        _dragStartImageLeft = GetLeftOrZero(image);
        _dragStartImageTop = GetTopOrZero(image);
        _lastAppliedDx = 0;
        _lastAppliedDy = 0;

        var imageBounds = new Rect(
            _dragStartImageLeft, _dragStartImageTop,
            image.Width, image.Height);

        var following = new List<Stroke>();
        foreach (var stroke in _inkCanvas.Strokes)
        {
            if (stroke.GetBounds().IntersectsWith(imageBounds))
            {
                following.Add(stroke);
            }
        }
        _followingStrokes = following.ToArray();
        return true;
    }

    public void UpdateMove(Point cursorWorld)
    {
        if (_draggingImage == null) return;

        double totalDx = cursorWorld.X - _dragStartCursorWorld.X;
        double totalDy = cursorWorld.Y - _dragStartCursorWorld.Y;

        Canvas.SetLeft(_draggingImage, _dragStartImageLeft + totalDx);
        Canvas.SetTop(_draggingImage, _dragStartImageTop + totalDy);

        double incDx = totalDx - _lastAppliedDx;
        double incDy = totalDy - _lastAppliedDy;
        if (_followingStrokes.Length > 0 && (incDx != 0 || incDy != 0))
        {
            var matrix = new Matrix(1, 0, 0, 1, incDx, incDy);
            foreach (var stroke in _followingStrokes)
            {
                stroke.Transform(matrix, applyToStylusTip: false);
            }
        }
        _lastAppliedDx = totalDx;
        _lastAppliedDy = totalDy;
    }

    public void EndMove()
    {
        var img = _draggingImage;
        var strokes = _followingStrokes;
        var dx = _lastAppliedDx;
        var dy = _lastAppliedDy;

        _draggingImage = null;
        _followingStrokes = Array.Empty<Stroke>();

        if (img is null) return;
        if (dx == 0 && dy == 0) return;

        _undoStack?.Push(new LambdaCommand(
            redo: () => ApplyMoveDelta(img, dx, dy, strokes),
            undo: () => ApplyMoveDelta(img, -dx, -dy, strokes)));
    }

    private static void ApplyMoveDelta(Image img, double dx, double dy, Stroke[] strokes)
    {
        double left = Canvas.GetLeft(img);
        if (double.IsNaN(left)) left = 0;
        double top = Canvas.GetTop(img);
        if (double.IsNaN(top)) top = 0;
        Canvas.SetLeft(img, left + dx);
        Canvas.SetTop(img, top + dy);

        if (strokes.Length > 0)
        {
            var m = new Matrix(1, 0, 0, 1, dx, dy);
            foreach (var s in strokes) s.Transform(m, applyToStylusTip: false);
        }
    }

    /// <summary>
    /// Exports a PSD containing a white background + the ink strokes currently
    /// visible in the viewport, at the given viewport pixel size. Used when
    /// the canvas has no images and the user still wants to save their notes.
    /// Strokes are rendered using the world-to-viewport transform so what
    /// lands in the PSD matches what the user sees on screen.
    /// </summary>
    /// <summary>
    /// Exports the current viewport view as a single PSD at viewport pixel
    /// dimensions. White background, any loaded images rendered at their
    /// transformed positions (respecting the image-layer opacity), ink
    /// strokes on top. Used both as the fallback when the canvas has no
    /// images and for the explicit "capture viewport" action (📷).
    /// </summary>
    public ExportOutcome ExportViewportAsPsd(
        string outputFolder,
        string inkLayerName,
        string backgroundLayerName,
        int width,
        int height,
        Matrix worldToViewport)
    {
        if (width <= 0 || height <= 0) return new ExportOutcome(0, 1);

        try
        {
            var outputPath = Path.Combine(outputFolder, "canvas-marked.psd");
            for (int suffix = 2; File.Exists(outputPath); suffix++)
            {
                outputPath = Path.Combine(outputFolder, $"canvas-marked-{suffix}.psd");
            }

            // Background is transparent — only images contribute to the
            // "background" layer. Anywhere else stays alpha=0 so the PSD can
            // be composited over other content.
            byte[] backgroundBgra = Rasterize(width, height, dc =>
                DrawImagesOnto(dc, worldToViewport));

            byte[] inkBgra = Rasterize(width, height, dc =>
            {
                dc.PushTransform(new MatrixTransform(worldToViewport));
                foreach (var s in _inkCanvas.Strokes) s.Draw(dc);
            });

            byte[] compositeBgra = Rasterize(width, height, dc =>
            {
                DrawImagesOnto(dc, worldToViewport);
                dc.PushTransform(new MatrixTransform(worldToViewport));
                foreach (var s in _inkCanvas.Strokes) s.Draw(dc);
            });

            bool ok = PsdIO.TryExportNewPsd(
                backgroundBgra, compositeBgra, inkBgra,
                width, height,
                backgroundLayerName, inkLayerName,
                outputPath);
            return new ExportOutcome(ok ? 1 : 0, ok ? 0 : 1);
        }
        catch
        {
            return new ExportOutcome(0, 1);
        }
    }

    private void DrawImagesOnto(DrawingContext dc, Matrix worldToViewport)
    {
        if (_imageLayer.Children.Count == 0) return;

        double opacity = _imageLayer.Opacity;
        bool opacityApplied = opacity < 1.0;
        if (opacityApplied) dc.PushOpacity(opacity);
        dc.PushTransform(new MatrixTransform(worldToViewport));
        foreach (UIElement el in _imageLayer.Children)
        {
            if (el is not Image img || img.Source is null) continue;
            double left = GetLeftOrZero(img);
            double top = GetTopOrZero(img);
            dc.DrawImage(img.Source, new Rect(left, top, img.Width, img.Height));
        }
        dc.Pop(); // transform
        if (opacityApplied) dc.Pop(); // opacity
    }

    /// <summary>
    /// Removes every image and every stroke from the canvas. Undoable — the
    /// entire state (images with their positions and origins, strokes) is
    /// captured and restored on undo.
    /// </summary>
    public void Reset()
    {
        if (_imageLayer.Children.Count == 0 && _inkCanvas.Strokes.Count == 0) return;

        var imageRecords = _imageLayer.Children.OfType<Image>()
            .Select(img => (
                image: img,
                left: GetLeftOrZero(img),
                top: GetTopOrZero(img),
                origin: _origins.GetValueOrDefault(img)))
            .ToArray();
        var strokeSnapshot = _inkCanvas.Strokes.ToArray();

        var command = new LambdaCommand(
            redo: () =>
            {
                _imageLayer.Children.Clear();
                _origins.Clear();
                _inkCanvas.Strokes.Clear();
            },
            undo: () =>
            {
                foreach (var r in imageRecords)
                {
                    Canvas.SetLeft(r.image, r.left);
                    Canvas.SetTop(r.image, r.top);
                    _imageLayer.Children.Add(r.image);
                    if (r.origin is not null) _origins[r.image] = r.origin;
                }
                foreach (var s in strokeSnapshot) _inkCanvas.Strokes.Add(s);
            });

        if (_undoStack is not null)
        {
            _undoStack.PushAndExecute(command);
        }
        else
        {
            command.Redo();
        }
    }

    /// <summary>
    /// Exports one PSD file per image on the canvas into the given folder.
    /// For PSD-origin images, the original PSD is re-read and the ink layer
    /// is appended on top (original layer structure preserved). For images
    /// from other formats a fresh PSD is created with two layers: the image
    /// below, the ink on top, plus a flat composite for preview.
    /// Each output is named "{source-stem}-marked.psd", with a numeric
    /// suffix if a file of that name already exists.
    /// </summary>
    public ExportOutcome ExportAllPsds(string outputFolder, string inkLayerName, string imageLayerName)
    {
        int success = 0, failed = 0;
        int index = 0;

        foreach (UIElement el in _imageLayer.Children)
        {
            if (el is not Image img) continue;
            index++;

            var origin = _origins.GetValueOrDefault(img);
            var stem = origin is not null
                ? Path.GetFileNameWithoutExtension(origin.SourcePath)
                : $"image-{index}";

            var outputPath = Path.Combine(outputFolder, $"{stem}-marked.psd");
            for (int suffix = 2; File.Exists(outputPath); suffix++)
            {
                outputPath = Path.Combine(outputFolder, $"{stem}-marked-{suffix}.psd");
            }

            int w = (int)img.Width;
            int h = (int)img.Height;
            if (w <= 0 || h <= 0) { failed++; continue; }

            double left = GetLeftOrZero(img);
            double top = GetTopOrZero(img);
            var imgRect = new Rect(left, top, w, h);

            byte[] inkBgra = Rasterize(w, h, dc =>
            {
                dc.PushTransform(new TranslateTransform(-left, -top));
                DrawIntersectingStrokes(dc, imgRect);
            });

            byte[] compositeBgra = Rasterize(w, h, dc =>
            {
                dc.DrawImage(img.Source, new Rect(0, 0, w, h));
                dc.PushTransform(new TranslateTransform(-left, -top));
                DrawIntersectingStrokes(dc, imgRect);
            });

            bool ok;
            if (origin is not null && origin.IsPsd)
            {
                ok = PsdIO.TryExportWithInkLayer(
                    origin.SourcePath, inkBgra, compositeBgra,
                    w, h, inkLayerName, outputPath);
            }
            else
            {
                byte[] imageBgra = Rasterize(w, h, dc =>
                    dc.DrawImage(img.Source, new Rect(0, 0, w, h)));
                ok = PsdIO.TryExportNewPsd(
                    imageBgra, compositeBgra, inkBgra,
                    w, h, imageLayerName, inkLayerName, outputPath);
            }

            if (ok) success++; else failed++;
        }

        return new ExportOutcome(success, failed);
    }

    private void DrawIntersectingStrokes(DrawingContext dc, Rect imageRect)
    {
        foreach (var stroke in _inkCanvas.Strokes)
        {
            if (!stroke.GetBounds().IntersectsWith(imageRect)) continue;
            stroke.Draw(dc);
        }
    }

    private static byte[] Rasterize(int width, int height, Action<DrawingContext> draw)
    {
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            draw(dc);
        }
        rtb.Render(visual);

        byte[] bgra = new byte[width * height * 4];
        rtb.CopyPixels(bgra, width * 4, 0);
        return bgra;
    }

    private double GetNextImageX()
    {
        double maxRight = 0;
        foreach (UIElement el in _imageLayer.Children)
        {
            if (el is not Image img) continue;
            double right = GetLeftOrZero(img) + img.Width + ImageGap;
            if (right > maxRight) maxRight = right;
        }
        return maxRight;
    }

    private static double GetLeftOrZero(UIElement el)
    {
        double v = Canvas.GetLeft(el);
        return double.IsNaN(v) ? 0 : v;
    }

    private static double GetTopOrZero(UIElement el)
    {
        double v = Canvas.GetTop(el);
        return double.IsNaN(v) ? 0 : v;
    }
}
