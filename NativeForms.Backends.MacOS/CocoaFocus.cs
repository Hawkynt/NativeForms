using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>Something that wants to hear when it gains or loses the keyboard.</summary>
/// <remarks>
/// An interface rather than a pair of actions in a map, because the two peers that implement it
/// already exist and already own the events — what was missing was anything to raise them.
/// </remarks>
internal interface ICocoaFocusTarget
{
    /// <summary>The widget behind this peer now holds the keyboard.</summary>
    void RaiseGotFocus();

    /// <summary>The widget behind this peer no longer holds it.</summary>
    void RaiseLostFocus();
}

/// <summary>
/// Who has the keyboard, tracked through the one call every focus change on this platform goes
/// through: <c>-[NSWindow makeFirstResponder:]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Nothing raised <see cref="ICocoaFocusTarget.RaiseGotFocus"/> before this existed, so
/// <c>Control.Focused</c> was false on macOS however the keyboard had actually moved — and everything
/// in the toolkit that reasons about focus was therefore wrong there: a spin box never committed its
/// edit, a link label never drew its ring, a form never had an <c>ActiveControl</c>.
/// </para>
/// <para>
/// There is no notification to subscribe to. AppKit reports a responder change to the responders
/// themselves, through <c>becomeFirstResponder</c> and <c>resignFirstResponder</c>, which are methods
/// on the view's class — and most of the views here are AppKit's own classes, which this backend
/// cannot add a method to. What every one of them does have in common is the window: a first responder
/// only ever changes because something asked the window to change it, whether that was the user
/// clicking, the key loop moving on, or the toolkit's own <c>Focus()</c>. So the window is the class
/// that is subclassed, and one override covers a canvas, an <c>NSTextField</c> and an
/// <c>NSTableView</c> alike.
/// </para>
/// <para>
/// The change is read back off the window afterwards rather than taken from the argument, because the
/// two differ for exactly the widget that matters most: a field does not edit itself, it borrows the
/// window's shared field editor, so asking for the field to take the keyboard leaves an
/// <c>NSTextView</c> holding it.
/// </para>
/// </remarks>
internal static unsafe class CocoaFocus
{
    /// <summary>The peers that can hold the keyboard, by the view AppKit would name.</summary>
    private static readonly ConcurrentDictionary<nint, ICocoaFocusTarget> _targets = new();

    /// <summary>Whoever the toolkit last said had it, so a change is reported once and only once.</summary>
    /// <remarks>
    /// One field for the whole process, which is what <c>Control.Focused</c> is: the toolkit has one
    /// focused control, not one per window. It is also what makes the reporting idempotent —
    /// <c>makeFirstResponder:</c> nests (a field's own <c>becomeFirstResponder</c> asks the window
    /// again, for the field editor), so the outer call would otherwise report the same arrival a
    /// second time.
    /// </remarks>
    private static ICocoaFocusTarget? _focused;

    /// <summary>Makes a peer findable by the view AppKit will hand the keyboard to.</summary>
    internal static void Watch(nint view, ICocoaFocusTarget target)
    {
        if (view != 0)
            _targets[view] = target;
    }

    /// <summary>
    /// The counterpart of <see cref="Watch"/>, for a view that has been swapped out or disposed.
    /// </summary>
    /// <remarks>
    /// The current holder is dropped along with it. A peer that goes away while it has the keyboard
    /// would otherwise stay in <see cref="_focused"/> and swallow the next arrival as a repeat.
    /// </remarks>
    internal static void Forget(nint view)
    {
        if (view == 0 || !_targets.TryRemove(view, out var target))
            return;

        if (ReferenceEquals(target, _focused))
            _focused = null;
    }

    /// <summary>
    /// The keyboard is now on <paramref name="responder"/>: tell whoever lost it and whoever gained it.
    /// </summary>
    internal static void Moved(nint responder)
    {
        var target = Resolve(responder);
        if (ReferenceEquals(target, _focused))
            return;

        var lost = _focused;
        _focused = target;
        lost?.RaiseLostFocus();
        target?.RaiseGotFocus();
    }

