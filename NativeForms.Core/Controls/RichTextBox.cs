using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Text;

namespace Hawkynt.NativeForms;

/// <summary>
/// A rich-text editor: a <see cref="TextBox"/> (always multiline) whose selection can carry
/// character styles, color and size, whose paragraphs can be aligned and bulleted, and whose whole
/// document round-trips as RTF. Backed by the platform's rich editor (Win32 <c>RICHEDIT50W</c>, a
/// tagged <c>GtkTextView</c>), so editing, caret and clipboard behave natively.
/// </summary>
/// <remarks>
/// The <c>Selection…</c> properties are write-through commands: setting one formats whatever is
/// selected in the widget <em>right now</em>. Their getters return the last value written (or the
/// default), not the format under the caret — reading mixed-selection state back is not part of the
/// peer contract. For the same reason they only take effect while the control is realized; unlike
/// the buffered <see cref="TextBox"/> settings there is no meaningful "selection formatting" to
/// flush into a widget that does not exist yet. <see cref="Rtf"/> <em>is</em> buffered: assigned
/// before realization it is pushed into the fresh widget (after the plain <see cref="TextBox.Text"/>,
/// so the richer of the two wins).
/// </remarks>
public class RichTextBox : TextBox
{
    private IRichTextBoxPeer? _peer;

    /// <summary>The RTF buffered for realization, or <see langword="null"/> when plain <see cref="TextBox.Text"/> rules.</summary>
    private string? _rtf;

    /// <summary>Creates the editor; a rich text box is always multiline.</summary>
    public RichTextBox() => this.Multiline = true;

    /// <summary>Raised when the user clicks an auto-detected link (see <see cref="DetectUrls"/>).</summary>
    public event EventHandler<LinkClickedEventArgs>? LinkClicked;

    /// <summary>
    /// The content of the box. Assigning plain text discards any RTF buffered for realization —
    /// last writer wins.
    /// </summary>
    public override string Text
    {
        get => base.Text;
        set
        {
            _rtf = null;
            base.Text = value;
        }
    }

    /// <summary>Whether the current selection is bold. Writing formats the selection.</summary>
    public bool SelectionBold
    {
        get => field;
        set
        {
            field = value;
            _peer?.SetSelectionStyle(FontStyle.Bold, value);
        }
    }

    /// <summary>Whether the current selection is italic. Writing formats the selection.</summary>
    public bool SelectionItalic
    {
        get => field;
        set
        {
            field = value;
            _peer?.SetSelectionStyle(FontStyle.Italic, value);
        }
    }

    /// <summary>Whether the current selection is underlined. Writing formats the selection.</summary>
    public bool SelectionUnderline
    {
        get => field;
        set
        {
            field = value;
            _peer?.SetSelectionStyle(FontStyle.Underline, value);
        }
    }

    /// <summary>Whether the current selection is struck through. Writing formats the selection.</summary>
    public bool SelectionStrikeout
    {
        get => field;
        set
        {
            field = value;
            _peer?.SetSelectionStyle(FontStyle.Strikeout, value);
        }
    }

    /// <summary>The text color of the current selection; <see cref="Color.Empty"/> means the default.</summary>
    public Color SelectionColor
    {
        get => field;
        set
        {
            field = value;
            _peer?.SetSelectionColor(value);
        }
    }

    /// <summary>The font size, in points, of the current selection; 0 means the default.</summary>
    public float SelectionFontSize
    {
        get => field;
        set
        {
            field = value;
            _peer?.SetSelectionFontSize(value);
        }
    }

    /// <summary>
    /// The alignment of the paragraphs the current selection touches. Only the horizontal component
    /// (left/center/right) is meaningful.
    /// </summary>
    public ContentAlignment SelectionAlignment
    {
        get => field;
        set
        {
            field = value;
            _peer?.SetSelectionAlignment(value);
        }
    }

    /// <summary>Whether the paragraphs the current selection touches are bulleted list items.</summary>
    public bool SelectionBullet
    {
        get => field;
        set
        {
            field = value;
            _peer?.SetSelectionBullet(value);
        }
    }

    /// <summary>Whether URLs in the text are detected, rendered as links and raise <see cref="LinkClicked"/>.</summary>
    public bool DetectUrls
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _peer?.SetDetectUrls(value);
        }
    } = true;

    /// <summary>The display scale of the text (1.0 = normal size); a pure view setting, not part of the document.</summary>
    public float ZoomFactor
    {
        get => field;
        set
        {
            if (field.Equals(value))
                return;

            field = value;
            _peer?.SetZoom(value);
        }
    } = 1f;

    /// <summary>
    /// The whole document as RTF (the NativeForms subset — see <see cref="RtfSerializer"/>).
    /// Before realization the getter serializes the buffered state; afterwards both directions go
    /// straight through the native widget.
    /// </summary>
    public string Rtf
    {
        get => _peer?.GetRtf() ?? _rtf ?? RtfSerializer.Write(RichDocument.FromPlainText(this.Text));
        set
        {
            value ??= string.Empty;
            if (_peer is not null)
            {
                // The widget parses it and reports the resulting plain text back like a user edit.
                _peer.SetRtf(value);
                return;
            }

            _rtf = value;
            base.Text = RtfSerializer.Parse(value).ToPlainText();
        }
    }

    private protected override IControlPeer CreatePeer(IPlatformBackend backend) => backend.CreateRichTextBox();

    private protected override void OnRealized(IControlPeer peer)
    {
        base.OnRealized(peer);
        if (peer is not IRichTextBoxPeer rich)
            return;

        _peer = rich;
        rich.LinkClicked += this.OnPeerLinkClicked;
        rich.SetDetectUrls(this.DetectUrls);
        if (this.ZoomFactor != 1f)
            rich.SetZoom(this.ZoomFactor);

        if (_rtf is not null)
            rich.SetRtf(_rtf);
    }

    private protected override void OnUnrealized()
    {
        if (_peer is not null)
        {
            // The native widget is already gone here, so the peer answers from its buffers — the
            // last RTF pushed into it, or its buffered plain text. Keeping that beats flattening a
            // re-realized box to whatever plain text the core last saw.
            _rtf = _peer.GetRtf();
            _peer.LinkClicked -= this.OnPeerLinkClicked;
            _peer = null;
        }

        base.OnUnrealized();
    }

    /// <summary>Raises <see cref="LinkClicked"/>.</summary>
    protected virtual void OnLinkClicked(LinkClickedEventArgs e) => LinkClicked?.Invoke(this, e);

    /// <summary>Translates the peer's link notification into the WinForms-shaped event.</summary>
    private void OnPeerLinkClicked(object? sender, string linkText) => this.OnLinkClicked(new(linkText));
}
