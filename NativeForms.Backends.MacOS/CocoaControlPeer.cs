using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// The shared half of every native-widget peer: an <c>NSView</c>-derived control positioned, titled
/// and shown through Objective-C messaging.
/// </summary>
/// <remarks>
/// The AppKit controls all descend from <c>NSView</c> and answer the same handful of messages for
/// frame, hidden state and enablement, so those live here once rather than in each peer. What differs
/// — a label's alignment, a button's action — belongs to the subclass.
/// </remarks>
internal abstract class CocoaControlPeer : IControlPeer
{
    private Rectangle _bounds;

    private protected CocoaControlPeer(nint handle) => this.Handle = handle;

    /// <summary>The underlying AppKit object.</summary>
    internal nint Handle { get; }

    public event EventHandler? GotFocus;
    public event EventHandler? LostFocus;
    public event EventHandler<MouseEventArgs>? PointerMove;
    public event EventHandler? PointerLeave;
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;

    public void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        if (this.Handle != 0)
            CocoaRuntime.SendRectVoidOnly(this.Handle, CocoaRuntime.sel_registerName("setFrame:"), new(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    public virtual void SetText(string text)
    {
        if (this.Handle == 0)
            return;

        var title = CocoaRuntime.NSString(text);
        if (title == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setStringValue:"), title);
        CocoaNative.CFRelease(title);
    }

    public void SetVisible(bool visible)
    {
        if (this.Handle != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setHidden:"), !visible);
    }

    public void SetEnabled(bool enabled)
    {
        if (this.Handle != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setEnabled:"), enabled);
    }

    public Point PointToScreen(Point clientPoint) => new(_bounds.X + clientPoint.X, _bounds.Y + clientPoint.Y);

    public void Focus()
    {
        if (this.Handle != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("becomeFirstResponder"));
    }

    // --- Not yet, and deliberately not fatal (docs/PRD.md §2) ------------------------------------

    public void SetFont(Font font) { }

    public void SetColors(Color foreColor, Color backColor) { }

    public void SetCursor(Cursor cursor) { }

    public void ShowToolTip(string? text) { }

    public virtual void Dispose() { }

    /// <summary>Keeps the events referenced until AppKit's routing feeds them.</summary>
    private protected void Unused()
    {
        GotFocus?.Invoke(this, EventArgs.Empty);
        LostFocus?.Invoke(this, EventArgs.Empty);
        PointerMove?.Invoke(this, new(MouseButtons.None, 0, 0, 0));
        PointerLeave?.Invoke(this, EventArgs.Empty);
        ContextMenuRequested?.Invoke(this, new(Point.Empty));
    }
}

/// <summary>A caption: a non-editable, borderless <c>NSTextField</c>, which is what AppKit calls a label.</summary>
internal sealed class CocoaLabelPeer : CocoaControlPeer, ILabelPeer
{
    public CocoaLabelPeer()
        : base(Create())
    {
    }

    private static nint Create()
    {
        var allocated = CocoaRuntime.Allocate("NSTextField");
        if (allocated == 0)
            return 0;

        var field = CocoaRuntime.SendRectInit(allocated, CocoaRuntime.sel_registerName("initWithFrame:"), new(0, 0, 1, 1));
        if (field == 0)
            return 0;

        // A label is a text field that cannot be typed in and draws no chrome.
        CocoaRuntime.SendVoid(field, CocoaRuntime.sel_registerName("setEditable:"), false);
        CocoaRuntime.SendVoid(field, CocoaRuntime.sel_registerName("setBezeled:"), false);
        CocoaRuntime.SendVoid(field, CocoaRuntime.sel_registerName("setDrawsBackground:"), false);
        return field;
    }

    /// <inheritdoc/>
    public void SetTextAlign(ContentAlignment alignment)
    {
        if (this.Handle == 0)
            return;

        // NSTextAlignment: left 0, right 1, centre 2.
        var value = alignment switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => 2,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => 1,
            _ => 0,
        };

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setAlignment:"), value);
    }

    /// <inheritdoc/>
    /// <remarks>Bezel on or off; the toolkit's three border styles collapse to AppKit's two.</remarks>
    public void SetBorderStyle(BorderStyle borderStyle)
    {
        if (this.Handle != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setBezeled:"), borderStyle != BorderStyle.None);
    }

    /// <inheritdoc/>
    /// <remarks>Not yet: AppKit underlines a mnemonic through an attributed title, which wants the
    /// text-drawing work that has not landed.</remarks>
    public void SetUseMnemonic(bool useMnemonic) { }

    /// <inheritdoc/>
    /// <inheritdoc cref="SetUseMnemonic"/>
    public void SetImage(IImage? image, ContentAlignment alignment) { }
}

/// <summary>A push button: a real <c>NSButton</c>.</summary>
internal sealed class CocoaButtonPeer : CocoaControlPeer, IButtonPeer
{
    public CocoaButtonPeer()
        : base(Create())
    {
    }

    /// <inheritdoc/>
    public event EventHandler? Clicked;

    private static nint Create()
    {
        var allocated = CocoaRuntime.Allocate("NSButton");
        return allocated == 0
            ? 0
            : CocoaRuntime.SendRectInit(allocated, CocoaRuntime.sel_registerName("initWithFrame:"), new(0, 0, 1, 1));
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
    /// <remarks>
    /// The Return key's button carries the key equivalent "\r", which is how AppKit marks the default
    /// one rather than through a style flag.
    /// </remarks>
    public void SetDefault(bool isDefault)
    {
        if (this.Handle == 0)
            return;

        var equivalent = CocoaRuntime.NSString(isDefault ? "\r" : string.Empty);
        if (equivalent == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setKeyEquivalent:"), equivalent);
        CocoaNative.CFRelease(equivalent);
    }

    /// <inheritdoc/>
    /// <remarks>Not yet: an NSButton takes an NSImage, which wants the CGImage work images have not
    /// had, since nothing draws them either.</remarks>
    public void SetImage(IImage? image, ContentAlignment alignment, TextImageRelation relation) { }

    /// <summary>Raises <see cref="Clicked"/> once AppKit's target/action routing is wired.</summary>
    private void Unused2() => Clicked?.Invoke(this, EventArgs.Empty);
}
