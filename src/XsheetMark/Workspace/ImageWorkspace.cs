using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XsheetMark.Psd;

namespace XsheetMark.Workspace;

/// <summary>
/// Owns the image collection on the canvas and the image-move gesture.
/// During a move, ink strokes whose bounding box intersects the dragged
/// image's starting bounds are translated along with it, so annotations
/// stay attached to the image the user placed them on (model-B with the
/// stroke-follow compromise).
/// </summary>
public class ImageWorkspace
{
    private const double ImageGap = 50;

    private static readonly string[] SupportedExtensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".psd", ".psb" };

    private static readonly string[] PsdExtensions = { ".psd", ".psb" };

    private readonly Canvas _imageLayer;
    private readonly InkCanvas _inkCanvas;

    private Image? _draggingImage;
    private Point _dragStartCursorWorld;
    private double _dragStartImageLeft;
    private double _dragStartImageTop;
    private double _lastAppliedDx;
    private double _lastAppliedDy;
    private Stroke[] _followingStrokes = Array.Empty<Stroke>();

    public bool IsMoving => _draggingImage != null;
    public bool HasImages => _imageLayer.Children.Count > 0;

    public ImageWorkspace(Canvas imageLayer, InkCanvas inkCanvas)
    {
        ArgumentNullException.ThrowIfNull(imageLayer);
        ArgumentNullException.ThrowIfNull(inkCanvas);
        _imageLayer = imageLayer;
        _inkCanvas = inkCanvas;
    }

    /// <summary>
    /// Loads an image from disk and places it to the right of any existing images.
    /// Returns false (with default outs) if the format is unsupported or the file unreadable.
    /// </summary>
    public bool TryAddImage(string path, out int pixelWidth, out int pixelHeight, out Rect bounds)
    {
        pixelWidth = pixelHeight = 0;
        bounds = default;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (Array.IndexOf(SupportedExtensions, ext) < 0) return false;

        var source = Array.IndexOf(PsdExtensions, ext) >= 0
            ? PsdIO.TryLoadComposite(path)
            : TryLoadStandardBitmap(path);
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

    /// <summary>Returns the union of all image bounds in world coords, or null if empty.</summary>
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

    /// <summary>
    /// Starts a move on the given image if it belongs to this workspace. Captures
    /// the strokes intersecting the image's starting bounds to follow along.
    /// </summary>
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

    /// <summary>Moves the active image (and its attached strokes) to track the cursor world position.</summary>
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
        _draggingImage = null;
        _followingStrokes = Array.Empty<Stroke>();
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
