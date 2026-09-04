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
internal sealed class CocoaGraphics(nint context) : IGraphics, IDisposable {
  private readonly nint _context = Enter(context);

  /// <summary>Saves the state this instance will restore, leaving the flipped view's transform alone.</summary>
  private static nint Enter(nint context) {
    CocoaNative.CGContextSaveGState(context);
    return context;
  }

  private void SetColor(Color color, bool fill) {
    double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0, a = color.A / 255.0;
    if (fill)
      CocoaNative.CGContextSetRGBFillColor(_context, r, g, b, a);
    else
      CocoaNative.CGContextSetRGBStrokeColor(_context, r, g, b, a);
  }

  private static CocoaRuntime.CGRect Rect(Rectangle bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

  public void FillRectangle(Color color, Rectangle bounds) {
    this.SetColor(color, fill: true);
    CocoaNative.CGContextFillRect(_context, Rect(bounds));
  }

  /// <summary>
  /// Outlines <paramref name="bounds"/>, on the pixel grid rather than across it.
  /// </summary>
  /// <remarks>
  /// A stroke is centred on its path, so a one-pixel line laid along an integer edge covers half of
  /// the pixel on each side of it and lands as two rows at half coverage — and half of a colour the
  /// desktop already draws borders in at a tenth of black is nothing you can see. Every frame this
  /// backend painted was that: the ribbon's group panels, the grid's cell buttons, a picture box, a
  /// table layout's cell rules. The half-pixel offset is the same one <see cref="DrawLine"/> already
  /// used and the same one the Cairo backend applies here, so a rectangle and the lines beside it
  /// land on the same grid.
  /// </remarks>
  public void DrawRectangle(Color color, Rectangle bounds, int thickness = 1) {
    this.SetColor(color, fill: false);
    CocoaNative.CGContextSetLineWidth(_context, thickness);
    CocoaNative.CGContextStrokeRect(_context, new(bounds.X + 0.5, bounds.Y + 0.5, bounds.Width - 1, bounds.Height - 1));
  }

  public void FillEllipse(Color color, Rectangle bounds) {
    this.SetColor(color, fill: true);
    CocoaNative.CGContextFillEllipseInRect(_context, Rect(bounds));
  }

  public void DrawEllipse(Color color, Rectangle bounds, int thickness = 1) {
    this.SetColor(color, fill: false);
    CocoaNative.CGContextSetLineWidth(_context, thickness);
    CocoaNative.CGContextStrokeEllipseInRect(_context, Rect(bounds));
  }

  public void FillRoundedRectangle(Color color, Rectangle bounds, int radius) {
    if (bounds.Width <= 0 || bounds.Height <= 0)
      return;

    radius = ClampRadius(radius, bounds);
    if (radius <= 0) {
      this.FillRectangle(color, bounds);
      return;
    }

    this.SetColor(color, fill: true);
    this.AddRoundedRectPath(bounds.X, bounds.Y, bounds.Right, bounds.Bottom, radius);
    CocoaNative.CGContextFillPath(_context);
  }

  /// <inheritdoc cref="FillRoundedRectangle"/>
  public void DrawRoundedRectangle(Color color, Rectangle bounds, int radius, int thickness = 1) {
    if (thickness <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
      return;

    radius = ClampRadius(radius, bounds);
    if (radius <= 0) {
      this.DrawRectangle(color, bounds, thickness);
      return;
    }

    this.SetColor(color, fill: false);
    CocoaNative.CGContextSetLineWidth(_context, thickness);

    // The edges sit exactly where DrawRectangle's do — half a pixel in, so the stroke lands on the
    // grid instead of across it — which is also where the Cairo backend puts them.
    this.AddRoundedRectPath(bounds.X + 0.5, bounds.Y + 0.5, bounds.Right - 0.5, bounds.Bottom - 0.5, radius);
    CocoaNative.CGContextStrokePath(_context);
  }

  /// <summary>Limits a corner radius to half the rectangle's smaller dimension.</summary>
  /// <remarks>The same clamp the Cairo backend applies, so a pill asked for a radius larger than it
  /// can hold is the same shape on both — a capsule, not a shape CoreGraphics had to invent.</remarks>
  private static int ClampRadius(int radius, Rectangle bounds)
      => Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);

  /// <summary>
  /// Lays down a rounded rectangle as four corner arcs joined by the straight runs between them.
  /// </summary>
  /// <remarks>
  /// <c>CGContextAddArcToPoint</c> takes the corner it is cutting and where the path goes next, and
  /// fits the arc of the given radius tangent to both — so the path is described by the rectangle's
  /// own corners rather than by four centres and eight angles worked out by hand. It also needs a
  /// current point to start from, which is why the run begins on the top edge past the first corner
  /// and not at the corner itself.
  /// </remarks>
  private void AddRoundedRectPath(double left, double top, double right, double bottom, int radius) {
    CocoaNative.CGContextBeginPath(_context);
    CocoaNative.CGContextMoveToPoint(_context, left + radius, top);
    CocoaNative.CGContextAddArcToPoint(_context, right, top, right, bottom, radius);
    CocoaNative.CGContextAddArcToPoint(_context, right, bottom, left, bottom, radius);
    CocoaNative.CGContextAddArcToPoint(_context, left, bottom, left, top, radius);
    CocoaNative.CGContextAddArcToPoint(_context, left, top, right, top, radius);
    CocoaNative.CGContextClosePath(_context);
  }

  public void DrawLine(Color color, int x1, int y1, int x2, int y2, int thickness = 1) {
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
  public void DrawText(string text, Font font, Color color, Rectangle bounds, ContentAlignment alignment = ContentAlignment.TopLeft) {
    if (text.Length == 0)
      return;

    var size = this.MeasureText(text, font);
    var x = alignment switch {
      ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter
          => bounds.X + ((bounds.Width - size.Width) / 2),
      ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
          => bounds.Right - size.Width,
      _ => bounds.X,
    };

    var top = alignment switch {
      ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight
          => bounds.Y + ((bounds.Height - size.Height) / 2),
      ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight
          => bounds.Bottom - size.Height,
      _ => bounds.Y,
    };

    // CoreText draws from the baseline, not the top of the line box; the ascent is most of the
    // height, and using the top directly puts every string one line too high.
    CocoaNative.TryDrawText(_context, text, font, x, top + (size.Height * 0.8), color);
  }

  public Size MeasureText(string text, Font font)
      => CocoaNative.TryMeasure(text, font, out var width, out var height)
          ? new((int)Math.Ceiling(width), (int)Math.Ceiling(height))
          : Size.Empty;

  /// <summary>
  /// Blits a backend bitmap into <paramref name="bounds"/>, scaling it to fit.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The flip is handled here and nowhere else. <c>CGContextDrawImage</c> lays an image out from the
  /// bottom of the rectangle upward, which is right in CoreGraphics' own coordinates and wrong in
  /// this context: the canvas view answers <c>isFlipped</c>, so y grows downward and an image drawn
  /// straight arrives on its head. Mirroring the context about the destination rectangle corrects it
  /// inside a saved state, so the text and the primitives around it keep the transform they expect —
  /// flipping the whole context instead would put every string upside down.
  /// </para>
  /// <para>
  /// An image from another backend, or one already disposed, draws nothing rather than throwing: a
  /// painter is called from a native draw callback, where an exception has nowhere to go.
  /// </para>
  /// </remarks>
  public void DrawImage(IImage image, Rectangle bounds) {
    if (image is not CocoaImage native || bounds.Width <= 0 || bounds.Height <= 0)
      return;

    var handle = native.Handle;
    if (handle == 0)
      return;

    CocoaNative.CGContextSaveGState(_context);
    CocoaNative.CGContextTranslateCTM(_context, bounds.X, bounds.Y + bounds.Height);
    CocoaNative.CGContextScaleCTM(_context, 1, -1);
    CocoaNative.CGContextDrawImage(_context, new CocoaRuntime.CGRect(0, 0, bounds.Width, bounds.Height), handle);
    CocoaNative.CGContextRestoreGState(_context);
  }

  public void PushClip(Rectangle bounds) {
    CocoaNative.CGContextSaveGState(_context);
    CocoaNative.CGContextClipToRect(_context, Rect(bounds));
  }

  public void PopClip() => CocoaNative.CGContextRestoreGState(_context);

  /// <summary>Restores the state, leaving the context as AppKit handed it over.</summary>
  public void Dispose() => CocoaNative.CGContextRestoreGState(_context);
}
