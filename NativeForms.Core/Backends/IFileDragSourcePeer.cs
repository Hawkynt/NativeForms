namespace Hawkynt.NativeForms.Backends;

/// <summary>
/// Optional capability of a control peer: it can hand a list of files to the operating system's own
/// drag-and-drop, so the files can be dropped onto the platform file manager or any other application
/// (the <c>SHDoDragDrop</c> shell drag on Win32, a <c>text/uri-list</c> drag on GTK, an
/// <c>NSDraggingSession</c> of file URLs on macOS).
/// </summary>
/// <remarks>
/// A peer opts in by implementing this interface. A drag started with <see cref="Control.DoDragDrop(object, DragDropEffects)"/>
/// whose payload is a file list stays in process while the pointer is over the application's window,
/// and the core asks the source's peer for this native drag at the moment the pointer leaves the
/// window with the button still held. A peer that does not implement the interface, or declines,
/// leaves the drag in process.
/// </remarks>
public interface IFileDragSourcePeer {
  /// <summary>
  /// Starts an operating-system drag of <paramref name="paths"/> from this peer, called from within the
  /// peer's own pointer-move notification while a button is held.
  /// </summary>
  /// <param name="paths">
  /// At least one fully qualified path; every entry names an existing file or directory. The core
  /// validates this before it asks.
  /// </param>
  /// <param name="allowedEffects">
  /// The effects the source permits: a non-empty combination of <see cref="DragDropEffects.Copy"/>,
  /// <see cref="DragDropEffects.Move"/> and <see cref="DragDropEffects.Link"/>.
  /// </param>
  /// <param name="completed">
  /// Invoked exactly once, on the UI thread, with the effect the drop target performed —
  /// <see cref="DragDropEffects.None"/> when the drag was cancelled or refused. A platform whose drag is
  /// modal invokes it before this call returns; an asynchronous one invokes it when its session ends.
  /// Never invoked when the call returns <see langword="false"/>.
  /// </param>
  /// <returns>
  /// <see langword="true"/> when the native drag ran or is running; <see langword="false"/> when the
  /// platform cannot start one right now — no pointer button is held, or the UI thread cannot host
  /// the native protocol — in which case nothing happened and the drag stays in process.
  /// </returns>
  bool TryBeginFileDrag(string[] paths, DragDropEffects allowedEffects, Action<DragDropEffects> completed);
}
