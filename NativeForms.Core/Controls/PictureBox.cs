using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn image surface. Shows one <see cref="IImage"/> — or an <see cref="AnimatedImage"/>,
/// whose current frame is picked from elapsed time and repainted by the shared animation clock — under
/// a <see cref="SizeMode"/> policy (top-left at native size, stretched, centered, aspect-fit zoomed, or
/// fit-to-width / fit-to-height), clipped to the client area, with an optional themed single-line border.
/// </summary>
public class PictureBox : OwnerDrawnControl
{
    /// <inheritdoc/>
    private protected override AccessibleRole DefaultAccessibleRole => AccessibleRole.Graphic;

    /// <summary>
    /// The image to display, or <see langword="null"/>. It may be an <see cref="AnimatedImage"/> (which
    /// is an <see cref="IImage"/>): when animated the box subscribes to the shared animation clock and
    /// repaints as the frame advances, and a disabled box freezes on and greys the current frame.
    /// </summary>
    public IImage? Image
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.UpdateImageAnimation();
            this.Invalidate();
        }
    }

    /// <inheritdoc/>
    private protected override IImage? AnimatedImageSlot => this.Image;

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

    /// <summary>The checker tile size, in pixels, of a transparency backdrop drawn behind the image so
    /// translucent regions read against a grid instead of a flat fill. <c>0</c> (the default) keeps the
    /// plain <see cref="ITheme.ControlBackground"/> fill; any positive value turns the checkerboard on.</summary>
    public int TransparencyGridSize
    {
        get => field;
        set
        {
            value = Math.Max(0, value);
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    }

    /// <summary>The two colours of the transparency backdrop checkerboard (see <see cref="TransparencyGridSize"/>).</summary>
    public Color TransparencyGridColor1
    {
        get => field;
        set { if (field != value) { field = value; this.Invalidate(); } }
    } = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <inheritdoc cref="TransparencyGridColor1"/>
    public Color TransparencyGridColor2
    {
        get => field;
        set { if (field != value) { field = value; this.Invalidate(); } }
    } = Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC);

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var client = new Rectangle(0, 0, this.Width, this.Height);
        if (this.TransparencyGridSize > 0)
            this.PaintTransparencyGrid(g, client);
        else
            g.FillRectangle(theme.ControlBackground, client);

        // CurrentFrameOf resolves an animated image to its current frame (frozen and greyed while
        // disabled) and returns a still image unchanged, so one path serves both.
        if (this.Image is { Width: > 0, Height: > 0 } image && this.CurrentFrameOf(image) is { } frame)
        {
            g.PushClip(client);
            g.DrawImage(frame, GetImageRectangle(client.Size, new Size(image.Width, image.Height), this.SizeMode));
            g.PopClip();
        }

        if (this.BorderStyle != BorderStyle.None)
            g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }

    /// <summary>Fills the client with a two-colour checker so translucency shows against a grid.</summary>
    private void PaintTransparencyGrid(IGraphics g, Rectangle client)
    {
        var size = this.TransparencyGridSize;
        g.FillRectangle(this.TransparencyGridColor1, client);
        for (var y = 0; y < client.Height; y += size)
        for (var x = 0; x < client.Width; x += size)
        {
            if (((x / size) + (y / size)) % 2 == 0)
                continue;

            var w = Math.Min(size, client.Width - x);
            var h = Math.Min(size, client.Height - y);
            g.FillRectangle(this.TransparencyGridColor2, new Rectangle(x, y, w, h));
        }
    }

    /// <summary>Computes the destination rectangle the image is drawn into for a given mode.</summary>
    private static Rectangle GetImageRectangle(Size client, Size image, PictureBoxSizeMode mode)
        => mode switch
        {
            PictureBoxSizeMode.StretchImage => new(Point.Empty, client),
            PictureBoxSizeMode.CenterImage
                => new((client.Width - image.Width) / 2, (client.Height - image.Height) / 2, image.Width, image.Height),
            PictureBoxSizeMode.Zoom => Zoom(client, image),
            PictureBoxSizeMode.FitToWidth => FitToWidth(client, image),
            PictureBoxSizeMode.FitToHeight => FitToHeight(client, image),
            _ => new(Point.Empty, image),
        };

    /// <summary>Scales the image so its width fills the client area (aspect kept) and centers it vertically.</summary>
    private static Rectangle FitToWidth(Size client, Size image)
    {
        var width = client.Width;
        var height = image.Height * client.Width / image.Width;
        return new(0, (client.Height - height) / 2, width, height);
    }

    /// <summary>Scales the image so its height fills the client area (aspect kept) and centers it horizontally.</summary>
    private static Rectangle FitToHeight(Size client, Size image)
    {
        var height = client.Height;
        var width = image.Width * client.Height / image.Height;
        return new((client.Width - width) / 2, 0, width, height);
    }

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