    /// <summary>How far up a responder's superviews a peer is looked for.</summary>
    /// <remarks>
    /// A promoted widget is at most a couple of views deep inside the object the peer holds — a text
    /// view inside a clip view inside a scroll view is the deepest of them — and a walk with no bound
    /// would climb out of the control and find the window's content view, which answers for the whole
    /// form.
    /// </remarks>
    private const int _Depth = 4;

    /// <summary>The peer a first responder belongs to, or null for one nothing here owns.</summary>
    /// <remarks>
    /// <para>
    /// Two routes, because AppKit hands the keyboard to two different kinds of object. A view the
    /// peer built — a canvas, an <c>NSButton</c>, an <c>NSPopUpButton</c> — is the responder itself, and
    /// a view <em>inside</em> one — the <c>NSTableView</c> of a promoted list, the <c>NSTextView</c> of a
    /// multiline box — is reached by climbing its superviews, since the peer holds the scroll view
    /// around it.
    /// </para>
    /// <para>
    /// The second route is the borrowed field editor, which is neither: it is an <c>NSTextView</c> the
    /// window owns and lends to whichever field is being typed in, and it carries that field as its
    /// delegate. That is the same fact <c>CocoaTextBoxPeer.InterceptKey</c> and the link label are both
    /// built on, and it is asked second because a peer's own view answers first and asking a canvas for
    /// a delegate it has not got would cost a message for nothing.
    /// </para>
    /// </remarks>
    private static ICocoaFocusTarget? Resolve(nint responder)
    {
        if (responder == 0)
            return null;

        if (Climb(responder) is { } own)
            return own;

        var lender = CocoaRuntime.Responds(responder, "delegate")
            ? CocoaRuntime.SendPointer(responder, CocoaRuntime.sel_registerName("delegate"))
            : 0;

        return lender == 0 ? null : Climb(lender);
    }

    /// <summary>A view or its nearest watched ancestor, within <see cref="_Depth"/> steps.</summary>
    /// <remarks>
    /// The step is guarded rather than sent, because a first responder is not always a view: a window
    /// answers for itself whenever nothing else holds the keyboard, which is where every form starts
    /// and where AppKit parks the responder while it takes an editor apart. <c>NSWindow</c> has no
    /// <c>superview</c>, and an unrecognized selector aborts the process rather than answering nil.
    /// </remarks>
    private static ICocoaFocusTarget? Climb(nint view)
    {
        var superview = CocoaRuntime.sel_registerName("superview");
        for (var i = 0; view != 0 && i < _Depth; ++i)
        {
            if (_targets.TryGetValue(view, out var target))
                return target;

            if (!CocoaRuntime.Responds(view, "superview"))
                return null;

            view = CocoaRuntime.SendPointer(view, superview);
        }

        return null;
    }

    /// <summary>The runtime <c>NSWindow</c> subclass, built on first use.</summary>
    private static nint _windowClass;

    /// <summary>The runtime <c>NSPanel</c> subclass a popup is, built on first use.</summary>
    private static nint _panelClass;

    /// <summary>
    /// Each superclass's own <c>makeFirstResponder:</c>, so the override can run the real one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Looked up once and called as a function pointer rather than sent with
    /// <c>objc_msgSendSuper</c>. Both reach the same code; this one needs no <c>objc_super</c> struct
    /// and, more to the point, does not have to work out the receiver's superclass at call time — a
    /// class AppKit has swizzled underneath us (KVO builds one silently) would make that answer point
    /// back at this class and turn the call into recursion.
    /// </para>
    /// <para>
    /// Two of them, and therefore two overrides that do nothing but name one. <c>NSPanel</c> inherits
    /// this method rather than defining one, so today both hold the same address — but a shared
    /// implementation would have to pick a superclass without being told which object it is answering
    /// for, and the version of AppKit where that assumption stops holding would not announce itself.
    /// </para>
    /// </remarks>
    private static nint _windowBase, _panelBase;

