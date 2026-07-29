using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// A two-state box: an <c>NSButton</c> wearing the switch button type, which is what AppKit calls a
/// check box.
/// </summary>
/// <remarks>
/// <para>
/// The first of the promotions in PRD §12 to reach this backend. The control is the same
/// <see cref="CheckBox"/> either way — the core decides at realization whether its configured state
/// stays inside what a real widget can do, and drops back to the owner-drawn twin when it does not.
/// What is bought here is the part owner-draw cannot have: VoiceOver knows what this is without being
/// told, the press animation is the desktop's own, and it follows a high-contrast or accent-colour
/// setting the painter would have to be taught.
/// </para>
/// <para>
/// A programmatic <c>setState:</c> does not run the action, so nothing here has to suppress an echo the
/// way the GTK peer suppresses <c>toggled</c>: AppKit only sends the action when the user works the
/// control.
/// </para>
/// </remarks>
internal class CocoaCheckBoxPeer : CocoaControlPeer, ICheckBoxPeer
{
    /// <summary>NSButtonType: switch 3, radio 4.</summary>
    private protected const nint _Switch = 3;
    private protected const nint _Radio = 4;

    private readonly nint _target;

    private protected CocoaCheckBoxPeer(nint buttonType)
        : base(Create(buttonType))
    {
        if (this.Handle == 0)
            return;

        _target = CocoaAction.Create(this.OnToggled);
        if (_target == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setTarget:"), _target);
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setAction:"), CocoaAction.Selector);
    }

    public CocoaCheckBoxPeer()
        : this(_Switch)
    {
    }

    /// <inheritdoc/>
    public event EventHandler? CheckedChanged;

    private static nint Create(nint buttonType)
    {
        var allocated = CocoaRuntime.Allocate("NSButton");
        var button = allocated == 0
            ? 0
            : CocoaRuntime.SendRectInit(allocated, CocoaRuntime.sel_registerName("initWithFrame:"), new(0, 0, 1, 1));

        if (button != 0)
            CocoaRuntime.SendVoid(button, CocoaRuntime.sel_registerName("setButtonType:"), buttonType);

        return button;
    }

    /// <summary>A button carries its caption as a title, not as a string value.</summary>
    public override void SetText(string text)
    {
        if (this.Handle == 0)
            return;

        var title = CocoaRuntime.NSString(text);
        if (title == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setTitle:"), title);
        CocoaNative.CFRelease(title);
    }

    /// <inheritdoc/>
    /// <remarks>NSControlStateValue: off 0, on 1.</remarks>
    public void SetChecked(bool value)
    {
        _checked = value;
        if (this.Handle != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setState:"), value ? 1 : 0);
    }

    /// <inheritdoc/>
    public bool GetChecked()
        => this.Handle == 0 ? _checked : CocoaRuntime.SendInteger(this.Handle, CocoaRuntime.sel_registerName("state")) != 0;

    private bool _checked;

    /// <summary>The widget reporting that the user worked it.</summary>
    private void OnToggled()
    {
        _checked = this.GetChecked();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        CocoaAction.Forget(_target);
        base.Dispose();
    }
}

/// <summary>A radio button: the same <c>NSButton</c>, wearing the radio type.</summary>
/// <remarks>
/// Grouping stays in the core, which unchecks the siblings sharing a parent. AppKit applies the same
/// rule to radio buttons sharing a superview, and a peer's superview is its control's parent, so the
/// two cannot reach different answers — which is why this asks for the real radio type rather than
/// dressing a switch up as one and losing the platform's keyboard behaviour with it.
/// </remarks>
internal sealed class CocoaRadioButtonPeer : CocoaCheckBoxPeer, IRadioButtonPeer
{
    public CocoaRadioButtonPeer()
        : base(_Radio)
    {
    }
}

/// <summary>
/// A caption frame: a real <c>NSBox</c>, filling a plain view that carries the control's own
/// coordinate system and holds the children on top of it.
/// </summary>
/// <remarks>
/// <para>
/// The shape <see cref="IGroupBoxPeer"/> prescribes, and for the reason it gives: parenting the
/// children into the box instead would shift every one of them by whatever inset AppKit reserves for
/// the border and the title, so the same bounds would land in a different place depending on whether
/// the control was promoted — which is the one thing a promotion is not allowed to change.
/// </para>
/// <para>
/// The box is added before any child and AppKit draws subviews in order, so it stays behind them; and
/// because it is behind them, hit-testing finds a child first and the frame never eats a click meant
/// for what it surrounds.
/// </para>
/// <para>
/// The host view is the canvas class, which answers <c>isFlipped</c>. A plain <c>NSView</c> would put
/// its origin at the bottom left and mirror every child inside the frame — laid out correctly and
/// drawn in the wrong half, which reads as a layout bug rather than a coordinate one.
/// </para>
/// </remarks>
internal sealed class CocoaGroupBoxPeer : CocoaControlPeer, IGroupBoxPeer
{
    private readonly nint _box;

    public CocoaGroupBoxPeer()
        : base(CocoaCanvasPeer.CreateFlippedView())
    {
        if (this.Handle == 0)
            return;

        var allocated = CocoaRuntime.Allocate("NSBox");
        _box = allocated == 0
            ? 0
            : CocoaRuntime.SendRectInit(allocated, CocoaRuntime.sel_registerName("initWithFrame:"), new(0, 0, 1, 1));

        if (_box != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("addSubview:"), _box);
    }

    /// <inheritdoc/>
    /// <remarks>The frame is stretched over the whole surface, which a plain view will not do for it.</remarks>
    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        if (_box != 0)
            CocoaRuntime.SendRectVoidOnly(_box, CocoaRuntime.sel_registerName("setFrame:"), new(0, 0, bounds.Width, bounds.Height));
    }

    /// <summary>The caption belongs to the box; the view behind it has nothing to say.</summary>
    public override void SetText(string text)
    {
        if (_box == 0)
            return;

        var title = CocoaRuntime.NSString(text);
        if (title == 0)
            return;

        CocoaRuntime.SendVoid(_box, CocoaRuntime.sel_registerName("setTitle:"), title);
        CocoaNative.CFRelease(title);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Neither object is an <c>NSControl</c>, so neither answers <c>setEnabled:</c> — and an
    /// unrecognized selector does not fail quietly here, it ends the process. A group box has no
    /// disabled look on macOS anyway; what a disabled group means is that its children are disabled,
    /// and the core disables each of those itself.
    /// </remarks>
    public override void SetEnabled(bool enabled) { }

    /// <inheritdoc/>
    public void AddChild(IControlPeer child)
    {
        if (this.Handle == 0 || ViewOf(child) is not { } view)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("addSubview:"), view);
    }

    /// <inheritdoc/>
    /// <remarks>Bookkeeping only, and there is none: the child peer's own disposal takes its view out
    /// of this one.</remarks>
    public void RemoveChild(IControlPeer child) { }

    /// <summary>The AppKit object behind a peer, whichever kind of peer it is.</summary>
    private static nint? ViewOf(IControlPeer child)
        => child switch
        {
            CocoaCanvasPeer canvas when canvas.Handle != 0 => canvas.Handle,
            CocoaControlPeer control when control.Handle != 0 => control.Handle,
            _ => null,
        };
}

/// <summary>
/// A hyperlink: an <c>NSTextField</c> subclass carrying an attributed link over the whole caption,
/// which is what AppKit has where Win32 has a <c>SysLink</c> and GTK a <c>GtkLinkButton</c>.
/// </summary>
/// <remarks>
/// <para>
/// There is no hyperlink control on this desktop; there is a convention, and this is it — a selectable,
/// non-editable text field whose string carries <c>NSLinkAttributeName</c>. What that buys over the
/// owner-drawn twin is the platform's own pointing-hand cursor, a link colour that follows the
/// appearance and the accent without the painter being taught either, and a field an accessibility
/// client already understands as text containing a link.
/// </para>
/// <para>
/// The subclass is the whole trick. A field lends its editing to the window's shared field editor and
/// makes itself that editor's delegate, so <c>textView:clickedOnLink:atIndex:</c> arrives at the field
/// — not at anything the field's own <c>setDelegate:</c> was given, because that protocol does not
/// carry the method. So the class is built at run time with the method added to it, the same shape as
/// the canvas's <c>drawRect:</c>.
/// </para>
/// <para>
/// AppKit has no visited state, so the one thing here that is not the platform's own is the visited
/// colour — and it is computed by the toolkit's own rule (the link colour half of the way to the
/// disabled text colour) applied to the platform's own colours, so the promoted link and the painted
/// one reach the same answer by the same arithmetic rather than by coincidence.
/// </para>
/// </remarks>
internal sealed unsafe class CocoaLinkLabelPeer : CocoaControlPeer, ILinkLabelPeer
{
    private static readonly nint _Link = CocoaRuntime.Constant("NSLinkAttributeName");
    private static readonly nint _Underline = CocoaRuntime.Constant("NSUnderlineStyleAttributeName");
    private static readonly nint _Foreground = CocoaRuntime.Constant("NSForegroundColorAttributeName");

    /// <summary>The runtime class, built on first use.</summary>
    private static nint _class;

    /// <summary>Live links by field pointer, so the static callback can find the one that was clicked.</summary>
    private static readonly ConcurrentDictionary<nint, CocoaLinkLabelPeer> _links = new();

    private string _text = string.Empty;
    private bool _visited;

    public CocoaLinkLabelPeer()
        : base(Create())
    {
        if (this.Handle != 0)
            _links[this.Handle] = this;
    }

    /// <inheritdoc/>
    public event EventHandler? LinkActivated;

    private static nint Create()
    {
        EnsureClass();
        if (_class == 0)
            return 0;

        var allocated = CocoaRuntime.SendPointer(_class, CocoaRuntime.sel_registerName("alloc"));
        var field = allocated == 0
            ? 0
            : CocoaRuntime.SendRectInit(allocated, CocoaRuntime.sel_registerName("initWithFrame:"), new(0, 0, 1, 1));

        if (field == 0)
            return 0;

        // Selectable is what makes the link clickable at all; editable would make it a text box.
        CocoaRuntime.SendVoid(field, CocoaRuntime.sel_registerName("setEditable:"), false);
        CocoaRuntime.SendVoid(field, CocoaRuntime.sel_registerName("setSelectable:"), true);
        CocoaRuntime.SendVoid(field, CocoaRuntime.sel_registerName("setBezeled:"), false);
        CocoaRuntime.SendVoid(field, CocoaRuntime.sel_registerName("setDrawsBackground:"), false);
        CocoaRuntime.SendVoid(field, CocoaRuntime.sel_registerName("setAllowsEditingTextAttributes:"), true);
        return field;
    }

    private static void EnsureClass()
    {
        if (_class != 0 || !CocoaRuntime.Available)
            return;

        var superclass = CocoaRuntime.objc_getClass("NSTextField");
        if (superclass == 0)
            return;

        var created = CocoaRuntime.objc_allocateClassPair(superclass, "NativeFormsLinkField", 0);
        if (created == 0)
            return;

        // "c@:@@Q": returns BOOL, takes self, _cmd, the field editor, the link and the character index.
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
        if (!_links.TryGetValue(self, out var label))
            return 0;

        label.LinkActivated?.Invoke(label, EventArgs.Empty);
        return 1; // handled: the application's LinkClicked is the hook, not the platform's browser
    }

    /// <inheritdoc/>
    /// <remarks>The caption and the link are the same thing here, so setting one rebuilds the other.</remarks>
    public override void SetText(string text)
    {
        _text = text;
        this.Restyle();
    }

    /// <inheritdoc/>
    public void SetVisited(bool visited)
    {
        _visited = visited;
        this.Restyle();
    }

    /// <summary>Rebuilds the attributed caption: the link, its underline and the colour of the moment.</summary>
    private void Restyle()
    {
        if (this.Handle == 0 || _Link == 0)
            return;

        var value = CocoaRuntime.NSString(_text);
        if (value == 0)
            return;

        var allocated = CocoaRuntime.Allocate("NSMutableAttributedString");
        var styled = allocated == 0
            ? 0
            : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("initWithString:"), value);

        CocoaNative.CFRelease(value);
        if (styled == 0)
            return;

        var range = new CocoaRuntime.NSRange { Location = 0, Length = _text.Length };
        var add = CocoaRuntime.sel_registerName("addAttribute:value:range:");

        // The link's value is the caption. Nothing reads it — the toolkit reports the activation and
        // the application decides what it meant — but the attribute has to hold something, because an
        // attribute with no value is no attribute and the click would never be a link click.
        var target = CocoaRuntime.NSString(_text);
        if (target != 0)
        {
            CocoaRuntime.SendAttribute(styled, add, _Link, target, range);
            CocoaNative.CFRelease(target);
        }

        if (_Underline != 0
            && CocoaRuntime.SendPointer(CocoaRuntime.objc_getClass("NSNumber"), CocoaRuntime.sel_registerName("numberWithInteger:"), 1) is var single
            && single != 0)
            CocoaRuntime.SendAttribute(styled, add, _Underline, single, range);

        if (_Foreground != 0 && this.Colour() is var colour && colour != 0)
            CocoaRuntime.SendAttribute(styled, add, _Foreground, colour, range);

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setAttributedStringValue:"), styled);
        CocoaRuntime.SendVoid(styled, CocoaRuntime.sel_registerName("release"));
    }

    /// <summary>The link colour, dimmed halfway toward disabled text once the link has been followed.</summary>
    private nint Colour()
    {
        var link = CocoaRuntime.SendToClass("NSColor", "linkColor");
        if (!_visited || link == 0)
            return link;

        var dim = CocoaRuntime.SendToClass("NSColor", "disabledControlTextColor");
        if (dim == 0)
            return link;

        // A blend across two colour spaces answers nil rather than failing, so the unblended colour is
        // the fallback: a visited link that looks unvisited beats a caption with no colour at all.
        var blended = CocoaRuntime.SendPointer(link, CocoaRuntime.sel_registerName("blendedColorWithFraction:ofColor:"), 0.5, dim);
        return blended != 0 ? blended : link;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (this.Handle != 0)
            _links.TryRemove(this.Handle, out _);

        base.Dispose();
    }
}

/// <summary>A progress indicator: a real <c>NSProgressIndicator</c> in its bar style.</summary>
/// <remarks>
/// It is an <c>NSView</c> and not an <c>NSControl</c>, so it answers neither <c>setStringValue:</c> nor
/// <c>setEnabled:</c> — and an unrecognized selector here does not fail quietly, it ends the process.
/// Both are therefore refused rather than inherited: a progress bar has no caption to set, and macOS
/// has no disabled look for one.
/// </remarks>
internal sealed class CocoaProgressBarPeer : CocoaControlPeer, IProgressBarPeer
{
    public CocoaProgressBarPeer()
        : base(Create())
    {
    }

