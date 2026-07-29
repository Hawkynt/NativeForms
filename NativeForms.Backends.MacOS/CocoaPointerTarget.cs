using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// The pointer's path into a widget whose class this backend did not build: a tracking area owned by
/// a run-time object that turns AppKit's <c>mouseMoved:</c> and <c>mouseExited:</c> into the peer's
/// hover channel.
/// </summary>
/// <remarks>
/// <para>
/// A canvas hears the pointer because its class carries the methods; an <c>NSButton</c> or an
/// <c>NSTextField</c> is AppKit's class and cannot be given any. What both need is a tracking area —
/// a window sends <c>mouseMoved:</c> to whichever view holds the keyboard, not to the one under the
/// pointer — and a tracking area's owner is an object of the installer's choosing. So the owner is
/// one of these, and the managed peer comes back through a static map exactly as every other callback
/// in this backend recovers its state.
/// </para>
/// <para>
/// Installed on the first subscriber and not before. The core only subscribes to a peer's hover
/// channel for a control something is watching — a registered tooltip is the whole of it today — so a
/// gallery of three hundred widgets grows tracking areas for the handful that are tipped rather than
/// for all of them.
/// </para>
/// </remarks>
internal static unsafe class CocoaPointerTarget
{
    /// <summary>The runtime class, built on first use.</summary>
    private static nint _class;

    /// <summary>Which peer a target speaks for, by target pointer.</summary>
    private static readonly ConcurrentDictionary<nint, CocoaControlPeer> _peers = new();

    /// <summary>The target watching a widget, by view pointer — so one is installed once.</summary>
    private static readonly ConcurrentDictionary<nint, nint> _targetsByView = new();

    /// <summary>Starts reporting the pointer over <paramref name="peer"/>'s widget, if it is not already.</summary>
    internal static void Track(CocoaControlPeer peer)
    {
        var view = peer.Handle;
        if (view == 0 || _targetsByView.ContainsKey(view))
            return;

        EnsureClass();
        if (_class == 0)
            return;

        var allocated = CocoaRuntime.SendPointer(_class, CocoaRuntime.sel_registerName("alloc"));
        var target = allocated == 0 ? 0 : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("init"));
        if (target == 0)
            return;

        _peers[target] = peer;
        _targetsByView[view] = target;

        // NSTrackingMouseEnteredAndExited | NSTrackingMouseMoved | NSTrackingActiveAlways |
        // NSTrackingInVisibleRect — the same set the canvas asks for, and for the same reasons.
        const nint options = 0x01 | 0x02 | 0x80 | 0x200;

        var area = CocoaRuntime.Allocate("NSTrackingArea") is var slot && slot != 0
            ? CocoaRuntime.SendTrackingArea(
                slot,
                CocoaRuntime.sel_registerName("initWithRect:options:owner:userInfo:"),
                new(0, 0, 1, 1), // ignored: NSTrackingInVisibleRect substitutes the view's visible rect
                options,
                target,
                0)
            : 0;

        if (area != 0)
            CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("addTrackingArea:"), area);
    }

    /// <summary>Drops a disposed widget's target, so the map does not hold its peer alive.</summary>
    internal static void Forget(nint view)
    {
        if (view != 0 && _targetsByView.TryRemove(view, out var target))
            _peers.TryRemove(target, out _);
    }

    private static void EnsureClass()
    {
        if (_class != 0 || !CocoaRuntime.Available)
            return;

        var superclass = CocoaRuntime.objc_getClass("NSObject");
        if (superclass == 0)
            return;

        var created = CocoaRuntime.objc_allocateClassPair(superclass, "NativeFormsPointerTarget", 0);
        if (created == 0)
            return;

        // "v@:@": returns void, takes self, _cmd and the event. Entering is a move like any other;
        // leaving is what lets a tip go away again.
        Add(created, "mouseMoved:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&Moved);
        Add(created, "mouseEntered:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&Moved);
        Add(created, "mouseExited:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&Exited);

        CocoaRuntime.objc_registerClassPair(created);
        _class = created;
    }

    private static void Add(nint cls, string selector, nint implementation)
        => CocoaRuntime.class_addMethod(cls, CocoaRuntime.sel_registerName(selector), implementation, "v@:@");

    [UnmanagedCallersOnly]
    private static void Moved(nint self, nint selector, nint theEvent)
    {
        if (!_peers.TryGetValue(self, out var peer))
            return;

        var at = CocoaCanvasPeer.LocationOf(peer.Handle, theEvent);
        peer.RaisePointerMove(at.X, at.Y);
    }

    [UnmanagedCallersOnly]
    private static void Exited(nint self, nint selector, nint theEvent)
    {
        if (_peers.TryGetValue(self, out var peer))
            peer.RaisePointerLeave();
    }
}
