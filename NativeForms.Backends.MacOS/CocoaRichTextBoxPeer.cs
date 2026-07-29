using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// A rich text editor: the multiline text box's <c>NSTextView</c>, with the formatting calls applied to
/// its text storage.
/// </summary>
/// <remarks>
/// <para>
/// A <c>RichTextBox</c> is multiline by construction, so the peer inherits the text view the plain box
/// already builds and has nothing of its own to create. Everything here therefore reduces to the same
/// two questions: which characters, and which attribute — an <c>NSTextStorage</c> is an
/// <c>NSMutableAttributedString</c>, and AppKit relays the whole document out when it is edited.
/// </para>
/// <para>
/// The attribute keys are <c>NSString</c> globals exported by AppKit rather than values a header could
/// inline, so they are read from the framework's symbols. Their literal contents are stable and widely
/// known, and writing them out as string literals would still be guessing at a private detail that has
/// no reason to stay put.
/// </para>
/// </remarks>
internal sealed unsafe class CocoaRichTextBoxPeer : CocoaTextBoxPeer, IRichTextBoxPeer
{
    private static readonly nint _ForegroundColour = CocoaRuntime.Constant("NSForegroundColorAttributeName");
    private static readonly nint _Font = CocoaRuntime.Constant("NSFontAttributeName");
    private static readonly nint _Underline = CocoaRuntime.Constant("NSUnderlineStyleAttributeName");
    private static readonly nint _Strikethrough = CocoaRuntime.Constant("NSStrikethroughStyleAttributeName");
    private static readonly nint _Paragraph = CocoaRuntime.Constant("NSParagraphStyleAttributeName");

    /// <summary>NSFontTraitMask: italic is bit 0, bold bit 1.</summary>
    private const nint _Italic = 1;
    private const nint _Bold = 2;

    private string _rtf = string.Empty;

    /// <inheritdoc/>
    public event EventHandler<string>? LinkClicked;

    /// <inheritdoc/>
    /// <remarks>
    /// The text view is not this peer's own object — the plain box builds it when it is told it is
    /// multiline, and builds another one if it is ever told again — so the link handler is pointed at
    /// the delegate here rather than in a constructor that runs before there is anything to attach it
    /// to. The delegate itself belongs to the plain box, since a maximum length arrives through the
    /// same object and a text view has only one.
    /// </remarks>
    private protected override void OnEditorChanged()
    {
        base.OnEditorChanged();
        if (this.EnsureEditorDelegate() is var target and not 0)
            CocoaTextViewDelegate.Report(target, this.OnLinkClicked);
    }

    /// <summary>AppKit reporting that the user clicked a link, with the link it was.</summary>
    private void OnLinkClicked(string url) => LinkClicked?.Invoke(this, url);

    /// <summary>The document being edited, or zero before the text view exists.</summary>
    private nint Storage
        => this.TextView == 0 ? 0 : CocoaRuntime.SendPointer(this.TextView, CocoaRuntime.sel_registerName("textStorage"));