    private static nint Create()
    {
        var allocated = CocoaRuntime.Allocate("NSProgressIndicator");
        var bar = allocated == 0
            ? 0
            : CocoaRuntime.SendRectInit(allocated, CocoaRuntime.sel_registerName("initWithFrame:"), new(0, 0, 1, 1));

        if (bar == 0)
            return 0;

        // NSProgressIndicatorStyleBar, and a fraction rather than the toolkit's own range: the core
        // has already reduced value/minimum/maximum to one number between nothing and everything.
        CocoaRuntime.SendVoid(bar, CocoaRuntime.sel_registerName("setStyle:"), 0);
        CocoaRuntime.SendVoid(bar, CocoaRuntime.sel_registerName("setIndeterminate:"), false);
        CocoaRuntime.SendVoid(bar, CocoaRuntime.sel_registerName("setMinValue:"), 0.0);
        CocoaRuntime.SendVoid(bar, CocoaRuntime.sel_registerName("setMaxValue:"), 1.0);
        return bar;
    }

    /// <inheritdoc/>
    public void SetFraction(double fraction)
    {
        if (this.Handle != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setDoubleValue:"), fraction);
    }

    /// <inheritdoc/>
    public void SetMarquee(bool marquee)
    {
        if (this.Handle == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setIndeterminate:"), marquee);
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName(marquee ? "startAnimation:" : "stopAnimation:"), 0);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing to do: an indeterminate <c>NSProgressIndicator</c> animates itself once started, so a
    /// caller stepping it by hand would only be competing with the platform's own timing.
    /// </remarks>
    public void Pulse() { }

    /// <inheritdoc cref="CocoaProgressBarPeer"/>
    public override void SetText(string text) { }

    /// <inheritdoc cref="CocoaProgressBarPeer"/>
    public override void SetEnabled(bool enabled) { }
}