    /// <summary>The class a window peer allocates: an <c>NSWindow</c> that reports responder changes.</summary>
    internal static nint WindowClass
        => Ensure(
            ref _windowClass,
            ref _windowBase,
            "NSWindow",
            "NativeFormsWindow",
            (nint)(delegate* unmanaged<nint, nint, nint, byte>)&WindowMakeFirstResponder);

    /// <summary>
    /// The class a popup allocates: the same reporting, on an <c>NSPanel</c> so it can be told to work
    /// while a dialog is modal.
    /// </summary>
    internal static nint PanelClass
        => Ensure(
            ref _panelClass,
            ref _panelBase,
            "NSPanel",
            "NativeFormsPanel",
            (nint)(delegate* unmanaged<nint, nint, nint, byte>)&PanelMakeFirstResponder);

    /// <summary>Builds one of the two runtime classes, or answers zero where AppKit is not here.</summary>
    private static nint Ensure(ref nint cls, ref nint baseImplementation, string superName, string name, nint implementation)
    {
        if (cls != 0 || !CocoaRuntime.Available)
            return cls;

        var superclass = CocoaRuntime.objc_getClass(superName);
        if (superclass == 0)
            return 0;

        var selector = CocoaRuntime.sel_registerName("makeFirstResponder:");
        baseImplementation = CocoaRuntime.class_getMethodImplementation(superclass, selector);
        if (baseImplementation == 0)
            return 0;

        var created = CocoaRuntime.objc_allocateClassPair(superclass, name, 0);
        if (created == 0)
            return 0;

        // "c@:@": answers a BOOL, takes self, _cmd and the responder being installed.
        CocoaRuntime.class_addMethod(created, selector, implementation, "c@:@");
        CocoaRuntime.objc_registerClassPair(created);
        return cls = created;
    }

    /// <summary>How deep AppKit currently is inside this override.</summary>
    /// <remarks>
    /// It nests, and further than it looks. Moving the keyboard off a field runs the whole of AppKit's
    /// end-of-editing dance — the text view resigns, which posts a notification, which has the field
    /// end its cell's editing, which takes the field editor out of the view hierarchy, which asks the
    /// window to end editing for it, which moves the responder again — so one call from the toolkit
    /// arrives here three or four times over, each of the inner ones with the responder somewhere in
    /// between the two the user would recognize.
    /// </remarks>
    private static int _depth;

    /// <summary>
    /// AppKit is moving the keyboard: let it finish, then say where it went.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported from the outermost turn only, which does two things. It reports the responder that
    /// settled rather than one of the transient ones AppKit passes through, and it keeps the toolkit's
    /// own handlers — which repaint, and may move focus again — out of the middle of a responder
    /// transition the platform has not finished making.
    /// </para>
    /// <para>
    /// The answer is read back off the window rather than taken from the argument or from the return
    /// value. <c>makeFirstResponder:</c> answers NO when whatever holds the keyboard refuses to give it
    /// up, and installs something other than what it was asked for when the target is a field that
    /// edits through a borrowed editor — so the window's own account is the only one that is true in
    /// both cases, and a refusal simply reads as no change.
    /// </para>
    /// </remarks>
    [UnmanagedCallersOnly]
    private static byte WindowMakeFirstResponder(nint self, nint selector, nint responder)
        => Forward(_windowBase, self, selector, responder);

    /// <inheritdoc cref="WindowMakeFirstResponder"/>
    [UnmanagedCallersOnly]
    private static byte PanelMakeFirstResponder(nint self, nint selector, nint responder)
        => Forward(_panelBase, self, selector, responder);

    /// <inheritdoc cref="WindowMakeFirstResponder"/>
    private static byte Forward(nint implementation, nint self, nint selector, nint responder)
    {
        byte accepted;
        ++_depth;
        try
        {
            accepted = ((delegate* unmanaged<nint, nint, nint, byte>)implementation)(self, selector, responder);
        }
        finally
        {
            --_depth;
        }

        if (_depth == 0)
            Moved(CocoaRuntime.SendPointer(self, CocoaRuntime.sel_registerName("firstResponder")));

        return accepted;
    }
}
