using System;
using System.Collections.Generic;

namespace XsheetMark.Commands;

public interface ICanvasCommand
{
    void Redo();
    void Undo();
}

/// <summary>
/// Command built from two Action delegates. Lets call sites that know how to
/// reverse themselves register an undoable operation without needing a
/// dedicated class per operation type.
/// </summary>
public sealed class LambdaCommand : ICanvasCommand
{
    private readonly Action _redo;
    private readonly Action _undo;

    public LambdaCommand(Action redo, Action undo)
    {
        ArgumentNullException.ThrowIfNull(redo);
        ArgumentNullException.ThrowIfNull(undo);
        _redo = redo;
        _undo = undo;
    }

    public void Redo() => _redo();
    public void Undo() => _undo();
}

/// <summary>
/// Simple two-stack undo/redo manager. IsApplying is on while a command is
/// running so that event handlers that also call Push (e.g., InkCanvas's
/// StrokesChanged) can short-circuit and avoid recording the undo's own
/// side-effects.
/// </summary>
public sealed class UndoStack
{
    private readonly Stack<ICanvasCommand> _undo = new();
    private readonly Stack<ICanvasCommand> _redo = new();

    public bool IsApplying { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public event EventHandler? Changed;

    public void Push(ICanvasCommand command)
    {
        if (IsApplying) return;
        _undo.Push(command);
        _redo.Clear();
        OnChanged();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var cmd = _undo.Pop();
        IsApplying = true;
        try { cmd.Undo(); }
        finally { IsApplying = false; }
        _redo.Push(cmd);
        OnChanged();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var cmd = _redo.Pop();
        IsApplying = true;
        try { cmd.Redo(); }
        finally { IsApplying = false; }
        _undo.Push(cmd);
        OnChanged();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
