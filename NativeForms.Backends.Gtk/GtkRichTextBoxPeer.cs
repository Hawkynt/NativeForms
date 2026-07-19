using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Text;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a rich text box: a <c>GtkTextView</c> in a <c>GtkScrolledWindow</c> whose
/// formatting is expressed as named <c>GtkTextTag</c>s. The tag <em>names</em> encode the semantics
/// (<c>bold</c>, <c>fg:#RRGGBB</c>, <c>size:14</c>, <c>align:center</c>, …), so the RTF export can
/// walk the buffer's tag toggles and rebuild a <see cref="RichDocument"/> without reading a single
/// tag property back — GTK has no native RTF, so both directions round-trip through the core
/// <see cref="RtfSerializer"/>.
/// </summary>
/// <remarks>
/// Honest simplifications, all visible and documented: bullets are a literal <c>"• "</c> paragraph
/// prefix (GTK text views have no list model), so they are part of the reported text; paragraph
/// alignment is a text tag over the paragraph's characters (empty paragraphs cannot hold one); URL
/// detection tags <c>http(s)://…</c> and <c>www.…</c> tokens after every change and reports a
/// left-click inside such a span as <see cref="LinkClicked"/>. Tag properties are set through
/// <c>GValue</c>s (<c>g_object_set_property</c>) — never the variadic <c>g_object_set</c>, whose
/// double-typed varargs source-generated P/Invoke cannot express safely. Placeholder, password
/// masking and max length remain single-line-entry features and are no-ops here, exactly like the
/// multiline half of <see cref="GtkTextBoxPeer"/>.
/// </remarks>
internal sealed class GtkRichTextBoxPeer : GtkControlPeer, IRichTextBoxPeer
{
    private const string _BulletPrefix = "• ";

    private bool _readOnly;
    private int _selectionStart;
    private int _selectionLength;
    private bool _detectUrls;
    private float _zoom = 1f;
    private string? _rtf;

    /// <summary>The <c>GtkTextView</c> inside the scrolled window.</summary>
    private nint _textView;

    /// <inheritdoc />
    public event EventHandler? TextChangedByUser;

    /// <inheritdoc />
    public event EventHandler<string>? LinkClicked;

    /// <summary>The view's <c>GtkTextBuffer</c> (owned by the view).</summary>
    private nint Buffer => NativeMethods.gtk_text_view_get_buffer(_textView);

    /// <inheritdoc />
    protected override nint CreateWidget()
    {
        var scrolled = NativeMethods.gtk_scrolled_window_new(0, 0);
        _textView = NativeMethods.gtk_text_view_new();
        NativeMethods.gtk_container_add(scrolled, _textView);
        NativeMethods.gtk_widget_set_visible(_textView, 1);
        return scrolled;
    }

    /// <inheritdoc />
    protected override void ApplyText(string text)
        => NativeMethods.gtk_text_buffer_set_text(this.Buffer, text, -1);

    /// <inheritdoc />
    protected override void OnWidgetRealized()
    {
        this.ApplyText(_text);
        NativeMethods.gtk_text_view_set_editable(_textView, Bool(!_readOnly));
        this.SetSelection(_selectionStart, _selectionLength);
        if (_rtf is not null)
            this.ApplyRtf(_rtf);

        this.ApplyZoom();
        this.RefreshLinks();

        if (!_selfHandle.IsAllocated)
            _selfHandle = GCHandle.Alloc(this);

        unsafe
        {
            var changed = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnChanged;
            NativeMethods.g_signal_connect_data(this.Buffer, "changed", changed, GCHandle.ToIntPtr(_selfHandle), 0, 0);

            var released = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnButtonReleased;
            NativeMethods.g_signal_connect_data(_textView, "button-release-event", released, GCHandle.ToIntPtr(_selfHandle), 0, 0);
        }
    }

    // --- ITextBoxPeer ----------------------------------------------------------------------------

    /// <inheritdoc />
    public void SetMultiline(bool multiline)
    {
        // A rich text box is inherently multiline; the flag has nothing to switch here.
    }

