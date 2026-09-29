using System.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>The effects a drag-and-drop operation may have, matching <c>System.Windows.Forms.DragDropEffects</c>.</summary>
[Flags]
public enum DragDropEffects {
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
public sealed class DragEventArgs(object data, DragDropEffects allowedEffect, int x, int y) : EventArgs {
  /// <summary>The payload the drag source handed to <see cref="Control.DoDragDrop(object, DragDropEffects)"/>, or a payload translated by a native backend.</summary>
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
/// The toolkit's drag-and-drop routing engine (PRD §8). In-process drags are driven by the source's
/// mouse stream; operating-system-originated drops are translated by a platform backend and forwarded
/// through <see cref="Backends.ExternalDropBridge"/>. Both routes share the same hit-testing and
/// <see cref="Control.AllowDrop"/> semantics. A file list leaving the source's window is handed over
/// to the operating system through <see cref="Backends.IFileDragSourcePeer"/>.
/// </summary>
internal static class DragDropSession {
  /// <summary>The control that started the active drag, or <see langword="null"/> when idle.</summary>
  internal static Control? Source { get; private set; }

  private static object? _data;
  private static DragDropEffects _allowed;
  private static Control? _target;
  private static DragDropEffects _effect;
  private static Action<DragDropEffects>? _completed;

  /// <summary>The native drag a file list may be handed to when the pointer leaves the window, or <see langword="null"/>.</summary>
  private static Backends.IFileDragSourcePeer? _handover;
  private static string[]? _paths;

  /// <summary>
  /// Starts a drag; any drag still in flight is abandoned first (its target gets a leave). A file list
  /// on a peer that can drag files natively is armed for the handover in <see cref="RouteMouseMove"/>.
  /// </summary>
  internal static void Begin(Control source, object data, DragDropEffects allowedEffects, Action<DragDropEffects>? completed) {
    Abandon();
    Source = source;
    _data = data;
    _allowed = allowedEffects;
    _target = null;
    _effect = DragDropEffects.None;
    _completed = completed;
    if ((allowedEffects & DragDropEffects.All) != DragDropEffects.None
        && source.Peer is Backends.IFileDragSourcePeer handover
        && TryGetFileList(data, out var paths)) {
      _handover = handover;
      _paths = paths;
    }
  }

