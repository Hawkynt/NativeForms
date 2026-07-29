using System.Collections.Concurrent;
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

    /// <summary>
    /// The underlying AppKit object. A peer may swap it — AppKit fixes a control's class at
    /// construction, so a state change the class cannot express is served by building the other object
    /// and replacing this one in its superview.
    /// </summary>
    internal nint Handle { get; private protected set; }

    /// <summary>Where the widget was last put, so a replacement can be given the same frame.</summary>
    private protected Rectangle BoundsValue => _bounds;

    public event EventHandler? GotFocus;
    public event EventHandler? LostFocus;
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;

    private EventHandler<MouseEventArgs>? _pointerMove;
    private EventHandler? _pointerLeave;

    /// <inheritdoc/>
    /// <remarks>
    /// The tracking area that feeds this is installed by the first subscriber rather than by the
    /// constructor. AppKit charges for one whether or not anybody is listening, and the core
    /// subscribes only for a control something watches — which today is a control with a tooltip.
    /// </remarks>
    public event EventHandler<MouseEventArgs>? PointerMove
    {
        add
        {
            _pointerMove += value;
            CocoaPointerTarget.Track(this);
        }

        remove => _pointerMove -= value;
    }

    /// <inheritdoc cref="PointerMove"/>
    public event EventHandler? PointerLeave
    {
        add
        {
            _pointerLeave += value;
            CocoaPointerTarget.Track(this);
        }

        remove => _pointerLeave -= value;
    }

    /// <summary>Reports the pointer at a point of this widget; called from the tracking area's target.</summary>
    internal void RaisePointerMove(int x, int y)
        => _pointerMove?.Invoke(this, new(MouseButtons.None, x, y, 0));

    /// <summary>Reports the pointer leaving this widget — the counterpart of <see cref="RaisePointerMove"/>.</summary>
    internal void RaisePointerLeave() => _pointerLeave?.Invoke(this, EventArgs.Empty);

    public virtual void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        if (this.Handle == 0)
            return;

        var frame = this.FrameFor(bounds);
        CocoaRuntime.SendRectVoidOnly(this.Handle, CocoaRuntime.sel_registerName("setFrame:"), new(frame.X, frame.Y, frame.Width, frame.Height));
    }

    /// <summary>
    /// The frame the widget takes for a rectangle the toolkit gave — the same rectangle, unless the
    /// AppKit control reserves part of it for something the toolkit does not know about.
    /// </summary>
    private protected virtual Rectangle FrameFor(Rectangle bounds) => bounds;

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

    public virtual void SetEnabled(bool enabled)
    {
        if (this.Handle != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setEnabled:"), enabled);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Asked of AppKit rather than computed, for the reason
    /// <see cref="CocoaRuntime.TryScreenPoint"/> gives. The frame arithmetic is kept only for a widget
    /// that is not in a window yet: the core maps points on controls it has realized but not yet
    /// shown, and answering nothing there would be worse than answering the parent's space.
    /// </remarks>
    public Point PointToScreen(Point clientPoint)
        => CocoaRuntime.TryScreenPoint(this.Handle, clientPoint, out var screen)
            ? screen
            : new(_bounds.X + clientPoint.X, _bounds.Y + clientPoint.Y);

    public virtual void Focus()
    {
        if (this.Handle != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("becomeFirstResponder"));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A real AppKit control already answers most of this for itself, so what lands here refines what
    /// the platform knows rather than inventing it.
    /// </remarks>
    public void SetAccessibleInfo(string? name, string? description, AccessibleRole role)
        => CocoaAccessibility.Describe(this.Handle, name, description, role);

    /// <inheritdoc/>
    /// <remarks>
    /// Offered rather than sent: <c>setFont:</c> is <c>NSControl</c>'s, and a peer whose handle is a
    /// plain <c>NSView</c> would abort the process on an unrecognized selector instead of ignoring it.
    /// </remarks>
    public virtual void SetFont(Font font)
    {
        if (!CocoaRuntime.Responds(this.Handle, "setFont:"))
            return;

        var native = CocoaRuntime.NSFontOf(font);
        if (native != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setFont:"), native);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// An empty colour means the core never had one set anywhere up the chain, so the widget keeps
    /// whatever the desktop gave it — the same reading as the other two backends.
    /// </para>
    /// <para>
    /// A background is turned on as well as set. AppKit's text-bearing widgets carry a colour they do
    /// not draw — a label is built with <c>drawsBackground</c> off precisely so it sits on whatever is
    /// behind it — and a colour that is stored and not painted is not an answer to what was asked.
    /// </para>
    /// <para>
    /// A button's title colour is not served. <c>NSButton</c> has no <c>setTextColor:</c>; its caption
    /// takes its colour from the button's own style, and the only way past that is an attributed title,
    /// which then owns the font and the mnemonic as well. Doing half of it would leave a button whose
    /// caption follows one property and ignores the next.
    /// </para>
    /// </remarks>
    public virtual void SetColors(Color foreColor, Color backColor)
    {
        if (this.Handle == 0)
            return;

        if (CocoaRuntime.NSColorOf(foreColor) is var text and not 0 && CocoaRuntime.Responds(this.Handle, "setTextColor:"))
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setTextColor:"), text);

        if (CocoaRuntime.NSColorOf(backColor) is not (var back and not 0) || !CocoaRuntime.Responds(this.Handle, "setBackgroundColor:"))
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setBackgroundColor:"), back);
        if (CocoaRuntime.Responds(this.Handle, "setDrawsBackground:"))
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setDrawsBackground:"), true);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The other of the two routes AppKit offers (see <see cref="CocoaCursor"/>). A widget whose class
    /// this backend did not build cannot be given a <c>resetCursorRects</c> of its own, so it is
    /// watched by a tracking area asking for cursor updates instead — installed on the first shape an
    /// application actually asks for, because an AppKit control already carries the right one and a
    /// rectangle laid over that unasked would take the platform's own answer away.
    /// </remarks>
    public void SetCursor(Cursor cursor)
    {
        if (this.Handle == 0)
            return;

        CocoaCursor.Track(this.Handle);
        CocoaCursor.Apply(this.Handle, cursor);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The platform's own tip, which is <c>NSView</c>'s <c>toolTip</c> — there is nothing else here, and
    /// no message that raises one now. So this hands the text over and AppKit decides when to draw it,
    /// on its own hover timing, which is a real difference from the other two backends: GTK is asked to
    /// re-run its tooltip query at once, and the Win32 tool is registered with <c>TTF_SUBCLASS</c> and
    /// activated. Here the toolkit's own delay elapses first and AppKit's runs after it, so the tip
    /// arrives late on the hover that asked for it and promptly on every one after. Clearing is
    /// <c>setToolTip:nil</c>, which is what the seam means by an empty text.
    /// </remarks>
    public void ShowToolTip(string? text) => CocoaToolTip.Apply(this.Handle, text);

    public virtual void Dispose()
    {
        CocoaCursor.Forget(this.Handle);
        CocoaPointerTarget.Forget(this.Handle);
    }

    /// <summary>Keeps the events referenced until AppKit's routing feeds them.</summary>
    private protected void Unused()
    {
        GotFocus?.Invoke(this, EventArgs.Empty);
        LostFocus?.Invoke(this, EventArgs.Empty);
        ContextMenuRequested?.Invoke(this, new(Point.Empty));
    }
}

/// <summary>A caption: a non-editable, borderless <c>NSTextField</c>, which is what AppKit calls a label.</summary>
/// <remarks>
/// <para>
/// A picture is the other two backends' answer rather than a new one. Neither draws an icon beside a
/// caption: GTK swaps its <c>GtkLabel</c> for a <c>GtkImage</c> when the label has an image and no
/// text, Win32 builds an <c>SS_BITMAP</c> static on the same condition, and a captioned label keeps
/// its text and does not render the picture at all. So this swaps its field for an
/// <c>NSImageView</c> on the same condition and by the same rule, which makes an image-only label mean
/// one thing on all three instead of three.
/// </para>
/// <para>
/// A mnemonic is an attributed value, and the reason this was put off is that an attributed value
/// owns everything the cell would otherwise have drawn with. So the font, the text colour and the
/// alignment go back into the string rather than being left to properties it overrides, and
/// <see cref="SetFont"/> and <see cref="SetColors"/> rebuild it — otherwise a caption would follow one
/// property and ignore the next, which is worse than not underlining anything.
/// </para>
/// </remarks>
internal sealed class CocoaLabelPeer : CocoaControlPeer, ILabelPeer
{
    private static readonly nint _FontKey = CocoaRuntime.Constant("NSFontAttributeName");
    private static readonly nint _Foreground = CocoaRuntime.Constant("NSForegroundColorAttributeName");
    private static readonly nint _Underline = CocoaRuntime.Constant("NSUnderlineStyleAttributeName");
    private static readonly nint _Paragraph = CocoaRuntime.Constant("NSParagraphStyleAttributeName");

    /// <summary>
    /// What an <c>NSTextFieldCell</c> keeps back at each end of its frame before it lays the caption
    /// out — two points, on a field with no bezel and no border.
    /// </summary>
    private const int _TitleInset = 2;

    private string _text = string.Empty;
    private bool _useMnemonic = true;
    private ContentAlignment _textAlign;
    private BorderStyle _borderStyle;
    private CocoaImage? _image;

    /// <summary>Whether the widget behind this peer is the image view rather than the field.</summary>
    private bool _isImageView;

    /// <summary>
    /// The bitmap the image view is currently showing, so a caption property that rebuilds everything
    /// does not also convert an icon it did not touch.
    /// </summary>
    private CocoaImage? _pushed;

    public CocoaLabelPeer()
        : base(CreateField())
    {
    }

    private static nint CreateField()
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

    /// <summary>The other widget this peer can be: what AppKit has where GTK has a <c>GtkImage</c>.</summary>
    private static nint CreateImageView()
    {
        var allocated = CocoaRuntime.Allocate("NSImageView");
        var view = allocated == 0
            ? 0
            : CocoaRuntime.SendRectInit(allocated, CocoaRuntime.sel_registerName("initWithFrame:"), new(0, 0, 1, 1));

        // NSImageScaleNone: the bitmap is drawn at the size it was handed over, which is what a
        // GtkImage does and what an SS_BITMAP static does. Scaling it to the label's bounds would
        // make the same icon a different size on one platform of three.
        if (view != 0)
            CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("setImageScaling:"), 2);

        return view;
    }

    /// <summary>Whether the peer should be showing the bitmap instead of a caption.</summary>
    private bool IsImageOnly => _image is not null && _text.Length == 0;

    /// <summary>
    /// The field's frame, widened by the inset its cell will take back, so the caption occupies exactly
    /// the rectangle the toolkit asked for.
    /// </summary>
    /// <remarks>
    /// An auto-sized label is given its text's own width, measured with the same font AppKit draws it
    /// in — and the cell then lays that text out inside a rectangle two points narrower at each end, so
    /// the last word never fitted and was dropped. "AutoSize measures this label." photographed as
    /// "AutoSize measures this" on every macOS run while both other backends showed it whole, because
    /// neither a <c>GtkLabel</c> nor an <c>SS_LEFT</c> static holds anything back. Widening the frame
    /// rather than padding the measurement keeps the number the toolkit measured the number every
    /// owner-drawn control lays out with. A bezelled field keeps its frame: there the inset is the
    /// border, and moving it would draw the box somewhere the toolkit did not put it.
    /// </remarks>
    private protected override Rectangle FrameFor(Rectangle bounds)
        => _isImageView || _borderStyle != BorderStyle.None
            ? bounds
            : new(bounds.X - _TitleInset, bounds.Y, bounds.Width + (2 * _TitleInset), bounds.Height);

    /// <inheritdoc/>
    public override void SetText(string text)
    {
        _text = text;
        this.Rebuild();
    }

    /// <inheritdoc/>
    public void SetTextAlign(ContentAlignment alignment)
    {
        _textAlign = alignment;
        this.Apply();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Bezel on or off; the toolkit's three border styles collapse to AppKit's two. The frame goes back
    /// as well, because which of the two the field is decides whether it is widened (see
    /// <see cref="FrameFor"/>).
    /// </remarks>
    public void SetBorderStyle(BorderStyle borderStyle)
    {
        if (_borderStyle == borderStyle)
            return;

        _borderStyle = borderStyle;
        this.SetBounds(this.BoundsValue);
        this.Apply();
    }

    /// <inheritdoc/>
    public void SetUseMnemonic(bool useMnemonic)
    {
        if (_useMnemonic == useMnemonic)
            return;

        _useMnemonic = useMnemonic;
        this.Apply();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The alignment is carried and not rendered, which is what the seam already says of the other
    /// two: an image-only label fills its bounds with one picture and there is nothing to align it
    /// against.
    /// </remarks>
    public void SetImage(IImage? image, ContentAlignment alignment)
    {
        var native = image as CocoaImage;
        if (ReferenceEquals(_image, native))
            return;

        _image = native;
        this.Rebuild();
    }

    /// <inheritdoc/>
    /// <remarks>Both rebuild the caption, because an attributed one carries its own copy of each.</remarks>
    public override void SetFont(Font font)
    {
        base.SetFont(font);
        this.Apply();
    }

    /// <inheritdoc cref="SetFont"/>
    public override void SetColors(Color foreColor, Color backColor)
    {
        base.SetColors(foreColor, backColor);
        this.Apply();
    }

    /// <summary>
    /// Puts the right kind of widget in place for the state the peer is in, and then applies it.
    /// </summary>
    /// <remarks>
    /// The swap is the same one <see cref="CocoaTextBoxPeer"/> makes and for the same reason: AppKit
    /// fixes a widget's class when the object is made. In practice it happens during realization,
    /// before the peer has been handed to its container — the core flushes a peer's own state first —
    /// so there is usually no superview to tell, and the branch that tells one is for the label that
    /// gains or loses its picture while it is on screen.
    /// </remarks>
    private void Rebuild()
    {
        var wanted = this.IsImageOnly;
        if (wanted == _isImageView)
        {
            this.Apply();
            return;
        }

        var replacement = wanted ? CreateImageView() : CreateField();
        if (replacement == 0)
        {
            this.Apply(); // keep the widget there is rather than trade a working one for nothing
            return;
        }

        var replaced = this.Handle;
        var superview = replaced == 0
            ? 0
            : CocoaRuntime.SendPointer(replaced, CocoaRuntime.sel_registerName("superview"));

        this.Handle = replacement;
        _isImageView = wanted;
        _pushed = null; // a fresh widget shows nothing until it is given something

        if (superview != 0)
            CocoaRuntime.SendVoid(superview, CocoaRuntime.sel_registerName("replaceSubview:with:"), replaced, replacement);

        this.SetBounds(this.BoundsValue);
        this.Apply();
    }

    /// <summary>Pushes everything the current widget renders from.</summary>
    private void Apply()
    {
        if (this.Handle == 0)
            return;

        if (_isImageView)
        {
            this.PushImage();
            return;
        }

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setBezeled:"), _borderStyle != BorderStyle.None);
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setAlignment:"), this.Alignment);

        var (caption, mnemonic) = _useMnemonic ? WithoutMnemonics(_text) : (_text, -1);
        if (mnemonic < 0 || !this.SetUnderlined(caption, mnemonic))
            base.SetText(caption);
    }

    private nint Alignment => CocoaRuntime.TextAlignment(_textAlign);

    /// <summary>Hands the bitmap to the image view, or takes the one it has away.</summary>
    /// <remarks>
    /// The <c>NSImage</c> is handed over and released, exactly as a button's is: the view retains it,
    /// so keeping a reference here would only be a second one to account for — and an animated image
    /// arrives once per frame, where the frame before has to go away rather than pile up.
    /// </remarks>
    private void PushImage()
    {
        if (_image is not { } bitmap)
        {
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setImage:"), 0);
            _pushed = null;
            return;
        }

        if (ReferenceEquals(_pushed, bitmap))
            return;

        var native = CocoaImage.CreateNSImage(bitmap.Width, bitmap.Height, bitmap.Pixels);
        if (native == 0)
            return;

        _pushed = bitmap;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setImage:"), native);
        CocoaRuntime.SendVoid(native, CocoaRuntime.sel_registerName("release"));
    }

    /// <summary>
    /// Sets the caption as an attributed string with one character underlined, answering whether it
    /// could be built.
    /// </summary>
    /// <remarks>
    /// The font, the colour and the alignment are read off the widget and written back into the
    /// string. An attributed value is what the cell draws with, in full — a property it does not carry
    /// an attribute for is a property that stops working the moment a caption has a mnemonic in it,
    /// which is the trap this was declined over rather than a reason to keep declining.
    /// </remarks>
    private bool SetUnderlined(string caption, int mnemonic)
    {
        if (_Underline == 0)
            return false;

        var value = CocoaRuntime.NSString(caption);
        if (value == 0)
            return false;

        var allocated = CocoaRuntime.Allocate("NSMutableAttributedString");
        var styled = allocated == 0
            ? 0
            : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("initWithString:"), value);

        CocoaNative.CFRelease(value);
        if (styled == 0)
            return false;

        var add = CocoaRuntime.sel_registerName("addAttribute:value:range:");
        var whole = new CocoaRuntime.NSRange { Location = 0, Length = caption.Length };

        if (_FontKey != 0 && CocoaRuntime.SendPointer(this.Handle, CocoaRuntime.sel_registerName("font")) is var font && font != 0)
            CocoaRuntime.SendAttribute(styled, add, _FontKey, font, whole);

        if (_Foreground != 0 && CocoaRuntime.SendPointer(this.Handle, CocoaRuntime.sel_registerName("textColor")) is var colour && colour != 0)
            CocoaRuntime.SendAttribute(styled, add, _Foreground, colour, whole);

        if (_Paragraph != 0 && this.ParagraphStyle() is var paragraph && paragraph != 0)
        {
            CocoaRuntime.SendAttribute(styled, add, _Paragraph, paragraph, whole);
            CocoaRuntime.SendVoid(paragraph, CocoaRuntime.sel_registerName("release"));
        }

        // NSUnderlineStyleSingle, boxed: an attribute's value is an object and 1 is not one.
        var single = CocoaRuntime.SendPointer(
            CocoaRuntime.objc_getClass("NSNumber"),
            CocoaRuntime.sel_registerName("numberWithInteger:"),
            1);

        if (single != 0)
            CocoaRuntime.SendAttribute(styled, add, _Underline, single, new() { Location = mnemonic, Length = 1 });

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setAttributedStringValue:"), styled);
        CocoaRuntime.SendVoid(styled, CocoaRuntime.sel_registerName("release"));
        return true;
    }

    /// <summary>The alignment as a paragraph style, since an attributed value carries its own.</summary>
    private nint ParagraphStyle()
    {
        var allocated = CocoaRuntime.Allocate("NSMutableParagraphStyle");
        var style = allocated == 0 ? 0 : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("init"));
        if (style != 0)
            CocoaRuntime.SendVoid(style, CocoaRuntime.sel_registerName("setAlignment:"), this.Alignment);

        return style;
    }

    /// <summary>
    /// The caption as it is drawn, and where the mnemonic landed in it — or -1 for a caption that
    /// marks none.
    /// </summary>
    /// <remarks>
    /// Windows Forms' own reading, which the other two backends translate rather than interpret:
    /// <c>&amp;x</c> underlines <c>x</c>, <c>&amp;&amp;</c> is one literal ampersand, and only the
    /// first mark counts. A trailing ampersand marks nothing and is dropped, because there is no
    /// character after it to underline.
    /// </remarks>
    private static (string Caption, int Mnemonic) WithoutMnemonics(string text)
    {
        if (text.IndexOf('&') < 0)
            return (text, -1);

        var caption = new System.Text.StringBuilder(text.Length);
        var mnemonic = -1;
        for (var i = 0; i < text.Length; ++i)
        {
            if (text[i] != '&')
            {
                caption.Append(text[i]);
                continue;
            }

            if (i + 1 >= text.Length)
                continue;

            if (text[i + 1] == '&')
            {
                caption.Append('&');
                ++i;
                continue;
            }

            if (mnemonic < 0)
                mnemonic = caption.Length;

            caption.Append(text[++i]);
        }

        return (caption.ToString(), mnemonic);
    }
}

/// <summary>A push button: a real <c>NSButton</c>.</summary>
/// <remarks>
/// The press comes back through AppKit's target/action, which is the only route an <c>NSControl</c>
/// has: there is no click signal to connect and no callback to register, so an object has to be the
/// target and a selector has to be the action. Both are <see cref="CocoaAction"/>'s, exactly as the
/// promoted check box, radio button and tray item already use them — one target per peer, so a button
/// cannot report for another one.
/// </remarks>
internal sealed class CocoaButtonPeer : CocoaControlPeer, IButtonPeer
{
    private readonly nint _target;

    public CocoaButtonPeer()
        : base(Create())
    {
        if (this.Handle == 0)
            return;

        _target = CocoaAction.Create(this.OnClicked);
        if (_target == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setTarget:"), _target);
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setAction:"), CocoaAction.Selector);
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

    /// <summary>The bitmap last put on the face, so an unchanged one is not converted again.</summary>
    private IImage? _image;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Set on the widget rather than declined. Neither of the other two backends demotes an
    /// image-bearing button — GTK hands the icon to <c>gtk_button_set_image</c> and places it with the
    /// button's own image position, Win32 attaches it with <c>BM_SETIMAGE</c> — so a Cocoa button
    /// showing one is what agrees with them, and refusing the promotion would have been the odd one
    /// out rather than the careful one.
    /// </para>
    /// <para>
    /// <c>NSCellImagePosition</c> has exactly the four places GTK's has, so the relation maps across
    /// one for one. Overlay takes the left-hand place, which is what GTK does with it: AppKit does
    /// have an overlapping position, but a caption printed over an icon on one platform of three is a
    /// difference an application cannot design around.
    /// </para>
    /// <para>
    /// The alignment is advisory here as it is there. A button places its image relative to its
    /// caption and there is no second anchor to give it, so the nine-way value is carried and not
    /// rendered — the same thing the seam already says of the other two.
    /// </para>
    /// <para>
    /// The <c>NSImage</c> is handed over and released: the button retains it, so keeping a reference
    /// here would only be a second one to account for. That matters most for an animated image, which
    /// arrives once per frame — the previous image goes away when the button lets go of it rather than
    /// piling up.
    /// </para>
    /// </remarks>
    public void SetImage(IImage? image, ContentAlignment alignment, TextImageRelation relation)
    {
        if (this.Handle == 0 || ReferenceEquals(_image, image))
            return;

        _image = image;
        if (image is not CocoaImage bitmap)
        {
            // NSNoImage, so the caption reclaims the room the icon was holding.
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setImage:"), 0);
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setImagePosition:"), 0);
            return;
        }

        var native = CocoaImage.CreateNSImage(bitmap.Width, bitmap.Height, bitmap.Pixels);
        if (native == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setImage:"), native);
        CocoaRuntime.SendVoid(native, CocoaRuntime.sel_registerName("release"));

        // NSCellImagePosition: left 2, right 3, below 4, above 5.
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setImagePosition:"), relation switch
        {
            TextImageRelation.TextBeforeImage => 3,
            TextImageRelation.ImageAboveText => 5,
            TextImageRelation.TextAboveImage => 4,
            _ => 2,
        });
    }

    /// <summary>The widget reporting that the user pressed it.</summary>
    /// <remarks>
    /// A key equivalent counts as a press here, which is what makes <see cref="SetDefault"/> work: the
    /// Return key sends the same action to the same target as the pointer does.
    /// </remarks>
    private void OnClicked() => Clicked?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc/>
    public override void Dispose()
    {
        CocoaAction.Forget(_target);
        base.Dispose();
    }
}

/// <summary>
/// An editable field: an <c>NSTextField</c> (an <c>NSSecureTextField</c> when masked) on one line, or
/// an <c>NSTextView</c> inside an <c>NSScrollView</c> once it is told it is multiline.
/// </summary>
/// <remarks>
/// <para>
/// AppKit fixes the class at construction — there is no "make this one a password field" message and
/// no "make this one multiline" one either — so a state change the class cannot express is served by
/// building the other object and swapping it into the superview. That is the recreation
/// <see cref="ITextBoxPeer.SetMultiline"/> and <see cref="ITextBoxPeer.SetPasswordChar"/> allow: the
/// text and the frame move across, the parent's child order is preserved, and the core never learns
/// that the widget it holds is a different one.
/// </para>
/// <para>
/// One box cannot be both, and the multiline one wins: AppKit's secure editing lives in the field
/// rather than in the text view, so a masked multiline box is not a thing there is a class for. The
/// wish is kept rather than dropped, and applied if the box ever goes back to a single line.
/// </para>
/// <para>
/// The two things the user does to a box arrive by two different routes, because AppKit offers no
/// third. An edit comes through the delegate, which both halves have a change message for. A key does
/// not: a field lends its editing to the window's shared field editor, so the keystroke is delivered
/// to an <c>NSTextView</c> this backend did not build and cannot add a method to — the same fact that
/// made the link label a subclass rather than a delegate. What stands in for it is the loop:
/// <see cref="CocoaBackend.Run"/> already pulls every event before AppKit dispatches it, which is one
/// step earlier than any widget hears anything, so the key is offered to the box that has the keyboard
/// there and only reaches the editor if nothing consumed it.
/// </para>
/// </remarks>
internal class CocoaTextBoxPeer : CocoaControlPeer, ITextBoxPeer
{
    /// <summary>
    /// The boxes currently editing, by the object AppKit will make first responder for each — the text
    /// view of a multiline box, the field of a single-line one.
    /// </summary>
    private static readonly ConcurrentDictionary<nint, CocoaTextBoxPeer> _boxes = new();

    private bool _secure;

    /// <summary>Whether the peer is reporting a change — see <see cref="GetSelection"/>.</summary>
    private bool _inChange;

    /// <summary>The text the core last wrote, which during a change is the content before the edit.</summary>
    private string _pushed = string.Empty;

    /// <summary>
    /// The editing view when this is multiline, otherwise zero. A field has none of its own: AppKit
    /// lends every text field in a window the same shared field editor while it has focus.
    /// </summary>
    private nint _textView;

    /// <summary>The object AppKit sends the text view's delegate messages to, or zero.</summary>
    private nint _editorDelegate;

    /// <summary>The formatter holding a single-line box to its length, or zero.</summary>
    private nint _formatter;

    // Remembered because a swap has to put them back, and nothing re-flushes them from the core.
    private int _maxLength;
    private bool _readOnly;
    private bool _hasFrame = true;
    private bool _enabled = true;
    private string _placeholder = string.Empty;

    public CocoaTextBoxPeer()
        : base(Create(secure: false))
    {
        this.Bind();
        this.AttachEditorDelegate();
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

    /// <summary>Whether this box edits through a text view of its own rather than a shared field editor.</summary>
    private protected bool IsMultiline => _textView != 0;

    /// <summary>The text view behind a multiline box, or zero.</summary>
    private protected nint TextView => _textView;

    /// <inheritdoc/>
    public string GetText()
    {
        if (_textView != 0)
        {
            var content = CocoaRuntime.SendPointer(_textView, CocoaRuntime.sel_registerName("string"));
            return content == 0 ? string.Empty : CocoaNative.ReadString(content);
        }

        if (this.Handle == 0)
            return string.Empty;

        var value = CocoaRuntime.SendPointer(this.Handle, CocoaRuntime.sel_registerName("stringValue"));
        return value == 0 ? string.Empty : CocoaNative.ReadString(value);
    }

    /// <inheritdoc/>
    /// <remarks>A text view holds a string, not a "string value"; the base class speaks to controls.</remarks>
    public override void SetText(string text)
    {
        _pushed = text;
        if (_textView == 0)
        {
            base.SetText(text);
            return;
        }

        var value = CocoaRuntime.NSString(text);
        if (value == 0)
            return;

        CocoaRuntime.SendVoid(_textView, CocoaRuntime.sel_registerName("setString:"), value);
        CocoaNative.CFRelease(value);
    }

    /// <inheritdoc/>
    public void SetReadOnly(bool readOnly)
    {
        _readOnly = readOnly;
        var target = _textView != 0 ? _textView : this.Handle;
        if (target != 0)
            CocoaRuntime.SendVoid(target, CocoaRuntime.sel_registerName("setEditable:"), !readOnly && _enabled);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A scroll view frames itself with a border type rather than a bezel, so the same wish reaches two
    /// different messages. Sending the field's to the scroll view would not be ignored — it would be an
    /// unrecognized selector, which ends the process.
    /// </remarks>
    public void SetHasFrame(bool hasFrame)
    {
        _hasFrame = hasFrame;
        if (this.Handle == 0)
            return;

        if (_textView != 0)
            // NSBorderType: none 0, bezel 2.
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setBorderType:"), hasFrame ? 2 : 0);
        else
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setBezeled:"), hasFrame);
    }

    /// <inheritdoc/>
    /// <remarks>Single-line only: a text view has no placeholder, and AppKit offers none for one.</remarks>
    public void SetPlaceholder(string placeholder)
    {
        _placeholder = placeholder;
        if (this.Handle == 0 || _textView != 0)
            return;

        var text = CocoaRuntime.NSString(placeholder);
        if (text == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setPlaceholderString:"), text);
        CocoaNative.CFRelease(text);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Served the same way multiline is, and it has to be: AppKit has no "start masking" message —
    /// <c>NSSecureTextField</c> is a different class, chosen when the object is made. So the other
    /// object is built and swapped into the superview, which is what turns "remembered and reported"
    /// into a box that actually hides what it was told to hide. A box that showed the characters it
    /// promised to mask is not a missing feature, it is the wrong one.
    /// </remarks>
    public void SetPasswordChar(char passwordChar)
    {
        var secure = passwordChar != '\0';
        if (secure == _secure)
            return;

        _secure = secure;

        // A multiline box has no secure form to swap to — AppKit's secure editing lives in the field,
        // not in the text view — so the wish is kept for whenever the box goes back to one line.
        if (_textView != 0)
            return;

        this.Replace(Create(secure), 0);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A scroll view is a plain view and answers no <c>setEnabled:</c>, so a multiline box takes its
    /// enablement where the editing happens: a disabled editor is one that cannot be typed in and
    /// cannot be selected out of.
    /// </remarks>
    public override void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (_textView == 0)
        {
            base.SetEnabled(enabled);
            return;
        }

        CocoaRuntime.SendVoid(_textView, CocoaRuntime.sel_registerName("setEditable:"), enabled && !_readOnly);
        CocoaRuntime.SendVoid(_textView, CocoaRuntime.sel_registerName("setSelectable:"), enabled);
    }

    /// <inheritdoc/>
    /// <remarks>The scroll view is scenery; the keyboard belongs to the view inside it.</remarks>
    public override void Focus()
    {
        if (_textView == 0)
        {
            base.Focus();
            return;
        }

        var window = CocoaRuntime.SendPointer(_textView, CocoaRuntime.sel_registerName("window"));
        if (window != 0)
            CocoaRuntime.SendVoid(window, CocoaRuntime.sel_registerName("makeFirstResponder:"), _textView);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The swap, and the reason the class remarks exist. It happens before the parent has been given
    /// the view during realization — the core flushes a peer's own state and only then hands it to its
    /// container — and after it when the property is set on a live control, which is what the
    /// <c>replaceSubview:with:</c> is for.
    /// </remarks>
    public void SetMultiline(bool multiline)
    {
        if (multiline == (_textView != 0))
            return;

        var (host, editor) = multiline ? CreateScrolled(this.BoundsValue) : (Create(_secure), 0);
        this.Replace(host, editor);
    }

    /// <summary>
    /// Puts a freshly built object where the current one stands, carrying across everything the core
    /// will not send again.
    /// </summary>
    /// <remarks>
    /// The core flushes a peer's buffered state once and then hands the peer to its container, so a
    /// widget built after that point is never told any of it a second time — which is why the text,
    /// the frame and every remembered flag are re-applied here rather than left to the caller.
    /// </remarks>
    private void Replace(nint host, nint editor)
    {
        if (host == 0)
            return; // keep the widget there is rather than trade a working one for nothing

        var carried = this.GetText();
        var replaced = this.Handle;
        this.Unbind();
        _textView = editor;
        this.Handle = host;
        this.Bind();

        var superview = replaced == 0 ? 0 : CocoaRuntime.SendPointer(replaced, CocoaRuntime.sel_registerName("superview"));
        if (superview != 0)
            CocoaRuntime.SendVoid(superview, CocoaRuntime.sel_registerName("replaceSubview:with:"), replaced, host);

        this.SetBounds(this.BoundsValue);
        this.SetText(carried);
        this.SetEnabled(_enabled);
        this.SetReadOnly(_readOnly);
        this.SetHasFrame(_hasFrame);
        this.SetPlaceholder(_placeholder);
        this.OnEditorChanged();
    }

    /// <summary>
    /// Called after a swap has settled, so whatever was hung off the editing view is re-attached —
    /// it is a different object than it was a moment ago.
    /// </summary>
    private protected virtual void OnEditorChanged()
    {
        this.AttachEditorDelegate();
        this.ApplyMaxLength();
    }

    /// <summary>Attaches the delegate and points its change notification at this peer.</summary>
    /// <remarks>
    /// Not virtual, because the constructor calls it: a box reports the user's edits from the moment it
    /// exists rather than from the first time something else happens to need a delegate.
    /// </remarks>
    private void AttachEditorDelegate()
        => CocoaTextViewDelegate.ReportChanges(this.EnsureEditorDelegate(), this.OnTextChanged);

    /// <summary>The widget reporting that the user changed the text.</summary>
    /// <remarks>
    /// The flag is what <see cref="GetSelection"/> reads: AppKit reports the change once the editor has
    /// already advanced its caret past what was inserted, and the seam promises the caret as it stood
    /// before the edit.
    /// </remarks>
    private void OnTextChanged()
    {
        _inChange = true;
        try
        {
            TextChangedByUser?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _inChange = false;
        }
    }

    /// <summary>The editor's delegate, built and attached on the first thing that needs one.</summary>
    /// <remarks>
    /// One object for every job AppKit routes through a delegate, because an editor has one: a second
    /// one attached would silently unhook the first. Which object it goes on is the box's current half
    /// — a multiline box's own text view, or the field, which forwards what the shared field editor
    /// tells it.
    /// </remarks>
    private protected nint EnsureEditorDelegate()
    {
        var editor = _textView != 0 ? _textView : this.Handle;
        if (editor == 0)
            return 0;

        if (_editorDelegate == 0)
            _editorDelegate = CocoaTextViewDelegate.Create();

        if (_editorDelegate != 0)
            CocoaRuntime.SendVoid(editor, CocoaRuntime.sel_registerName("setDelegate:"), _editorDelegate);

        return _editorDelegate;
    }

    /// <summary>Makes this box findable by whatever AppKit will make first responder for it.</summary>
    private void Bind()
    {
        if (_textView != 0)
            _boxes[_textView] = this;
        else if (this.Handle != 0)
            _boxes[this.Handle] = this;
    }

    /// <summary>The counterpart of <see cref="Bind"/>, so a swapped-out object stops answering.</summary>
    private void Unbind()
    {
        if (_textView != 0)
            _boxes.TryRemove(_textView, out _);

        if (this.Handle != 0)
            _boxes.TryRemove(this.Handle, out _);
    }

    /// <summary>
    /// Offers a key to the box that currently has the keyboard, before AppKit dispatches it, and
    /// answers whether the toolkit consumed it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The seam says a key reaches the box <em>before</em> the native editor acts on it and that a
    /// handled one never gets there — which on this platform can only be done here. There is no class
    /// to override: a text field is edited by the window's shared field editor, an object AppKit owns,
    /// and a delegate hears about commands (<c>insertNewline:</c>, <c>insertTab:</c>) rather than about
    /// keys. The loop stands one step earlier than either, so a consumed key is simply one that is
    /// never sent on.
    /// </para>
    /// <para>
    /// The first responder is the editing object itself for a multiline box and the borrowed field
    /// editor for a single-line one — and a field editor carries the field it is lent to as its
    /// delegate, which is the same fact the link label relies on. So the responder is looked up
    /// directly first and through its delegate second.
    /// </para>
    /// </remarks>
    internal static bool InterceptKey(nint theEvent)
    {
        // NSEventTypeKeyDown.
        if (theEvent == 0 || _boxes.IsEmpty
            || (int)CocoaRuntime.SendInteger(theEvent, CocoaRuntime.sel_registerName("type")) != 10)
            return false;

        var window = CocoaRuntime.SendPointer(theEvent, CocoaRuntime.sel_registerName("window"));
        var responder = window == 0
            ? 0
            : CocoaRuntime.SendPointer(window, CocoaRuntime.sel_registerName("firstResponder"));

        if (responder == 0)
            return false;

        if (!_boxes.TryGetValue(responder, out var box))
        {
            var lender = CocoaRuntime.Responds(responder, "delegate")
                ? CocoaRuntime.SendPointer(responder, CocoaRuntime.sel_registerName("delegate"))
                : 0;

            if (lender == 0 || !_boxes.TryGetValue(lender, out box))
                return false;
        }

        if (box.KeyDown is not { } handler)
            return false;

        var args = new KeyEventArgs(CocoaCanvasPeer.KeyOf(theEvent), CocoaCanvasPeer.ModifiersOf(theEvent));
        handler(box, args);
        return args.Handled;
    }

    /// <summary>
    /// Builds the multiline editor: a text view inside a scroll view, which is the pair AppKit uses for
    /// every editor taller than a line.
    /// </summary>
    /// <remarks>
    /// The text grows downward without bound and never sideways, and the container tracks the view's
    /// width — which is what makes a resize re-wrap the text instead of scrolling it out of sight.
    /// </remarks>
    private static (nint Host, nint Editor) CreateScrolled(Rectangle bounds)
    {
        var width = Math.Max(1, bounds.Width);
        var height = Math.Max(1, bounds.Height);
        var frame = new CocoaRuntime.CGRect(0, 0, width, height);

        var scroll = CocoaRuntime.Allocate("NSScrollView");
        if (scroll != 0)
            scroll = CocoaRuntime.SendRectInit(scroll, CocoaRuntime.sel_registerName("initWithFrame:"), frame);

        var view = CocoaRuntime.Allocate("NSTextView");
        if (view != 0)
            view = CocoaRuntime.SendRectInit(view, CocoaRuntime.sel_registerName("initWithFrame:"), frame);

        if (scroll == 0 || view == 0)
            return (0, 0);

        // Far enough that no document reaches it; AppKit's own idiom here is FLT_MAX, which is only
        // "no limit" spelled in a way that invites a float to overflow into it.
        const double unbounded = 1e7;
        CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("setMinSize:"), new CocoaRuntime.CGSize(0, height));
        CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("setMaxSize:"), new CocoaRuntime.CGSize(unbounded, unbounded));
        CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("setVerticallyResizable:"), true);
        CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("setHorizontallyResizable:"), false);

        // NSViewWidthSizable, so the editor follows the clip view when the box is resized.
        CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("setAutoresizingMask:"), 2);

        if (CocoaRuntime.SendPointer(view, CocoaRuntime.sel_registerName("textContainer")) is var container && container != 0)
        {
            CocoaRuntime.SendVoid(container, CocoaRuntime.sel_registerName("setContainerSize:"), new CocoaRuntime.CGSize(width, unbounded));
            CocoaRuntime.SendVoid(container, CocoaRuntime.sel_registerName("setWidthTracksTextView:"), true);
        }

        CocoaRuntime.SendVoid(scroll, CocoaRuntime.sel_registerName("setHasVerticalScroller:"), true);
        CocoaRuntime.SendVoid(scroll, CocoaRuntime.sel_registerName("setAutohidesScrollers:"), true);
        CocoaRuntime.SendVoid(scroll, CocoaRuntime.sel_registerName("setDocumentView:"), view);
        return (scroll, view);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The selection belongs to the field editor, not the field: AppKit shares one <c>NSTextView</c>
    /// between every text field in a window and lends it to whichever has focus. A field that is not
    /// focused has no editor and therefore no selection to set, which is why this asks first rather
    /// than messaging null and quietly doing nothing. A multiline box owns its editor, so it always has
    /// one.
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
    /// <remarks>
    /// The change notification arrives once the editor has finished the edit and moved its caret past
    /// what was inserted, so during a change the caret is walked back to where the edit began — the
    /// convention <see cref="ITextBoxPeer.GetSelection"/> promises, and the one a <c>GtkEntry</c>
    /// reports natively. What the core last wrote is exactly the pre-edit content, so the distance is
    /// the length the text has grown by since.
    /// </remarks>
    public (int Start, int Length) GetSelection()
    {
        if (Editor() is not { } editor)
            return (this.GetText().Length, 0);

        var range = CocoaRuntime.SendRange(editor, CocoaRuntime.sel_registerName("selectedRange"));
        var start = (int)range.Location;
        if (_inChange)
            start -= Math.Max(0, this.GetText().Length - _pushed.Length);

        return (Math.Max(0, start), (int)range.Length);
    }

    /// <summary>The view the selection lives in: this box's own, or the field editor lent to it.</summary>
    private protected nint? Editor()
    {
        if (_textView != 0)
            return _textView;

        if (this.Handle == 0)
            return null;

        var editor = CocoaRuntime.SendPointer(this.Handle, CocoaRuntime.sel_registerName("currentEditor"));
        return editor == 0 ? null : editor;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The two halves of this box have nothing in common here, so the limit is served twice. A field
    /// has no length of its own and is held to one by a formatter, which its field editor consults
    /// before a keystroke is committed; a text view has no formatter and is held to one by its
    /// delegate, which AppKit asks whether an edit may go ahead. Both are re-applied after a swap,
    /// since the object that carried the limit a moment ago is not the object there now.
    /// </remarks>
    public void SetMaxLength(int maxLength)
    {
        _maxLength = maxLength;
        this.ApplyMaxLength();
    }

    /// <summary>Puts the remembered limit onto whichever of the two objects is currently editing.</summary>
    private void ApplyMaxLength()
    {
        if (_textView != 0)
        {
            CocoaTextViewDelegate.Limit(this.EnsureEditorDelegate(), _maxLength);
            return;
        }

        if (this.Handle == 0)
            return;

        if (_maxLength > 0 && _formatter == 0)
            _formatter = CocoaLengthFormatter.Create(_maxLength);
        else
            CocoaLengthFormatter.Limit(_formatter, _maxLength);

        // The formatter stays on the field even at no limit, because one that lets everything through
        // is the same object doing nothing — and taking it off and putting it back is a chance for the
        // field's object value to round-trip through a formatter that is not there.
        if (_formatter != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setFormatter:"), _formatter);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        this.Unbind();
        CocoaTextViewDelegate.Forget(_editorDelegate);
        CocoaLengthFormatter.Forget(_formatter);
        if (_formatter != 0)
        {
            CocoaRuntime.SendVoid(_formatter, CocoaRuntime.sel_registerName("release"));
            _formatter = 0;
        }

        base.Dispose();
    }
}
