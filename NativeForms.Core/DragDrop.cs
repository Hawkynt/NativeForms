using System.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>The effects a drag-and-drop operation may have, matching <c>System.Windows.Forms.DragDropEffects</c>.</summary>
[Flags]
public enum DragDropEffects
{
    /// <summary>The target refuses the data.</summary>
    None = 0,

    /// <summary>The data is copied to the target.</summary>
    Copy = 1,

    /// <summary>The data is moved to the target.</summary>
    Move = 2,

    /// <summary>The target creates a link to the data.</summary>
    Link = 4,

    /// <summary>Every effect: <see cref="Copy"/>, <see cref="Move"/> and <see cref="Link"/>.</summary>
    All = Copy | Move | Link,
}

/// <summary>
/// Describes a drag over a potential drop target. A handler inspects <see cref="Data"/> and answers
/// by setting <see cref="Effect"/> — leaving it <see cref="DragDropEffects.None"/> refuses the drop.
/// </summary>
public sealed class DragEventArgs(object data, DragDropEffects allowedEffect, int x, int y) : EventArgs
{
    /// <summary>The payload the drag source handed to <see cref="Control.DoDragDrop"/>.</summary>
    public object Data { get; } = data;

    /// <summary>The effects the drag source permits.</summary>
    public DragDropEffects AllowedEffect { get; } = allowedEffect;

    /// <summary>
    /// The target's answer: which effect the drop would have here. Effects outside
    /// <see cref="AllowedEffect"/> are ignored.
    /// </summary>
    public DragDropEffects Effect { get; set; }

    /// <summary>The pointer's x-coordinate in screen space.</summary>
    public int X { get; } = x;

    /// <summary>The pointer's y-coordinate in screen space.</summary>
    public int Y { get; } = y;
}

/// <summary>
/// The toolkit's in-process drag-and-drop engine (PRD §8). One drag can be active at a time: the
/// source's mouse stream — which stays with the source while a button is held, on every backend — is
/// rerouted here, translated to screen space and hit-tested against the source's own window tree, so
/// <see cref="Control.DragEnter"/>/<see cref="Control.DragOver"/>/<see cref="Control.DragLeave"/>/
/// <see cref="Control.DragDrop"/> fire exactly like their WinForms counterparts. Deliberately
/// in-process only: OS-level drags (OLE <c>RegisterDragDrop</c>, <c>gtk_drag_*</c>) need COM or
/// per-platform integrations that are tracked in <c>docs/PRD.md</c> §8 — this engine is the shared
/// behavior they will forward into.
/// </summary>
internal static class DragDropSession
{
    /// <summary>The control that started the active drag, or <see langword="null"/> when idle.</summary>
    internal static Control? Source { get; private set; }

    private static object? _data;
    private static DragDropEffects _allowed;
    private static Control? _target;
    private static DragDropEffects _effect;

    /// <summary>Starts a drag; any drag still in flight is abandoned first (its target gets a leave).</summary>
    internal static void Begin(Control source, object data, DragDropEffects allowedEffects)
    {
        _target?.RaiseDragLeave();
        Source = source;
        _data = data;
        _allowed = allowedEffects;
        _target = null;
        _effect = DragDropEffects.None;
    }

    /// <summary>
    /// Routes a pointer move from the drag source: hit-tests the tree under the screen point and
    /// raises enter/over/leave on the drop target it finds. Returns whether the move belonged to
    /// (and was consumed by) an active drag.
    /// </summary>
    internal static bool RouteMouseMove(Control sender, MouseEventArgs e)
    {
        if (Source is not { } source || !ReferenceEquals(sender, source))
            return false;

        var screen = source.PointToScreen(e.Location);
        var target = FindDropTarget(RootOf(source), screen);
        if (!ReferenceEquals(target, _target))
        {
            _target?.RaiseDragLeave();
            _target = target;
            _effect = DragDropEffects.None;
            if (target is not null)
            {
                var args = new DragEventArgs(_data!, _allowed, screen.X, screen.Y);
                target.RaiseDragEnter(args);
                _effect = args.Effect & _allowed;
            }
        }
        else if (target is not null)
        {
            var args = new DragEventArgs(_data!, _allowed, screen.X, screen.Y) { Effect = _effect };
            target.RaiseDragOver(args);
            _effect = args.Effect & _allowed;
        }

        return true;
    }

    /// <summary>
    /// Routes the button release that ends the drag: drops onto the current target when it accepted
    /// an effect, otherwise just leaves it. Returns whether the release belonged to an active drag.
    /// </summary>
    internal static bool RouteMouseUp(Control sender, MouseEventArgs e)
    {
        if (Source is not { } source || !ReferenceEquals(sender, source))
            return false;

        var screen = source.PointToScreen(e.Location);
        var data = _data!;
        var allowed = _allowed;
        var target = _target;
        var effect = _effect;
        Reset(); // idle again before handlers run, so a handler may start the next drag

        if (target is null)
            return true;

        if (effect == DragDropEffects.None)
            target.RaiseDragLeave();
        else
            target.RaiseDragDrop(new DragEventArgs(data, allowed, screen.X, screen.Y) { Effect = effect });

        return true;
    }

    /// <summary>Returns the session to idle without raising anything.</summary>
    private static void Reset()
    {
        Source = null;
        _data = null;
        _target = null;
        _effect = DragDropEffects.None;
    }

    /// <summary>The top of <paramref name="control"/>'s parent chain — the window the drag stays within.</summary>
    private static Control RootOf(Control control)
    {
        while (control.Parent is { } parent)
            control = parent;

        return control;
    }

    /// <summary>
    /// The deepest visible, enabled, realized control under <paramref name="screen"/> that opted in
    /// via <see cref="Control.AllowDrop"/>. Later siblings win, mirroring z-order; the drag source
    /// itself is a legal target.
    /// </summary>
    private static Control? FindDropTarget(Control node, Point screen)
    {
        for (var i = node.Controls.Count - 1; i >= 0; --i)
            if (FindDropTarget(node.Controls[i], screen) is { } hit)
                return hit;

        if (!node.AllowDrop || !node.Visible || !node.Enabled || node.Peer is null)
            return null;

        var origin = node.PointToScreen(Point.Empty);
        return new Rectangle(origin, node.Bounds.Size).Contains(screen) ? node : null;
    }
}
