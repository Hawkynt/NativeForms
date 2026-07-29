using System.Drawing;
using System.Globalization;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Shortening a label to the width it has. Two properties matter and neither is about the ellipsis:
/// the result has to fit, and the cut has to land between glyphs rather than inside one.
/// </summary>
[TestFixture]
internal sealed class TextTrimTests
{
    /// <summary>Ordinary characters are this wide.</summary>
    private const int Narrow = 10;

    /// <summary>An emoji is this wide — the point being that it is not the width of its two chars.</summary>
    private const int Wide = 25;

    /// <summary>
    /// Measures by grapheme cluster rather than by char, the way a real shaper does: an emoji is one
    /// glyph however many code points spell it, and it is wider than a letter. A char-counting
    /// measurement would make every assertion here pass for the wrong reason.
    /// </summary>
    private sealed class ShapingGraphics : IGraphics
    {
        public void FillRectangle(Color color, Rectangle bounds) { }
        public void DrawRectangle(Color color, Rectangle bounds, int thickness = 1) { }
        public void FillEllipse(Color color, Rectangle bounds) { }
        public void DrawEllipse(Color color, Rectangle bounds, int thickness = 1) { }
        public void FillRoundedRectangle(Color color, Rectangle bounds, int radius) { }
        public void DrawRoundedRectangle(Color color, Rectangle bounds, int radius, int thickness = 1) { }
        public void DrawLine(Color color, int x1, int y1, int x2, int y2, int thickness = 1) { }
        public void DrawText(string text, Font font, Color color, Rectangle bounds, ContentAlignment alignment = ContentAlignment.TopLeft) { }
        public void DrawImage(IImage image, Rectangle bounds) { }
        public void PushClip(Rectangle bounds) { }
        public void PopClip() { }

        public Size MeasureText(string text, Font font) => new(Width(text), 16);

        internal static int Width(string text)
        {
            var total = 0;
            foreach (var cluster in Clusters(text))
                // The same boundary the colour-glyph scan uses, so the ellipsis (U+2026) and accented
                // Latin stay narrow while pictographs and CJK are wide.
                total += cluster.Any(c => c >= '←') ? Wide : Narrow;

            return total;
        }
    }

    private static readonly Font TestFont = new("Test", 12);

    /// <summary>The grapheme clusters of a string, in order.</summary>
    private static List<string> Clusters(string text)
    {
        var result = new List<string>();
        var offset = 0;
        while (offset < text.Length)
        {
            var length = StringInfo.GetNextTextElementLength(text.AsSpan(offset));
            result.Add(text.Substring(offset, length));
            offset += length;
        }

        return result;
    }

    private static string Trim(string text, int maxWidth)
        => TextTrim.ToWidth(new ShapingGraphics(), text, TestFont, maxWidth);

    /// <summary>The names a file manager actually has to draw, spelled the awkward ways.</summary>
    private static readonly string[] Names =
    [
        "a very long plain ascii filename indeed.txt",
        "🎉 party notes.txt",
        "vacation 🏖️.md",                 // emoji + variation selector
        "café ☕ résumé.pdf",
        "family 👩‍💻 photo.jpg",           // zero-width-joiner sequence
        "flag 🇩🇪 and 🇯🇵.txt",             // regional indicator pairs
        "wave 👋🏽 with a skin tone.png",  // base + modifier
        "日本語のとても長いファイル名.txt",
        "🎉🎉🎉🎉🎉🎉🎉🎉",                    // nothing but emoji
    ];

    [Test]
    public void A_label_that_already_fits_comes_back_untouched()
    {
        foreach (var name in Names)
            Assert.That(Trim(name, ShapingGraphics.Width(name)), Is.EqualTo(name), name);
    }

    [Test]
    public void A_shortened_label_fits_the_width_it_was_given()
    {
        foreach (var name in Names)
            for (var width = 0; width <= ShapingGraphics.Width(name) + Narrow; width += 5)
            {
                var trimmed = Trim(name, width);
                Assert.That(
                    ShapingGraphics.Width(trimmed),
                    Is.LessThanOrEqualTo(width),
                    $"\"{name}\" at {width}px came back as \"{trimmed}\"");
            }
    }

    [Test]
    public void The_cut_lands_between_glyphs_and_never_inside_one()
    {
        foreach (var name in Names)
        {
            // Every prefix that is a whole number of grapheme clusters. Anything else would mean a
            // lone surrogate, half a flag, or an emoji severed from its modifier.
            var whole = new HashSet<string> { string.Empty };
            var built = string.Empty;
            foreach (var cluster in Clusters(name))
            {
                built += cluster;
                whole.Add(built);
            }

            for (var width = 0; width <= ShapingGraphics.Width(name) + Narrow; width += 5)
            {
                var trimmed = Trim(name, width);
                if (trimmed == name || trimmed.Length == 0)
                    continue;

                Assert.That(trimmed, Does.EndWith(TextTrim.Ellipsis), $"\"{name}\" at {width}px");
                var kept = trimmed[..^TextTrim.Ellipsis.Length];
                Assert.That(whole, Does.Contain(kept), $"\"{name}\" at {width}px cut inside a glyph");
            }
        }
    }

    [Test]
    public void The_cut_is_measured_rather_than_counted()
    {
        // Same char count, different drawn width: eight emoji are 200px, eight letters are 80px. A
        // count-based cut would keep the same number of both.
        const int Width = 85;
        var letters = Trim("abcdefgh.txt", Width);
        var emoji = Trim("🎉🎉🎉🎉🎉🎉🎉🎉", Width);

        Assert.Multiple(() =>
        {
            Assert.That(Clusters(letters[..^1]), Has.Count.EqualTo(7), "7 letters + ellipsis = 80px");
            Assert.That(Clusters(emoji[..^1]), Has.Count.EqualTo(3), "3 emoji + ellipsis = 85px");
        });
    }

    [Test]
    public void A_width_too_small_for_anything_gives_back_nothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Trim("filename.txt", 0), Is.Empty);
            Assert.That(Trim("filename.txt", Narrow - 1), Is.Empty, "not even the ellipsis fits");
            Assert.That(Trim("filename.txt", Narrow), Is.EqualTo(TextTrim.Ellipsis), "exactly the ellipsis");
        });
    }

    [Test]
    public void An_empty_label_stays_empty()
        => Assert.That(Trim(string.Empty, 500), Is.Empty);

    [Test]
    public void A_label_longer_than_the_stack_buffer_still_trims_on_a_boundary()
    {
        // Past the point where the cluster walk moves to the heap, with an emoji at the seam so a
        // mistake there shows up as a broken glyph rather than a slightly different length.
        var name = string.Concat(Enumerable.Repeat("🎉a", 400));

        var trimmed = Trim(name, (10 * Wide) + (10 * Narrow));

        Assert.Multiple(() =>
        {
            Assert.That(trimmed, Does.EndWith(TextTrim.Ellipsis));
            Assert.That(ShapingGraphics.Width(trimmed), Is.LessThanOrEqualTo((10 * Wide) + (10 * Narrow)));
            Assert.That(char.IsHighSurrogate(trimmed[^2]), Is.False, "cut between the surrogates");
        });
    }
}