    /// <inheritdoc />
    public void SetPlaceholder(string placeholder)
    {
        // GtkTextView has no native placeholder (single-line-entry feature) — documented no-op.
    }

    /// <inheritdoc />
    public void SetPasswordChar(char passwordChar)
    {
        // Password masking is a single-line-entry feature; a rich editor never masks.
    }

    /// <inheritdoc />
    public void SetReadOnly(bool readOnly)
    {
        _readOnly = readOnly;
        if (_widget != 0)
            NativeMethods.gtk_text_view_set_editable(_textView, Bool(!readOnly));
    }

    /// <inheritdoc />
    public void SetMaxLength(int maxLength)
    {
        // GtkTextView has no native length limit — documented no-op, like the multiline text box.
    }

    /// <inheritdoc />
    public void SetSelection(int start, int length)
    {
        _selectionStart = start;
        _selectionLength = length;
        if (_widget == 0)
            return;

        var buffer = this.Buffer;
        NativeMethods.gtk_text_buffer_get_iter_at_offset(buffer, out var bound, start);
        NativeMethods.gtk_text_buffer_get_iter_at_offset(buffer, out var ins, start + length);
        NativeMethods.gtk_text_buffer_select_range(buffer, in ins, in bound);
    }

    /// <inheritdoc />
    public (int Start, int Length) GetSelection()
    {
        if (_widget == 0)
            return (_selectionStart, _selectionLength);

        NativeMethods.gtk_text_buffer_get_selection_bounds(this.Buffer, out var startIter, out var endIter);
        var start = NativeMethods.gtk_text_iter_get_offset(in startIter);
        var end = NativeMethods.gtk_text_iter_get_offset(in endIter);
        return (start, end - start);
    }

    /// <inheritdoc />
    public string GetText()
    {
        if (_widget == 0)
            return _text;

        var buffer = this.Buffer;
        NativeMethods.gtk_text_buffer_get_bounds(buffer, out var start, out var end);
        var ptr = NativeMethods.gtk_text_buffer_get_text(buffer, in start, in end, 0);
        var text = Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
        NativeMethods.g_free(ptr);
        return text;
    }

    // --- IRichTextBoxPeer ------------------------------------------------------------------------

    /// <inheritdoc />
    public void SetSelectionStyle(FontStyle style, bool enabled)
    {
        if (_widget == 0)
            return;

        var (start, length) = this.GetSelection();
        if ((style & FontStyle.Bold) != 0)
            this.SetTagged("bold", start, length, enabled);
        if ((style & FontStyle.Italic) != 0)
            this.SetTagged("italic", start, length, enabled);
        if ((style & FontStyle.Underline) != 0)
            this.SetTagged("underline", start, length, enabled);
        if ((style & FontStyle.Strikeout) != 0)
            this.SetTagged("strike", start, length, enabled);
    }

    /// <inheritdoc />
    public void SetSelectionColor(Color color)
    {
        if (_widget == 0)
            return;

        var (start, length) = this.GetSelection();

        // Exactly one color tag may cover a character, or tag priority (creation order) would pick
        // the winner instead of the caller — strip competitors first.
        this.RemoveTagsWithPrefix("fg:", start, length);
        if (!color.IsEmpty)
            this.SetTagged(ColorTagName(color), start, length, true);
    }

    /// <inheritdoc />
    public void SetSelectionFontSize(float sizeInPoints)
    {
        if (_widget == 0)
            return;

        var (start, length) = this.GetSelection();
        this.RemoveTagsWithPrefix("size:", start, length);
        if (sizeInPoints > 0f)
            this.SetTagged(SizeTagName(sizeInPoints), start, length, true);
    }

