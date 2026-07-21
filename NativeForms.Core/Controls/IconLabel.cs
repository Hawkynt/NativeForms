using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A caption that renders an image <em>and</em> text together, owner-drawn in the platform theme.
/// </summary>
/// <remarks>
/// <see cref="Label"/> is backed by the native static widget, and no toolkit renders both parts in
/// one of those: Win32's <c>SS_BITMAP</c> static is image-only and GTK swaps the whole widget for a
/// <c>GtkImage</c>, so a captioned <see cref="Label"/> keeps its text and drops its image (see
/// <c>docs/controls/label.md</c>). This control gives up the native widget to get both, laying the
/// two parts out through the shared <see cref="Drawing.ContentLayout"/> — the same geometry
/// <see cref="Button"/>, <see cref="CheckBox"/> and <see cref="GroupBox"/> use — and painting with
/// the ambient <see cref="Control.Font"/> and <see cref="Control.ForeColor"/> so it still matches
/// the desktop. Like <see cref="Label"/> it takes no focus and handles no input.
/// </remarks>
public class IconLabel : OwnerDrawnControl
{
    /// <summary>The image shown beside the caption, or <see langword="null"/> for text only.</summary>
    public IImage? Image
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.ApplyAutoSize();
            this.Invalidate();
        }
    }

    /// <summary>
    /// Where the whole image+text block anchors within the control's bounds. Defaults to
    /// <see cref="ContentAlignment.MiddleLeft"/>, the reading position a captioned icon wants.
    /// </summary>
    public ContentAlignment TextAlign
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    } = ContentAlignment.MiddleLeft;

    /// <summary>
    /// Where the image anchors when it is the only content — a text-less <see cref="IconLabel"/>
    /// places its image by this rather than by <see cref="TextAlign"/>, matching Windows Forms.
    /// </summary>
    public ContentAlignment ImageAlign
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    } = ContentAlignment.MiddleCenter;

    /// <summary>
    /// How the image sits relative to the text. Defaults to
    /// <see cref="TextImageRelation.ImageBeforeText"/> — the icon leads, the caption follows.
    /// </summary>
    public TextImageRelation TextImageRelation
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.ApplyAutoSize();
            this.Invalidate();
        }
    } = TextImageRelation.ImageBeforeText;

    /// <summary>
    /// When <see langword="true"/>, the label sizes itself to fit image and text in the ambient font.
    /// Measured through the backend on realization and on every content change; before realization
    /// the wish is simply buffered. Defaults to <see langword="false"/>, matching Windows Forms.
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

    /// <summary>Static text never takes keyboard focus (and so never joins the tab order).</summary>
    protected override bool Focusable => false;

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var font = this.Font;
        var client = this.DisplayRectangle;
        g.FillRectangle(this.BackColor, new Rectangle(0, 0, this.Width, this.Height));

        var text = this.Text;
        var color = this.Enabled ? this.ForeColor : this.Theme.DisabledText;
        var image = this.Image;
        if (image is null)
        {
            g.DrawText(text, font, color, client, this.EffectiveAlignment);
            return;
        }

        // Right-to-left mirrors which side the icon leads on, exactly like the CheckBox face.
        var relation = this.IsRightToLeft ? Mirror(this.TextImageRelation) : this.TextImageRelation;
        ContentLayout.Arrange(
            client,
            new Size(image.Width, image.Height),
            text.Length == 0 ? Size.Empty : g.MeasureText(text, font),
            relation,
            text.Length == 0 ? this.ImageAlign : this.EffectiveAlignment,
            out var imageRect,
            out var textRect);

        g.DrawImage(image, imageRect);
        if (text.Length > 0)
            g.DrawText(text, font, color, textRect, ContentAlignment.MiddleCenter);
    }

    /// <inheritdoc/>
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        this.ApplyAutoSize();
    }

    /// <summary>The block anchor, mirrored when the control reads right to left.</summary>
    private ContentAlignment EffectiveAlignment
        => this.IsRightToLeft ? RtlLayout.Mirror(this.TextAlign) : this.TextAlign;

    /// <summary>Resizes to the measured image+text block when <see cref="AutoSize"/> is on.</summary>
    private void ApplyAutoSize()
    {
        if (!this.AutoSize || this.Backend is not { } backend)
            return;

        var text = this.Text;
        var textSize = text.Length == 0 ? Size.Empty : backend.MeasureText(text, this.Font);
        if (this.Image is not { } image)
        {
            this.Size = textSize;
            return;
        }

        var imageSize = new Size(image.Width, image.Height);
        if (textSize.IsEmpty)
        {
            this.Size = imageSize;
            return;
        }

        this.Size = this.TextImageRelation is TextImageRelation.ImageBeforeText or TextImageRelation.TextBeforeImage
            ? new(imageSize.Width + ContentLayout.Gap + textSize.Width, Math.Max(imageSize.Height, textSize.Height))
            : new(Math.Max(imageSize.Width, textSize.Width), imageSize.Height + ContentLayout.Gap + textSize.Height);
    }

    /// <summary>Swaps the leading side of a horizontal relation; vertical ones are unaffected.</summary>
    private static TextImageRelation Mirror(TextImageRelation relation) => relation switch
    {
        TextImageRelation.ImageBeforeText => TextImageRelation.TextBeforeImage,
        TextImageRelation.TextBeforeImage => TextImageRelation.ImageBeforeText,
        _ => relation,
    };

    /// <inheritdoc/>
    private protected override void OnRealized(Backends.IControlPeer peer)
    {
        base.OnRealized(peer);
        this.ApplyAutoSize();
    }
}
