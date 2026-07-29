using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// The receiving end of AppKit's target/action: an object a control can be told to message when the
/// user works it.
/// </summary>
/// <remarks>
/// <para>
/// An <c>NSControl</c> reports a click by sending a selector to a target object. There is no callback
/// to register and no signal to connect, so something has to be that object — and it cannot be a
/// managed instance, because there is no such thing to Objective-C. So the class is built at run time
/// with <c>objc_allocateClassPair</c> and given one method whose implementation is an
/// <see cref="UnmanagedCallersOnlyAttribute"/> static passed as a function pointer, which is the same
/// shape the canvas's <c>drawRect:</c> already uses.
/// </para>
/// <para>
/// One instance per control rather than one shared instance keyed by sender: the sender is the control
/// and would work, but a peer that swaps its widget would then have to remember to re-key itself, and
/// a target that answers for exactly one control cannot be pointed at the wrong one.
/// </para>
/// </remarks>
internal static unsafe class CocoaAction
{
    /// <summary>The runtime class, built on first use.</summary>
    private static nint _class;

    /// <summary>What each target does, by target pointer — the map that replaces a captured closure.</summary>
    private static readonly ConcurrentDictionary<nint, Action> _handlers = new();

    /// <summary>The selector every target built here answers.</summary>
    internal static nint Selector => CocoaRuntime.sel_registerName("nativeFormsAction:");

    /// <summary>Builds a target that runs <paramref name="handler"/> when messaged, or zero.</summary>
    internal static nint Create(Action handler)
    {
        EnsureClass();
        if (_class == 0)
            return 0;

        var allocated = CocoaRuntime.SendPointer(_class, CocoaRuntime.sel_registerName("alloc"));
        var target = allocated == 0 ? 0 : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("init"));
        if (target != 0)
            _handlers[target] = handler;

        return target;
    }

    /// <summary>Forgets a target, so a disposed peer's handler is not held alive by this map.</summary>
    internal static void Forget(nint target)
    {
        if (target != 0)
            _handlers.TryRemove(target, out _);
    }

    private static void EnsureClass()
    {
        if (_class != 0 || !CocoaRuntime.Available)
            return;

        var superclass = CocoaRuntime.objc_getClass("NSObject");
        if (superclass == 0)
            return;

        var created = CocoaRuntime.objc_allocateClassPair(superclass, "NativeFormsAction", 0);
        if (created == 0)
            return;

        // "v@:@": returns void, takes self, _cmd and the sending control.
        CocoaRuntime.class_addMethod(created, Selector, (nint)(delegate* unmanaged<nint, nint, nint, void>)&Perform, "v@:@");
        CocoaRuntime.objc_registerClassPair(created);
        _class = created;
    }

    [UnmanagedCallersOnly]
    private static void Perform(nint self, nint selector, nint sender)
    {
        if (_handlers.TryGetValue(self, out var handler))
            handler();
    }
}
