using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Tests;

/// <summary><see cref="ColorMath"/> converts between RGB and HSV/HSL/CMYK and parses/formats hex.</summary>
[TestFixture]
internal sealed class ColorMathTests
{
    [TestCase(0, 1.0, 1.0, 255, 0, 0)]       // red
    [TestCase(120, 1.0, 1.0, 0, 255, 0)]     // green
    [TestCase(240, 1.0, 1.0, 0, 0, 255)]     // blue
    [TestCase(0, 0.0, 1.0, 255, 255, 255)]   // white (no saturation)
    [TestCase(0, 0.0, 0.0, 0, 0, 0)]         // black
    [TestCase(60, 1.0, 1.0, 255, 255, 0)]    // yellow
    public void HsvToColor_hits_the_known_anchors(double h, double s, double v, int r, int g, int b)
    {
        var color = ColorMath.HsvToColor(h, s, v);

        Assert.Multiple(() =>
        {
            Assert.That(color.R, Is.EqualTo(r));
            Assert.That(color.G, Is.EqualTo(g));
            Assert.That(color.B, Is.EqualTo(b));
        });
    }

    [Test]
    public void HSV_round_trips_within_one_step()
    {
        foreach (var color in new[] { Color.Crimson, Color.SeaGreen, Color.RoyalBlue, Color.Goldenrod, Color.MediumOrchid, Color.DimGray })
        {
            ColorMath.ColorToHsv(color, out var h, out var s, out var v);
            var back = ColorMath.HsvToColor(h, s, v);
            Assert.Multiple(() =>
            {
                Assert.That(back.R, Is.EqualTo(color.R).Within(1), $"{color} R");
                Assert.That(back.G, Is.EqualTo(color.G).Within(1), $"{color} G");
                Assert.That(back.B, Is.EqualTo(color.B).Within(1), $"{color} B");
            });
        }
    }

    [Test]
    public void HSL_round_trips_within_one_step()
    {
        foreach (var color in new[] { Color.Crimson, Color.SeaGreen, Color.RoyalBlue, Color.Goldenrod, Color.MediumOrchid })
        {
            ColorMath.ColorToHsl(color, out var h, out var s, out var l);
            var back = ColorMath.HslToColor(h, s, l);
            Assert.Multiple(() =>
            {
                Assert.That(back.R, Is.EqualTo(color.R).Within(1), $"{color} R");
                Assert.That(back.G, Is.EqualTo(color.G).Within(1), $"{color} G");
                Assert.That(back.B, Is.EqualTo(color.B).Within(1), $"{color} B");
            });
        }
    }

    [Test]
    public void CMYK_round_trips_within_one_step()
    {
        foreach (var color in new[] { Color.Crimson, Color.SeaGreen, Color.RoyalBlue, Color.White, Color.Black })
        {
            ColorMath.ColorToCmyk(color, out var c, out var m, out var y, out var k);
            var back = ColorMath.CmykToColor(c, m, y, k);
            Assert.Multiple(() =>
            {
                Assert.That(back.R, Is.EqualTo(color.R).Within(1), $"{color} R");
                Assert.That(back.G, Is.EqualTo(color.G).Within(1), $"{color} G");
                Assert.That(back.B, Is.EqualTo(color.B).Within(1), $"{color} B");
            });
        }
    }

    [TestCase("#FF0000", 255, 0, 0, 255)]
    [TestCase("00FF00", 0, 255, 0, 255)]
    [TestCase("#f00", 255, 0, 0, 255)]
    [TestCase("#FF000080", 255, 0, 0, 128)]
    [TestCase("#F008", 255, 0, 0, 136)]
    public void TryParseHex_reads_every_length(string text, int r, int g, int b, int a)
    {
        Assert.That(ColorMath.TryParseHex(text, out var color), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(color.R, Is.EqualTo(r));
            Assert.That(color.G, Is.EqualTo(g));
            Assert.That(color.B, Is.EqualTo(b));
            Assert.That(color.A, Is.EqualTo(a));
        });
    }

    [TestCase("#GG0000")]
    [TestCase("12345")]
    [TestCase("")]
    [TestCase("#")]
    public void TryParseHex_rejects_malformed_text(string text)
        => Assert.That(ColorMath.TryParseHex(text, out _), Is.False);

    [Test]
    public void ToHex_formats_with_and_without_alpha()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ColorMath.ToHex(Color.FromArgb(255, 18, 52, 86), withAlpha: false), Is.EqualTo("#123456"));
            Assert.That(ColorMath.ToHex(Color.FromArgb(128, 18, 52, 86), withAlpha: true), Is.EqualTo("#12345680"));
        });
    }
}
