using System.Drawing;
using System.Runtime.CompilerServices;

namespace Hawkynt.NativeForms.Backends;

/// <summary>
/// Backend hook for operating-system-originated drops. Platform peers report a native payload and
/// screen position here; the core keeps ownership of hit-testing and the managed drag-event contract.
/// </summary>
/// <remarks>
/// <para>
/// This is intentionally not a second drag-and-drop engine. In-process drags continue to flow through
/// <see cref="Control.DoDragDrop(object, DragDropEffects)"/>; native backends only translate their platform file-drop protocol
/// into the same <see cref="Control.AllowDrop"/>, <see cref="Control.DragEnter"/> and
/// <see cref="Control.DragDrop"/> path. Backends may use the returned effect to acknowledge protocols
/// that require an acceptance result.
/// </para>
/// <para>
/// A protocol that only reports the final drop calls <see cref="Route"/>. One that reports the whole
/// hover calls <see cref="DragEnter"/>, then <see cref="DragOver"/> as the pointer moves, and ends with
/// either <see cref="DragLeave"/> or <see cref="Drop"/>; the managed targets then see the same
/// enter/over/leave/drop sequence an in-process drag raises.
/// </para>
/// <para>
/// The payload is a <c>string[]</c> of paths for files that exist on disk, or a <c>VirtualFile[]</c>
/// for files another application produces on demand.
/// </para>
/// </remarks>
public static class ExternalDropBridge {
  private sealed class Root(Form form) {
    public Form Form { get; } = form;
    public DragDropSession.ExternalDrag Drag { get; } = new();
  }

  private static readonly ConditionalWeakTable<IWindowPeer, Root> _roots = new();

  /// <summary>Associates a realized native window with the managed form whose tree it hosts.</summary>
  internal static void Attach(IWindowPeer window, Form form) {
    _roots.Remove(window);
    _roots.Add(window, new Root(form));
  }

  /// <summary>Removes a realization-time association before the peer is disposed.</summary>
  internal static void Detach(IWindowPeer window) => _roots.Remove(window);

  /// <summary>
  /// Routes one native final-drop notification into the managed control tree.
  /// </summary>
  /// <param name="window">The top-level peer receiving the operating-system drop.</param>
  /// <param name="data">The translated payload: a <c>string[]</c> of paths, or a <c>VirtualFile[]</c>.</param>
  /// <param name="allowedEffects">Effects the native source/protocol permits.</param>
  /// <param name="screenLocation">Pointer position in toolkit screen coordinates.</param>
  /// <returns>The effect accepted by the managed target, or <see cref="DragDropEffects.None"/>.</returns>
  public static DragDropEffects Route(
      IWindowPeer window,
      object data,
      DragDropEffects allowedEffects,
      Point screenLocation) {
    ArgumentNullException.ThrowIfNull(window);
    ArgumentNullException.ThrowIfNull(data);

    return _roots.TryGetValue(window, out var root)
        ? DragDropSession.RouteExternalDrop(root.Form, data, allowedEffects, screenLocation)
        : DragDropEffects.None;
  }

  /// <summary>
  /// An operating-system drag entered <paramref name="window"/>: the target under the pointer gets
  /// <see cref="Control.DragEnter"/>. A hover still in flight on the window is left first.
  /// </summary>
  /// <param name="window">The top-level peer the drag entered.</param>
  /// <param name="data">The translated payload: a <c>string[]</c> of paths, or a <c>VirtualFile[]</c>.</param>
  /// <param name="allowedEffects">Effects the native source permits.</param>
  /// <param name="screenLocation">Pointer position in toolkit screen coordinates.</param>
  /// <returns>The effect the target under the pointer accepts, or <see cref="DragDropEffects.None"/>.</returns>
  public static DragDropEffects DragEnter(IWindowPeer window, object data, DragDropEffects allowedEffects, Point screenLocation) {
    ArgumentNullException.ThrowIfNull(window);
    ArgumentNullException.ThrowIfNull(data);

    return _roots.TryGetValue(window, out var root)
        ? DragDropSession.ExternalDragEnter(root.Form, root.Drag, data, allowedEffects, screenLocation)
        : DragDropEffects.None;
  }

  /// <summary>
  /// The operating-system drag moved over <paramref name="window"/>: the target under the pointer gets
  /// <see cref="Control.DragOver"/>, or targets leave and enter as the pointer crosses between them.
  /// </summary>
  /// <param name="window">The top-level peer the drag is over.</param>
  /// <param name="allowedEffects">Effects the native source permits.</param>
  /// <param name="screenLocation">Pointer position in toolkit screen coordinates.</param>
  /// <returns>The effect the target under the pointer accepts, or <see cref="DragDropEffects.None"/>.</returns>
  public static DragDropEffects DragOver(IWindowPeer window, DragDropEffects allowedEffects, Point screenLocation) {
    ArgumentNullException.ThrowIfNull(window);

    return _roots.TryGetValue(window, out var root)
        ? DragDropSession.ExternalDragOver(root.Form, root.Drag, allowedEffects, screenLocation)
        : DragDropEffects.None;
  }

  /// <summary>
  /// The operating-system drag left <paramref name="window"/> or was cancelled: its target gets
  /// <see cref="Control.DragLeave"/>.
  /// </summary>
  /// <param name="window">The top-level peer the drag left.</param>
  public static void DragLeave(IWindowPeer window) {
    ArgumentNullException.ThrowIfNull(window);

    if (_roots.TryGetValue(window, out var root))
      DragDropSession.ExternalDragLeave(root.Drag);
  }

  /// <summary>
  /// The operating-system drag was released over <paramref name="window"/>: the target that accepted
  /// an effect gets <see cref="Control.DragDrop"/> with the payload given to <see cref="DragEnter"/>,
  /// one that refused gets <see cref="Control.DragLeave"/>.
  /// </summary>
  /// <param name="window">The top-level peer the drag was released over.</param>
  /// <param name="allowedEffects">Effects the native source permits.</param>
  /// <param name="screenLocation">Pointer position in toolkit screen coordinates.</param>
  /// <returns>The effect of the drop, or <see cref="DragDropEffects.None"/> when it was refused.</returns>
  public static DragDropEffects Drop(IWindowPeer window, DragDropEffects allowedEffects, Point screenLocation) {
    ArgumentNullException.ThrowIfNull(window);

    return _roots.TryGetValue(window, out var root)
        ? DragDropSession.ExternalDrop(root.Form, root.Drag, allowedEffects, screenLocation)
        : DragDropEffects.None;
  }
}
