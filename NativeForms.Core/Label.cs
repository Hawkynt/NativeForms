using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A non-interactive line of static text, backed by the platform's native label widget. Supports
/// WinForms-style <see cref="AutoSize"/>, <see cref="TextAlign"/>, <see cref="BorderStyle"/> and
/// mnemonic rendering (<see cref="UseMnemonic"/>).
/// </summary>
public class Label : Control
{
    private ILabelPeer? _labelPeer;

    /// <summary>Static text never takes keyboard focus (and so never joins the tab order).</summary>
    protected override bool Focusable => false;

    /// <summary>
    /// When <see langword="true"/>, the label sizes itself to fit its text in the theme's default
    /// font. The size is computed through the backend's text measurement on realization and again on
    /// every <see cref="Control.Text"/> change; before realization the wish is simply buffered.
    /// Defaults to <see langword="false"/>, matching Windows Forms.
    /// </summary>
    public bool AutoSize
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.ApplyAutoSize();
        }
    }

    /// <summary>
    /// Where the text sits within the label's bounds. Win32 static controls honor the horizontal
    /// component plus a coarse vertical centering only; GTK honors all nine anchors.
    /// </summary>
    public ContentAlignment TextAlign
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _labelPeer?.SetTextAlign(value);
        }
    }

    /// <summary>
    /// The border drawn around the label — <see cref="BorderStyle.None"/> or
    /// <see cref="BorderStyle.FixedSingle"/>. Rendered natively on Win32 (<c>WS_BORDER</c>); GTK has
    /// no native label frame, so the value is not rendered there.
    /// </summary>
    public BorderStyle BorderStyle
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _labelPeer?.SetBorderStyle(value);
        }
    } = BorderStyle.None;

    /// <summary>
    /// Whether <c>&amp;</c> in <see cref="Control.Text"/> marks the following character as a mnemonic
    /// and renders it underlined (<c>&amp;&amp;</c> escapes a literal ampersand). Alt+mnemonic
    /// focuses the next tab stop after the label through the owning form's dialog-key chain — fed by
    /// owner-drawn surfaces; keys held inside native widgets cannot trigger it yet. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool UseMnemonic
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _labelPeer?.SetUseMnemonic(value);
        }
    } = true;

    /// <summary>
    /// The image shown by the label, or <see langword="null"/>. Rendered natively only while
    /// <see cref="Control.Text"/> is empty (Win32 shows an <c>SS_BITMAP</c> static, GTK swaps in a
    /// <c>GtkImage</c>) — no toolkit renders image and text in one static widget, so a captioned
    /// label keeps its text and the image stays pending there (see <c>docs/PRD.md</c> §7.3).
    /// </summary>
    public IImage? Image
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.TrackImageAnimation(value, this.PushImage);
            this.PushImage();
        }
    }

    /// <summary>
    /// Where the image anchors within the label's bounds. Advisory for now: the native image-only
    /// renderings ignore it (Win32 pins the bitmap top-left, GTK centers it). Defaults to
    /// <see cref="ContentAlignment.MiddleCenter"/>, matching Windows Forms.
    /// </summary>
    public ContentAlignment ImageAlign
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.PushImage();
        }
    } = ContentAlignment.MiddleCenter;

    /// <summary>
    /// The label's uppercased mnemonic character — the one after a single <c>&amp;</c> in
    /// <see cref="Control.Text"/> (<c>&amp;&amp;</c> escapes) — or <c>'\0'</c> when there is none or
    /// <see cref="UseMnemonic"/> is off.
    /// </summary>
    internal char Mnemonic
    {
        get
        {
            if (!this.UseMnemonic)
                return '\0';

            var text = this.Text;
            for (var i = 0; i < text.Length - 1; ++i)
            {
                if (text[i] != '&')
                    continue;

                if (text[i + 1] == '&')
                {
                    ++i;
                    continue;
                }

                return char.ToUpperInvariant(text[i + 1]);
            }

            return '\0';
        }
    }

    private protected override IControlPeer CreatePeer(IPlatformBackend backend) => backend.CreateLabel();

    /// <inheritdoc/>
    private protected override void OnRealized(IControlPeer peer)
    {
        if (peer is not ILabelPeer label)
            return;

        _labelPeer = label;
        label.SetTextAlign(this.TextAlign);
        label.SetBorderStyle(this.BorderStyle);
        label.SetUseMnemonic(this.UseMnemonic);
        this.PushImage();
        this.TrackImageAnimation(this.Image, this.PushImage); // subscribe now that a backend exists
        this.ApplyAutoSize();
    }

    /// <summary>Pushes the image to the peer, resolving an animated image to its current frame — the
    /// shared clock calls this again as the frame advances.</summary>
    private void PushImage() => _labelPeer?.SetImage(this.CurrentFrameOf(this.Image), this.ImageAlign);

    /// <inheritdoc/>
    private protected override void OnUnrealized() => _labelPeer = null;

    /// <inheritdoc/>
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        this.ApplyAutoSize();
    }

    /// <summary>Resizes the label to its measured text when <see cref="AutoSize"/> is on and a backend exists.</summary>
    private void ApplyAutoSize()
    {
        if (!this.AutoSize)
            return;

        var backend = this.Backend;
        if (backend is null)
            return;

        this.Size = backend.MeasureText(this.Text, backend.Theme.DefaultFont);
    }
}
