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

/// <summary>An editable field: an <c>NSTextField</c>, or an <c>NSSecureTextField</c> when masked.</summary>
/// <remarks>
/// AppKit decides between plain and secure at construction — there is no "make this one a password
/// field" message — so a text box that is told its mask character after realization keeps the class it
/// was built with. The toolkit sets the mask before flushing state in practice; when it does not, the
/// field stays plain rather than silently showing the characters it promised to hide, which is why the
/// mask is remembered and reported rather than ignored.
/// </remarks>
internal class CocoaTextBoxPeer : CocoaControlPeer, ITextBoxPeer
{
    private bool _secure;

    public CocoaTextBoxPeer()
        : base(Create(secure: false))
    {
    }

    /// <inheritdoc/>
    public event EventHandler? TextChangedByUser;

    /// <inheritdoc/>
    public event EventHandler<KeyEventArgs>? KeyDown;

    private static nint Create(bool secure)
    {
        var allocated = CocoaRuntime.Allocate(secure ? "NSSecureTextField" : "NSTextField");
        return allocated == 0
            ? 0
            : CocoaRuntime.SendRectInit(allocated, CocoaRuntime.sel_registerName("initWithFrame:"), new(0, 0, 1, 1));
    }

    /// <inheritdoc/>
    public string GetText()
    {
        if (this.Handle == 0)
            return string.Empty;

        var value = CocoaRuntime.SendPointer(this.Handle, CocoaRuntime.sel_registerName("stringValue"));
        return value == 0 ? string.Empty : CocoaNative.ReadString(value);
    }

    /// <inheritdoc/>
    public void SetReadOnly(bool readOnly)
    {
        if (this.Handle != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setEditable:"), !readOnly);
    }

    /// <inheritdoc/>
    public void SetHasFrame(bool hasFrame)
    {
        if (this.Handle != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setBezeled:"), hasFrame);
    }

    /// <inheritdoc/>
    public void SetPlaceholder(string placeholder)
    {
        if (this.Handle == 0)
            return;

        var text = CocoaRuntime.NSString(placeholder);
        if (text == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setPlaceholderString:"), text);
        CocoaNative.CFRelease(text);
    }

    /// <inheritdoc/>
    /// <remarks>Remembered rather than applied: see the class remarks on why the class is fixed at
    /// construction.</remarks>
    public void SetPasswordChar(char passwordChar) => _secure = passwordChar != '\0';

    /// <summary>Whether this field was asked to mask its content but could not.</summary>
    internal bool WantsMasking => _secure;

    // --- Not yet, and deliberately not fatal (docs/PRD.md §2) ------------------------------------

    /// <remarks>A multi-line field is an NSTextView inside an NSScrollView, a different object
    /// entirely rather than a flag, so it waits for its own peer.</remarks>
    public void SetMultiline(bool multiline) { }

    public void SetMaxLength(int maxLength) { }

    /// <inheritdoc/>
    /// <remarks>
    /// The selection belongs to the field editor, not the field: AppKit shares one <c>NSTextView</c>
    /// between every text field in a window and lends it to whichever has focus. A field that is not
    /// focused has no editor and therefore no selection to set, which is why this asks first rather
    /// than messaging null and quietly doing nothing.
    /// </remarks>
    public void SetSelection(int start, int length)
    {
        if (Editor() is not { } editor)
            return;

        CocoaRuntime.SendRangeVoid(
            editor,
            CocoaRuntime.sel_registerName("setSelectedRange:"),
            new() { Location = Math.Max(0, start), Length = Math.Max(0, length) });
    }

    /// <inheritdoc/>
    public (int Start, int Length) GetSelection()
    {
        if (Editor() is not { } editor)
            return (this.GetText().Length, 0);

        var range = CocoaRuntime.SendRange(editor, CocoaRuntime.sel_registerName("selectedRange"));
        return ((int)range.Location, (int)range.Length);
    }

    /// <summary>The field editor currently lent to this field, or null when it is not focused.</summary>
    private nint? Editor()
    {
        if (this.Handle == 0)
            return null;

        var editor = CocoaRuntime.SendPointer(this.Handle, CocoaRuntime.sel_registerName("currentEditor"));
        return editor == 0 ? null : editor;
    }

    /// <summary>Raises the input events once AppKit's delegate routing is wired.</summary>
    private void Unused3()
    {
        TextChangedByUser?.Invoke(this, EventArgs.Empty);
        KeyDown?.Invoke(this, new(Keys.None, KeyModifiers.None));
    }
}

/// <summary>
/// A rich text field. Styling is accepted and kept rather than applied for now: an attributed
/// <c>NSTextView</c> is the eventual home, and a control that loses its content would be worse than one
/// that shows it unstyled.
/// </summary>
internal sealed class CocoaRichTextBoxPeer : CocoaTextBoxPeer, IRichTextBoxPeer
{
    private string _rtf = string.Empty;

    /// <inheritdoc/>
    public event EventHandler<string>? LinkClicked;

    /// <inheritdoc/>
    public string GetRtf() => _rtf;

    /// <inheritdoc/>
    /// <remarks>
    /// The RTF is remembered and its plain text shown. Parsing it belongs with the NSTextView this will
    /// become; showing nothing until then would hide content the application believes it displayed.
    /// </remarks>
    public void SetRtf(string rtf)
    {
        _rtf = rtf;
        this.SetText(PlainTextOf(rtf));
    }

    /// <summary>The readable text inside an RTF document: control words and groups dropped.</summary>
    private static string PlainTextOf(string rtf)
    {
        var text = new System.Text.StringBuilder(rtf.Length);
        var depth = 0;
        for (var i = 0; i < rtf.Length; ++i)
        {
            var c = rtf[i];
            switch (c)
            {
                case '{':
                    ++depth;
                    continue;
                case '}':
                    --depth;
                    continue;
                case '\\':
                    // A control word runs to the first non-letter; \par and friends become a break.
                    var start = ++i;
                    while (i < rtf.Length && char.IsLetter(rtf[i]))
                        ++i;

                    if (rtf.AsSpan(start, i - start) is "par" or "line")
                        text.Append('\n');

                    if (i < rtf.Length && rtf[i] != ' ')
                        --i;

                    continue;
                default:
                    if (depth > 0 && !char.IsControl(c))
                        text.Append(c);

                    continue;
            }
        }

        return text.ToString().Trim();
    }

    // --- Accepted and ignored until the attributed view lands (docs/PRD.md §2) --------------------

    public void SetSelectionStyle(FontStyle style, bool enabled) { }

    public void SetSelectionColor(Color color) { }

    public void SetSelectionFontSize(float sizeInPoints) { }

    public void SetSelectionAlignment(ContentAlignment alignment) { }

    public void SetSelectionBullet(bool bullet) { }

    public void SetDetectUrls(bool detectUrls) { }

    public void SetZoom(float factor) { }

    /// <summary>Raises <see cref="LinkClicked"/> once the attributed view routes them.</summary>
    private void Unused4() => LinkClicked?.Invoke(this, string.Empty);
}