  /// <summary>
  /// Routes a pointer move from the drag source: hit-tests the tree under the screen point and
  /// raises enter/over/leave on the drop target it finds. Returns whether the move belonged to
  /// (and was consumed by) an active drag.
  /// </summary>
  internal static bool RouteMouseMove(Control sender, MouseEventArgs e) {
    if (Source is not { } source || !ReferenceEquals(sender, source))
      return false;

    var screen = source.PointToScreen(e.Location);
    var root = RootOf(source);
    if (_handover is { } handover && !ScreenRectangleOf(root).Contains(screen)) {
      HandOver(source, handover);
      return true;
    }

    var target = FindDropTarget(root, screen);
    if (!ReferenceEquals(target, _target)) {
      _target?.RaiseDragLeave();
      _target = target;
      _effect = DragDropEffects.None;
      if (target is not null) {
        var args = new DragEventArgs(_data!, _allowed, screen.X, screen.Y);
        target.RaiseDragEnter(args);
        _effect = args.Effect & _allowed;
      }
    } else if (target is not null) {
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
  internal static bool RouteMouseUp(Control sender, MouseEventArgs e) {
    if (Source is not { } source || !ReferenceEquals(sender, source))
      return false;

    var screen = source.PointToScreen(e.Location);
    var data = _data!;
    var allowed = _allowed;
    var target = _target;
    var effect = _effect;
    var completed = _completed;
    Reset(); // idle again before handlers run, so a handler may start the next drag

    if (target is null)
      effect = DragDropEffects.None;
    else if (effect == DragDropEffects.None)
      target.RaiseDragLeave();
    else
      target.RaiseDragDrop(new DragEventArgs(data, allowed, screen.X, screen.Y) { Effect = effect });

    completed?.Invoke(effect);
    return true;
  }

  /// <summary>
  /// Hands the drag of a file list to the operating system as the pointer leaves the window: the
  /// in-process target is left, the session goes idle, and the source's peer runs the native drag,
  /// which reports the final effect to the caller of <see cref="Control.DoDragDrop(object, DragDropEffects, Action{DragDropEffects})"/>.
  /// A peer that declines gets the session back unchanged, and is not asked again for this drag.
  /// </summary>
  private static void HandOver(Control source, Backends.IFileDragSourcePeer handover) {
    _target?.RaiseDragLeave();
    _target = null;
    _effect = DragDropEffects.None;

    var data = _data!;
    var allowed = _allowed;
    var completed = _completed;
    var paths = _paths!;
    var native = allowed & DragDropEffects.All;

    // Idle before the platform runs: a modal drag (Win32) completes inside this call, and its
    // completion handler may start the next drag.
    Reset();
    if (handover.TryBeginFileDrag(paths, native, effect => completed?.Invoke(effect & native))) {
      // The platform consumes the button release, so the press this control saw never ends.
      (source as OwnerDrawnControl)?.ForgetMousePress();
      return;
    }

    Source = source;
    _data = data;
    _allowed = allowed;
    _completed = completed;
  }

  /// <summary>
  /// Routes one final drop delivered by an operating-system backend. Native file-drop protocols do
  /// not all expose the same hover lifecycle (the Win32 shell path, for example, only delivers the
  /// final <c>WM_DROPFILES</c>), so this intentionally synthesizes only the common contract:
  /// <see cref="Control.DragEnter"/> decides whether the target accepts the payload, a rejection is
  /// paired with <see cref="Control.DragLeave"/>, and an accepted payload raises
  /// <see cref="Control.DragDrop"/>. Continuous <see cref="Control.DragOver"/> remains available to
  /// in-process drags and to a future richer native protocol bridge.
  /// </summary>
  internal static DragDropEffects RouteExternalDrop(
      Control root,
      object data,
      DragDropEffects allowedEffects,
      Point screenLocation) {
    var target = FindDropTarget(root, screenLocation);
    if (target is null)
      return DragDropEffects.None;

    var enter = new DragEventArgs(data, allowedEffects, screenLocation.X, screenLocation.Y);
    target.RaiseDragEnter(enter);
    var effect = enter.Effect & allowedEffects;
    if (effect == DragDropEffects.None) {
      target.RaiseDragLeave();
      return DragDropEffects.None;
    }

    target.RaiseDragDrop(new DragEventArgs(data, allowedEffects, screenLocation.X, screenLocation.Y) {
      Effect = effect,
    });
    return effect;
  }

  /// <summary>
  /// Ends any in-process drag still in flight without dropping: its current target gets a leave and the
  /// source's mouse stream is its own again. Idle sessions are left untouched.
  /// </summary>
  private static void Abandon() {
    if (Source is null)
      return;

    var target = _target;
    var completed = _completed;
    Reset();
    target?.RaiseDragLeave();
    completed?.Invoke(DragDropEffects.None);
  }

  /// <summary>
  /// Whether <paramref name="data"/> is a file list an operating-system drag can carry: a non-empty
  /// <c>string[]</c> whose every entry is a fully qualified path naming an existing file or directory.
  /// A string array is an ordinary in-process payload too, so anything short of that is not an error —
  /// it just stays in process.
  /// </summary>
  internal static bool TryGetFileList(object data, out string[] paths) {
    paths = [];
    if (data is not string[] { Length: > 0 } candidates)
      return false;

    foreach (var path in candidates)
      if (string.IsNullOrEmpty(path)
          || !Path.IsPathFullyQualified(path)
          || !(File.Exists(path) || Directory.Exists(path)))
        return false;

    paths = candidates;
    return true;
  }

  /// <summary>Returns the session to idle without raising anything.</summary>
  private static void Reset() {
    Source = null;
    _data = null;
    _target = null;
    _effect = DragDropEffects.None;
    _completed = null;
    _handover = null;
    _paths = null;
  }

  /// <summary>Where <paramref name="control"/> sits on screen — for the root, the window a handover leaves.</summary>
  private static Rectangle ScreenRectangleOf(Control control)
      => new(control.PointToScreen(Point.Empty), control.Bounds.Size);

  /// <summary>The top of <paramref name="control"/>'s parent chain — the window the drag stays within.</summary>
  private static Control RootOf(Control control) {
    while (control.Parent is { } parent)
      control = parent;

    return control;
  }

  /// <summary>
  /// The deepest visible, enabled, realized control under <paramref name="screen"/> that opted in
  /// via <see cref="Control.AllowDrop"/>. Later siblings win, mirroring z-order; the drag source
  /// itself is a legal target.
  /// </summary>
  private static Control? FindDropTarget(Control node, Point screen) {
    if (node.ChildrenOrNull is { } children)
      for (var i = children.Count - 1; i >= 0; --i)
        if (FindDropTarget(children[i], screen) is { } hit)
          return hit;

    if (!node.AllowDrop || !node.Visible || !node.Enabled || node.Peer is null)
      return null;

    var origin = node.PointToScreen(Point.Empty);
    return new Rectangle(origin, node.Bounds.Size).Contains(screen) ? node : null;
  }
}
