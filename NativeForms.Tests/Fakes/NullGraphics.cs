using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Tests.Fakes;

/// <summary>
/// An <see cref="IGraphics"/> that draws nothing and allocates nothing — the surface the
/// steady-state paint-allocation test paints onto, so every byte the measurement sees comes from
/// the control's own paint path. Text measures with the same deterministic metrics as
/// <see cref="RecordingGraphics"/>.
/// </summary>
internal sealed class NullGraphics : IGraphics
{
    public void FillRectangle(Color color, Rectangle bounds) { }
    public void DrawRectangle(Color color, Rectangle bounds, int thickness = 1) { }
    public void FillEllipse(Color color, Rectangle bounds) { }
    public void DrawEllipse(Color color, Rectangle bounds, int thickness = 1) { }
    public void FillRoundedRectangle(Color color, Rectangle bounds, int radius) { }
    public void DrawRoundedRectangle(Color color, Rectangle bounds, int radius, int thickness = 1) { }
    public void DrawLine(Color color, int x1, int y1, int x2, int y2, int thickness = 1) { }
    public void DrawText(string text, Font font, Color color, Rectangle bounds, ContentAlignment alignment = ContentAlignment.TopLeft) { }
    public Size MeasureText(string text, Font font) => RecordingGraphics.Measure(text);
    public void DrawImage(IImage image, Rectangle bounds) { }
    public void PushClip(Rectangle bounds) { }
    public void PopClip() { }
}
