using System.Drawing;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The shared native-style primitives in <see cref="GlyphRenderer"/> and the rounded-rectangle
/// surface ops: the combo arrow, header-cell face, focus ring and selection fill must render the
/// documented shapes through the theme, and <see cref="RecordingGraphics"/> must record the new
/// rounded-rectangle calls so control tests can assert on them.
/// </summary>
[TestFixture]
internal sealed class GlyphPrimitiveTests
{
    private static readonly StubTheme _theme = new();

    [Test]
    public void Recording_graphics_records_rounded_rectangles()
    {
        var g = new RecordingGraphics();

        g.FillRoundedRectangle(Color.FromArgb(0xFF, 0x11, 0x22, 0x33), new Rectangle(1, 2, 30, 10), 5);
        g.DrawRoundedRectangle(Color.FromArgb(0xFF, 0x44, 0x55, 0x66), new Rectangle(0, 0, 20, 20), 4);

        Assert.That(g.Operations, Is.EqualTo(new[]
        {
            "fillround #FF112233 1,2,30,10 r5",
            "round #FF445566 0,0,20,20 r4",
        }));
    }

    [Test]
    public void Combo_arrow_paints_five_stacked_lines_centered()
    {
        var g = new RecordingGraphics();

        GlyphRenderer.DrawComboArrow(g, Color.Black, new Rectangle(100, 0, 17, 24));

        // Center x = 108, top = (24-5)/2 + 0 = 9; five lines shrinking toward the apex.
        Assert.That(g.Operations, Is.EqualTo(new[]
        {
            "line #FF000000 104,9-112,9",
            "line #FF000000 105,10-111,10",
            "line #FF000000 106,11-110,11",
            "line #FF000000 107,12-109,12",
            "line #FF000000 108,13-108,13",
        }));
    }

    [Test]
    public void Header_cell_fills_the_face_and_clips_the_caption()
    {
        var g = new RecordingGraphics();

        GlyphRenderer.DrawHeaderCell(g, _theme, new Rectangle(10, 0, 80, 24), "Name", ContentAlignment.MiddleLeft, 2, separator: true);

        Assert.That(g.Operations, Is.EqualTo(new[]
        {
            "fill #FFECECEC 10,0,80,24",
            "clip 10,0,80,24",
            "text \"Name\" #FF303030 MiddleLeft @12,0",
            "unclip",
            "line #FFC8C8C8 90,0-90,24",
        }));
    }

    [Test]
    public void Header_cell_omits_the_separator_on_request()
    {
        var g = new RecordingGraphics();

        GlyphRenderer.DrawHeaderCell(g, _theme, new Rectangle(0, 0, 40, 20), "A", ContentAlignment.MiddleCenter, 4, separator: false);

        Assert.That(g.Operations.Exists(o => o.StartsWith("line ")), Is.False);
    }

    [Test]
    public void Focus_ring_paints_a_faint_accent_rectangle()
    {
        var g = new RecordingGraphics();

        GlyphRenderer.DrawFocusRing(g, _theme, new Rectangle(0, 0, 50, 16));

        // Accent #0078D4 blended halfway toward the control background #FDFDFD.
        Assert.That(g.Operations, Is.EqualTo(new[] { "rect #FF7EBAE8 0,0,50,16" }));
    }

    [Test]
    public void Focus_ring_uses_the_full_accent_under_high_contrast()
    {
        var g = new RecordingGraphics();
        var highContrast = new StubTheme { IsHighContrast = true };

        GlyphRenderer.DrawFocusRing(g, highContrast, new Rectangle(0, 0, 50, 16));

        Assert.That(g.Operations, Is.EqualTo(new[] { "rect #FF0078D4 0,0,50,16" }));
    }

    [Test]
    public void Selection_fill_uses_the_theme_selection_background()
    {
        var g = new RecordingGraphics();

        GlyphRenderer.FillSelection(g, _theme, new Rectangle(0, 22, 100, 22));

        Assert.That(g.Operations, Is.EqualTo(new[] { "fill #FF0078D4 0,22,100,22" }));
    }
}
