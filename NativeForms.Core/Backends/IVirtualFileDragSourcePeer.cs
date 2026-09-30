namespace Hawkynt.NativeForms.Backends;

/// <summary>
/// Optional capability of a control peer: it can hand <see cref="VirtualFile"/> entries — files whose
/// content is produced on demand — to the operating system's own drag-and-drop without writing them
/// anywhere first. The drop target either pulls the content straight into its destination (Win32
/// <c>CFSTR_FILEDESCRIPTORW</c>/<c>CFSTR_FILECONTENTS</c>), or names a destination the peer writes
/// into (the GTK direct-save protocol, macOS file promises).
/// </summary>
/// <remarks>
/// A drag started with <see cref="Control.DoDragDrop(object, DragDropEffects, Action{DragDropEffects})"/>
/// whose payload is a non-empty <c>VirtualFile[]</c> stays in process while the pointer is over the
/// application's window, exactly like a file list, and the core asks the source's peer for this
/// native drag when the pointer leaves the window with the button held. A peer that only implements
/// <see cref="IFileDragSourcePeer"/> gets the entries written into a temporary folder and dragged as
/// paths instead; a peer that implements neither, or declines, leaves the drag in process.
/// </remarks>
public interface IVirtualFileDragSourcePeer {
  /// <summary>
  /// Starts an operating-system drag of <paramref name="files"/> from this peer, called from within the
  /// peer's own pointer-move notification while a button is held.
  /// </summary>
  /// <param name="files">At least one entry; paths are validated by <see cref="VirtualFile"/>.</param>
  /// <param name="allowedEffects">
  /// The effects the source permits: a non-empty combination of <see cref="DragDropEffects.Copy"/>,
  /// <see cref="DragDropEffects.Move"/> and <see cref="DragDropEffects.Link"/>.
  /// </param>
  /// <param name="completed">
  /// Invoked exactly once, on the UI thread, with the effect the drop target performed, as for
  /// <see cref="IFileDragSourcePeer.TryBeginFileDrag"/>. Never invoked when the call returns
  /// <see langword="false"/>.
  /// </param>
  /// <returns>
  /// <see langword="true"/> when the native drag ran or is running; <see langword="false"/> when the
  /// platform cannot start one right now, in which case nothing happened and the drag stays in process.
  /// </returns>
  bool TryBeginVirtualFileDrag(VirtualFile[] files, DragDropEffects allowedEffects, Action<DragDropEffects> completed);
}
