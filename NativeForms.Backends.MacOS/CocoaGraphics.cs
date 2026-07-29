using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// The drawing surface for an owner-drawn control, backed by a <c>CGContext</c>.
/// </summary>
/// <remarks>
/// <para>
/// CoreGraphics is plain C, so every primitive here is a direct <c>[LibraryImport]</c> call with no
/// messaging involved.
/// </para>
/// <para>
/// The context arrives already in the toolkit's coordinates and is left that way. The canvas view
/// answers <c>isFlipped</c>, which is AppKit's own way of saying "origin top left, y downward", and it
/// flips the drawing context to match — so flipping again here mirrors everything. It did: the grid's
/// header row photographed at the bottom of the canvas with its text upside down, which is what a
/// double flip looks like and is easy to miss while the only things drawn are symmetric rectangles.
/// </para>
/// </remarks>
internal sealed class CocoaGraphics(nint context) : IGraphics, IDisposable
{
    private readonly nint _context = Enter(context);

    /// <summary>Saves the state this instance will restore, leaving the flipped view's transform alone.</summary>
    private static nint Enter(nint context)
    {
        CocoaNative.CGContextSaveGState(context);
        return context;
    }

    private void SetColor(Color color, bool fill)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0, a = color.A / 255.0;
        if (fill)
            CocoaNative.CGContextSetRGBFillColor(_context, r, g, b, a);
        else
            CocoaNative.CGContextSetRGBStrokeColor(_context, r, g, b, a);
    }

    private static CocoaRuntime.CGRect Rect(Rectangle bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    public void FillRectangle(Color color, Rectangle bounds)
    {
        this.SetColor(color, fill: true);
        CocoaNative.CGContextFillRect(_context, Rect(bounds));
    }

    public void DrawRectangle(Color color, Rectangle bounds, int thickness = 1)
    {
        this.SetColor(color, fill: false);
        CocoaNative.CGContextSetLineWidth(_context, thickness);
        CocoaNative.CGContextStrokeRect(_context, Rect(bounds));
    }

    public void FillEllipse(Color color, Rectangle bounds)
    {
        this.SetColor(color, fill: true);
        CocoaNative.CGContextFillEllipseInRect(_context, Rect(bounds));
    }

    public void DrawEllipse(Color color, Rectangle bounds, int thickness = 1)
    {
        this.SetColor(color, fill: false);
        CocoaNative.CGContextSetLineWidth(_context, thickness);
        CocoaNative.CGContextStrokeEllipseInRect(_context, Rect(bounds));
    }

    /// <remarks>Square corners for now: the rounded path wants an arc-by-arc build, and a wrong-shaped
    /// rectangle is a cosmetic gap where a missing one is a hole in the picture.</remarks>
    public void FillRoundedRectangle(Color color, Rectangle bounds, int radius) => this.FillRectangle(color, bounds);

    /// <inheritdoc cref="FillRoundedRectangle"/>
    public void DrawRoundedRectangle(Color color, Rectangle bounds, int radius, int thickness = 1)
        => this.DrawRectangle(color, bounds, thickness);

    public void DrawLine(Color color, int x1, int y1, int x2, int y2, int thickness = 1)
    {
        this.SetColor(color, fill: false);
        CocoaNative.CGContextSetLineWidth(_context, thickness);
        CocoaNative.CGContextBeginPath(_context);
        CocoaNative.CGContextMoveToPoint(_context, x1 + 0.5, y1 + 0.5);
        CocoaNative.CGContextAddLineToPoint(_context, x2 + 0.5, y2 + 0.5);
        CocoaNative.CGContextStrokePath(_context);
    }

    /// <summary>
    /// Draws one line of text inside <paramref name="bounds"/>, aligned as asked.
    /// </summary>
    /// <remarks>
    /// The measurement that positions it is the same CoreText call the backend answers
    /// <c>MeasureText</c> with, so what is drawn and what was laid out agree by construction rather
    /// than by two estimates happening to match.
    /// </remarks>
    public void DrawText(string text, Font font, Color color, Rectangle bounds, ContentAlignment alignment = ContentAlignment.TopLeft)
    {
        if (text.Length == 0)
            return;

        var size = this.MeasureText(text, font);
        var x = alignment switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter
                => bounds.X + ((bounds.Width - size.Width) / 2),
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
                => bounds.Right - size.Width,
            _ => bounds.X,
        };

        var top = alignment switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight
                => bounds.Y + ((bounds.Height - size.Height) / 2),
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight
                => bounds.Bottom - size.Height,
            _ => bounds.Y,
        };

        // CoreText draws from the baseline, not the top of the line box; the ascent is most of the
        // height, and using the top directly puts every string one line too high.
        CocoaNative.TryDrawText(_context, text, font.Family, font.SizeInPoints, x, top + (size.Height * 0.8), color);
    }

    public Size MeasureText(string text, Font font)
        => CocoaNative.TryMeasure(text, font.Family, font.SizeInPoints, out var width, out var height)
            ? new((int)Math.Ceiling(width), (int)Math.Ceiling(height))
            : Size.Empty;

    /// <inheritdoc cref="DrawText"/>
    public void DrawImage(IImage image, Rectangle bounds) { }

    public void PushClip(Rectangle bounds)
    {
        CocoaNative.CGContextSaveGState(_context);
        CocoaNative.CGContextClipToRect(_context, Rect(bounds));
    }

    public void PopClip() => CocoaNative.CGContextRestoreGState(_context);

    /// <summary>Restores the state, leaving the context as AppKit handed it over.</summary>
    public void Dispose() => CocoaNative.CGContextRestoreGState(_context);
}
