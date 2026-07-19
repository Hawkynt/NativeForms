using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn image surface. Shows one <see cref="IImage"/> under a <see cref="SizeMode"/>
/// policy — top-left at native size, stretched, centered, or aspect-fit zoomed — clipped to the
/// client area, with an optional themed single-line border.
/// </summary>
public class PictureBox : OwnerDrawnControl
{
    /// <summary>The image to display, or <see langword="null"/> for background (and border) only.</summary>
    public IImage? Image
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    }

    /// <summary>How the image is fitted into the client area.</summary>
    public PictureBoxSizeMode SizeMode
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    } = PictureBoxSizeMode.Normal;

    /// <summary>
    /// The border drawn around the box — <see cref="BorderStyle.None"/> or
    /// <see cref="BorderStyle.FixedSingle"/> in the theme's border color.
    /// </summary>
    public BorderStyle BorderStyle
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    } = BorderStyle.None;

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var client = new Rectangle(0, 0, this.Width, this.Height);
        g.FillRectangle(theme.ControlBackground, client);

        if (this.Image is { Width: > 0, Height: > 0 } image)
        {
            g.PushClip(client);
            g.DrawImage(image, GetImageRectangle(client.Size, new Size(image.Width, image.Height), this.SizeMode));
            g.PopClip();
        }

        if (this.BorderStyle != BorderStyle.None)
            g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }

    /// <summary>Computes the destination rectangle the image is drawn into for a given mode.</summary>
    private static Rectangle GetImageRectangle(Size client, Size image, PictureBoxSizeMode mode)
        => mode switch
        {
            PictureBoxSizeMode.StretchImage => new(Point.Empty, client),
            PictureBoxSizeMode.CenterImage
                => new((client.Width - image.Width) / 2, (client.Height - image.Height) / 2, image.Width, image.Height),
            PictureBoxSizeMode.Zoom => Zoom(client, image),
            _ => new(Point.Empty, image),
        };

    /// <summary>Aspect-fits the image into the client area and centers the result.</summary>
    private static Rectangle Zoom(Size client, Size image)
    {
        // The relatively wider dimension pins the scale; the cross product avoids fractions.
        int width, height;
        if ((long)image.Width * client.Height >= (long)image.Height * client.Width)
        {
            width = client.Width;
            height = image.Height * client.Width / image.Width;
        }
        else
        {
            height = client.Height;
            width = image.Width * client.Height / image.Height;
        }

        return new((client.Width - width) / 2, (client.Height - height) / 2, width, height);
    }
}
