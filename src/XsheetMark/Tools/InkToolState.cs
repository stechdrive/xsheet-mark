using System;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;

namespace XsheetMark.Tools;

public enum Tool { Pen, Eraser, Move }

/// <summary>
/// Holds the active drawing tool (Pen/Eraser/Move), stroke color, and per-tool
/// widths, and keeps the backing InkCanvas synchronized. Eraser is point-based
/// (EraseByPoint) with an elliptical tip sized proportionally to the eraser's
/// own width — so switching tools restores the width each tool was using.
/// </summary>
public class InkToolState
{
    private const double EraserMultiplier = 4;

    private readonly InkCanvas _ink;

    private Tool _tool = Tool.Pen;
    private Color _color = Colors.Black;
    private double _penWidth = 2;
    private double _eraserWidth = 2;

    public InkToolState(InkCanvas ink)
    {
        ArgumentNullException.ThrowIfNull(ink);
        _ink = ink;
        ApplyColor();
        ApplyTool();
    }

    public Tool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            _tool = value;
            ApplyTool();
        }
    }

    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            ApplyColor();
        }
    }

    /// <summary>Returns the width that belongs to the currently active tool.</summary>
    public double Width
    {
        get => _tool == Tool.Eraser ? _eraserWidth : _penWidth;
        set
        {
            if (value <= 0) return;
            if (_tool == Tool.Eraser) _eraserWidth = value;
            else _penWidth = value;
            ApplyWidth();
        }
    }

    private void ApplyTool()
    {
        switch (_tool)
        {
            case Tool.Pen:
                _ink.EditingMode = InkCanvasEditingMode.Ink;
                _ink.IsHitTestVisible = true;
                break;
            case Tool.Eraser:
                _ink.EditingMode = InkCanvasEditingMode.EraseByPoint;
                _ink.IsHitTestVisible = true;
                break;
            case Tool.Move:
                _ink.EditingMode = InkCanvasEditingMode.None;
                _ink.IsHitTestVisible = false;
                break;
        }
        // Re-apply width so the cursor (and pen drawing attrs) reflect the
        // current tool's width. Works around InkCanvas caching the cursor
        // shape from the previous mode in some cases.
        ApplyWidth();
    }

    private void ApplyColor() => _ink.DefaultDrawingAttributes.Color = _color;

    private void ApplyWidth()
    {
        _ink.DefaultDrawingAttributes.Width = _penWidth;
        _ink.DefaultDrawingAttributes.Height = _penWidth;
        double eraserSize = _eraserWidth * EraserMultiplier;
        _ink.EraserShape = new EllipseStylusShape(eraserSize, eraserSize);

        // InkCanvas caches the eraser cursor from EraserShape at the moment
        // EditingMode was last assigned. Changing EraserShape alone doesn't
        // redraw the on-screen cursor — we have to poke EditingMode for the
        // new size to take effect visually.
        if (_tool == Tool.Eraser)
        {
            _ink.EditingMode = InkCanvasEditingMode.None;
            _ink.EditingMode = InkCanvasEditingMode.EraseByPoint;
        }
    }
}
