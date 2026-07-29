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
/// The object AppKit sends an <c>NSTextView</c>'s delegate messages to: a link was clicked, and an
/// edit is about to happen.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CocoaAction"/>'s pattern with one difference that earns its own class: a delegate method
/// carries an argument worth having and an answer AppKit acts on. For a link the argument is the URL,
/// which is what the toolkit reports, and the answer is whether the click was handled — returning yes
/// stops AppKit opening it in a browser behind the application's back, since the toolkit's
/// <c>LinkClicked</c> is the application's hook and the platform must not act on the click as well.
/// For an edit the answer is whether it may happen at all, which is how a multiline box holds a
/// maximum length: AppKit offers the range being replaced and the string replacing it, so the
/// resulting length is known before a character exists.
/// </para>
/// <para>
/// Both live on one class because a text view has one delegate. Splitting them would mean the second
/// thing attached silently unhooked the first, which is the sort of failure that shows up as "links
/// stopped working when I set a length limit" a long way from here.
/// </para>
/// <para>
/// The link arrives as whatever was put in the attribute: AppKit's own detector stores an
/// <c>NSURL</c>, an RTF document may carry either, and a hand-applied attribute is often a plain
/// string. Both are asked rather than assumed, and anything else is refused — reading a third kind of
/// object as a string would not fail, it would read some other field's bytes as characters.
/// </para>
/// </remarks>
internal static unsafe class CocoaTextViewDelegate
{
    /// <summary>The runtime class, built on first use.</summary>
    private static nint _class;

    /// <summary>What each delegate reports link clicks to, by delegate pointer.</summary>
    private static readonly ConcurrentDictionary<nint, Action<string>> _handlers = new();

    /// <summary>The character limit each delegate enforces, by delegate pointer.</summary>
    private static readonly ConcurrentDictionary<nint, int> _limits = new();

