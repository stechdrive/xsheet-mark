using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using XsheetMark.Commands;
using XsheetMark.Interop;
using XsheetMark.Localization;
using XsheetMark.Tools;
using XsheetMark.Viewport;
using XsheetMark.Workspace;

namespace XsheetMark;

public partial class MainWindow : Window
{
    private readonly CanvasViewport _viewport;
    private readonly InkToolState _inkTools;
    private readonly ImageWorkspace _workspace;
    private readonly UndoStack _undoStack = new();

    private int _strokeCount;

    public MainWindow()
    {
        InitializeComponent();

        _viewport = new CanvasViewport(Viewport, WorldScale, WorldTranslate);
        _inkTools = new InkToolState(Ink)
        {
            Color = Color.FromRgb(0x20, 0x20, 0x20),
        };
        _workspace = new ImageWorkspace(ImageLayer, Ink, _undoStack);
        WindowChromeInterop.Attach(this);

        Ink.StrokeCollected += OnStrokeCollected;
        Ink.Strokes.StrokesChanged += OnStrokesChanged;
        _undoStack.Changed += OnUndoStackChanged;
        RefreshUndoButtons();
    }

    private void OnStrokesChanged(object? sender, System.Windows.Ink.StrokeCollectionChangedEventArgs e)
    {
        if (_undoStack.IsApplying) return;
        if (e.Added.Count == 0 && e.Removed.Count == 0) return;

        var added = e.Added.ToArray();
        var removed = e.Removed.ToArray();
        var strokes = Ink.Strokes;

        _undoStack.Push(new LambdaCommand(
            redo: () =>
            {
                foreach (var s in removed) strokes.Remove(s);
                foreach (var s in added) strokes.Add(s);
            },
            undo: () =>
            {
                foreach (var s in added) strokes.Remove(s);
                foreach (var s in removed) strokes.Add(s);
            }));
    }

    private void OnUndoStackChanged(object? sender, EventArgs e) => RefreshUndoButtons();

    private void RefreshUndoButtons()
    {
        if (UndoButton is not null) UndoButton.IsEnabled = _undoStack.CanUndo;
        if (RedoButton is not null) RedoButton.IsEnabled = _undoStack.CanRedo;
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => _undoStack.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _undoStack.Redo();
    private void Reset_Click(object sender, RoutedEventArgs e) => _workspace.Reset();

    private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        WindowChromeInterop.BeginTitleBarDrag(this);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void WindowOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Opacity = e.NewValue;

    private void ImageOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ImageLayer is not null) ImageLayer.Opacity = e.NewValue;
    }

    private void Viewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var cursor = e.GetPosition(Viewport);
        var factor = Math.Pow(1.1, e.Delta / 120.0);
        _viewport.ZoomAt(cursor, factor);
        StatusText.Text = Localizer.Format("Status.Zoom", _viewport.Scale * 100);
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
            && e.OriginalSource is Image img
            && _workspace.TryBeginMove(img, e.GetPosition(ImageLayer)))
        {
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

        if (_workspace.IsMoving)
        {
            _workspace.UpdateMove(e.GetPosition(ImageLayer));
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

        if (_workspace.IsMoving && e.ChangedButton == MouseButton.Left)
        {
            _workspace.EndMove();
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

        var toolName = tool switch
        {
            Tool.Eraser => Localizer.Get("ToolName.Eraser"),
            Tool.Move => Localizer.Get("ToolName.Move"),
            _ => Localizer.Get("ToolName.Pen"),
        };
        StatusText.Text = Localizer.Format("Status.Tool", toolName);
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
        var bounds = _workspace.GetAllBounds();
        if (bounds.HasValue) _viewport.FitToBounds(bounds.Value);
    }

    private void ExportPsd_Click(object sender, RoutedEventArgs e)
    {
        if (!_workspace.HasImages)
        {
            StatusText.Text = Localizer.Get("Export.NoImages");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Localizer.Get("Export.FolderDialogTitle"),
        };
        if (dialog.ShowDialog(this) != true) return;

        var outcome = _workspace.ExportAllPsds(
            dialog.FolderName,
            Localizer.Get("Export.InkLayerName"),
            Localizer.Get("Export.ImageLayerName"));

        StatusText.Text = outcome.Failed == 0
            ? Localizer.Format("Export.Success", outcome.Success)
            : Localizer.Format("Export.PartialSuccess", outcome.Success, outcome.Failed);
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
        bool wasEmpty = !_workspace.HasImages;

        int loaded = 0, failed = 0;
        Rect? unionBounds = null;
        string? lastFile = null;
        int lastW = 0, lastH = 0;

        foreach (var file in files)
        {
            if (_workspace.TryAddImage(file, out var w, out var h, out var rect))
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
            ? Localizer.Format("Status.FitAt", _viewport.Scale * 100)
            : "";
        var skippedSuffix = failed > 0
            ? Localizer.Format("Status.Skipped", failed)
            : "";
        StatusText.Text = loaded switch
        {
            0 => Localizer.Format("Status.DropFailed", failed),
            1 => Localizer.Format("Status.Loaded1", lastFile ?? "", lastW, lastH) + skippedSuffix + fitSuffix,
            _ => Localizer.Format("Status.LoadedMany", loaded) + skippedSuffix + fitSuffix,
        };
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
        var pressureLabel = flat ? Localizer.Get("Pressure.Flat") : Localizer.Get("Pressure.Varies");
        StatusText.Text = Localizer.Format(
            "Status.StrokeInfo",
            _strokeCount, minP, maxP, pressureLabel, _viewport.Scale * 100);
    }
}
