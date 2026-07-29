using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// The drawing surface for an owner-drawn control, backed by a <c>CGContext</c>.
/// </summary>
/// <remarks>
/// CoreGraphics is plain C, so every primitive here is a direct <c>[LibraryImport]</c> call with no
/// messaging involved. Its y-axis grows upward like the rest of Cocoa, so the context is flipped once
/// on construction and every rectangle afterwards is in the toolkit's top-left coordinates — the same
/// decision the window peer makes, made once more in the one other place coordinates cross over.
/// </remarks>
internal sealed class CocoaGraphics(nint context, int height) : IGraphics, IDisposable
{
    private readonly nint _context = Flip(context, height);

    /// <summary>Turns the context upside down so the toolkit's top-left origin is the natural one.</summary>
    private static nint Flip(nint context, int height)
    {
        CocoaNative.CGContextSaveGState(context);
        CocoaNative.CGContextTranslateCTM(context, 0, height);
        CocoaNative.CGContextScaleCTM(context, 1, -1);
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

    /// <remarks>
    /// Not drawn yet: text wants a CoreText line laid out into the flipped context, which is the next
    /// piece rather than this one. Measurement already works, so layout is correct and only the glyphs
    /// are absent — which is exactly what the probe's screenshot will show.
    /// </remarks>
    public void DrawText(string text, Font font, Color color, Rectangle bounds, ContentAlignment alignment = ContentAlignment.TopLeft) { }

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

    /// <summary>Undoes the flip, leaving the context as AppKit handed it over.</summary>
    public void Dispose() => CocoaNative.CGContextRestoreGState(_context);
}
