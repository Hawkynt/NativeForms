using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// One reading of the mnemonic convention (PRD §7.3), because a caption is read three times over — for
/// the key the form answers to, for the string that is drawn, and for where the underline goes — and
/// three copies of the escape rule is three chances to disagree about <c>&amp;&amp;</c>.
/// </summary>
[TestFixture]
internal sealed class MnemonicsTests
{
    [TestCase("plain", "plain", -1, '\0')]
    [TestCase("&Save", "Save", 0, 'S')]
    [TestCase("Sa&ve", "Save", 2, 'V')]
    [TestCase("Save &as", "Save as", 5, 'A')]
    [TestCase("A && B", "A & B", -1, '\0')]
    [TestCase("R&&D &now", "R&D now", 4, 'N')]
    [TestCase("trailing&", "trailing", -1, '\0')]
    [TestCase("", "", -1, '\0')]
    public void A_caption_reads_the_same_three_ways(string text, string stripped, int index, char key)
        => Assert.Multiple(() =>
        {
            Assert.That(Mnemonics.Strip(text), Is.EqualTo(stripped));
            Assert.That(Mnemonics.IndexOf(text), Is.EqualTo(index));
            Assert.That(Mnemonics.CharOf(text), Is.EqualTo(key));
        });

    /// <summary>The index has to address the stripped string, since that is the one being drawn.</summary>
    [TestCase("&Save")]
    [TestCase("Sa&ve")]
    [TestCase("R&&D &now")]
    public void The_index_points_at_the_marked_character_of_the_drawn_string(string text)
    {
        var stripped = Mnemonics.Strip(text);
        var index = Mnemonics.IndexOf(text);

        Assert.That(char.ToUpperInvariant(stripped[index]), Is.EqualTo(Mnemonics.CharOf(text)));
    }

    /// <summary>A caption with no mark-up is handed straight back, so nothing is allocated for it.</summary>
    [Test]
    public void A_caption_without_mark_up_is_the_same_instance()
    {
        var text = "nothing to strip";

        Assert.That(Mnemonics.Strip(text), Is.SameAs(text));
    }
}