    /// <summary>Builds a text view delegate, or zero.</summary>
    internal static nint Create()
    {
        EnsureClass();
        if (_class == 0)
            return 0;

        var allocated = CocoaRuntime.SendPointer(_class, CocoaRuntime.sel_registerName("alloc"));
        return allocated == 0 ? 0 : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("init"));
    }

    /// <summary>Points a delegate's link clicks at <paramref name="handler"/>.</summary>
    internal static void Report(nint target, Action<string> handler)
    {
        if (target != 0)
            _handlers[target] = handler;
    }

    /// <summary>Sets the characters a delegate lets its text view hold; zero or less lifts the limit.</summary>
    internal static void Limit(nint target, int maximum)
    {
        if (target == 0)
            return;

        if (maximum > 0)
            _limits[target] = maximum;
        else
            _limits.TryRemove(target, out _);
    }

    /// <summary>Forgets a delegate, so a disposed peer's handler is not held alive by these maps.</summary>
    internal static void Forget(nint target)
    {
        if (target == 0)
            return;

        _handlers.TryRemove(target, out _);
        _limits.TryRemove(target, out _);
    }

    private static void EnsureClass()
    {
        if (_class != 0 || !CocoaRuntime.Available)
            return;

        var superclass = CocoaRuntime.objc_getClass("NSObject");
        if (superclass == 0)
            return;

        var created = CocoaRuntime.objc_allocateClassPair(superclass, "NativeFormsTextViewDelegate", 0);
        if (created == 0)
            return;

        // "c@:@@Q": returns BOOL, takes self, _cmd, the text view, the link and the character index.
        CocoaRuntime.class_addMethod(
            created,
            CocoaRuntime.sel_registerName("textView:clickedOnLink:atIndex:"),
            (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, byte>)&ClickedOnLink,
            "c@:@@Q");

        // "c@:@{_NSRange=QQ}@": returns BOOL, takes self, _cmd, the text view, the range being
        // replaced -- two integers by value, which AArch64 hands over in two registers -- and the
        // string replacing it.
        CocoaRuntime.class_addMethod(
            created,
            CocoaRuntime.sel_registerName("textView:shouldChangeTextInRange:replacementString:"),
            (nint)(delegate* unmanaged<nint, nint, nint, CocoaRuntime.NSRange, nint, byte>)&ShouldChangeText,
            "c@:@{_NSRange=QQ}@");

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

    /// <summary>
    /// Whether an edit may go ahead: it may, unless it would leave the box longer than it is allowed
    /// to be.
    /// </summary>
    /// <remarks>
    /// The length is worked out from the range being replaced rather than measured afterwards, which
    /// is the whole reason this seam is the right one: a check made after the fact would have to undo
    /// the edit, and an undone edit is visible. A replacement of nothing is an attribute-only change
    /// and cannot lengthen anything, so it is waved through rather than measured.
    /// </remarks>
    [UnmanagedCallersOnly]
    private static byte ShouldChangeText(nint self, nint selector, nint textView, CocoaRuntime.NSRange range, nint replacement)
    {
        if (!_limits.TryGetValue(self, out var limit) || limit <= 0 || replacement == 0)
            return 1;

        var added = (int)CocoaRuntime.SendInteger(replacement, CocoaRuntime.sel_registerName("length"));
        var content = CocoaRuntime.SendPointer(textView, CocoaRuntime.sel_registerName("string"));
        var current = (int)CocoaRuntime.SendInteger(content, CocoaRuntime.sel_registerName("length"));
        return current - (int)range.Length + added <= limit ? (byte)1 : (byte)0;
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

/// <summary>
/// The gate a single-line field's typing passes through: an <c>NSFormatter</c> subclass that refuses
/// an edit which would make the text longer than the box is allowed to be.
/// </summary>
/// <remarks>
/// <para>
/// AppKit has no "maximum length" on an <c>NSTextField</c>. What it has is a formatter, which the
/// field editor consults on every keystroke through
/// <c>isPartialStringValid:proposedSelectedRange:originalString:originalSelectedRange:errorDescription:</c>
/// — before the character is committed, which is what makes the limit invisible rather than a
/// flicker followed by an undo.
/// </para>
/// <para>
/// Too-long text is truncated rather than refused outright. That is what a paste does on the other two
/// backends (<c>EM_LIMITTEXT</c> truncates, and so does <c>gtk_entry_set_max_length</c>), and a
/// formatter says so by replacing the proposed string and answering no — the answer means "not as you
/// proposed", not "nothing happens".
/// </para>
/// <para>
/// <c>NSFormatter</c> is abstract, so the two conversion methods are supplied as well and both are the
/// identity: a text field's object value is its string, and a formatter that answered nothing for it
/// would display an empty box, which is a far stranger bug than the one being fixed.
/// </para>
/// </remarks>
internal static unsafe class CocoaLengthFormatter
{
    /// <summary>The runtime class, built on first use.</summary>
    private static nint _class;

    /// <summary>The characters each formatter allows, by formatter pointer.</summary>
    private static readonly ConcurrentDictionary<nint, int> _limits = new();

    /// <summary>Builds a formatter allowing <paramref name="maximum"/> characters, or zero.</summary>
    internal static nint Create(int maximum)
    {
        EnsureClass();
        if (_class == 0)
            return 0;

        var allocated = CocoaRuntime.SendPointer(_class, CocoaRuntime.sel_registerName("alloc"));
        var formatter = allocated == 0 ? 0 : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("init"));
        if (formatter != 0)
            _limits[formatter] = maximum;

        return formatter;
    }

    /// <summary>Changes what an existing formatter allows, so a new limit needs no new object.</summary>
    internal static void Limit(nint formatter, int maximum)
    {
        if (formatter != 0)
            _limits[formatter] = maximum;
    }

    /// <summary>Forgets a formatter, so a disposed peer's limit is not held by this map.</summary>
    internal static void Forget(nint formatter)
    {
        if (formatter != 0)
            _limits.TryRemove(formatter, out _);
    }

    private static void EnsureClass()
    {
        if (_class != 0 || !CocoaRuntime.Available)
            return;

        var superclass = CocoaRuntime.objc_getClass("NSFormatter");
        if (superclass == 0)
            return;

        var created = CocoaRuntime.objc_allocateClassPair(superclass, "NativeFormsLengthFormatter", 0);
        if (created == 0)
            return;

        // "@@:@": answers an object, takes self, _cmd and the value being displayed.
        CocoaRuntime.class_addMethod(
            created,
            CocoaRuntime.sel_registerName("stringForObjectValue:"),
            (nint)(delegate* unmanaged<nint, nint, nint, nint>)&StringForObjectValue,
            "@@:@");

        // "c@:^@@^@": answers BOOL, takes self, _cmd, where to put the value, the text and where to
        // put an error description.
        CocoaRuntime.class_addMethod(
            created,
            CocoaRuntime.sel_registerName("getObjectValue:forString:errorDescription:"),
            (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, byte>)&GetObjectValue,
            "c@:^@@^@");

        // "c@:^@^{_NSRange=QQ}@{_NSRange=QQ}^@": answers BOOL, takes self, _cmd, the proposed string
        // by reference, the proposed selection by reference, the string before the edit, the selection
        // before it by value, and where to put an error description.
        CocoaRuntime.class_addMethod(
            created,
            CocoaRuntime.sel_registerName("isPartialStringValid:proposedSelectedRange:originalString:originalSelectedRange:errorDescription:"),
            (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, CocoaRuntime.NSRange, nint, byte>)&IsPartialStringValid,
            "c@:^@^{_NSRange=QQ}@{_NSRange=QQ}^@");

        CocoaRuntime.objc_registerClassPair(created);
        _class = created;
    }

    /// <summary>A field's object value is its string, so displaying it is handing it back.</summary>
    [UnmanagedCallersOnly]
    private static nint StringForObjectValue(nint self, nint selector, nint value)
    {
        if (value == 0)
            return 0;

        var strings = CocoaRuntime.objc_getClass("NSString");
        return strings != 0 && CocoaRuntime.SendBool(value, CocoaRuntime.sel_registerName("isKindOfClass:"), strings)
            ? value
            : 0;
    }

    /// <summary>And reading it back is the same identity in the other direction.</summary>
    [UnmanagedCallersOnly]
    private static byte GetObjectValue(nint self, nint selector, nint value, nint text, nint error)
    {
        if (value != 0)
            Marshal.WriteIntPtr(value, text);

        return 1;
    }

    /// <summary>
    /// The field editor asking whether what the user just typed may stand. It may, unless it is longer
    /// than the limit — in which case the head of it stands instead and the caret goes to the end.
    /// </summary>
    [UnmanagedCallersOnly]
    private static byte IsPartialStringValid(
        nint self,
        nint selector,
        nint partial,
        nint proposedRange,
        nint original,
        CocoaRuntime.NSRange originalRange,
        nint error)
    {
        if (!_limits.TryGetValue(self, out var limit) || limit <= 0 || partial == 0)
            return 1;

        var proposed = Marshal.ReadIntPtr(partial);
        if (proposed == 0)
            return 1;

        var length = (int)CocoaRuntime.SendInteger(proposed, CocoaRuntime.sel_registerName("length"));
        if (length <= limit)
            return 1;

        // Autoreleased, which is what a formatter is required to hand back here: the field editor
        // takes it and the pool the event was pulled inside of owns it.
        var truncated = CocoaRuntime.SendIndex(proposed, CocoaRuntime.sel_registerName("substringToIndex:"), limit);
        if (truncated == 0)
            return 0; // nothing to substitute, so refuse the edit rather than let it through

        Marshal.WriteIntPtr(partial, truncated);
        if (proposedRange != 0 && Marshal.ReadIntPtr(proposedRange) > limit)
        {
            Marshal.WriteIntPtr(proposedRange, limit);
            Marshal.WriteIntPtr(proposedRange, nint.Size, 0);
        }

        return 0; // "not as proposed" -- the substitute above is what stands
    }
}
