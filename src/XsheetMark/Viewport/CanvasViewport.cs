using System;
using System.Windows;
using System.Windows.Media;

namespace XsheetMark.Viewport;

/// <summary>
/// Owns pan/zoom state for an infinite-canvas viewport. Operates on a
/// ScaleTransform + TranslateTransform pair applied to a world layer,
/// and uses a viewport FrameworkElement for ActualWidth/Height when fitting.
/// </summary>
public class CanvasViewport
{
    private readonly FrameworkElement _viewportElement;
    private readonly ScaleTransform _scale;
    private readonly TranslateTransform _translate;

    private Point _panStart;
    private double _panStartTx;
    private double _panStartTy;

    public double MinScale { get; set; } = 0.02;
    public double MaxScale { get; set; } = 16.0;

    public bool IsPanning { get; private set; }
    public double Scale => _scale.ScaleX;

    public CanvasViewport(FrameworkElement viewportElement, ScaleTransform scale, TranslateTransform translate)
    {
        ArgumentNullException.ThrowIfNull(viewportElement);
        ArgumentNullException.ThrowIfNull(scale);
        ArgumentNullException.ThrowIfNull(translate);
        _viewportElement = viewportElement;
        _scale = scale;
        _translate = translate;
    }

    public Point ScreenToWorld(Point screen) => new(
        (screen.X - _translate.X) / _scale.ScaleX,
        (screen.Y - _translate.Y) / _scale.ScaleY);

    /// <summary>
    /// Zooms around a screen point. The world position under screenPoint stays fixed.
    /// </summary>
    public void ZoomAt(Point screenPoint, double factor)
    {
        var worldBefore = ScreenToWorld(screenPoint);
        var newScale = Math.Clamp(_scale.ScaleX * factor, MinScale, MaxScale);
        if (Math.Abs(newScale - _scale.ScaleX) < 1e-12) return;

        _scale.ScaleX = newScale;
        _scale.ScaleY = newScale;

        var worldAfter = ScreenToWorld(screenPoint);
        _translate.X += (worldAfter.X - worldBefore.X) * newScale;
        _translate.Y += (worldAfter.Y - worldBefore.Y) * newScale;
    }

    /// <summary>
    /// Scales and centers the given world-space bounds in the viewport.
    /// Never scales up past 1:1 for small content.
    /// </summary>
    public bool FitToBounds(Rect bounds, double padding = 20)
    {
        if (_viewportElement.ActualWidth <= 0 || _viewportElement.ActualHeight <= 0) return false;
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;

        double scaleX = (_viewportElement.ActualWidth - padding * 2) / bounds.Width;
        double scaleY = (_viewportElement.ActualHeight - padding * 2) / bounds.Height;
        double scale = Math.Min(scaleX, scaleY);
        scale = Math.Min(scale, 1.0);
        scale = Math.Clamp(scale, MinScale, MaxScale);

        _scale.ScaleX = scale;
        _scale.ScaleY = scale;

        double centerX = bounds.X + bounds.Width / 2;
        double centerY = bounds.Y + bounds.Height / 2;
        _translate.X = _viewportElement.ActualWidth / 2 - centerX * scale;
        _translate.Y = _viewportElement.ActualHeight / 2 - centerY * scale;
        return true;
    }

    public void BeginPan(Point screenPoint)
    {
        IsPanning = true;
        _panStart = screenPoint;
        _panStartTx = _translate.X;
        _panStartTy = _translate.Y;
    }

    public void UpdatePan(Point screenPoint)
    {
        if (!IsPanning) return;
        _translate.X = _panStartTx + (screenPoint.X - _panStart.X);
        _translate.Y = _panStartTy + (screenPoint.Y - _panStart.Y);
    }

    public void EndPan() => IsPanning = false;
}
