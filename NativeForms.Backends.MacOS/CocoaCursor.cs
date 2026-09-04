using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// The pointer shape a view asks for, and the two routes AppKit offers for asking.
/// </summary>
/// <remarks>
/// <para>
/// There is no "set the cursor on this view" message here. A view declares the rectangles it wants a
/// shape over inside <c>resetCursorRects</c>, which AppKit calls when it feels like it, and the shape
/// is set from that — so the wish has to be parked somewhere the callback can find it. That is what
/// this map is: view pointer to <c>NSCursor</c>, read back by the canvas class's own
/// <c>resetCursorRects</c>, which is the same static-map-instead-of-a-closure shape every other
/// callback in this backend uses.
/// </para>
/// <para>
/// A widget whose class this backend did not build cannot be given a <c>resetCursorRects</c> — the
/// class belongs to AppKit — so a native control takes the other route: a tracking area asking for
/// cursor updates, owned by a run-time class that answers <c>cursorUpdate:</c> by setting the shape.
/// Both routes end at the same map, so a caller sees one behaviour whichever kind of peer is
/// underneath.
/// </para>
/// </remarks>
internal static unsafe partial class CocoaCursor {
  /// <summary>What each view wants under the pointer, by view pointer.</summary>
  private static readonly ConcurrentDictionary<nint, Entry> _byView = new();

  /// <summary>The cursor-update target watching a native widget, by view pointer.</summary>
  private static readonly ConcurrentDictionary<nint, nint> _targetsByView = new();

  /// <summary>Which view a cursor-update target speaks for, by target pointer.</summary>
  private static readonly ConcurrentDictionary<nint, nint> _viewsByTarget = new();

  /// <summary>A resolved shape, and whether this backend owns it and must release it.</summary>
  /// <remarks>
  /// The stock shapes are AppKit's own singletons and are borrowed; a custom bitmap cursor is built
  /// here with <c>alloc</c>/<c>init…</c> and is therefore ours to release when it is replaced.
  /// </remarks>
  private readonly record struct Entry(nint Cursor, bool Owned);

  /// <summary>
  /// Records the shape <paramref name="view"/> wants and asks AppKit to pick it up.
  /// </summary>
  /// <remarks>
  /// An unchanged shape returns without touching anything. The core calls this from mouse-move
  /// handlers — a splitter swaps the shape as the pointer crosses its band — and asking the window
  /// to rebuild its cursor rectangles on every step across a control would cost far more than the
  /// property is worth.
  /// </remarks>
  internal static void Apply(nint view, Cursor? cursor) {
    if (view == 0)
      return;

    var resolved = Resolve(cursor);
    _byView.TryGetValue(view, out var previous);
    if (previous.Cursor == resolved.Cursor && !resolved.Owned)
      return;

    if (previous.Owned && previous.Cursor != 0)
      CocoaRuntime.SendVoid(previous.Cursor, CocoaRuntime.sel_registerName("release"));

    if (resolved.Cursor == 0)
      _byView.TryRemove(view, out _);
    else
      _byView[view] = resolved;

    Invalidate(view);
  }

  /// <summary>The shape a view asked for, or zero — what the canvas's <c>resetCursorRects</c> reads.</summary>
  internal static nint For(nint view)
      => _byView.TryGetValue(view, out var entry) ? entry.Cursor : 0;

  /// <summary>Drops a disposed view's shape, releasing it if this backend built it.</summary>
  internal static void Forget(nint view) {
    if (view != 0 && _byView.TryRemove(view, out var entry) && entry.Owned && entry.Cursor != 0)
      CocoaRuntime.SendVoid(entry.Cursor, CocoaRuntime.sel_registerName("release"));

    if (view != 0 && _targetsByView.TryRemove(view, out var target))
      _viewsByTarget.TryRemove(target, out _);
  }