    /// <inheritdoc />
    public void SetSelectionAlignment(ContentAlignment alignment)
    {
        if (_widget == 0)
            return;

        var (start, length) = this.ParagraphRange();
        this.RemoveTagsWithPrefix("align:", start, length);
        var name = alignment switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => "align:center",
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => "align:right",
            _ => null,
        };
        if (name is not null)
            this.SetTagged(name, start, length, true);
    }

    /// <inheritdoc />
    public void SetSelectionBullet(bool bullet)
    {
        if (_widget == 0)
            return;

        var text = this.GetText();
        var (start, length) = this.ParagraphRange();
        var buffer = this.Buffer;

        // Walk the paragraphs back to front so earlier offsets stay valid across edits.
        var lineStarts = new List<int>();
        var lineStart = start;
        lineStarts.Add(lineStart);
        for (var i = start; i < start + length && i < text.Length; ++i)
            if (text[i] == '\n')
                lineStarts.Add(i + 1);

        for (var i = lineStarts.Count - 1; i >= 0; --i)
        {
            var offset = lineStarts[i];
            var hasPrefix = string.CompareOrdinal(text, offset, _BulletPrefix, 0, _BulletPrefix.Length) == 0;
            if (bullet && !hasPrefix)
            {
                NativeMethods.gtk_text_buffer_get_iter_at_offset(buffer, out var iter, offset);
                NativeMethods.gtk_text_buffer_insert(buffer, ref iter, _BulletPrefix, -1);
            }
            else if (!bullet && hasPrefix)
            {
                NativeMethods.gtk_text_buffer_get_iter_at_offset(buffer, out var from, offset);
                NativeMethods.gtk_text_buffer_get_iter_at_offset(buffer, out var to, offset + _BulletPrefix.Length);
                NativeMethods.gtk_text_buffer_delete(buffer, ref from, ref to);
            }
        }
    }

    /// <inheritdoc />
    public void SetDetectUrls(bool detectUrls)
    {
        _detectUrls = detectUrls;
        if (_widget != 0)
            this.RefreshLinks();
    }

    /// <inheritdoc />
    public void SetZoom(float factor)
    {
        _zoom = factor;
        if (_widget != 0)
            this.ApplyZoom();
    }

    /// <inheritdoc />
    public string GetRtf()
    {
        if (_widget == 0)
            return _rtf ?? RtfSerializer.Write(RichDocument.FromPlainText(_text));

        return RtfSerializer.Write(this.ExportDocument());
    }

    /// <inheritdoc />
    public void SetRtf(string rtf)
    {
        _rtf = rtf;
        if (_widget != 0)
            this.ApplyRtf(rtf);
    }

    // --- Formatting helpers ----------------------------------------------------------------------

    /// <summary>Applies or removes the named tag over a character range, creating the tag on first use.</summary>
    private void SetTagged(string name, int start, int length, bool apply)
    {
        var buffer = this.Buffer;
        var tag = this.EnsureTag(name);
        NativeMethods.gtk_text_buffer_get_iter_at_offset(buffer, out var from, start);
        NativeMethods.gtk_text_buffer_get_iter_at_offset(buffer, out var to, start + length);
        if (apply)
            NativeMethods.gtk_text_buffer_apply_tag(buffer, tag, in from, in to);
        else
            NativeMethods.gtk_text_buffer_remove_tag(buffer, tag, in from, in to);
    }

    /// <summary>
    /// Removes every tag of a family (<c>fg:</c>, <c>size:</c>, <c>align:</c>) from a range by
    /// walking the range's tag toggles — the buffer knows which tags exist, we only match names.
    /// </summary>
    private void RemoveTagsWithPrefix(string prefix, int start, int length)
    {
        var buffer = this.Buffer;
        var names = new HashSet<string>();
        NativeMethods.gtk_text_buffer_get_iter_at_offset(buffer, out var iter, start);
        NativeMethods.gtk_text_buffer_get_iter_at_offset(buffer, out var end, start + length);
        while (true)
        {
            foreach (var name in ActiveTagNames(in iter))
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                    names.Add(name);

            if (NativeMethods.gtk_text_iter_forward_to_tag_toggle(ref iter, 0) == 0)
                break;

            if (NativeMethods.gtk_text_iter_get_offset(in iter) >= NativeMethods.gtk_text_iter_get_offset(in end))
                break;
        }

        foreach (var name in names)
            this.SetTagged(name, start, length, false);
    }

    /// <summary>Looks the named tag up, creating and configuring it on first use.</summary>
    private nint EnsureTag(string name)
    {
        var buffer = this.Buffer;
        var table = NativeMethods.gtk_text_buffer_get_tag_table(buffer);
        var tag = NativeMethods.gtk_text_tag_table_lookup(table, name);
        if (tag != 0)
            return tag;

        tag = NativeMethods.gtk_text_buffer_create_tag(buffer, name, 0);
        switch (name)
        {
            case "bold":
                SetIntProperty(tag, "weight", NativeMethods.PANGO_WEIGHT_BOLD);
                break;

            case "italic":
                SetIntProperty(tag, "style", NativeMethods.PANGO_STYLE_ITALIC);
                break;

            case "underline":
                SetIntProperty(tag, "underline", NativeMethods.PANGO_UNDERLINE_SINGLE);
                break;

            case "strike":
                SetBoolProperty(tag, "strikethrough", true);
                break;

            case "align:center":
                SetIntProperty(tag, "justification", NativeMethods.GTK_JUSTIFY_CENTER);
                break;

            case "align:right":
                SetIntProperty(tag, "justification", NativeMethods.GTK_JUSTIFY_RIGHT);
                break;

            case "link":
                SetStringProperty(tag, "foreground", "#0066CC");
                SetIntProperty(tag, "underline", NativeMethods.PANGO_UNDERLINE_SINGLE);
                break;

            case "zoom":
                SetDoubleProperty(tag, "scale", _zoom);
                break;

            default:
                if (name.StartsWith("fg:", StringComparison.Ordinal))
                    SetStringProperty(tag, "foreground", name[3..]);
                else if (name.StartsWith("size:", StringComparison.Ordinal))
                    SetIntProperty(tag, "size", (int)(float.Parse(name[5..], CultureInfo.InvariantCulture) * NativeMethods.PANGO_SCALE));
                break;
        }

        return tag;
    }

    /// <summary>Scales the whole buffer by the current zoom factor via the shared <c>zoom</c> tag.</summary>
    private void ApplyZoom()
    {
        var tag = this.EnsureTag("zoom");
        SetDoubleProperty(tag, "scale", _zoom);
        var buffer = this.Buffer;
        NativeMethods.gtk_text_buffer_get_bounds(buffer, out var start, out var end);
        NativeMethods.gtk_text_buffer_apply_tag(buffer, tag, in start, in end);
    }

    /// <summary>Re-tags every URL-shaped token (<c>http://…</c>, <c>https://…</c>, <c>www.…</c>) as a link.</summary>
    private void RefreshLinks()
    {
        var buffer = this.Buffer;
        var tag = this.EnsureTag("link");
        NativeMethods.gtk_text_buffer_get_bounds(buffer, out var start, out var end);
        NativeMethods.gtk_text_buffer_remove_tag(buffer, tag, in start, in end);
        if (!_detectUrls)
            return;

        var text = this.GetText();
        var i = 0;
        while (i < text.Length)
        {
            var length = UrlLengthAt(text, i);
            if (length == 0)
            {
                ++i;
                continue;
            }

            NativeMethods.gtk_text_buffer_get_iter_at_offset(buffer, out var from, i);
            NativeMethods.gtk_text_buffer_get_iter_at_offset(buffer, out var to, i + length);
            NativeMethods.gtk_text_buffer_apply_tag(buffer, tag, in from, in to);
            i += length;
        }
    }

    /// <summary>The length of the URL token starting at <paramref name="i"/>, or 0 when none starts there.</summary>
    private static int UrlLengthAt(string text, int i)
    {
        if (i != 0 && !char.IsWhiteSpace(text[i - 1]))
            return 0;

        var isUrl = string.CompareOrdinal(text, i, "http://", 0, 7) == 0
                    || string.CompareOrdinal(text, i, "https://", 0, 8) == 0
                    || string.CompareOrdinal(text, i, "www.", 0, 4) == 0;
        if (!isUrl)
            return 0;

        var end = i;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            ++end;

        return end - i;
    }

    /// <summary>Expands the current selection to whole paragraphs (line starts to line ends).</summary>
    private (int Start, int Length) ParagraphRange()
    {
        var text = this.GetText();
        var (start, length) = this.GetSelection();
        start = Math.Clamp(start, 0, text.Length);
        var end = Math.Clamp(start + length, start, text.Length);
        while (start > 0 && text[start - 1] != '\n')
            --start;

        while (end < text.Length && text[end] != '\n')
            ++end;

        return (start, end - start);
    }

    // --- RTF bridge ------------------------------------------------------------------------------

    /// <summary>Replaces the buffer from RTF, rebuilding the tag soup run by run.</summary>
    private void ApplyRtf(string rtf)
    {
        var document = RtfSerializer.Parse(rtf);
        var buffer = this.Buffer;

        var plain = new System.Text.StringBuilder();
        for (var p = 0; p < document.Paragraphs.Count; ++p)
        {
            if (p != 0)
                plain.Append('\n');

            var paragraph = document.Paragraphs[p];
            if (paragraph.Bullet)
                plain.Append(_BulletPrefix);

            plain.Append(paragraph.ToPlainText());
        }

        NativeMethods.gtk_text_buffer_set_text(buffer, plain.ToString(), -1);

        var offset = 0;
        foreach (var paragraph in document.Paragraphs)
        {
            var paragraphStart = offset;
            if (paragraph.Bullet)
                offset += _BulletPrefix.Length;

            foreach (var run in paragraph.Runs)
            {
                var length = run.Text.Length;
                if ((run.Style & FontStyle.Bold) != 0)
                    this.SetTagged("bold", offset, length, true);
                if ((run.Style & FontStyle.Italic) != 0)
                    this.SetTagged("italic", offset, length, true);
                if ((run.Style & FontStyle.Underline) != 0)
                    this.SetTagged("underline", offset, length, true);
                if ((run.Style & FontStyle.Strikeout) != 0)
                    this.SetTagged("strike", offset, length, true);
                if (!run.Color.IsEmpty)
                    this.SetTagged(ColorTagName(run.Color), offset, length, true);
                if (run.FontSize > 0f)
                    this.SetTagged(SizeTagName(run.FontSize), offset, length, true);

                offset += length;
            }

            var alignTag = RtfSerializer.HorizontalComponent(paragraph.Alignment) switch
            {
                ContentAlignment.TopCenter => "align:center",
                ContentAlignment.TopRight => "align:right",
                _ => null,
            };
            if (alignTag is not null && offset > paragraphStart)
                this.SetTagged(alignTag, paragraphStart, offset - paragraphStart, true);

            ++offset; // the '\n'
        }

        this.ApplyZoom();
        this.RefreshLinks();
    }

    /// <summary>Rebuilds a <see cref="RichDocument"/> from the buffer by walking its tag toggles.</summary>
    private RichDocument ExportDocument()
    {
        var buffer = this.Buffer;
        var document = new RichDocument();
        var paragraph = new RichParagraph();

        NativeMethods.gtk_text_buffer_get_bounds(buffer, out var iter, out var end);
        var endOffset = NativeMethods.gtk_text_iter_get_offset(in end);
        while (NativeMethods.gtk_text_iter_get_offset(in iter) < endOffset)
        {
            var segmentEnd = iter;
            NativeMethods.gtk_text_iter_forward_to_tag_toggle(ref segmentEnd, 0);
            if (NativeMethods.gtk_text_iter_get_offset(in segmentEnd) > endOffset)
                segmentEnd = end;

            var ptr = NativeMethods.gtk_text_buffer_get_text(buffer, in iter, in segmentEnd, 0);
            var segment = Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
            NativeMethods.g_free(ptr);

            var style = FontStyle.Regular;
            var color = Color.Empty;
            var size = 0f;
            ContentAlignment? alignment = null;
            foreach (var name in ActiveTagNames(in iter))
                switch (name)
                {
                    case "bold": style |= FontStyle.Bold; break;
                    case "italic": style |= FontStyle.Italic; break;
                    case "underline": style |= FontStyle.Underline; break;
                    case "strike": style |= FontStyle.Strikeout; break;
                    case "align:center": alignment = ContentAlignment.TopCenter; break;
                    case "align:right": alignment = ContentAlignment.TopRight; break;
                    default:
                        if (name.StartsWith("fg:", StringComparison.Ordinal))
                            color = ParseColorTag(name);
                        else if (name.StartsWith("size:", StringComparison.Ordinal))
                            size = float.Parse(name[5..], CultureInfo.InvariantCulture);
                        break;
                }

            var pieceStart = 0;
            while (true)
            {
                var newline = segment.IndexOf('\n', pieceStart);
                var piece = newline < 0 ? segment[pieceStart..] : segment[pieceStart..newline];
                if (piece.Length != 0)
                {
                    paragraph.Runs.Add(new(piece, style, color, size));
                    if (alignment is not null)
                        paragraph.Alignment = alignment.Value;
                }

                if (newline < 0)
                    break;

                document.Paragraphs.Add(paragraph);
                paragraph = new();
                pieceStart = newline + 1;
            }

            iter = segmentEnd;
        }

        document.Paragraphs.Add(paragraph);

        // The literal bullet prefixes are formatting, not content — translate them back.
        foreach (var candidate in document.Paragraphs)
        {
            if (candidate.Runs.Count == 0)
                continue;

            var first = candidate.Runs[0];
            if (!first.Text.StartsWith(_BulletPrefix, StringComparison.Ordinal))
                continue;

            candidate.Bullet = true;
            if (first.Text.Length == _BulletPrefix.Length)
                candidate.Runs.RemoveAt(0);
            else
                candidate.Runs[0] = new(first.Text[_BulletPrefix.Length..], first.Style, first.Color, first.FontSize);
        }

        return document;
    }

    // --- Tag plumbing ----------------------------------------------------------------------------

    /// <summary>The names of the semantic tags active at an iterator (the <c>zoom</c> view tag excluded).</summary>
    private static List<string> ActiveTagNames(in NativeMethods.GtkTextIter iter)
    {
        var names = new List<string>();
        var list = NativeMethods.gtk_text_iter_get_tags(in iter);
        var cell = list;
        unsafe
        {
            while (cell != 0)
            {
                var entry = *(NativeMethods.GSList*)cell;
                NativeMethods.g_object_get(entry.data, "name", out var namePtr, 0);
                var name = Marshal.PtrToStringUTF8(namePtr);
                if (namePtr != 0)
                    NativeMethods.g_free(namePtr);

                if (name is not null && name is not "zoom" and not "link")
                    names.Add(name);

                cell = entry.next;
            }
        }

        if (list != 0)
            NativeMethods.g_slist_free(list);

        return names;
    }

    /// <summary>The canonical tag name of a color (<c>fg:#RRGGBB</c>).</summary>
    private static string ColorTagName(Color color) => $"fg:#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>The canonical tag name of a font size (<c>size:14</c>, invariant).</summary>
    private static string SizeTagName(float sizeInPoints) => $"size:{sizeInPoints.ToString("0.##", CultureInfo.InvariantCulture)}";

    /// <summary>Parses a <c>fg:#RRGGBB</c> tag name back into its color.</summary>
    private static Color ParseColorTag(string name)
        => name.Length == 10 && int.TryParse(name.AsSpan(4), NumberStyles.HexNumber, null, out var rgb)
            ? Color.FromArgb(rgb >> 16 & 0xFF, rgb >> 8 & 0xFF, rgb & 0xFF)
            : Color.Empty;

    /// <summary>Sets an <c>int</c>-typed object property through a <c>GValue</c>.</summary>
    private static void SetIntProperty(nint @object, string name, int value)
    {
        var gValue = default(NativeMethods.GValue);
        NativeMethods.g_value_init(ref gValue, NativeMethods.G_TYPE_INT);
        NativeMethods.g_value_set_int(ref gValue, value);
        NativeMethods.g_object_set_property(@object, name, in gValue);
        NativeMethods.g_value_unset(ref gValue);
    }

    /// <summary>Sets a <c>gboolean</c>-typed object property through a <c>GValue</c>.</summary>
    private static void SetBoolProperty(nint @object, string name, bool value)
    {
        var gValue = default(NativeMethods.GValue);
        NativeMethods.g_value_init(ref gValue, NativeMethods.G_TYPE_BOOLEAN);
        NativeMethods.g_value_set_boolean(ref gValue, Bool(value));
        NativeMethods.g_object_set_property(@object, name, in gValue);
        NativeMethods.g_value_unset(ref gValue);
    }

    /// <summary>Sets a <c>double</c>-typed object property through a <c>GValue</c>.</summary>
    private static void SetDoubleProperty(nint @object, string name, double value)
    {
        var gValue = default(NativeMethods.GValue);
        NativeMethods.g_value_init(ref gValue, NativeMethods.G_TYPE_DOUBLE);
        NativeMethods.g_value_set_double(ref gValue, value);
        NativeMethods.g_object_set_property(@object, name, in gValue);
        NativeMethods.g_value_unset(ref gValue);
    }

    /// <summary>Sets a string-typed object property through a <c>GValue</c>.</summary>
    private static void SetStringProperty(nint @object, string name, string value)
    {
        var gValue = default(NativeMethods.GValue);
        NativeMethods.g_value_init(ref gValue, NativeMethods.G_TYPE_STRING);
        NativeMethods.g_value_set_string(ref gValue, value);
        NativeMethods.g_object_set_property(@object, name, in gValue);
        NativeMethods.g_value_unset(ref gValue);
    }

    // --- Signal handlers -------------------------------------------------------------------------

    /// <summary>Reports the change and re-derives the derived tagging (links, zoom coverage).</summary>
    private void HandleChanged()
    {
        TextChangedByUser?.Invoke(this, EventArgs.Empty);

        // Fresh characters carry neither the zoom scale nor link detection yet; re-derive both.
        // Tag application does not re-emit "changed", so this cannot recurse.
        this.ApplyZoom();
        this.RefreshLinks();
    }

    /// <summary>Raises <see cref="LinkClicked"/> when a left-click release lands inside a link span.</summary>
    private void HandleButtonReleased(nint eventPtr)
    {
        if (!_detectUrls)
            return;

        double x, y;
        uint button;
        unsafe
        {
            // GdkEventButton, x64 layout: type(0) window(8) send_event(16) time(20) x(24) y(32)
            // axes(40) state(48) button(52) — read the three fields we need by offset.
            var p = (byte*)eventPtr;
            x = *(double*)(p + 24);
            y = *(double*)(p + 32);
            button = *(uint*)(p + 52);
        }

        if (button != 1)
            return;

        NativeMethods.gtk_text_view_window_to_buffer_coords(_textView, NativeMethods.GTK_TEXT_WINDOW_WIDGET, (int)x, (int)y, out var bufferX, out var bufferY);
        NativeMethods.gtk_text_view_get_iter_at_location(_textView, out var iter, bufferX, bufferY);

        var tag = this.EnsureTag("link");
        if (NativeMethods.gtk_text_iter_has_tag(in iter, tag) == 0)
            return;

        // Expand to the tagged span: URLs are whitespace-delimited, so the managed scan suffices.
        var text = this.GetText();
        var offset = Math.Clamp(NativeMethods.gtk_text_iter_get_offset(in iter), 0, text.Length);
        var start = offset;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            --start;

        var length = UrlLengthAt(text, start);
        if (length != 0)
            LinkClicked?.Invoke(this, text.Substring(start, length));
    }

    /// <summary>
    /// Native handler for the buffer's "changed" signal, shaped as
    /// <c>void (GObject *emitter, gpointer user_data)</c>; recovers the peer from
    /// <paramref name="userData"/>.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnChanged(nint emitter, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkRichTextBoxPeer peer)
            peer.HandleChanged();
    }

    /// <summary>
    /// Native handler for the view's "button-release-event" signal, shaped as
    /// <c>gboolean (GtkWidget *widget, GdkEvent *event, gpointer user_data)</c>. Always returns 0
    /// so GTK's own selection handling still runs.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnButtonReleased(nint widget, nint eventPtr, nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkRichTextBoxPeer peer)
            peer.HandleButtonReleased(eventPtr);

        return 0;
    }
}
