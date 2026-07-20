using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// The drawing surface passed to a control's paint handler. A minimal, immediate-mode API — enough to
/// render every owner-drawn control in the toolkit — that each backend maps onto its native drawing
/// stack (GDI on Win32, Cairo/GDK on GTK, CoreGraphics on Cocoa). Colors carry alpha; coordinates are
/// device pixels in the control's client space.
/// </summary>
public interface IGraphics
{
    /// <summary>Fills a rectangle with a solid color.</summary>
    void FillRectangle(Color color, Rectangle bounds);

    /// <summary>Strokes the outline of a rectangle.</summary>
    void DrawRectangle(Color color, Rectangle bounds, int thickness = 1);

    /// <summary>Fills an ellipse inscribed in the given bounds.</summary>
    void FillEllipse(Color color, Rectangle bounds);

    /// <summary>Strokes the outline of an ellipse inscribed in the given bounds.</summary>
    void DrawEllipse(Color color, Rectangle bounds, int thickness = 1);

    /// <summary>Fills a rectangle whose corners are rounded with the given radius; a radius of at
    /// least half the smaller dimension yields a pill/circle.</summary>
    void FillRoundedRectangle(Color color, Rectangle bounds, int radius);

    /// <summary>Strokes the outline of a rounded rectangle.</summary>
    void DrawRoundedRectangle(Color color, Rectangle bounds, int radius, int thickness = 1);

    /// <summary>Draws a straight line.</summary>
    void DrawLine(Color color, int x1, int y1, int x2, int y2, int thickness = 1);

    /// <summary>Draws text within <paramref name="bounds"/> using the given alignment.</summary>
    void DrawText(
        string text,
        Font font,
        Color color,
        Rectangle bounds,
        ContentAlignment alignment = ContentAlignment.TopLeft);

    /// <summary>Measures the pixel size a string would occupy in the given font.</summary>
    Size MeasureText(string text, Font font);

    /// <summary>Draws an image, scaled into <paramref name="bounds"/>.</summary>
    void DrawImage(IImage image, Rectangle bounds);

    /// <summary>Intersects the clip region with <paramref name="bounds"/> and pushes it on a stack.</summary>
    void PushClip(Rectangle bounds);

    /// <summary>Restores the clip region pushed by the matching <see cref="PushClip"/>.</summary>
    void PopClip();
}