  /// <summary>
  /// Puts a cursor-update tracking area on a widget this backend did not build the class of.
  /// </summary>
  /// <remarks>
  /// Installed once, and only when an application actually asked for a shape: an AppKit control
  /// already carries the right one — a text field puts an I-beam over itself — and a rectangle laid
  /// over that unasked would take the platform's own answer away. <c>NSTrackingInVisibleRect</c> for
  /// the reason the canvas's hover area gives: the widget is built at 1x1 and moved by every layout
  /// afterwards, so a rectangle passed once would drift away from the control it was meant for.
  /// </remarks>
  internal static void Track(nint view) {
    if (view == 0 || CocoaCanvasPeer.IsOwnView(view) || _targetsByView.ContainsKey(view))
      return;

    var target = CreateTarget(view);
    if (target == 0)
      return;

    // NSTrackingCursorUpdate | NSTrackingActiveAlways | NSTrackingInVisibleRect.
    const nint options = 0x04 | 0x80 | 0x200;

    var allocated = CocoaRuntime.Allocate("NSTrackingArea");
    var area = allocated == 0
        ? 0
        : CocoaRuntime.SendTrackingArea(
            allocated,
            CocoaRuntime.sel_registerName("initWithRect:options:owner:userInfo:"),
            new(0, 0, 1, 1), // ignored: NSTrackingInVisibleRect substitutes the view's own visible rect
            options,
            target,
            0);

    if (area != 0)
      CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("addTrackingArea:"), area);
  }

  /// <summary>Tells the window holding a view that its cursor rectangles are stale.</summary>
  private static void Invalidate(nint view) {
    var window = CocoaRuntime.SendPointer(view, CocoaRuntime.sel_registerName("window"));
    if (window != 0)
      CocoaRuntime.SendVoid(window, CocoaRuntime.sel_registerName("invalidateCursorRectsForView:"), view);
  }

  /// <summary>
  /// The <c>NSCursor</c> for a toolkit cursor, and whether it was built here.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Five of the toolkit's shapes have no public equivalent on this desktop and fall back to the
  /// arrow rather than to something that merely looks busy. The wait and app-starting pointers are
  /// the window server's, shown when a process stops answering, and there is no message for an
  /// application to raise one. The help pointer and the two diagonal resize arrows simply do not
  /// exist in <c>NSCursor</c>'s published set; a horizontal arrow over a corner grip would point the
  /// wrong way, which is worse than the default shape.
  /// </para>
  /// <para>
  /// The splitter shapes take the plain resize arrows, which is exactly what the Win32 backend does
  /// with them for the same reason: the stock set has no splitter. "Move everything" takes the open
  /// hand, this desktop's own way of saying a thing can be picked up, since there is no four-headed
  /// arrow here either.
  /// </para>
  /// </remarks>
  private static Entry Resolve(Cursor? cursor) {
    if (cursor is null)
      return default;

    if (cursor.Kind == CursorKind.Custom)
      return Custom(cursor);

    var name = cursor.Kind switch {
      CursorKind.Hand => "pointingHandCursor",
      CursorKind.IBeam => "IBeamCursor",
      CursorKind.Cross => "crosshairCursor",
      CursorKind.SizeWE or CursorKind.VSplit => "resizeLeftRightCursor",
      CursorKind.SizeNS or CursorKind.HSplit => "resizeUpDownCursor",
      CursorKind.No => "operationNotAllowedCursor",
      CursorKind.SizeAll => "openHandCursor",
      _ => "arrowCursor",
    };

    return new(CocoaRuntime.SendToClass("NSCursor", name), false);
  }

  /// <summary>Builds an <c>NSCursor</c> from a custom cursor's own pixels and hotspot.</summary>
  /// <remarks>
  /// The hotspot passes straight through: AppKit reads it in the image's coordinates with the origin
  /// at the top left, which is the corner the toolkit counts from as well. The image is released
  /// once the cursor holds it, so the pair is one object to account for rather than two.
  /// </remarks>
  private static Entry Custom(Cursor cursor) {
    if (cursor.Pixels is not { } pixels)
      return default;

    var image = CocoaImage.CreateNSImage(cursor.Width, cursor.Height, pixels);
    if (image == 0)
      return default;

    var allocated = CocoaRuntime.Allocate("NSCursor");
    var built = allocated == 0
        ? 0
        : SendCursor(
            allocated,
            CocoaRuntime.sel_registerName("initWithImage:hotSpot:"),
            image,
            new() { X = cursor.HotspotX, Y = cursor.HotspotY });

    CocoaRuntime.SendVoid(image, CocoaRuntime.sel_registerName("release"));
    return new(built, built != 0);
  }

  /// <summary>The run-time class answering <c>cursorUpdate:</c>, built on first use.</summary>
  private static nint _targetClass;

  /// <summary>Builds the object a native widget's tracking area reports to, or zero.</summary>
  private static nint CreateTarget(nint view) {
    EnsureTargetClass();
    if (_targetClass == 0)
      return 0;

    var allocated = CocoaRuntime.SendPointer(_targetClass, CocoaRuntime.sel_registerName("alloc"));
    var target = allocated == 0 ? 0 : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("init"));
    if (target == 0)
      return 0;

    _viewsByTarget[target] = view;
    _targetsByView[view] = target;
    return target;
  }

  private static void EnsureTargetClass() {
    if (_targetClass != 0 || !CocoaRuntime.Available)
      return;

    var superclass = CocoaRuntime.objc_getClass("NSObject");
    if (superclass == 0)
      return;

    var created = CocoaRuntime.objc_allocateClassPair(superclass, "NativeFormsCursorTarget", 0);
    if (created == 0)
      return;

    // "v@:@": returns void, takes self, _cmd and the event that crossed the tracking area.
    CocoaRuntime.class_addMethod(
        created,
        CocoaRuntime.sel_registerName("cursorUpdate:"),
        (nint)(delegate* unmanaged<nint, nint, nint, void>)&CursorUpdate,
        "v@:@");

    CocoaRuntime.objc_registerClassPair(created);
    _targetClass = created;
  }

  /// <summary>AppKit crossed into a tracked widget: set the shape that widget asked for.</summary>
  [UnmanagedCallersOnly]
  private static void CursorUpdate(nint self, nint selector, nint theEvent) {
    if (_viewsByTarget.TryGetValue(self, out var view) && For(view) is var cursor && cursor != 0)
      CocoaRuntime.SendVoid(cursor, CocoaRuntime.sel_registerName("set"));
  }

  /// <summary>Builds a cursor from an image and a hotspot: one pointer and a point of two doubles.</summary>
  [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
  private static partial nint SendCursor(nint receiver, nint selector, nint image, CocoaRuntime.CGPoint hotSpot);
}
