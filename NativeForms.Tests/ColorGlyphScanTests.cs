using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// PRD §13: the scan that decides whether a string needs the colour-glyph renderer at all.
///
/// It guards the common case, so what matters most is that ordinary text is rejected — every plain string
/// a UI draws must reach the platform's own fast text call having paid nothing. In the other direction it
/// is deliberately generous: a false positive costs a slower path that still renders correctly, a false
/// negative renders a coloured glyph in black.
/// </summary>
[TestFixture]
internal sealed class ColorGlyphScanTests
{
    [TestCase("")]
    [TestCase("Open the project page")]
    [TestCase("Grüße, Ärger, naïve — em dashes and accents are text")]
    [TestCase("1234567890 !\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~")]
    [TestCase("日本語のテキスト")]
    [TestCase("Ελληνικά и кириллица")]
    [TestCase("\t\r\n ")]
    public void Ordinary_text_takes_the_fast_path(string text)
        => Assert.That(
            ColorGlyphScan.MayContainColorGlyphs(text),
            Is.False,
            "this string would have paid for a renderer it does not need");

    [TestCase("🐣", TestName = "a supplementary-plane emoji")]
    [TestCase("Small 🐣", TestName = "an emoji after text")]
    [TestCase("🇩🇪", TestName = "a flag, which is two regional indicators")]
    [TestCase("👩‍💻", TestName = "a zero-width-joiner sequence")]
    [TestCase("❤️", TestName = "a textual character asked to present as emoji")]
    [TestCase("1⃣", TestName = "a keycap")]
    [TestCase("✔", TestName = "a dingbat")]
    [TestCase("©", TestName = "the copyright sign")]
    public void Anything_that_might_carry_colour_takes_the_slow_path(string text)
        => Assert.That(ColorGlyphScan.MayContainColorGlyphs(text), Is.True);

    [Test]
    public void The_scan_stops_at_the_first_hit_rather_than_walking_the_whole_string()
    {
        // Not observable directly, but a very long tail after an early hit must not change the answer or
        // the cost characteristic the design rests on.
        var text = "🐣" + new string('a', 100_000);

        Assert.That(ColorGlyphScan.MayContainColorGlyphs(text), Is.True);
    }

    [Test]
    public void A_long_plain_string_is_still_rejected()
        => Assert.That(ColorGlyphScan.MayContainColorGlyphs(new string('a', 100_000)), Is.False);
}
