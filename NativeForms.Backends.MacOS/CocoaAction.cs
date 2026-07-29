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

/// <summary>
/// The receiving end of a text view's link activation: an object answering
/// <c>textView:clickedOnLink:atIndex:</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CocoaAction"/>'s pattern with one difference that earns its own class: a delegate method
/// carries an argument worth having and an answer AppKit acts on. The argument is the link, which is
/// what the toolkit reports; the answer is whether the click was handled, and returning yes is what
/// stops AppKit opening the URL in a browser behind the application's back — the toolkit's
/// <c>LinkClicked</c> is the application's hook, so the platform must not act on the click as well.
/// </para>
/// <para>
/// The link arrives as whatever was put in the attribute: AppKit's own detector stores an
/// <c>NSURL</c>, an RTF document may carry either, and a hand-applied attribute is often a plain
/// string. Both are asked rather than assumed, and anything else is refused — reading a third kind of
/// object as a string would not fail, it would read some other field's bytes as characters.
/// </para>
/// </remarks>
internal static unsafe class CocoaLinkTarget
{
    /// <summary>The runtime class, built on first use.</summary>
    private static nint _class;

    /// <summary>What each target reports to, by target pointer.</summary>
    private static readonly ConcurrentDictionary<nint, Action<string>> _handlers = new();

    /// <summary>Builds a delegate reporting activations to <paramref name="handler"/>, or zero.</summary>
    internal static nint Create(Action<string> handler)
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

        var created = CocoaRuntime.objc_allocateClassPair(superclass, "NativeFormsLinkTarget", 0);
        if (created == 0)
            return;

        // "c@:@@Q": returns BOOL, takes self, _cmd, the text view, the link and the character index.
        CocoaRuntime.class_addMethod(
            created,
            CocoaRuntime.sel_registerName("textView:clickedOnLink:atIndex:"),
            (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, byte>)&ClickedOnLink,
            "c@:@@Q");

        CocoaRuntime.objc_registerClassPair(created);
        _class = created;
    }

    [UnmanagedCallersOnly]
    private static byte ClickedOnLink(nint self, nint selector, nint textView, nint link, nint index)
    {
        if (!_handlers.TryGetValue(self, out var handler) || UrlOf(link) is not { Length: > 0 } url)
            return 0;

        handler(url);
        return 1; // handled here, so AppKit does not also open it
    }

    /// <summary>The text of a link attribute's value, or empty when it is neither a URL nor a string.</summary>
    private static string UrlOf(nint link)
    {
        if (link == 0)
            return string.Empty;

        var absolute = CocoaRuntime.sel_registerName("absoluteString");
        if (CocoaRuntime.SendBool(link, CocoaRuntime.sel_registerName("respondsToSelector:"), absolute))
            return CocoaRuntime.SendPointer(link, absolute) is var text && text != 0
                ? CocoaNative.ReadString(text)
                : string.Empty;

        var strings = CocoaRuntime.objc_getClass("NSString");
        return strings != 0 && CocoaRuntime.SendBool(link, CocoaRuntime.sel_registerName("isKindOfClass:"), strings)
            ? CocoaNative.ReadString(link)
            : string.Empty;
    }
}
