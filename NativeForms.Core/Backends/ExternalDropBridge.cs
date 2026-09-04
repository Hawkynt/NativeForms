using System.Drawing;
using System.Runtime.CompilerServices;

namespace Hawkynt.NativeForms.Backends;

/// <summary>
/// Backend hook for operating-system-originated drops. Platform peers report a native payload and
/// screen position here; the core keeps ownership of hit-testing and the managed drag-event contract.
/// </summary>
/// <remarks>
/// This is intentionally not a second drag-and-drop engine. In-process drags continue to flow through
/// <see cref="Control.DoDragDrop"/>; native backends only translate their platform file-drop protocol
/// into the same <see cref="Control.AllowDrop"/>, <see cref="Control.DragEnter"/> and
/// <see cref="Control.DragDrop"/> path. Backends may use the returned effect to acknowledge protocols
/// that require an acceptance result.
/// </remarks>
public static class ExternalDropBridge {
  private sealed class Root(Form form) {
    public Form Form { get; } = form;
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
  /// <param name="data">The translated payload; native file-drop integrations pass a <c>string[]</c>.</param>
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
}