    /// <inheritdoc/>
    /// <remarks>
    /// AppKit writes the RTF, so what comes back is the document as the platform's own engine
    /// understands it rather than a re-serialization of what the toolkit thought it set. The remembered
    /// string is the answer only while there is no text view to ask.
    /// </remarks>
    public string GetRtf()
    {
        var storage = this.Storage;
        if (storage == 0)
            return _rtf;

        var length = CocoaRuntime.SendInteger(storage, CocoaRuntime.sel_registerName("length"));
        var data = CocoaRuntime.SendRangeObject(
            storage,
            CocoaRuntime.sel_registerName("RTFFromRange:documentAttributes:"),
            new() { Location = 0, Length = length },
            EmptyDictionary());

        return Read(data) is { Length: > 0 } written ? written : _rtf;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Read by AppKit's own RTF parser, which is the whole point of hosting a text view: the fonts,
    /// colours and paragraph styles in the document arrive as attributes rather than being thrown away.
    /// A document it declines falls back to showing the readable text, because a control that displays
    /// nothing is worse than one that displays it unstyled.
    /// </remarks>
    public void SetRtf(string rtf)
    {
        _rtf = rtf;
        var storage = this.Storage;
        if (storage != 0 && Attributed(rtf) is var attributed && attributed != 0)
        {
            CocoaRuntime.SendVoid(storage, CocoaRuntime.sel_registerName("setAttributedString:"), attributed);
            CocoaRuntime.SendVoid(attributed, CocoaRuntime.sel_registerName("release"));
            return;
        }

        this.SetText(PlainTextOf(rtf));
    }

    /// <summary>An <c>NSAttributedString</c> parsed from an RTF document, or zero.</summary>
    private static nint Attributed(string rtf)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(rtf);
        var data = CocoaRuntime.objc_getClass("NSData");
        if (data == 0)
            return 0;

        nint payload;
        fixed (byte* raw = bytes)
            payload = CocoaRuntime.SendPointer(data, CocoaRuntime.sel_registerName("dataWithBytes:length:"), (nint)raw, bytes.Length);

        if (payload == 0)
            return 0;

        var allocated = CocoaRuntime.Allocate("NSAttributedString");
        return allocated == 0
            ? 0
            : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("initWithRTF:documentAttributes:"), payload, 0);
    }

    /// <summary>The bytes behind an <c>NSData</c> as text, or null.</summary>
    private static string? Read(nint data)
    {
        if (data == 0)
            return null;

        var bytes = CocoaRuntime.SendPointer(data, CocoaRuntime.sel_registerName("bytes"));
        var length = (int)CocoaRuntime.SendInteger(data, CocoaRuntime.sel_registerName("length"));
        return bytes == 0 || length <= 0
            ? null
            : System.Text.Encoding.UTF8.GetString(new ReadOnlySpan<byte>((void*)bytes, length));
    }

    private static nint EmptyDictionary() => CocoaRuntime.SendToClass("NSDictionary", "dictionary");

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

    /// <summary>
    /// The characters the next formatting call acts on, or null when there is nothing to act on.
    /// </summary>
    /// <remarks>
    /// An empty selection is not an error and not a caret format: Windows Forms would remember the
    /// wish for the next characters typed, which here would mean writing the view's typing attributes
    /// — a separate dictionary with separate lifetime rules for a case the toolkit only reaches when a
    /// property is set with nothing selected. It does nothing instead, and says so.
    /// </remarks>
    private (nint Storage, CocoaRuntime.NSRange Range)? Target()
    {
        var storage = this.Storage;
        if (storage == 0)
            return null;

        var range = CocoaRuntime.SendRange(this.TextView, CocoaRuntime.sel_registerName("selectedRange"));
        return range.Length <= 0 ? null : (storage, range);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Bold and italic are font traits, so they go through <c>NSFontManager</c> — the only thing that
    /// knows which face in a family carries the trait, and the reason "bold" is not a matter of
    /// re-asking for the same font by a heavier name. Underline and strikethrough are attributes of the
    /// text rather than of its font, so they are set directly.
    /// </remarks>
    public void SetSelectionStyle(FontStyle style, bool enabled)
    {
        if (this.Target() is not var (storage, range))
            return;

        CocoaRuntime.SendVoid(storage, CocoaRuntime.sel_registerName("beginEditing"));

        if ((style & (FontStyle.Bold | FontStyle.Italic)) != 0 && this.FontAt(storage, range.Location) is var font && font != 0)
        {
            var traits = ((style & FontStyle.Bold) != 0 ? _Bold : 0) | ((style & FontStyle.Italic) != 0 ? _Italic : 0);
            var manager = CocoaRuntime.SendToClass("NSFontManager", "sharedFontManager");
            var converted = manager == 0
                ? 0
                : CocoaRuntime.SendPointer(
                    manager,
                    CocoaRuntime.sel_registerName(enabled ? "convertFont:toHaveTrait:" : "convertFont:toNotHaveTrait:"),
                    font,
                    traits);

            if (converted != 0 && _Font != 0)
                CocoaRuntime.SendAttribute(storage, CocoaRuntime.sel_registerName("addAttribute:value:range:"), _Font, converted, range);
        }

        // NSUnderlineStyle: none 0, single 1.
        if ((style & FontStyle.Underline) != 0)
            Mark(storage, _Underline, enabled, range);

        if ((style & FontStyle.Strikeout) != 0)
            Mark(storage, _Strikethrough, enabled, range);

        CocoaRuntime.SendVoid(storage, CocoaRuntime.sel_registerName("endEditing"));
    }

    /// <summary>Turns a numeric line attribute — underline, strikethrough — on or off over a range.</summary>
    private static void Mark(nint storage, nint key, bool enabled, CocoaRuntime.NSRange range)
    {
        if (key == 0)
            return;

        if (!enabled)
        {
            CocoaRuntime.SendVoid(storage, CocoaRuntime.sel_registerName("removeAttribute:range:"), key, range);
            return;
        }

        var number = CocoaRuntime.SendPointer(CocoaRuntime.objc_getClass("NSNumber"), CocoaRuntime.sel_registerName("numberWithInteger:"), 1);
        if (number != 0)
            CocoaRuntime.SendAttribute(storage, CocoaRuntime.sel_registerName("addAttribute:value:range:"), key, number, range);
    }

    /// <summary>
    /// The font in force at a character index, or the view's own when the run carries none.
    /// </summary>
    /// <remarks>
    /// One font for the whole selection, read where it starts. Walking the runs would mean
    /// <c>enumerateAttribute:…usingBlock:</c>, and a block is an Objective-C object with a calling
    /// convention — the shape these rules keep out. A selection spanning two faces therefore takes the
    /// first one's, which is a visible simplification rather than a hidden one.
    /// </remarks>
    private nint FontAt(nint storage, nint index)
    {
        if (_Font != 0
            && CocoaRuntime.SendAttributeAt(storage, CocoaRuntime.sel_registerName("attribute:atIndex:effectiveRange:"), _Font, index, 0) is var font
            && font != 0)
            return font;

        return CocoaRuntime.SendPointer(this.TextView, CocoaRuntime.sel_registerName("font"));
    }

    /// <inheritdoc/>
    public void SetSelectionColor(Color color)
    {
        if (_ForegroundColour == 0 || this.Target() is not var (storage, range))
            return;

        if (color.IsEmpty)
        {
            CocoaRuntime.SendVoid(storage, CocoaRuntime.sel_registerName("removeAttribute:range:"), _ForegroundColour, range);
            return;
        }

        var colours = CocoaRuntime.objc_getClass("NSColor");
        var value = colours == 0
            ? 0
            : CocoaRuntime.SendColor(
                colours,
                CocoaRuntime.sel_registerName("colorWithSRGBRed:green:blue:alpha:"),
                color.R / 255.0,
                color.G / 255.0,
                color.B / 255.0,
                color.A / 255.0);

        if (value != 0)
            CocoaRuntime.SendAttribute(storage, CocoaRuntime.sel_registerName("addAttribute:value:range:"), _ForegroundColour, value, range);
    }

    /// <inheritdoc/>
    public void SetSelectionFontSize(float sizeInPoints)
    {
        if (_Font == 0 || sizeInPoints <= 0 || this.Target() is not var (storage, range))
            return;

        var manager = CocoaRuntime.SendToClass("NSFontManager", "sharedFontManager");
        var font = this.FontAt(storage, range.Location);
        var resized = manager == 0 || font == 0
            ? 0
            : CocoaRuntime.SendPointer(manager, CocoaRuntime.sel_registerName("convertFont:toSize:"), font, sizeInPoints);

        if (resized != 0)
            CocoaRuntime.SendAttribute(storage, CocoaRuntime.sel_registerName("addAttribute:value:range:"), _Font, resized, range);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Widened to whole paragraphs first, because a paragraph style is one per paragraph however few of
    /// its characters are selected — applying it to the selection alone would leave AppKit deciding
    /// which of two conflicting styles the paragraph has.
    /// </remarks>
    public void SetSelectionAlignment(ContentAlignment alignment)
    {
        if (this.Paragraph() is not var (storage, range, style))
            return;

        // NSTextAlignment: left 0, right 1, centre 2.
        var value = alignment switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => 2,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => 1,
            _ => 0,
        };

        CocoaRuntime.SendVoid(style, CocoaRuntime.sel_registerName("setAlignment:"), value);
        CocoaRuntime.SendAttribute(storage, CocoaRuntime.sel_registerName("addAttribute:value:range:"), _Paragraph, style, range);
        CocoaRuntime.SendVoid(style, CocoaRuntime.sel_registerName("release"));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A disc list on the paragraph plus the indent it hangs from. AppKit draws the marker from the
    /// text list; the indents are what guarantee the wrapped lines line up under the first one rather
    /// than under the bullet.
    /// </remarks>
    public void SetSelectionBullet(bool bullet)
    {
        if (this.Paragraph() is not var (storage, range, style))
            return;

        const double indent = 18;
        CocoaRuntime.SendVoid(style, CocoaRuntime.sel_registerName("setHeadIndent:"), bullet ? indent : 0.0);
        CocoaRuntime.SendVoid(style, CocoaRuntime.sel_registerName("setFirstLineHeadIndent:"), bullet ? indent : 0.0);

        var lists = bullet ? DiscList() : CocoaRuntime.SendToClass("NSArray", "array");
        if (lists != 0)
            CocoaRuntime.SendVoid(style, CocoaRuntime.sel_registerName("setTextLists:"), lists);

        CocoaRuntime.SendAttribute(storage, CocoaRuntime.sel_registerName("addAttribute:value:range:"), _Paragraph, style, range);
        CocoaRuntime.SendVoid(style, CocoaRuntime.sel_registerName("release"));
    }

    /// <summary>A one-element array holding a disc-marker <c>NSTextList</c>, or zero.</summary>
    private static nint DiscList()
    {
        var allocated = CocoaRuntime.Allocate("NSTextList");
        if (allocated == 0)
            return 0;

        var format = CocoaRuntime.NSString("{disc}");
        var list = format == 0
            ? 0
            : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("initWithMarkerFormat:options:"), format, 0);

        if (format != 0)
            CocoaNative.CFRelease(format);

        var arrays = CocoaRuntime.objc_getClass("NSArray");
        return list == 0 || arrays == 0
            ? 0
            : CocoaRuntime.SendPointer(arrays, CocoaRuntime.sel_registerName("arrayWithObject:"), list);
    }

    /// <summary>
    /// The paragraphs the selection touches, with a fresh mutable style for the caller to fill in. The
    /// style is owned by the caller and released once applied.
    /// </summary>
    private (nint Storage, CocoaRuntime.NSRange Range, nint Style)? Paragraph()
    {
        var storage = this.Storage;
        if (storage == 0 || _Paragraph == 0)
            return null;

        var selected = CocoaRuntime.SendRange(this.TextView, CocoaRuntime.sel_registerName("selectedRange"));
        var text = CocoaRuntime.SendPointer(storage, CocoaRuntime.sel_registerName("string"));
        if (text == 0)
            return null;

        var range = CocoaRuntime.SendRange(text, CocoaRuntime.sel_registerName("paragraphRangeForRange:"), selected);
        if (range.Length <= 0)
            return null;

        var allocated = CocoaRuntime.Allocate("NSMutableParagraphStyle");
        var style = allocated == 0 ? 0 : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("init"));
        return style == 0 ? null : (storage, range, style);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Two calls, because the switch alone would only half serve the property. AppKit's automatic
    /// detection runs as text is typed, so a document that was set rather than typed — which is every
    /// document a program builds — would carry no links at all and the click would have nothing to
    /// report. <c>checkTextInDocument:</c> is the same checker run over what is already there, which is
    /// what Windows Forms means by <c>DetectUrls</c>.
    /// </remarks>
    public void SetDetectUrls(bool detectUrls)
    {
        if (this.TextView == 0)
            return;

        CocoaRuntime.SendVoid(this.TextView, CocoaRuntime.sel_registerName("setAutomaticLinkDetectionEnabled:"), detectUrls);

        var check = CocoaRuntime.sel_registerName("checkTextInDocument:");
        if (detectUrls && CocoaRuntime.SendBool(this.TextView, CocoaRuntime.sel_registerName("respondsToSelector:"), check))
            CocoaRuntime.SendVoid(this.TextView, check, 0);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Through the scroll view rather than the text view. <c>scaleUnitSquareToSize:</c> is the obvious
    /// call and the wrong one: it multiplies whatever scale the view already carries, so an absolute
    /// factor would have to be divided by an accumulated one that nothing can read back reliably, and
    /// the error compounds with every call. A scroll view's magnification is absolute — it is what the
    /// pinch gesture sets — so setting it twice to the same number is setting it once.
    /// </para>
    /// <para>
    /// The limits are widened because AppKit's defaults stop at a quarter and four times, and a
    /// caller asking for more would silently get less. This is the same thing <c>EM_SETZOOM</c> does
    /// on Windows: the rendering scales, the document does not change.
    /// </para>
    /// </remarks>
    public void SetZoom(float factor)
    {
        // The scroll view is the peer's own handle; a box that is not multiline has none, and a rich
        // text box always is.
        if (!this.IsMultiline || this.Handle == 0 || factor <= 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setAllowsMagnification:"), true);
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setMinMagnification:"), 0.05);
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setMaxMagnification:"), 20.0);
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setMagnification:"), (double)factor);
    }
}
