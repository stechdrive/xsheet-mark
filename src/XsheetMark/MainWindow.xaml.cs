using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;
using XsheetMark.Commands;
using XsheetMark.Interop;
using XsheetMark.Localization;
using XsheetMark.Settings;
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
        _inkTools = new InkToolState(Ink);
        _workspace = new ImageWorkspace(ImageLayer, Ink, _undoStack);
        WindowChromeInterop.Attach(this);

        Ink.StrokeCollected += OnStrokeCollected;
        Ink.Strokes.StrokesChanged += OnStrokesChanged;
        _undoStack.Changed += OnUndoStackChanged;
        RefreshUndoButtons();

        ApplySavedSettings();
        Closing += OnWindowClosing;
    }

    private void ApplySavedSettings()
    {
        var s = SettingsStore.Load();
        var workArea = SystemParameters.WorkArea;

        // Fall back to a screen-aware size when the user has no saved size yet,
        // so a fresh install on a 1366×768 laptop doesn't open cropped below
        // the taskbar.
        double fallbackWidth = Math.Min(Width, Math.Max(MinWidth, workArea.Width - 40));
        double fallbackHeight = Math.Min(Height, Math.Max(MinHeight, workArea.Height - 40));
        Width = Math.Max(MinWidth, s.Width ?? fallbackWidth);
        Height = Math.Max(MinHeight, s.Height ?? fallbackHeight);

        if (s.Left is double left && s.Top is double top)
        {
            // Clamp into the virtual screen so a window saved on a now-
            // disconnected monitor doesn't reappear off-screen.
            double minLeft = SystemParameters.VirtualScreenLeft;
            double minTop = SystemParameters.VirtualScreenTop;
            double maxLeft = minLeft + SystemParameters.VirtualScreenWidth - Width;
            double maxTop = minTop + SystemParameters.VirtualScreenHeight - Height;
            Left = Math.Clamp(left, minLeft, Math.Max(minLeft, maxLeft));
            Top = Math.Clamp(top, minTop, Math.Max(minTop, maxTop));
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        if (s.WindowOpacity is double wo)
        {
            WindowOpacitySlider.Value = Math.Clamp(wo, WindowOpacitySlider.Minimum, WindowOpacitySlider.Maximum);
        }
        if (s.ImageOpacity is double io)
        {
            ImageOpacitySlider.Value = Math.Clamp(io, ImageOpacitySlider.Minimum, ImageOpacitySlider.Maximum);
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        SettingsStore.Save(new UserSettings
        {
            Left = Left,
            Top = Top,
            Width = ActualWidth,
            Height = ActualHeight,
            WindowOpacity = WindowOpacitySlider.Value,
            ImageOpacity = ImageOpacitySlider.Value,
        });
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

    private bool _clickThrough;

    private void ToggleClickThrough_Click(object sender, RoutedEventArgs e) => ToggleClickThrough();

    private void ToggleClickThrough_ThumbClick(object? sender, EventArgs e) => ToggleClickThrough();

    private void ToggleClickThrough()
    {
        if (_clickThrough)
        {
            ApplyClickThrough(false);
            return;
        }

        var settings = SettingsStore.Load();
        if (settings.SuppressClickThroughWarning != true)
        {
            var dialog = new Dialogs.ClickThroughConfirmDialog { Owner = this };
            dialog.ShowDialog();
            if (!dialog.Confirmed) return;
            if (dialog.DoNotShowAgain)
            {
                settings.SuppressClickThroughWarning = true;
                SettingsStore.Save(settings);
            }
        }

        ApplyClickThrough(true);
    }

    private void ApplyClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        WindowChromeInterop.SetClickThrough(this, enabled);

        if (LockThumbButton is not null)
        {
            LockThumbButton.Description = Localizer.Get(enabled ? "ClickThrough.ThumbUnlock" : "ClickThrough.ThumbLock");
            LockThumbButton.ImageSource = new BitmapImage(
                new Uri(enabled ? "pack://application:,,,/unlock.ico" : "pack://application:,,,/lock.ico"));
        }

        if (OuterBorder is not null)
        {
            OuterBorder.BorderBrush = enabled
                ? new SolidColorBrush(Color.FromRgb(0xE0, 0x40, 0x40))
                : new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
        }
    }

    private void CapturePsd_Click(object sender, RoutedEventArgs e)
    {
        if (!_workspace.HasImages && !_workspace.HasStrokes)
        {
            StatusText.Text = Localizer.Get("Export.NoContent");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Localizer.Get("Export.FolderDialogTitle"),
        };
        if (dialog.ShowDialog(this) != true) return;

        int w = Math.Max(1, (int)Viewport.ActualWidth);
        int h = Math.Max(1, (int)Viewport.ActualHeight);

        var outcome = _workspace.ExportViewportAsPsd(
            dialog.FolderName,
            Localizer.Get("Export.InkLayerName"),
            Localizer.Get("Export.BackgroundLayerName"),
            w, h,
            _viewport.WorldToViewportMatrix);

        StatusText.Text = outcome.Failed == 0
            ? Localizer.Format("Export.Success", outcome.Success)
            : Localizer.Format("Export.PartialSuccess", outcome.Success, outcome.Failed);
    }

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

    private DispatcherTimer? _colorLongPressTimer;
    private static readonly TimeSpan ColorLongPressDuration = TimeSpan.FromMilliseconds(350);

    private void CurrentColor_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        CancelColorLongPress();
        _colorLongPressTimer = new DispatcherTimer { Interval = ColorLongPressDuration };
        _colorLongPressTimer.Tick += OnColorLongPressFired;
        _colorLongPressTimer.Start();
    }

    private void CurrentColor_PreviewMouseUp(object sender, MouseButtonEventArgs e) => CancelColorLongPress();

    private void CurrentColor_MouseLeave(object sender, MouseEventArgs e) => CancelColorLongPress();

    private void OnColorLongPressFired(object? sender, EventArgs e)
    {
        CancelColorLongPress();
        ColorPopup.IsOpen = true;
    }

    private void CancelColorLongPress()
    {
        if (_colorLongPressTimer is null) return;
        _colorLongPressTimer.Stop();
        _colorLongPressTimer.Tick -= OnColorLongPressFired;
        _colorLongPressTimer = null;
    }

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        if (_inkTools is null) return;
        if (sender is Button btn && btn.Background is SolidColorBrush brush)
        {
            _inkTools.Color = brush.Color;
            CurrentColorButton.Background = brush;
        }
        ColorPopup.IsOpen = false;
    }

    // StaysOpen=False is unreliable on a WS_EX_NOACTIVATE transparent window
    // (its mouse capture can fail to install), so we dismiss the popup manually.
    // Popup input DOES tunnel to the main window via the Popup's logical-parent
    // chain, so we must skip clicks whose source is inside the popup — otherwise
    // closing here swallows the MouseUp and PickColor_Click never fires.
    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!ColorPopup.IsOpen) return;
        if (ColorPopup.Child is Visual popupContent &&
            e.OriginalSource is Visual src &&
            (ReferenceEquals(src, popupContent) || popupContent.IsAncestorOf(src)))
        {
            return;
        }
        ColorPopup.IsOpen = false;
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
        if (!_workspace.HasImages && !_workspace.HasStrokes)
        {
            StatusText.Text = Localizer.Get("Export.NoContent");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Localizer.Get("Export.FolderDialogTitle"),
        };
        if (dialog.ShowDialog(this) != true) return;

        var inkLayerName = Localizer.Get("Export.InkLayerName");
        ImageWorkspace.ExportOutcome outcome;

        if (_workspace.HasImages)
        {
            outcome = _workspace.ExportAllPsds(
                dialog.FolderName,
                inkLayerName,
                Localizer.Get("Export.ImageLayerName"));
        }
        else
        {
            int w = Math.Max(1, (int)Viewport.ActualWidth);
            int h = Math.Max(1, (int)Viewport.ActualHeight);
            outcome = _workspace.ExportViewportAsPsd(
                dialog.FolderName,
                inkLayerName,
                Localizer.Get("Export.BackgroundLayerName"),
                w, h,
                _viewport.WorldToViewportMatrix);
        }

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
